﻿using Azure.Sdk.Tools.PerfAutomation.Models;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Azure.Sdk.Tools.PerfAutomation
{
    public class Python : LanguageBase
    {
        private const string _env = "env-perf";
        private static readonly string _envBin = Util.IsWindows ? "scripts" : "bin";

        protected override Language Language => Language.Python;

        public override async Task<(string output, string error, string context)> SetupAsync(
            string project, string languageVersion, IDictionary<string, string> packageVersions)
        {
            var projectDirectory = Path.Combine(WorkingDirectory, project);
            var env = Path.Combine(projectDirectory, _env);

            Util.DeleteIfExists(env);

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            // On Windows, always use "python".  On Unix-like systems, specify the major and minor versions, e.g "python3.7".
            var systemPython = Util.IsWindows ? "python" : "python" + Regex.Match(languageVersion, @"^\d+\.\d+").Value;

            // Create venv
            await Util.RunAsync(systemPython, $"-m venv {_env}", projectDirectory, outputBuilder: outputBuilder, errorBuilder: errorBuilder);

            var python = Path.Combine(env, _envBin, "python");
            var pip = Path.Combine(env, _envBin, "pip");

            // Install test tools
            // await Util.RunAsync(pip, $"install -r {WorkingDirectory}/eng/test_tools.txt", projectDirectory, outputBuilder: outputBuilder, errorBuilder: errorBuilder: errorBuilder);

            // Install dev reqs
            await Util.RunAsync(pip, "install -r dev_requirements.txt", projectDirectory, outputBuilder: outputBuilder, errorBuilder: errorBuilder);

            // TODO: Support multiple packages if possible.  Maybe by force installing?
            foreach (var v in packageVersions)
            {
                var packageName = v.Key;
                var packageVersion = v.Value;

                if (packageVersion == Program.PackageVersionSource)
                {
                    await Util.RunAsync(pip, "install -e .", projectDirectory, outputBuilder: outputBuilder, errorBuilder: errorBuilder);
                }
                else
                {
                    await Util.RunAsync(pip, $"install {packageName}=={packageVersion}", projectDirectory, outputBuilder: outputBuilder, errorBuilder: errorBuilder);
                }
            }

            return (outputBuilder.ToString(), errorBuilder.ToString(), null);
        }

        public override async Task<IterationResult> RunAsync(string project, string languageVersion,
            IDictionary<string, string> packageVersions, string testName, string arguments, string context)
        {
            var projectDirectory = Path.Combine(WorkingDirectory, project);

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            var env = Path.Combine(projectDirectory, _env);
            var pip = Path.Combine(env, _envBin, "pip");
            var perfstress = Path.Combine(env, _envBin, "perfstress");

            var runtimePackageVersions = new Dictionary<string, string>(packageVersions.Count);
            var freezeResult = await Util.RunAsync(pip, "freeze", projectDirectory, outputBuilder: outputBuilder, errorBuilder: errorBuilder);
            foreach (var package in packageVersions.Keys)
            {
                // Package: azure-core==1.12.0
                // Source: -e git+https://github.com/Azure/azure-sdk-for-python@895ce54e1ad45ae15a0cd0cff89a29026a8a5cd2#egg=azure_storage_blob&subdirectory=sdk\storage\azure-storage-blob
                var versionMatch = Regex.Match(freezeResult.StandardOutput, @$"^.*{package}.*$", RegexOptions.Multiline);
                runtimePackageVersions[package] = versionMatch.Value.Trim();
            }

            var processResult = await Util.RunAsync(
                perfstress,
                $"{testName} {arguments}",
                Path.Combine(projectDirectory, "tests"),
                outputBuilder: outputBuilder,
                errorBuilder: errorBuilder
            );

            // TODO: Why does Python perf framework write to StdErr instead of StdOut?
            // Completed 5,718,534 operations in a weighted-average of 2.00s (2,858,373.57 ops/s, 0.000 s/op)
            var match = Regex.Match(processResult.StandardError, @"\((.*) ops/s", RegexOptions.IgnoreCase | RegexOptions.RightToLeft);
            double opsPerSecond = -1;
            if (match.Success)
            {
                opsPerSecond = double.Parse(match.Groups[1].Value);
            }

            return new IterationResult
            {
                PackageVersions = runtimePackageVersions,
                OperationsPerSecond = opsPerSecond,
                StandardOutput = outputBuilder.ToString(),
                StandardError = errorBuilder.ToString()
            };
        }

        public override Task CleanupAsync(string project)
        {
            var projectDirectory = Path.Combine(WorkingDirectory, project);
            var env = Path.Combine(projectDirectory, _env);
            Util.DeleteIfExists(env);
            return Task.CompletedTask;
        }
    }
}

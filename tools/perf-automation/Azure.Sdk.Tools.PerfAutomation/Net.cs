﻿using Azure.Sdk.Tools.PerfAutomation.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Azure.Sdk.Tools.PerfAutomation
{
    public class Net : LanguageBase
    {
        protected override Language Language => Language.Net;

        // Azure.Core.TestFramework.TestEnvironment requires publishing under the "artifacts" folder to find the repository root.
        private string PublishDirectory => Path.Join(WorkingDirectory, "artifacts", "perf");

        public override async Task<(string output, string error, string context)> SetupAsync(
            string project, string languageVersion, IDictionary<string, string> packageVersions)
        {
            var projectFile = Path.Combine(WorkingDirectory, project);

            File.Copy(projectFile, projectFile + ".bak", overwrite: true);

            var projectContents = File.ReadAllText(projectFile);
            var additionalBuildArguments = String.Empty;

            foreach (var v in packageVersions)
            {
                var packageName = v.Key;
                var packageVersion = v.Value;

                if (packageVersion == Program.PackageVersionSource)
                {
                    // Force all transitive dependencies to use project references, to ensure all packages are build from source.
                    // The default is for transitive dependencies to use package references to the latest published version.
                    additionalBuildArguments = "-p:UseProjectReferenceToAzureClients=true";
                }
                else
                {
                    // TODO: Use XmlDocument instead of Regex

                    // Existing reference might be to package or project:
                    // - <PackageReference Include="Microsoft.Azure.Storage.Blob" />
                    // - <ProjectReference Include="$(MSBuildThisFileDirectory)..\..\src\Azure.Storage.Blobs.csproj" />

                    string pattern;
                    var packageReferencePattern = $"<PackageReference [^>]*{packageName}[^<]*/>";
                    var projectReferencePattern = $"<ProjectReference [^>]*{packageName}.csproj[^<]*/>";

                    if (Regex.IsMatch(projectContents, packageReferencePattern))
                    {
                        pattern = packageReferencePattern;
                    }
                    else if (Regex.IsMatch(projectContents, projectReferencePattern))
                    {
                        pattern = projectReferencePattern;
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            $"Project file {projectFile} does not contain existing package or project reference to {packageName}");
                    }

                    projectContents = Regex.Replace(
                        projectContents,
                        pattern,
                        @$"<PackageReference Include=""{packageName}"" VersionOverride=""{packageVersion}"" />",
                        RegexOptions.IgnoreCase | RegexOptions.Singleline
                    );
                }
            }

            File.WriteAllText(projectFile, projectContents);

            Util.DeleteIfExists(PublishDirectory);

            var processArguments = $"publish -c release -f {languageVersion} -o {PublishDirectory} {additionalBuildArguments} {project}";

            var result = await Util.RunAsync("dotnet", processArguments, workingDirectory: WorkingDirectory);

            return (result.StandardOutput, result.StandardError, null);
        }

        public override async Task<IterationResult> RunAsync(string project, string languageVersion,
            IDictionary<string, string> packageVersions, string testName, string arguments, string context)
        {
            var dllName = Path.GetFileNameWithoutExtension(project) + ".dll";
            var dllPath = Path.Combine(PublishDirectory, dllName);

            var processArguments = $"{dllPath} {testName} {arguments}";

            var result = await Util.RunAsync("dotnet", processArguments, WorkingDirectory, throwOnError: false);

            // Completed 693,696 operations in a weighted-average of 1.00s (692,328.31 ops/s, 0.000 s/op)
            var match = Regex.Match(result.StandardOutput, @"\((.*) ops/s", RegexOptions.IgnoreCase | RegexOptions.RightToLeft);

            var opsPerSecond = -1d;
            if (match.Success)
            {
                opsPerSecond = double.Parse(match.Groups[1].Value);
            }

            var runtimePackageVersions = new Dictionary<string, string>(packageVersions.Count);
            foreach (var package in packageVersions.Keys)
            {
                // Azure.Storage.Blobs:
                //   Referenced: 12.8.0.0
                //   Loaded: 12.8.0.0
                //   Informational: 12.8.0+430f2eba747d6de99a43f4f8bd63cd28e673f979
                var versionMatch = Regex.Match(result.StandardOutput, @$"{package}:.*?Informational: (\S*)", RegexOptions.Singleline);
                runtimePackageVersions[package] = versionMatch.Groups[1].Value;
            }

            return new IterationResult
            {
                PackageVersions = runtimePackageVersions,
                OperationsPerSecond = opsPerSecond,
                StandardError = result.StandardError,
                StandardOutput = result.StandardOutput,
            };
        }

        public override Task CleanupAsync(string project)
        {
            Util.DeleteIfExists(PublishDirectory);

            var projectFile = Path.Combine(WorkingDirectory, project);

            // Restore backup
            File.Move(projectFile + ".bak", projectFile, overwrite: true);

            return Task.CompletedTask;
        }
    }
}

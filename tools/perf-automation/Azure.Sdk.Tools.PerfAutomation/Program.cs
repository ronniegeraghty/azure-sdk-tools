using Azure.Sdk.Tools.PerfAutomation.Models;
using CommandLine;
using CommandLine.Text;
using CsvHelper;
using CsvHelper.Configuration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace Azure.Sdk.Tools.PerfAutomation
{
    public static class Program
    {
        public const string PackageVersionSource = "source";

        public static Config Config { get; set; }

        private static Dictionary<Language, ILanguage> _languages = new Dictionary<Language, ILanguage>
        {
            { Language.Java, new Java() },
            { Language.JS, new JavaScript() },
            { Language.Net, new Net() },
            { Language.Python, new Python() },
            { Language.Cpp, new Cpp() }
        };

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
            },
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true,
        };

        private static readonly CsvConfiguration CsvConfiguration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = ", ",
        };

        [Verb("run", HelpText = "Run perf tests and collect results")]
        public class RunOptions
        {
            [Option('a', "arguments", HelpText = "Regex of arguments to run")]
            public string Arguments { get; set; }

            [Option('c', "configFile", Default = "config.yml")]
            public string ConfigFile { get; set; }

            [Option('d', "debug")]
            public bool Debug { get; set; }

            [Option('n', "dry-run")]
            public bool DryRun { get; set; }

            [Option("input-file", Default = "tests.yml")]
            public string InputFile { get; set; }

            [Option("insecure", HelpText = "Allow untrusted SSL certs")]
            public bool Insecure { get; set; }

            [Option('i', "iterations", Default = 1)]
            public int Iterations { get; set; }

            [Option('l', "languages", HelpText = "List of languages (separated by spaces)")]
            public IEnumerable<Language> Languages { get; set; }

            [Option('v', "language-versions", HelpText = "Regex of language versions to run")]
            public string LanguageVersions { get; set; }

            [Option("no-async")]
            public bool NoAsync { get; set; }

            [Option("no-cleanup", HelpText = "Disables test cleanup")]
            public bool NoCleanup { get; set; }

            [Option("no-sync")]
            public bool NoSync { get; set; }

            [Option('o', "output-file-prefix", Default = "results/results")]
            public string OutputFilePrefix { get; set; }

            [Option('p', "package-versions", HelpText = "Regex of package versions to run")]
            public string PackageVersions { get; set; }

            [Option('s', "services", HelpText = "Regex of services to run")]
            public string Services { get; set; }

            // TODO: Configure YAML serialization to print URI values
            [Option('x', "test-proxies", Separator = ';', HelpText = "URIs of TestProxy Servers")]
            [YamlMember(typeof(string))]
            public IEnumerable<Uri> TestProxies { get; set; }

            [Option("test-proxy", HelpText = "URI of TestProxy Server")]
            [YamlMember(typeof(string))]
            public Uri TestProxy { get; set; }

            [Option('t', "tests", HelpText = "Regex of tests to run")]
            public string Tests { get; set; }
        }

        public static async Task Main(string[] args)
        {
            var parser = new CommandLine.Parser(settings =>
            {
                settings.CaseSensitive = false;
                settings.CaseInsensitiveEnumValues = true;
                settings.HelpWriter = null;
            });

            var parserResult = parser.ParseArguments<RunOptions>(args);

            await parserResult.MapResult(
                (RunOptions options) => Run(options),
                errors => DisplayHelp(parserResult)
            );
        }

        static Task DisplayHelp<T>(ParserResult<T> result)
        {
            var helpText = HelpText.AutoBuild(result, settings =>
            {
                settings.AddEnumValuesToHelpText = true;
                return settings;
            });

            Console.Error.WriteLine(helpText);

            return Task.CompletedTask;
        }

        private static async Task Run(RunOptions options)
        {
            Config = DeserializeYaml<Config>(options.ConfigFile);

            var input = DeserializeYaml<Input>(options.InputFile);

            var selectedlanguages = input.Languages
                .Where(l => !options.Languages.Any() || options.Languages.Contains(l.Key))
                .ToDictionary(l => l.Key, l => new LanguageInfo()
                {
                    DefaultVersions = l.Value.DefaultVersions.Where(v =>
                            (String.IsNullOrEmpty(options.LanguageVersions) || Regex.IsMatch(v, options.LanguageVersions)) &&
                            !(l.Key == Language.Net && v == "net461" && !Util.IsWindows)),
                    OptionalVersions = l.Value.OptionalVersions.Where(v =>
                            (!String.IsNullOrEmpty(options.LanguageVersions) && Regex.IsMatch(v, options.LanguageVersions)) &&
                            !(l.Key == Language.Net && v == "net461" && !Util.IsWindows))
                });


            var selectedServices = input.Services
                .Where(s => String.IsNullOrEmpty(options.Services) || Regex.IsMatch(s.Service, options.Services, RegexOptions.IgnoreCase))
                .Select(s => new ServiceInfo
                {
                    Service = s.Service,
                    Languages = s.Languages
                        .Where(l => !options.Languages.Any() || options.Languages.Contains(l.Key))
                        .ToDictionary(p => p.Key, p => new ServiceLanguageInfo()
                        {
                            Project = p.Value.Project,
                            AdditionalArguments = p.Value.AdditionalArguments,
                            PackageVersions = p.Value.PackageVersions.Where(d =>
                                String.IsNullOrEmpty(options.PackageVersions) || Regex.IsMatch(d[p.Value.PrimaryPackage], options.PackageVersions)),
                            PrimaryPackage = p.Value.PrimaryPackage,
                        }),
                    Tests = s.Tests
                        .Where(t => String.IsNullOrEmpty(options.Tests) || Regex.IsMatch(t.Test, options.Tests, RegexOptions.IgnoreCase))
                        .Select(t => new TestInfo
                        {
                            Test = t.Test,
                            Arguments = t.Arguments.Where(a =>
                                String.IsNullOrEmpty(options.Arguments) || Regex.IsMatch(a, options.Arguments, RegexOptions.IgnoreCase)),
                            TestNames = t.TestNames.Where(n => !options.Languages.Any() || options.Languages.Contains(n.Key))
                                        .ToDictionary(p => p.Key, p => p.Value)
                        })
                        .Where(t => t.TestNames.Any())
                        .Where(t => t.Arguments.Any()),
                })
                .Where(s => s.Tests.Any());

            var serializer = new Serializer();
            Console.WriteLine("=== Options ===");
            serializer.Serialize(Console.Out, options);

            Console.WriteLine();

            Console.WriteLine("=== Test Plan ===");
            serializer.Serialize(Console.Out, new Input() { Languages = selectedlanguages, Services = selectedServices });

            if (options.DryRun)
            {
                Console.WriteLine();
                Console.Write("Press 'y' to continue, or any other key to exit: ");
                var key = Console.ReadKey();
                Console.WriteLine();
                Console.WriteLine();
                if (char.ToLowerInvariant(key.KeyChar) != 'y')
                {
                    return;
                }
            }

            var outputFiles = Util.GetUniquePaths(options.OutputFilePrefix, ".json", ".csv");

            // Create output file early so user sees it immediately
            foreach (var outputFile in outputFiles)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputFile));
                using (File.Create(outputFile)) { }
            }

            var outputJson = outputFiles[0];
            var outputCsv = outputFiles[1];

            var results = new List<Result>();

            foreach (var service in selectedServices)
            {
                foreach (var l in service.Languages)
                {
                    var language = l.Key;
                    var serviceLanugageInfo = l.Value;

                    var languageInfo = selectedlanguages[language];

                    foreach (var languageVersion in languageInfo.DefaultVersions.Concat(languageInfo.OptionalVersions))
                    {

                        foreach (var packageVersions in serviceLanugageInfo.PackageVersions)
                        {
                            await RunPackageVersion(options, outputJson, outputCsv, results, service,
                                language, serviceLanugageInfo, languageVersion, packageVersions);
                        }
                    }
                }
            }
        }

        private static async Task RunPackageVersion(RunOptions options, string outputJson, string outputCsv, List<Result> results, ServiceInfo service,
            Language language, ServiceLanguageInfo serviceLanguageInfo, string languageVersion, IDictionary<string, string> packageVersions)
        {
            try
            {
                Console.WriteLine($"SetupAsync({serviceLanguageInfo.Project}, {languageVersion}, " +
                    $"{JsonSerializer.Serialize(packageVersions)})");
                Console.WriteLine();

                string setupOutput = null;
                string setupError = null;
                string context = null;
                string setupException = null;

                try
                {
                    (setupOutput, setupError, context) = await _languages[language].SetupAsync(
                        serviceLanguageInfo.Project, languageVersion, packageVersions);
                }
                catch (Exception e)
                {
                    setupException = e.ToString();

                    Console.WriteLine(e);
                    Console.WriteLine();
                }

                foreach (var test in service.Tests)
                {
                    IEnumerable<string> selectedArguments;
                    if (!options.NoAsync && !options.NoSync)
                    {
                        selectedArguments = test.Arguments.SelectMany(a => new string[] { a, a + " --sync" });
                    }
                    else if (!options.NoSync)
                    {
                        selectedArguments = test.Arguments.Select(a => a + " --sync");
                    }
                    else if (!options.NoAsync)
                    {
                        selectedArguments = test.Arguments;
                    }
                    else
                    {
                        throw new InvalidOperationException("Cannot set both --no-sync and --no-async");
                    }

                    foreach (var arguments in selectedArguments)
                    {
                        var allArguments = arguments;

                        if (serviceLanguageInfo.AdditionalArguments != null)
                        {
                            foreach (var kvp in serviceLanguageInfo.AdditionalArguments)
                            {
                                var (name, value) = (kvp.Key, kvp.Value);

                                if (!arguments.Contains($"--{name} "))
                                {
                                    allArguments += $" --{name} {value}";
                                }
                            }
                        }

                        if (options.Insecure)
                        {
                            allArguments += " --insecure";
                        }

                        if (options.TestProxies != null && options.TestProxies.Any())
                        {
                            allArguments += $" --test-proxies {String.Join(';', options.TestProxies)}";
                        }

                        if (options.TestProxy != null)
                        {
                            allArguments += $" --test-proxy {options.TestProxy}";
                        }

                        var result = new Result
                        {
                            Service = service.Service,
                            Test = test.Test,
                            Start = DateTime.Now,
                            Language = language,
                            LanguageVersion = languageVersion,
                            Project = serviceLanguageInfo.Project,
                            LanguageTestName = test.TestNames[language],
                            Arguments = allArguments,
                            PrimaryPackage = serviceLanguageInfo.PrimaryPackage,
                            PackageVersions = packageVersions,
                            SetupStandardOutput = setupOutput,
                            SetupStandardError = setupError,
                            SetupException = setupException,
                        };

                        results.Add(result);

                        await WriteResults(outputJson, outputCsv, results);
                        if (setupException == null)
                        {
                            for (var i = 0; i < options.Iterations; i++)
                            {
                                IterationResult iterationResult;
                                try
                                {
                                    Console.WriteLine($"RunAsync({serviceLanguageInfo.Project}, {languageVersion}, " +
                                        $"{test.TestNames[language]}, {allArguments}, {context})");
                                    Console.WriteLine();

                                    iterationResult = await _languages[language].RunAsync(
                                        serviceLanguageInfo.Project,
                                        languageVersion,
                                        packageVersions,
                                        test.TestNames[language],
                                        allArguments,
                                        context
                                    );
                                }
                                catch (Exception e)
                                {
                                    iterationResult = new IterationResult
                                    {
                                        OperationsPerSecond = double.MinValue,
                                        Exception = e.ToString(),
                                    };

                                    Console.WriteLine(e);
                                    Console.WriteLine();
                                }

                                // Replace non-finite values with minvalue, since non-finite values
                                // are not JSON serializable
                                if (!double.IsFinite(iterationResult.OperationsPerSecond))
                                {
                                    iterationResult.OperationsPerSecond = double.MinValue;
                                }

                                result.Iterations.Add(iterationResult);

                                await WriteResults(outputJson, outputCsv, results);
                            }
                        }

                        result.End = DateTime.Now;
                    }
                }
            }
            finally
            {
                if (!options.NoCleanup)
                {
                    Console.WriteLine($"CleanupAsync({serviceLanguageInfo.Project})");
                    Console.WriteLine();

                    try
                    {
                        await _languages[language].CleanupAsync(serviceLanguageInfo.Project);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                        Console.WriteLine();
                    }
                }
            }
        }

        private static async Task WriteResults(string outputJson, string outputCsv, List<Result> results)
        {
            using (var stream = File.OpenWrite(outputJson))
            {
                await JsonSerializer.SerializeAsync(stream, results, JsonOptions);
            }

            var groups = results.GroupBy(r => (r.Language, r.LanguageVersion, r.Service, r.Test, r.Arguments));
            var resultSummaries = groups.Select(g =>
            {
                var resultSummary = new ResultSummary()
                {
                    Language = g.Key.Language,
                    LanguageVersion = g.Key.LanguageVersion,
                    Service = g.Key.Service,
                    Test = g.Key.Test,
                    Arguments = g.Key.Arguments,
                };

                foreach (var result in g)
                {
                    var primaryPackage = result.PrimaryPackage;
                    var primaryPackageVersion = result.PackageVersions[primaryPackage];
                    if (primaryPackageVersion == "source")
                    {
                        resultSummary.Source = result.OperationsPerSecondMax;
                    }
                    else
                    {
                        resultSummary.LastVersion = primaryPackageVersion;
                        resultSummary.Last = result.OperationsPerSecondMax;
                    }
                }

                return resultSummary;
            });

            using (var streamWriter = new StreamWriter(outputCsv))
            using (var csvWriter = new CsvWriter(streamWriter, CsvConfiguration))
            {
                await csvWriter.WriteRecordsAsync(resultSummaries);
            }
        }

        private static T DeserializeYaml<T>(string path)
        {
            using var fileReader = File.OpenText(path);
            var parser = new MergingParser(new YamlDotNet.Core.Parser(fileReader));
            return new Deserializer().Deserialize<T>(parser);
        }
    }
}

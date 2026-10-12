using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace OpenVisionLab.Inspection.Smoke
{
    internal sealed class SmokeRunner
    {
        private readonly string filter;
        private readonly bool listOnly;
        private readonly string reportPath;
        private readonly Dictionary<string, int> available = new Dictionary<string, int>();
        private readonly List<(string Name, string Scope, string Outcome)> results = new List<(string, string, string)>();

        internal SmokeRunner(string filter, bool listOnly, string reportPath = null)
        {
            this.filter = filter;
            this.listOnly = listOnly;
            this.reportPath = reportPath;
        }

        internal int Passed { get; private set; }

        internal int Total { get; private set; }

        internal void Run(IEnumerable<SmokeCase> cases)
        {
            foreach (SmokeCase smokeCase in cases)
            {
                available[smokeCase.Scope] = available.GetValueOrDefault(smokeCase.Scope) + 1;
                if (filter != null && !smokeCase.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Total++;
                if (listOnly)
                {
                    results.Add((smokeCase.Name, smokeCase.Scope, "Listed"));
                    Console.WriteLine(smokeCase.Name);
                    continue;
                }

                try
                {
                    smokeCase.Execute();
                    Passed++;
                    results.Add((smokeCase.Name, smokeCase.Scope, "Passed"));
                    Console.WriteLine("PASS | " + smokeCase.Name);
                }
                catch
                {
                    results.Add((smokeCase.Name, smokeCase.Scope, "Failed"));
                    throw;
                }
            }
        }

        internal void WriteReport(string failure)
        {
            if (reportPath == null) return;
            string[] scopes = { "cpu", "cuda-probe", "cuda-device", "native-fixture" };
            string[] switches = { "OVL_COMPUTE_TEST_MODULES", "OVL_COMPUTE_REAL_PROBE", "OVL_CUDA_TEST_MODULE", "OVL_CUDA_EXECUTION_FIXTURES" };
            var configured = switches.Select(name => new { Variable = name, Path = Environment.GetEnvironmentVariable(name) }).ToArray();
            bool enabled(string name) => configured.Any(item => item.Variable == name && !string.IsNullOrEmpty(item.Path));
            var scopeResults = scopes.Select(scope =>
            {
                int selected = results.Count(item => item.Scope == scope), passed = results.Count(item => item.Scope == scope && item.Outcome == "Passed");
                int failed = results.Count(item => item.Scope == scope && item.Outcome == "Failed"), listed = results.Count(item => item.Scope == scope && item.Outcome == "Listed");
                bool isEnabled = scope == "cpu" || (scope == "cuda-probe" && enabled("OVL_COMPUTE_REAL_PROBE"))
                    || (scope == "cuda-device" && enabled("OVL_CUDA_TEST_MODULE"))
                    || (scope == "native-fixture" && (enabled("OVL_COMPUTE_TEST_MODULES") || (enabled("OVL_CUDA_TEST_MODULE") && enabled("OVL_CUDA_EXECUTION_FIXTURES"))));
                return new { Scope = scope, Enabled = isEnabled, Available = available.GetValueOrDefault(scope), Selected = selected, Passed = passed, Failed = failed, Listed = listed,
                    Status = !isEnabled ? "NotEnabled" : selected == 0 ? "NotSelected" : listOnly ? "Listed" : failed > 0 ? "Failed" : passed == selected ? "Passed" : "Incomplete" };
            }).ToArray();
            Assembly assembly = typeof(Program).Assembly;
            var report = new
            {
                SchemaVersion = 1, Mode = listOnly ? "List" : "Execution", Status = failure != null ? "Failed" : listOnly ? "Listed" : "Passed",
                Filter = filter, EnumerationComplete = failure == null, Total, Passed, Failure = failure,
                Assembly = new { assembly.GetName().Name, Path = assembly.Location,
                    InformationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown",
                    ModuleVersionId = assembly.ManifestModule.ModuleVersionId.ToString("D"), Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))) },
                Scopes = scopeResults, Configuration = configured,
                Cases = results.Select(item => new { item.Name, item.Scope, item.Outcome }).ToArray(),
                Boundary = "Scopes describe selected assertions: cuda-probe is discovery only; native-fixture is synthetic ABI/failure evidence; cuda-device requires assertions against an actual device. NotEnabled, NotSelected and Listed are not execution passes. Managed coverage does not measure native C++ kernels."
            };
            string path = Path.GetFullPath(reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}

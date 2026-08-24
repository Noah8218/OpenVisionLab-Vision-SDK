using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace OpenVisionLab.Vision3D.Benchmark
{
    internal static class PairedBaselineComparer
    {
        private const string InputSchema =
            "openvisionlab-paired-baseline-input-v2";
        private const string ReportSchema =
            "openvisionlab-synthetic-mesh-benchmark-v1";
        private const string OutputSchema =
            "openvisionlab-paired-baseline-comparison-v2";
        private const int RoundCount = 5;
        private const double StabilityThresholdPercent = 5.0;
        private const double MedianRegressionThresholdPercent = 10.0;
        private const double P95RegressionThresholdPercent = 15.0;

        private static readonly JsonSerializerOptions JsonOptions =
            new JsonSerializerOptions
            {
                WriteIndented = true
            };

        internal static int Run(string[] args)
        {
            Dictionary<string, string> options = ParseOptions(args);
            string manifestPath = Required(options, "--manifest");
            string outputPath = Required(options, "--output");
            Require(options.Count == 2,
                "Unknown paired comparison argument.");

            string fullManifestPath = Path.GetFullPath(manifestPath);
            string attemptRoot = Path.GetDirectoryName(fullManifestPath);
            Require(IsWithinRoot(attemptRoot, Path.GetFullPath(outputPath)),
                "Paired comparison output escaped the attempt root.");
            PairedComparison comparison = Compare(fullManifestPath);
            WriteJson(outputPath, comparison);
            Console.WriteLine(
                comparison.Status
                + " | paired-comparison | "
                + Path.GetFullPath(outputPath));
            foreach (string failure in comparison.Failures)
            {
                Console.WriteLine("FAILURE | " + failure);
            }

            foreach (string alert in comparison.InvestigationAlerts)
            {
                Console.WriteLine("INVESTIGATE | " + alert);
            }

            if (comparison.Status == "Passed")
            {
                return 0;
            }

            return comparison.Status == "InvestigationRequired" ? 2 : 1;
        }

        internal static int RunSelfTest(string[] args)
        {
            Dictionary<string, string> options = ParseOptions(args);
            string tempRoot = Required(options, "--temp-root");
            Require(options.Count == 1,
                "Unknown paired self-test argument.");
            string root = Path.Combine(
                Path.GetFullPath(tempRoot),
                "paired-comparer-self-test-"
                + DateTime.UtcNow.ToString(
                    "yyyyMMddHHmmssfff",
                    CultureInfo.InvariantCulture));
            Directory.CreateDirectory(root);

            PairedComparison positive = Compare(
                CreateFixture(Path.Combine(root, "positive"),
                    new FixtureOptions()));
            Require(positive.Status == "Passed",
                "Positive paired fixture did not pass.");

            ExpectFailure(root, "wrong-order",
                new FixtureOptions { WrongTimestamp = true });
            ExpectFailure(root, "wrong-session-count",
                new FixtureOptions { RemoveRound = true });
            ExpectFailure(root, "cross-root",
                new FixtureOptions { CrossRootPath = true });
            ExpectFailure(root, "commit-mismatch",
                new FixtureOptions { CommitMismatch = true });
            ExpectFailure(root, "fingerprint-mismatch",
                new FixtureOptions { FingerprintMismatch = true });
            ExpectFailure(root, "single-session-noise",
                new FixtureOptions { SingleSessionNoiseFailure = true });
            ExpectFailure(root, "readiness-failure",
                new FixtureOptions { ReadinessFailure = true });

            PairedComparison unstable = Compare(
                CreateFixture(
                    Path.Combine(root, "ratio-instability"),
                    new FixtureOptions
                    {
                        WallMedianRatios = new[]
                        {
                            0.8,
                            0.9,
                            1.0,
                            1.1,
                            1.2
                        }
                    }));
            Require(unstable.Status == "IncompletePerformance",
                "Ratio instability fixture was not rejected.");

            PairedComparison medianRegression = Compare(
                CreateFixture(
                    Path.Combine(root, "median-regression"),
                    new FixtureOptions
                    {
                        WallMedianRatios = Enumerable.Repeat(1.10, 5).ToArray()
                    }));
            Require(medianRegression.Status == "InvestigationRequired",
                "Median regression fixture did not require investigation.");

            PairedComparison p95Regression = Compare(
                CreateFixture(
                    Path.Combine(root, "p95-regression"),
                    new FixtureOptions
                    {
                        WallP95Ratios = Enumerable.Repeat(1.15, 5).ToArray()
                    }));
            Require(p95Regression.Status == "InvestigationRequired",
                "P95 regression fixture did not require investigation.");

            Console.WriteLine("PASS | paired-self-test | " + root);
            return 0;
        }

        private static PairedComparison Compare(string manifestPath)
        {
            string fullManifestPath = Path.GetFullPath(manifestPath);
            string attemptRoot = Path.GetDirectoryName(fullManifestPath);
            Require(!string.IsNullOrEmpty(attemptRoot),
                "Paired manifest has no parent directory.");
            PairedInput input = ReadJson<PairedInput>(fullManifestPath);
            Require(input.SchemaVersion == InputSchema,
                "Paired input schema changed.");
            RequireCommit(input.BaselineCommit, "baseline commit");
            RequireCommit(input.CurrentCommit, "current commit");
            RequireCommit(input.HarnessCommit, "harness commit");
            RequireCommit(input.ComparerCommit, "comparer commit");
            Require(input.ComparerCommit == GetSourceRevision(
                    Assembly.GetExecutingAssembly()),
                "Input comparer commit does not match the running assembly.");
            Require(input.Accuracy != null,
                "Accuracy report map is missing.");
            Require(input.Workloads != null && input.Workloads.Count == 2,
                "Exactly two paired workloads are required.");
            Require(input.Workloads[0].Id == "planar-direct-v1"
                    && input.Workloads[1].Id == "planar-boundary-v1",
                "Paired workloads changed or are out of order.");

            ComparisonContext context = new ComparisonContext(
                attemptRoot,
                input);
            List<AccuracyDiagnostic> accuracyDiagnostics =
                new List<AccuracyDiagnostic>
                {
                    CompareAccuracy(
                        context,
                        "planar-direct-v1",
                        input.Accuracy.DirectBaseline,
                        input.Accuracy.DirectCurrent),
                    CompareAccuracy(
                        context,
                        "planar-boundary-v1",
                        input.Accuracy.BoundaryBaseline,
                        input.Accuracy.BoundaryCurrent)
                };

            List<WorkloadComparison> workloads =
                new List<WorkloadComparison>();
            List<string> failures = new List<string>();
            List<string> alerts = new List<string>();
            foreach (PairedWorkloadInput workload in input.Workloads)
            {
                WorkloadComparison comparison = CompareWorkload(
                    context,
                    workload,
                    failures,
                    alerts);
                workloads.Add(comparison);
            }

            string status = failures.Count > 0
                ? "IncompletePerformance"
                : alerts.Count > 0
                    ? "InvestigationRequired"
                    : "Passed";
            return new PairedComparison
            {
                SchemaVersion = OutputSchema,
                CreatedUtc = DateTimeOffset.UtcNow,
                ComparerCommit = GetSourceRevision(
                    Assembly.GetExecutingAssembly()),
                BaselineCommit = input.BaselineCommit,
                CurrentCommit = input.CurrentCommit,
                HarnessCommit = input.HarnessCommit,
                AccuracyParityPassed = true,
                AccuracyDiagnostics = accuracyDiagnostics,
                Workloads = workloads,
                Failures = failures,
                InvestigationAlerts = alerts,
                Status = status
            };
        }

        private static AccuracyDiagnostic CompareAccuracy(
            ComparisonContext context,
            string workload,
            string baselinePath,
            string currentPath)
        {
            RawReport baseline = context.ReadReport(
                baselinePath,
                "accuracy",
                workload,
                "A-accuracy",
                context.Input.BaselineCommit,
                false);
            RawReport current = context.ReadReport(
                currentPath,
                "accuracy",
                workload,
                "B-accuracy",
                context.Input.CurrentCommit,
                false);
            Require(baseline.Accuracy != null
                    && current.Accuracy != null
                    && baseline.Performance == null
                    && current.Performance == null,
                workload + " accuracy report shape is invalid.");
            Require(baseline.Accuracy.OraclePassed
                    && current.Accuracy.OraclePassed
                    && baseline.Accuracy.RepeatedResultExactFingerprintMatch
                    && current.Accuracy.RepeatedResultExactFingerprintMatch,
                workload + " analytical oracle failed.");
            Require(baseline.Accuracy.PerTargetOracleTolerance == 0.5e-12
                    && current.Accuracy.PerTargetOracleTolerance == 0.5e-12
                    && baseline.Accuracy.CrossTargetParityTolerance == 1e-12
                    && current.Accuracy.CrossTargetParityTolerance == 1e-12,
                workload + " accuracy tolerance changed.");
            Require(
                baseline.Accuracy.MaximumAbsoluteOracleError <= 0.5e-12
                && current.Accuracy.MaximumAbsoluteOracleError <= 0.5e-12,
                workload + " oracle error exceeded its fixed tolerance.");
            Require(
                baseline.Accuracy.PointExactFingerprint
                    == current.Accuracy.PointExactFingerprint
                && baseline.Accuracy.PointQuantizedFingerprint
                    == current.Accuracy.PointQuantizedFingerprint
                && baseline.Accuracy.ResultExactFingerprint
                    == current.Accuracy.ResultExactFingerprint
                && baseline.Accuracy.ResultQuantizedFingerprint
                    == current.Accuracy.ResultQuantizedFingerprint,
                workload + " cross-target accuracy fingerprint changed.");
            return new AccuracyDiagnostic
            {
                Workload = workload,
                CombinedMaximumAbsoluteOracleError = Math.Max(
                    baseline.Accuracy.MaximumAbsoluteOracleError,
                    current.Accuracy.MaximumAbsoluteOracleError),
                PointExactFingerprint = baseline.Accuracy.PointExactFingerprint,
                PointQuantizedFingerprint =
                    baseline.Accuracy.PointQuantizedFingerprint,
                ResultExactFingerprint = baseline.Accuracy.ResultExactFingerprint,
                ResultQuantizedFingerprint =
                    baseline.Accuracy.ResultQuantizedFingerprint
            };
        }

        private static WorkloadComparison CompareWorkload(
            ComparisonContext context,
            PairedWorkloadInput workload,
            ICollection<string> failures,
            ICollection<string> alerts)
        {
            Require(workload.Rounds != null
                    && workload.Rounds.Count == RoundCount,
                workload.Id + " must contain exactly five rounds.");
            List<RoundReports> rounds = new List<RoundReports>();
            for (int index = 0; index < RoundCount; index++)
            {
                PairedRoundInput round = workload.Rounds[index];
                int ordinal = index + 1;
                Require(round.Ordinal == ordinal,
                    workload.Id + " round ordinal changed.");
                string prefix = "R" + ordinal.ToString(
                    "00",
                    CultureInfo.InvariantCulture) + "-";
                context.ReadReadiness(
                    round.A1Readiness,
                    workload.Id,
                    prefix + "A1");
                RawReport a1 = context.ReadReport(
                        round.A1,
                        "performance",
                        workload.Id,
                        prefix + "A1",
                        context.Input.BaselineCommit,
                        true);
                context.ReadReadiness(
                    round.B1Readiness,
                    workload.Id,
                    prefix + "B1");
                RawReport b1 = context.ReadReport(
                        round.B1,
                        "performance",
                        workload.Id,
                        prefix + "B1",
                        context.Input.CurrentCommit,
                        true);
                context.ReadReadiness(
                    round.B2Readiness,
                    workload.Id,
                    prefix + "B2");
                RawReport b2 = context.ReadReport(
                        round.B2,
                        "performance",
                        workload.Id,
                        prefix + "B2",
                        context.Input.CurrentCommit,
                        true);
                context.ReadReadiness(
                    round.A2Readiness,
                    workload.Id,
                    prefix + "A2");
                RawReport a2 = context.ReadReport(
                        round.A2,
                        "performance",
                        workload.Id,
                        prefix + "A2",
                        context.Input.BaselineCommit,
                        true);
                rounds.Add(new RoundReports
                {
                    Ordinal = ordinal,
                    A1 = a1,
                    B1 = b1,
                    B2 = b2,
                    A2 = a2
                });
            }

            ValidatePerformanceFingerprints(workload.Id, rounds);
            List<PairedMetricComparison> metrics =
                new List<PairedMetricComparison>();
            metrics.Add(CompareMetric(
                workload.Id,
                "WallMillisecondsPerOperation",
                rounds,
                report => report.Performance.WallMillisecondsPerOperation,
                failures,
                alerts,
                true));
            metrics.Add(CompareMetric(
                workload.Id,
                "IndexMillisecondsPerOperation",
                rounds,
                report => report.Performance.IndexMillisecondsPerOperation,
                failures,
                alerts,
                true));
            metrics.Add(CompareMetric(
                workload.Id,
                "CalculationMillisecondsPerOperation",
                rounds,
                report => report.Performance.CalculationMillisecondsPerOperation,
                failures,
                alerts,
                true));
            metrics.Add(CompareMetric(
                workload.Id,
                "AllocatedBytesPerOperation",
                rounds,
                report => report.Performance.AllocatedBytesPerOperation,
                failures,
                alerts,
                false));

            return new WorkloadComparison
            {
                Workload = workload.Id,
                InputSha256 = rounds[0].A1.Workload.InputSha256,
                RoundCount = RoundCount,
                Metrics = metrics,
                BaselineGcTotals = SumGc(rounds, true),
                CurrentGcTotals = SumGc(rounds, false)
            };
        }

        private static PairedMetricComparison CompareMetric(
            string workload,
            string metric,
            IEnumerable<RoundReports> reports,
            Func<RawReport, MetricSummary> select,
            ICollection<string> failures,
            ICollection<string> alerts,
            bool timingMetric)
        {
            List<PairedRoundMetric> rounds = reports.Select(round =>
            {
                MetricSummary a1 = select(round.A1);
                MetricSummary a2 = select(round.A2);
                MetricSummary b1 = select(round.B1);
                MetricSummary b2 = select(round.B2);
                double baselineMedian = GeometricMean(a1.Median, a2.Median);
                double currentMedian = GeometricMean(b1.Median, b2.Median);
                double baselineP95 = GeometricMean(a1.P95, a2.P95);
                double currentP95 = GeometricMean(b1.P95, b2.P95);
                return new PairedRoundMetric
                {
                    Ordinal = round.Ordinal,
                    BaselineMedian = baselineMedian,
                    CurrentMedian = currentMedian,
                    MedianRatio = currentMedian / baselineMedian,
                    BaselineP95 = baselineP95,
                    CurrentP95 = currentP95,
                    P95Ratio = currentP95 / baselineP95
                };
            }).ToList();

            RatioSummary medianRatios = SummarizeRatios(
                rounds.Select(round => round.MedianRatio));
            RatioSummary p95Ratios = SummarizeRatios(
                rounds.Select(round => round.P95Ratio));
            if (timingMetric)
            {
                if (medianRatios.RelativeMadPercent
                    >= StabilityThresholdPercent - 1e-12)
                {
                    failures.Add(
                        workload + " " + metric
                        + " paired median-ratio RMAD = "
                        + Format(medianRatios.RelativeMadPercent)
                        + "%.");
                }

                if (p95Ratios.RelativeMadPercent
                    >= StabilityThresholdPercent - 1e-12)
                {
                    failures.Add(
                        workload + " " + metric
                        + " paired P95-ratio RMAD = "
                        + Format(p95Ratios.RelativeMadPercent)
                        + "%.");
                }

                double medianDelta = (medianRatios.Median - 1.0) * 100.0;
                double p95Delta = (p95Ratios.Median - 1.0) * 100.0;
                if (medianDelta
                    >= MedianRegressionThresholdPercent - 1e-12)
                {
                    alerts.Add(
                        workload + " " + metric
                        + " paired median regression = "
                        + Format(medianDelta) + "%.");
                }

                if (p95Delta
                    >= P95RegressionThresholdPercent - 1e-12)
                {
                    alerts.Add(
                        workload + " " + metric
                        + " paired P95 regression = "
                        + Format(p95Delta) + "%.");
                }
            }

            return new PairedMetricComparison
            {
                Metric = metric,
                Rounds = rounds,
                MedianRatios = medianRatios,
                P95Ratios = p95Ratios,
                MedianDeltaPercent = (medianRatios.Median - 1.0) * 100.0,
                P95DeltaPercent = (p95Ratios.Median - 1.0) * 100.0
            };
        }

        private static void ValidatePerformanceFingerprints(
            string workload,
            IEnumerable<RoundReports> rounds)
        {
            List<RawReport> reports = rounds.SelectMany(round =>
                new[] { round.A1, round.B1, round.B2, round.A2 }).ToList();
            string exact = reports[0].Performance.AggregateExactFingerprint;
            string quantized =
                reports[0].Performance.AggregateQuantizedFingerprint;
            Require(reports.All(report =>
                    report.Performance.AggregateExactFingerprint == exact
                    && report.Performance.AggregateQuantizedFingerprint
                        == quantized),
                workload + " performance fingerprint changed.");
        }

        private static GcTotals SumGc(
            IEnumerable<RoundReports> rounds,
            bool baseline)
        {
            IEnumerable<RawReport> reports = baseline
                ? rounds.SelectMany(round => new[] { round.A1, round.A2 })
                : rounds.SelectMany(round => new[] { round.B1, round.B2 });
            IEnumerable<IterationSample> samples = reports.SelectMany(
                report => report.Performance.Samples);
            return new GcTotals
            {
                Generation0 = samples.Sum(sample => (long)sample.Generation0CollectionsTotal),
                Generation1 = samples.Sum(sample => (long)sample.Generation1CollectionsTotal),
                Generation2 = samples.Sum(sample => (long)sample.Generation2CollectionsTotal)
            };
        }

        private static RatioSummary SummarizeRatios(
            IEnumerable<double> values)
        {
            double[] sorted = values.OrderBy(value => value).ToArray();
            Require(sorted.Length == RoundCount
                    && sorted.All(value => value > 0.0
                        && !double.IsNaN(value)
                        && !double.IsInfinity(value)),
                "Paired ratios are invalid.");
            double median = Median(sorted);
            double mad = Median(
                sorted.Select(value => Math.Abs(value - median))
                    .OrderBy(value => value)
                    .ToArray());
            return new RatioSummary
            {
                Count = sorted.Length,
                Minimum = sorted[0],
                Median = median,
                Maximum = sorted[sorted.Length - 1],
                MedianAbsoluteDeviation = mad,
                RelativeMadPercent = mad / median * 100.0
            };
        }

        private static double Median(IReadOnlyList<double> sorted)
        {
            int middle = sorted.Count / 2;
            return sorted.Count % 2 == 0
                ? (sorted[middle - 1] + sorted[middle]) / 2.0
                : sorted[middle];
        }

        private static double GeometricMean(double left, double right)
        {
            Require(left > 0.0 && right > 0.0,
                "A paired metric must be positive.");
            return Math.Sqrt(left * right);
        }

        private static void ValidateCommonReport(
            ComparisonContext context,
            RawReport report,
            string mode,
            string workload,
            string session,
            string targetCommit,
            bool performance)
        {
            Require(report.SchemaVersion == ReportSchema,
                "Raw report schema changed.");
            Require(report.Mode == mode
                    && report.Workload != null
                    && report.Workload.Id == workload
                    && report.Session == session
                    && string.Equals(
                        report.TargetCommit,
                        targetCommit,
                        StringComparison.OrdinalIgnoreCase),
                "Raw report identity changed for " + session + ".");
            ValidateWorkload(report.Workload, workload, performance);
            Require(report.Environment != null,
                session + " environment is missing.");
            Require(report.Environment.HarnessAssemblyConfiguration == "Release"
                    && report.Environment.TargetAssemblyConfiguration == "Release"
                    && report.Environment.ProcessArchitecture == "X64"
                    && !report.Environment.DebuggerAttached,
                session + " did not use a Release x64 no-debugger environment.");
            Require(GetSourceRevision(
                    report.Environment.HarnessAssemblyInformationalVersion)
                    == context.Input.HarnessCommit,
                session + " harness commit changed.");
            Require(report.Environment.TargetAssemblySourceRevision
                    == targetCommit
                    && GetSourceRevision(
                        report.Environment.TargetAssemblyInformationalVersion)
                        == targetCommit,
                session + " target provenance changed.");
            Require(!string.IsNullOrWhiteSpace(
                    report.Environment.HarnessAssemblySha256)
                    && !string.IsNullOrWhiteSpace(
                        report.Environment.TargetAssemblySha256),
                session + " assembly hash is missing.");
            context.AcceptEnvironment(report, targetCommit, performance);
            if (performance)
            {
                ValidatePerformance(report, session);
            }
        }

        private static void ValidateWorkload(
            WorkloadManifest workload,
            string id,
            bool performance)
        {
            int queryCount = id == "planar-direct-v1" ? 40960 : 12288;
            string inputHash = id == "planar-direct-v1"
                ? "1f5d56e45ca7b174ece2e573e07f0442e405430859839873b925810bcc1a2730"
                : "e56ce73c3e17565b7cfa5368ffb398d308759187a4c2883f94affb2f0284e58e";
            Require(workload.InputSha256 == inputHash
                    && workload.CellCountPerAxis == 64
                    && workload.CellSize == 3.0
                    && workload.TriangleCount == 8192
                    && workload.QueryCount == queryCount
                    && workload.LowerTolerance == -0.5
                    && workload.UpperTolerance == 0.5
                    && !workload.ProgressObserverEnabled,
                id + " fixed workload changed.");
            if (performance)
            {
                Require(workload.MaximumDisplaySamples == 0
                        && workload.WarmupCount == 3
                        && workload.MeasurementCount == 30
                        && workload.OperationsPerMeasurement == 10,
                    id + " performance protocol changed.");
            }
            else
            {
                Require(workload.MaximumDisplaySamples == queryCount
                        && workload.WarmupCount == 0
                        && workload.MeasurementCount == 0
                        && workload.OperationsPerMeasurement == 0,
                    id + " accuracy protocol changed.");
            }
        }

        private static void ValidatePerformance(
            RawReport report,
            string session)
        {
            PerformanceReport performance = report.Performance;
            Require(report.Accuracy == null && performance != null,
                session + " performance report shape is invalid.");
            Require(performance.SingleSessionNoisePassed
                    && performance.TimingNoiseThresholdPercent == 5.0,
                session + " single-session noise gate failed.");
            Require(performance.Samples != null
                    && performance.Samples.Count == 30,
                session + " measured sample count changed.");
            foreach (MetricSummary metric in new[]
            {
                performance.WallMillisecondsPerOperation,
                performance.IndexMillisecondsPerOperation,
                performance.CalculationMillisecondsPerOperation
            })
            {
                Require(metric != null
                        && metric.Median > 0.0
                        && metric.P95 > 0.0
                        && metric.RelativeMadPercent < 5.0,
                    session + " timing metric is invalid.");
            }

            Require(performance.AllocatedBytesPerOperation != null
                    && performance.AllocatedBytesPerOperation.Median > 0.0
                    && performance.AllocatedBytesPerOperation.P95 > 0.0,
                session + " allocation metric is invalid.");
        }

        private static string GetSourceRevision(Assembly assembly)
        {
            AssemblyInformationalVersionAttribute attribute =
                assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            Require(attribute != null,
                "Comparer informational version is missing.");
            return GetSourceRevision(attribute.InformationalVersion);
        }

        private static string GetSourceRevision(string informationalVersion)
        {
            Require(!string.IsNullOrWhiteSpace(informationalVersion),
                "Informational version is missing.");
            int separator = informationalVersion.LastIndexOf('+');
            Require(separator >= 0
                    && informationalVersion.Length - separator - 1 == 40,
                "Informational version has no 40-character source revision.");
            string revision = informationalVersion.Substring(separator + 1);
            RequireCommit(revision, "source revision");
            return revision.ToLowerInvariant();
        }

        private static void RequireCommit(string value, string label)
        {
            Require(value != null
                    && value.Length == 40
                    && value.All(character =>
                        character >= '0' && character <= '9'
                        || character >= 'a' && character <= 'f'
                        || character >= 'A' && character <= 'F'),
                "Invalid " + label + ".");
        }

        private static T ReadJson<T>(string path)
        {
            return JsonSerializer.Deserialize<T>(
                File.ReadAllText(path),
                JsonOptions);
        }

        private static void WriteJson<T>(string path, T value)
        {
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(
                fullPath,
                JsonSerializer.Serialize(value, JsonOptions));
        }

        private static Dictionary<string, string> ParseOptions(string[] args)
        {
            Require(args.Length % 2 == 0,
                "Paired arguments must be name/value pairs.");
            Dictionary<string, string> values =
                new Dictionary<string, string>(StringComparer.Ordinal);
            for (int index = 0; index < args.Length; index += 2)
            {
                Require(args[index].StartsWith("--", StringComparison.Ordinal)
                        && !values.ContainsKey(args[index]),
                    "Invalid paired argument.");
                values.Add(args[index], args[index + 1]);
            }

            return values;
        }

        private static string Required(
            IReadOnlyDictionary<string, string> values,
            string name)
        {
            string value;
            Require(values.TryGetValue(name, out value)
                    && !string.IsNullOrWhiteSpace(value),
                "Missing " + name + ".");
            return value;
        }

        private static string Format(double value)
        {
            return value.ToString("0.############", CultureInfo.InvariantCulture);
        }

        private static bool IsWithinRoot(string root, string path)
        {
            Require(!string.IsNullOrEmpty(root),
                "Attempt root is missing.");
            string relative = Path.GetRelativePath(
                Path.GetFullPath(root),
                Path.GetFullPath(path));
            return relative != ".."
                && !relative.StartsWith(
                    ".." + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal)
                && !Path.IsPathRooted(relative);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void ExpectFailure(
            string root,
            string name,
            FixtureOptions options)
        {
            try
            {
                Compare(CreateFixture(Path.Combine(root, name), options));
            }
            catch (InvalidOperationException)
            {
                return;
            }

            throw new InvalidOperationException(
                name + " negative fixture unexpectedly passed.");
        }

        private static string CreateFixture(
            string root,
            FixtureOptions options)
        {
            Directory.CreateDirectory(root);
            const string baseline =
                "c74b3bb5bf2f237eef800e50ef6951109bf07cc5";
            const string current =
                "3f6e35beb951b8412e6fcd116c959f0a5c4d9a99";
            const string harness =
                "3f6e35beb951b8412e6fcd116c959f0a5c4d9a99";
            DateTimeOffset created = new DateTimeOffset(
                2026,
                8,
                24,
                0,
                0,
                0,
                TimeSpan.Zero);
            AccuracyInput accuracy = new AccuracyInput();
            accuracy.DirectBaseline = WriteFixtureReport(
                root,
                CreateAccuracyFixture(
                    "planar-direct-v1",
                    "A-accuracy",
                    baseline,
                    harness,
                    created = created.AddSeconds(1),
                    false));
            accuracy.DirectCurrent = WriteFixtureReport(
                root,
                CreateAccuracyFixture(
                    "planar-direct-v1",
                    "B-accuracy",
                    current,
                    harness,
                    created = created.AddSeconds(1),
                    false));
            accuracy.BoundaryBaseline = WriteFixtureReport(
                root,
                CreateAccuracyFixture(
                    "planar-boundary-v1",
                    "A-accuracy",
                    baseline,
                    harness,
                    created = created.AddSeconds(1),
                    false));
            accuracy.BoundaryCurrent = WriteFixtureReport(
                root,
                CreateAccuracyFixture(
                    "planar-boundary-v1",
                    "B-accuracy",
                    current,
                    harness,
                    created = created.AddSeconds(1),
                    options.FingerprintMismatch));

            List<PairedWorkloadInput> workloads =
                new List<PairedWorkloadInput>();
            foreach (string workloadId in new[]
            {
                "planar-direct-v1",
                "planar-boundary-v1"
            })
            {
                PairedWorkloadInput workload = new PairedWorkloadInput
                {
                    Id = workloadId,
                    Rounds = new List<PairedRoundInput>()
                };
                for (int roundIndex = 0; roundIndex < RoundCount; roundIndex++)
                {
                    int ordinal = roundIndex + 1;
                    string prefix = "R" + ordinal.ToString(
                        "00",
                        CultureInfo.InvariantCulture) + "-";
                    double wallMedianRatio = options.WallMedianRatios == null
                        ? 0.90 + roundIndex * 0.001
                        : options.WallMedianRatios[roundIndex];
                    double wallP95Ratio = options.WallP95Ratios == null
                        ? 0.92 + roundIndex * 0.001
                        : options.WallP95Ratios[roundIndex];
                    PairedRoundInput round = new PairedRoundInput
                    {
                        Ordinal = ordinal
                    };
                    round.A1Readiness = WriteFixtureReadiness(
                        root,
                        workloadId,
                        prefix + "A1",
                        created = created.AddSeconds(1),
                        options.ReadinessFailure
                            && workloadId == "planar-direct-v1"
                            && ordinal == 1);
                    round.A1 = WriteFixtureReport(
                        root,
                        CreatePerformanceFixture(
                            workloadId,
                            prefix + "A1",
                            baseline,
                            harness,
                            created = created.AddSeconds(1),
                            1.0,
                            1.0,
                            options.SingleSessionNoiseFailure
                                && workloadId == "planar-direct-v1"
                                && ordinal == 1));
                    round.B1Readiness = WriteFixtureReadiness(
                        root,
                        workloadId,
                        prefix + "B1",
                        created = created.AddSeconds(1),
                        false);
                    DateTimeOffset b1Created = created.AddSeconds(1);
                    if (options.WrongTimestamp
                        && workloadId == "planar-direct-v1"
                        && ordinal == 1)
                    {
                        b1Created = created.AddSeconds(-1);
                    }
                    created = created.AddSeconds(1);
                    round.B1 = WriteFixtureReport(
                        root,
                        CreatePerformanceFixture(
                            workloadId,
                            prefix + "B1",
                            options.CommitMismatch
                                && workloadId == "planar-direct-v1"
                                && ordinal == 1
                                    ? baseline
                                    : current,
                            harness,
                            b1Created,
                            wallMedianRatio,
                            wallP95Ratio,
                            false));
                    round.B2Readiness = WriteFixtureReadiness(
                        root,
                        workloadId,
                        prefix + "B2",
                        created = created.AddSeconds(1),
                        false);
                    round.B2 = WriteFixtureReport(
                        root,
                        CreatePerformanceFixture(
                            workloadId,
                            prefix + "B2",
                            current,
                            harness,
                            created = created.AddSeconds(1),
                            wallMedianRatio,
                            wallP95Ratio,
                            false));
                    round.A2Readiness = WriteFixtureReadiness(
                        root,
                        workloadId,
                        prefix + "A2",
                        created = created.AddSeconds(1),
                        false);
                    round.A2 = WriteFixtureReport(
                        root,
                        CreatePerformanceFixture(
                            workloadId,
                            prefix + "A2",
                            baseline,
                            harness,
                            created = created.AddSeconds(1),
                            1.0,
                            1.0,
                            false));
                    workload.Rounds.Add(round);
                }

                workloads.Add(workload);
            }

            if (options.RemoveRound)
            {
                workloads[0].Rounds.RemoveAt(workloads[0].Rounds.Count - 1);
            }

            if (options.CrossRootPath)
            {
                workloads[0].Rounds[0].A1 = "..\\outside.json";
            }

            PairedInput input = new PairedInput
            {
                SchemaVersion = InputSchema,
                BaselineCommit = baseline,
                CurrentCommit = current,
                HarnessCommit = harness,
                ComparerCommit = GetSourceRevision(
                    Assembly.GetExecutingAssembly()),
                Accuracy = accuracy,
                Workloads = workloads
            };
            string manifestPath = Path.Combine(root, "paired-input.json");
            WriteJson(manifestPath, input);
            return manifestPath;
        }

        private static RawReport CreateAccuracyFixture(
            string workload,
            string session,
            string targetCommit,
            string harnessCommit,
            DateTimeOffset created,
            bool fingerprintMismatch)
        {
            RawReport report = CreateCommonFixture(
                workload,
                "accuracy",
                session,
                targetCommit,
                harnessCommit,
                created,
                false);
            string suffix = fingerprintMismatch ? "-changed" : string.Empty;
            report.Accuracy = new AccuracyReport
            {
                OraclePassed = true,
                PerTargetOracleTolerance = 0.5e-12,
                CrossTargetParityTolerance = 1e-12,
                MaximumAbsoluteOracleError = 1e-15,
                RepeatedResultExactFingerprintMatch = true,
                PointExactFingerprint = workload + "-point-exact" + suffix,
                PointQuantizedFingerprint = workload + "-point-quantized" + suffix,
                ResultExactFingerprint = workload + "-result-exact" + suffix,
                ResultQuantizedFingerprint = workload + "-result-quantized" + suffix
            };
            return report;
        }

        private static RawReport CreatePerformanceFixture(
            string workload,
            string session,
            string targetCommit,
            string harnessCommit,
            DateTimeOffset created,
            double wallMedianRatio,
            double wallP95Ratio,
            bool noiseFailure)
        {
            RawReport report = CreateCommonFixture(
                workload,
                "performance",
                session,
                targetCommit,
                harnessCommit,
                created,
                true);
            bool current = session.EndsWith("B1", StringComparison.Ordinal)
                || session.EndsWith("B2", StringComparison.Ordinal);
            report.Performance = new PerformanceReport
            {
                AggregateExactFingerprint = workload + "-performance-exact",
                AggregateQuantizedFingerprint =
                    workload + "-performance-quantized",
                TimingNoiseThresholdPercent = 5.0,
                SingleSessionNoisePassed = !noiseFailure,
                Samples = Enumerable.Range(1, 30)
                    .Select(index => new IterationSample
                    {
                        Generation0CollectionsTotal = 1,
                        Generation1CollectionsTotal = index % 2,
                        Generation2CollectionsTotal = 0
                    }).ToList(),
                WallMillisecondsPerOperation = Metric(
                    100.0 * (current ? wallMedianRatio : 1.0),
                    110.0 * (current ? wallP95Ratio : 1.0),
                    noiseFailure ? 5.0 : 1.0),
                IndexMillisecondsPerOperation = Metric(
                    30.0 * (current ? 0.98 : 1.0),
                    34.0 * (current ? 0.99 : 1.0),
                    1.0),
                CalculationMillisecondsPerOperation = Metric(
                    70.0 * (current ? 0.88 : 1.0),
                    76.0 * (current ? 0.90 : 1.0),
                    1.0),
                AllocatedBytesPerOperation = Metric(
                    8000000.0 * (current ? 1.02 : 1.0),
                    8001000.0 * (current ? 1.02 : 1.0),
                    0.01)
            };
            return report;
        }

        private static RawReport CreateCommonFixture(
            string workload,
            string mode,
            string session,
            string targetCommit,
            string harnessCommit,
            DateTimeOffset created,
            bool performance)
        {
            bool direct = workload == "planar-direct-v1";
            return new RawReport
            {
                SchemaVersion = ReportSchema,
                CreatedUtc = created,
                Mode = mode,
                Session = session,
                TargetCommit = targetCommit,
                Workload = new WorkloadManifest
                {
                    Id = workload,
                    InputSha256 = direct
                        ? "1f5d56e45ca7b174ece2e573e07f0442e405430859839873b925810bcc1a2730"
                        : "e56ce73c3e17565b7cfa5368ffb398d308759187a4c2883f94affb2f0284e58e",
                    CellCountPerAxis = 64,
                    CellSize = 3.0,
                    TriangleCount = 8192,
                    QueryCount = direct ? 40960 : 12288,
                    LowerTolerance = -0.5,
                    UpperTolerance = 0.5,
                    MaximumDisplaySamples = performance
                        ? 0
                        : direct ? 40960 : 12288,
                    ProgressObserverEnabled = false,
                    WarmupCount = performance ? 3 : 0,
                    MeasurementCount = performance ? 30 : 0,
                    OperationsPerMeasurement = performance ? 10 : 0
                },
                Environment = new EnvironmentManifest
                {
                    FrameworkDescription = ".NET 8.0.30",
                    RuntimeVersion = "8.0.30",
                    OsDescription = "Microsoft Windows 10.0.26100",
                    OsArchitecture = "X64",
                    ProcessArchitecture = "X64",
                    ProcessorCount = 12,
                    IsServerGc = false,
                    GcLatencyMode = "Interactive",
                    DebuggerAttached = false,
                    StopwatchFrequency = 10000000,
                    ProcessPriorityClass = performance ? "High" : "Normal",
                    ProcessorAffinityMask = "0xfff",
                    HarnessAssemblyConfiguration = "Release",
                    HarnessAssemblyInformationalVersion = "3.0.0+" + harnessCommit,
                    HarnessAssemblySha256 = "harness-hash",
                    TargetAssemblyConfiguration = "Release",
                    TargetAssemblyInformationalVersion = "3.0.0+" + targetCommit,
                    TargetAssemblySourceRevision = targetCommit,
                    TargetAssemblySha256 = targetCommit.StartsWith("c74", StringComparison.Ordinal)
                        ? "baseline-hash"
                        : "current-hash"
                }
            };
        }

        private static MetricSummary Metric(
            double median,
            double p95,
            double relativeMadPercent)
        {
            return new MetricSummary
            {
                Minimum = median * 0.98,
                Median = median,
                P95 = p95,
                Maximum = p95 * 1.02,
                MedianAbsoluteDeviation =
                    median * relativeMadPercent / 100.0,
                RelativeMadPercent = relativeMadPercent
            };
        }

        private static string WriteFixtureReport(
            string root,
            RawReport report)
        {
            string relative = report.Workload.Id
                + "-"
                + report.Session
                + ".json";
            WriteJson(Path.Combine(root, relative), report);
            return relative;
        }

        private static string WriteFixtureReadiness(
            string root,
            string workload,
            string session,
            DateTimeOffset created,
            bool failed)
        {
            string relative = workload + "-" + session + "-readiness.json";
            WriteJson(
                Path.Combine(root, relative),
                new ReadinessEvidence
                {
                    SchemaVersion = "openvisionlab-paired-readiness-v2",
                    Stage = workload + "|" + session,
                    CapturedUtc = created,
                    CpuThresholdPercent = 20.0,
                    CpuSampleCount = 6,
                    BlockingWorkloadsBefore = new List<JsonElement>(),
                    BlockingWorkloadsAfter = new List<JsonElement>(),
                    CpuSamples = Enumerable.Range(1, 6)
                        .Select(index => new CpuSample
                        {
                            Ordinal = index,
                            MaximumLoadPercentage = failed && index == 6
                                ? 21.0
                                : 10.0
                        }).ToList(),
                    Passed = !failed
                });
            return relative;
        }

        private sealed class ComparisonContext
        {
            private readonly string root;
            private readonly HashSet<string> paths =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private DateTimeOffset? lastCreatedUtc;
            private string harnessHash;
            private string baselineTargetHash;
            private string currentTargetHash;
            private string performanceEnvironment;
            private string performanceAffinity;

            internal ComparisonContext(string root, PairedInput input)
            {
                this.root = Path.GetFullPath(root).TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
                Input = input;
            }

            internal PairedInput Input { get; }

            internal RawReport ReadReport(
                string relativePath,
                string mode,
                string workload,
                string session,
                string targetCommit,
                bool performance)
            {
                string fullPath = ResolveEvidencePath(relativePath);
                RawReport report = ReadJson<RawReport>(fullPath);
                ValidateCommonReport(
                    this,
                    report,
                    mode,
                    workload,
                    session,
                    targetCommit,
                    performance);
                AcceptTimestamp(report.CreatedUtc);
                return report;
            }

            internal void ReadReadiness(
                string relativePath,
                string workload,
                string session)
            {
                string fullPath = ResolveEvidencePath(relativePath);
                ReadinessEvidence readiness =
                    ReadJson<ReadinessEvidence>(fullPath);
                Require(readiness != null
                        && readiness.SchemaVersion
                            == "openvisionlab-paired-readiness-v2"
                        && readiness.Stage == workload + "|" + session
                        && readiness.CpuThresholdPercent == 20.0
                        && readiness.CpuSampleCount == 6
                        && readiness.Passed,
                    session + " readiness evidence is invalid.");
                Require(readiness.BlockingWorkloadsBefore != null
                        && readiness.BlockingWorkloadsBefore.Count == 0
                        && readiness.BlockingWorkloadsAfter != null
                        && readiness.BlockingWorkloadsAfter.Count == 0,
                    session + " readiness found a competing workload.");
                Require(readiness.CpuSamples != null
                        && readiness.CpuSamples.Count == 6,
                    session + " readiness CPU sample count changed.");
                for (int index = 0; index < readiness.CpuSamples.Count; index++)
                {
                    Require(readiness.CpuSamples[index].Ordinal == index + 1
                            && readiness.CpuSamples[index]
                                .MaximumLoadPercentage <= 20.0,
                        session + " readiness CPU gate failed.");
                }

                AcceptTimestamp(readiness.CapturedUtc);
            }

            private string ResolveEvidencePath(string relativePath)
            {
                Require(!string.IsNullOrWhiteSpace(relativePath)
                        && !Path.IsPathRooted(relativePath),
                    "Evidence path must be relative to the attempt root.");
                string fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
                Require(IsWithinRoot(root, fullPath),
                    "Evidence path escaped the attempt root.");
                Require(paths.Add(fullPath),
                    "An evidence path was reused.");
                Require(File.Exists(fullPath),
                    "Evidence is missing: " + relativePath);
                return fullPath;
            }

            private void AcceptTimestamp(DateTimeOffset createdUtc)
            {
                Require(!lastCreatedUtc.HasValue
                        || createdUtc > lastCreatedUtc.Value,
                    "Evidence is not in strict manifest time order.");
                lastCreatedUtc = createdUtc;
            }

            internal void AcceptEnvironment(
                RawReport report,
                string targetCommit,
                bool performance)
            {
                string reportHarnessHash =
                    report.Environment.HarnessAssemblySha256;
                if (harnessHash == null)
                {
                    harnessHash = reportHarnessHash;
                }
                Require(harnessHash == reportHarnessHash,
                    "Harness assembly hash changed across reports.");

                bool baseline = targetCommit == Input.BaselineCommit;
                string targetHash = report.Environment.TargetAssemblySha256;
                if (baseline)
                {
                    if (baselineTargetHash == null)
                    {
                        baselineTargetHash = targetHash;
                    }
                    Require(baselineTargetHash == targetHash,
                        "Baseline target assembly hash changed.");
                }
                else
                {
                    if (currentTargetHash == null)
                    {
                        currentTargetHash = targetHash;
                    }
                    Require(currentTargetHash == targetHash,
                        "Current target assembly hash changed.");
                }

                if (!performance)
                {
                    return;
                }

                Require(report.Environment.ProcessPriorityClass == "High",
                    "Performance process priority changed.");
                string environment = string.Join(
                    "|",
                    report.Environment.FrameworkDescription,
                    report.Environment.RuntimeVersion,
                    report.Environment.OsDescription,
                    report.Environment.OsArchitecture,
                    report.Environment.ProcessArchitecture,
                    report.Environment.ProcessorCount.ToString(
                        CultureInfo.InvariantCulture),
                    report.Environment.IsServerGc.ToString(),
                    report.Environment.GcLatencyMode,
                    report.Environment.StopwatchFrequency.ToString(
                        CultureInfo.InvariantCulture));
                if (performanceEnvironment == null)
                {
                    performanceEnvironment = environment;
                    performanceAffinity =
                        report.Environment.ProcessorAffinityMask;
                }
                Require(performanceEnvironment == environment
                        && performanceAffinity
                            == report.Environment.ProcessorAffinityMask,
                    "Performance environment or affinity changed.");
            }
        }

        private sealed class FixtureOptions
        {
            internal bool WrongTimestamp { get; set; }
            internal bool RemoveRound { get; set; }
            internal bool CrossRootPath { get; set; }
            internal bool CommitMismatch { get; set; }
            internal bool FingerprintMismatch { get; set; }
            internal bool SingleSessionNoiseFailure { get; set; }
            internal bool ReadinessFailure { get; set; }
            internal double[] WallMedianRatios { get; set; }
            internal double[] WallP95Ratios { get; set; }
        }

        private sealed class RoundReports
        {
            internal int Ordinal { get; set; }
            internal RawReport A1 { get; set; }
            internal RawReport B1 { get; set; }
            internal RawReport B2 { get; set; }
            internal RawReport A2 { get; set; }
        }

        public sealed class PairedInput
        {
            public string SchemaVersion { get; set; }
            public string BaselineCommit { get; set; }
            public string CurrentCommit { get; set; }
            public string HarnessCommit { get; set; }
            public string ComparerCommit { get; set; }
            public AccuracyInput Accuracy { get; set; }
            public List<PairedWorkloadInput> Workloads { get; set; }
        }

        public sealed class AccuracyInput
        {
            public string DirectBaseline { get; set; }
            public string DirectCurrent { get; set; }
            public string BoundaryBaseline { get; set; }
            public string BoundaryCurrent { get; set; }
        }

        public sealed class PairedWorkloadInput
        {
            public string Id { get; set; }
            public List<PairedRoundInput> Rounds { get; set; }
        }

        public sealed class PairedRoundInput
        {
            public int Ordinal { get; set; }
            public string A1Readiness { get; set; }
            public string A1 { get; set; }
            public string B1Readiness { get; set; }
            public string B1 { get; set; }
            public string B2Readiness { get; set; }
            public string B2 { get; set; }
            public string A2Readiness { get; set; }
            public string A2 { get; set; }
        }

        public sealed class ReadinessEvidence
        {
            public string SchemaVersion { get; set; }
            public string Stage { get; set; }
            public DateTimeOffset CapturedUtc { get; set; }
            public double CpuThresholdPercent { get; set; }
            public int CpuSampleCount { get; set; }
            public List<JsonElement> BlockingWorkloadsBefore { get; set; }
            public List<JsonElement> BlockingWorkloadsAfter { get; set; }
            public List<CpuSample> CpuSamples { get; set; }
            public bool Passed { get; set; }
        }

        public sealed class CpuSample
        {
            public int Ordinal { get; set; }
            public double MaximumLoadPercentage { get; set; }
        }

        public sealed class PairedComparison
        {
            public string SchemaVersion { get; set; }
            public DateTimeOffset CreatedUtc { get; set; }
            public string ComparerCommit { get; set; }
            public string BaselineCommit { get; set; }
            public string CurrentCommit { get; set; }
            public string HarnessCommit { get; set; }
            public bool AccuracyParityPassed { get; set; }
            public List<AccuracyDiagnostic> AccuracyDiagnostics { get; set; }
            public List<WorkloadComparison> Workloads { get; set; }
            public List<string> Failures { get; set; }
            public List<string> InvestigationAlerts { get; set; }
            public string Status { get; set; }
        }

        public sealed class AccuracyDiagnostic
        {
            public string Workload { get; set; }
            public double CombinedMaximumAbsoluteOracleError { get; set; }
            public string PointExactFingerprint { get; set; }
            public string PointQuantizedFingerprint { get; set; }
            public string ResultExactFingerprint { get; set; }
            public string ResultQuantizedFingerprint { get; set; }
        }

        public sealed class WorkloadComparison
        {
            public string Workload { get; set; }
            public string InputSha256 { get; set; }
            public int RoundCount { get; set; }
            public List<PairedMetricComparison> Metrics { get; set; }
            public GcTotals BaselineGcTotals { get; set; }
            public GcTotals CurrentGcTotals { get; set; }
        }

        public sealed class PairedMetricComparison
        {
            public string Metric { get; set; }
            public List<PairedRoundMetric> Rounds { get; set; }
            public RatioSummary MedianRatios { get; set; }
            public RatioSummary P95Ratios { get; set; }
            public double MedianDeltaPercent { get; set; }
            public double P95DeltaPercent { get; set; }
        }

        public sealed class PairedRoundMetric
        {
            public int Ordinal { get; set; }
            public double BaselineMedian { get; set; }
            public double CurrentMedian { get; set; }
            public double MedianRatio { get; set; }
            public double BaselineP95 { get; set; }
            public double CurrentP95 { get; set; }
            public double P95Ratio { get; set; }
        }

        public sealed class RatioSummary
        {
            public int Count { get; set; }
            public double Minimum { get; set; }
            public double Median { get; set; }
            public double Maximum { get; set; }
            public double MedianAbsoluteDeviation { get; set; }
            public double RelativeMadPercent { get; set; }
        }

        public sealed class GcTotals
        {
            public long Generation0 { get; set; }
            public long Generation1 { get; set; }
            public long Generation2 { get; set; }
        }

        public sealed class RawReport
        {
            public string SchemaVersion { get; set; }
            public DateTimeOffset CreatedUtc { get; set; }
            public string Mode { get; set; }
            public string Session { get; set; }
            public string TargetCommit { get; set; }
            public WorkloadManifest Workload { get; set; }
            public EnvironmentManifest Environment { get; set; }
            public AccuracyReport Accuracy { get; set; }
            public PerformanceReport Performance { get; set; }
        }

        public sealed class WorkloadManifest
        {
            public string Id { get; set; }
            public string InputSha256 { get; set; }
            public int CellCountPerAxis { get; set; }
            public double CellSize { get; set; }
            public int TriangleCount { get; set; }
            public int QueryCount { get; set; }
            public double LowerTolerance { get; set; }
            public double UpperTolerance { get; set; }
            public int MaximumDisplaySamples { get; set; }
            public bool ProgressObserverEnabled { get; set; }
            public int WarmupCount { get; set; }
            public int MeasurementCount { get; set; }
            public int OperationsPerMeasurement { get; set; }
        }

        public sealed class EnvironmentManifest
        {
            public string FrameworkDescription { get; set; }
            public string RuntimeVersion { get; set; }
            public string OsDescription { get; set; }
            public string OsArchitecture { get; set; }
            public string ProcessArchitecture { get; set; }
            public int ProcessorCount { get; set; }
            public bool IsServerGc { get; set; }
            public string GcLatencyMode { get; set; }
            public bool DebuggerAttached { get; set; }
            public long StopwatchFrequency { get; set; }
            public string ProcessPriorityClass { get; set; }
            public string ProcessorAffinityMask { get; set; }
            public string HarnessAssemblyConfiguration { get; set; }
            public string HarnessAssemblyInformationalVersion { get; set; }
            public string HarnessAssemblySha256 { get; set; }
            public string TargetAssemblyConfiguration { get; set; }
            public string TargetAssemblyInformationalVersion { get; set; }
            public string TargetAssemblySourceRevision { get; set; }
            public string TargetAssemblySha256 { get; set; }
        }

        public sealed class AccuracyReport
        {
            public bool OraclePassed { get; set; }
            public double PerTargetOracleTolerance { get; set; }
            public double CrossTargetParityTolerance { get; set; }
            public double MaximumAbsoluteOracleError { get; set; }
            public bool RepeatedResultExactFingerprintMatch { get; set; }
            public string PointExactFingerprint { get; set; }
            public string PointQuantizedFingerprint { get; set; }
            public string ResultExactFingerprint { get; set; }
            public string ResultQuantizedFingerprint { get; set; }
        }

        public sealed class PerformanceReport
        {
            public string AggregateExactFingerprint { get; set; }
            public string AggregateQuantizedFingerprint { get; set; }
            public List<IterationSample> Samples { get; set; }
            public double TimingNoiseThresholdPercent { get; set; }
            public bool SingleSessionNoisePassed { get; set; }
            public MetricSummary WallMillisecondsPerOperation { get; set; }
            public MetricSummary IndexMillisecondsPerOperation { get; set; }
            public MetricSummary CalculationMillisecondsPerOperation { get; set; }
            public MetricSummary AllocatedBytesPerOperation { get; set; }
        }

        public sealed class IterationSample
        {
            public int Generation0CollectionsTotal { get; set; }
            public int Generation1CollectionsTotal { get; set; }
            public int Generation2CollectionsTotal { get; set; }
        }

        public sealed class MetricSummary
        {
            public double Minimum { get; set; }
            public double Median { get; set; }
            public double P95 { get; set; }
            public double Maximum { get; set; }
            public double MedianAbsoluteDeviation { get; set; }
            public double RelativeMadPercent { get; set; }
        }
    }
}

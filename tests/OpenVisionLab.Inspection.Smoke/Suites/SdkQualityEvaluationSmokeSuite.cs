using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using OpenCvSharp;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Tool;
using OpenVisionLab.Vision3D.FeatureExtraction;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class SdkQualityEvaluationSmokeSuite
    {
        private static readonly int[] EvaluationCounts = { 100, 120, 160, 200 };
        private static readonly double[] EvaluationContamination = { 0.2, 0.3, 0.4, 0.45 };
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };
        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase("SDK quality workload preserves dense full observations through new option combinations", TestDenseWorkload);
            yield return new SmokeCase("SDK quality workload releases native sources results and alternating learned models", TestNativeLifetime);
        }

        internal static int RunEvaluation(string reportPath)
        {
            string path = Path.GetFullPath(reportPath);
            Require(path.StartsWith(@"D:\", StringComparison.OrdinalIgnoreCase), "Quality evaluation evidence must be physically stored on D:.");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var inputs = (from count in EvaluationCounts
                          from contamination in EvaluationContamination
                          let seed = 9101 + count * 7 + (int)(contamination * 100)
                          select new { Count = count, Fraction = contamination, Seed = seed,
                              Input = SurfaceQualitySmokeSuite.Contamination(count, seed, contamination) }).ToArray();
            string[] policies = { "fraction-0.8-coupled", "fraction-0.6-coupled", "fraction-0.6-independent-4" };
            var identities = new[] { typeof(SurfacePoseRefinementOptions).Assembly, typeof(LineGaugeTool).Assembly, typeof(Program).Assembly }
                .Select(assembly => new { assembly.GetName().Name, Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))) }).ToArray();
            var plan = new
            {
                Scope = "Frozen synthetic conditions, not sensor qualification. Zero-noise rigid truth uses translation norm and maximum rotation-coefficient numerical tolerances 1e-8; contaminated original observations are retained.",
                Source = "6f2ca60 + PL-0036 working changes", Policies = policies, Repeats = 3, Binaries = identities,
                Inputs = inputs.Select(item => new { item.Count, item.Fraction, item.Seed, item.Input.OutlierCount, Hash = InputHash(item.Input),
                    Truth = new[] { item.Input.Truth.TranslationX, item.Input.Truth.TranslationY, item.Input.Truth.TranslationZ },
                    TruthRotation = new[] { item.Input.Truth.M11, item.Input.Truth.M12, item.Input.Truth.M13, item.Input.Truth.M21, item.Input.Truth.M22,
                        item.Input.Truth.M23, item.Input.Truth.M31, item.Input.Truth.M32, item.Input.Truth.M33 } }).ToArray()
            };
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(path), "evaluation-plan.json"), JsonSerializer.Serialize(plan, JsonOptions));
            var rows = new List<ContaminationRow>();
            foreach (var item in inputs)
            foreach (string policy in policies)
            for (int repeat = 0; repeat < 3; repeat++)
            {
                var options = SurfaceQualitySmokeSuite.Options((int)Math.Ceiling(item.Count * 0.65));
                options.RefinementOptions.RetainedCorrespondenceFraction = policy == policies[0] ? 0.8 : 0.6;
                if (policy == policies[2]) options.RefinementOptions.MinimumRetainedCorrespondenceCount = 4;
                long before = GC.GetAllocatedBytesForCurrentThread(); var timer = Stopwatch.StartNew();
                var result = new DeterministicSurfacePoseRefinementTool().Execute(item.Input.Model, item.Input.Scene, item.Input.Seed, options);
                timer.Stop();
                rows.Add(new ContaminationRow
                {
                    CaseId = item.Count + "/" + item.Fraction.ToString(System.Globalization.CultureInfo.InvariantCulture), Policy = policy, Trial = repeat,
                    OriginalSampleCount = item.Count, OutlierFraction = item.Fraction, InputHash = InputHash(item.Input), Success = result.Success,
                    Error = result.Success ? SurfaceQualitySmokeSuite.PoseError(result.Pose, item.Input.Truth) : null,
                    RotationCoefficientError = result.Success ? SurfaceQualitySmokeSuite.RotationCoefficientError(result.Pose, item.Input.Truth) : null,
                    RetainedCount = result.RetainedCorrespondenceCount, FullSupport = result.Coverage?.MatchedModelSampleCount ?? 0,
                    FullRmse = result.Coverage?.HasInlierRmse == true ? result.Coverage.InlierRmse : null,
                    RawIdsCorrect = result.Coverage?.Matches.All(match => match.ModelSampleOrder == match.SceneSampleOrder) == true,
                    Fingerprint = result.Success ? JsonSerializer.Serialize(new[] { result.Pose.TranslationX, result.Pose.TranslationY, result.Pose.TranslationZ,
                        result.Pose.M11, result.Pose.M12, result.Pose.M13, result.Pose.M21, result.Pose.M22, result.Pose.M23, result.Pose.M31, result.Pose.M32, result.Pose.M33,
                        result.Coverage.InlierRmse, result.RetainedCorrespondenceCount }) : result.Message,
                    Milliseconds = timer.Elapsed.TotalMilliseconds, CallerManagedBytes = GC.GetAllocatedBytesForCurrentThread() - before
                });
            }
            var work = new List<object>();
            foreach (int sceneCount in new[] { 256, 1024, 4096 })
            for (int repeat = 0; repeat < 5; repeat++)
            {
                long before = GC.GetTotalAllocatedBytes(true); var timer = Stopwatch.StartNew();
                var outcome = DenseWorkload(sceneCount);
                timer.Stop();
                work.Add(new { SceneCount = sceneCount, Trial = repeat, Phase = repeat == 0 ? "first-call" : "warm",
                    Milliseconds = timer.Elapsed.TotalMilliseconds, TotalManagedBytes = GC.GetTotalAllocatedBytes(true) - before,
                    Environment.WorkingSet, outcome.MatchedModelSampleCount, outcome.Candidates, outcome.RefinedCandidates, outcome.PreparedSamples });
            }
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            long heapBefore = GC.GetTotalMemory(true), workingBefore = Environment.WorkingSet;
            var weak = new List<WeakReference>();
            for (int i = 0; i < 32; i++) weak.AddRange(NativeCycle(i));
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            long heapAfter = GC.GetTotalMemory(true), workingAfter = Environment.WorkingSet;
            int alive = weak.Count(reference => reference.IsAlive);
            bool stable = rows.GroupBy(row => row.CaseId + "/" + row.Policy).All(group => group.Select(row => row.Fingerprint).Distinct().Count() == 1);
            bool accuracy = rows.Where(row => row.Policy == policies[2] && row.OutlierFraction <= 0.4).All(row => row.Success && row.Error <= 1e-8 && row.RotationCoefficientError <= 1e-8 && row.RawIdsCorrect && row.FullSupport == row.OriginalSampleCount);
            bool inputsStable = inputs.All(item => rows.Where(row => row.CaseId == item.Count + "/" + item.Fraction.ToString(System.Globalization.CultureInfo.InvariantCulture)).All(row => row.InputHash == InputHash(item.Input)));
            var report = new
            {
                Scope = plan.Scope, plan.Source, Binaries = identities, Runtime = RuntimeInformation.FrameworkDescription,
                OperatingSystem = RuntimeInformation.OSDescription, Architecture = RuntimeInformation.ProcessArchitecture.ToString(), Environment.ProcessorCount,
                Inputs = inputs.Length, Trials = rows.Count, RepeatedResultsConsistent = stable, InputsPreserved = inputsStable,
                SupportedAnalyticCasesPassed = accuracy,
                Summary = rows.GroupBy(row => row.Policy).Select(group => new { Policy = group.Key, Trials = group.Count(), ExecutionFailures = group.Count(row => !row.Success),
                    TruthViolations = group.Count(row => row.Error > 1e-8 || row.RotationCoefficientError > 1e-8 || !row.Success || !row.RawIdsCorrect || row.FullSupport != row.OriginalSampleCount), MaximumTranslationError = group.Max(row => row.Error), MaximumRotationCoefficientError = group.Max(row => row.RotationCoefficientError) }).ToArray(),
                Results = rows, DenseWorkloads = work,
                NativeLifecycle = new { Iterations = weak.Count / 2, ManagedToolOwners = weak.Count, AliveManagedToolOwnersAfterCollection = alive, HeapBefore = heapBefore, HeapAfter = heapAfter,
                    WorkingSetBefore = workingBefore, WorkingSetAfter = workingAfter, Scope = "Managed weak-owner collection and observed process memory after deterministic Dispose. Not native peak-heap attribution or long-term leak qualification." },
                Boundaries = "All local synthetic conditions; 45% contamination remains a challenge condition. Caller-thread bytes exclude worker/native allocations; total managed bytes and process working set have distinct meanings. No field Takt, hardware/UI, calibration, physical identity or universal outlier guarantee."
            };
            File.WriteAllText(path, JsonSerializer.Serialize(report, JsonOptions));
            Console.WriteLine("QUALITY | inputs=" + inputs.Length + " trials=" + rows.Count + " stable=" + stable + " supported=" + accuracy + " nativeOwnersAlive=" + alive + " | " + path);
            return stable && accuracy && inputsStable && alive == 0 ? 0 : 1;
        }
        private static string InputHash(SurfaceQualitySmokeSuite.ContaminationInput input) => Convert.ToHexString(SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input.Model.Concat(input.Scene).Select(sample => new[] { sample.Position.X, sample.Position.Y, sample.Position.Z })))));
        private sealed class ContaminationRow
        {
            public string CaseId { get; set; }
            public string Policy { get; set; }
            public int Trial { get; set; }
            public int OriginalSampleCount { get; set; }
            public double? RotationCoefficientError { get; set; }
            public double OutlierFraction { get; set; }
            public string InputHash { get; set; }
            public bool Success { get; set; }
            public double? Error { get; set; }
            public int RetainedCount { get; set; }
            public int FullSupport { get; set; }
            public double? FullRmse { get; set; }
            public bool RawIdsCorrect { get; set; }
            public string Fingerprint { get; set; }
            public double Milliseconds { get; set; }
            public long CallerManagedBytes { get; set; }
        }
        private static void TestDenseWorkload()
        {
            foreach (int count in new[] { 256, 1024, 4096 }) DenseWorkload(count);
        }
        private sealed class WorkOutcome
        {
            internal int MatchedModelSampleCount, Candidates, RefinedCandidates, PreparedSamples;
        }
        private static WorkOutcome DenseWorkload(int sceneCount)
        {
            var model = SurfaceQualitySmokeSuite.Contamination(128, 11003, 0).Model;
            var scene = SurfaceQualitySmokeSuite.Samples(model.Select(p => SurfaceQualitySmokeSuite.Transform(p.Position, SurfaceQualitySmokeSuite.Pose(-60, 0, 0)))
                .Concat(model.Select(p => SurfaceQualitySmokeSuite.Transform(p.Position, SurfaceQualitySmokeSuite.Pose(60, 0, 0, 0.2))))
                .Concat(Enumerable.Range(0, sceneCount - 256).Select(i => new ThreeDPoint(300 + i * 0.3, 90 + Math.Sin(i) * 20, 80 + Math.Cos(i) * 20))));
            var options = SurfaceQualitySmokeSuite.Options(128); options.MinimumTranslationX = -100; options.MaximumTranslationX = 100;
            options.MinimumRotationZDegrees = 0; options.MaximumRotationZDegrees = 1; options.RotationStepZDegrees = 1;
            options.ProposalOptions = new SurfacePoseProposalOptions { MaximumModelAnchorCount = 4, MaximumSceneAnchorCount = 8 };
            options.RefinementOptions.RetainedCorrespondenceFraction = 0.6; options.RefinementOptions.MinimumRetainedCorrespondenceCount = 4;
            options.AmbiguityOptions.MaximumTranslationSeparation = 200; options.AmbiguityOptions.MaximumRefinedCandidateCount = 100;
            var result = new DeterministicRigidSurfacePoseSearchTool().Execute(model, scene, options);
            Require(result.Success && result.Matched && result.Coverage.MatchedModelSampleCount == 128
                && result.FinalAmbiguityEvidence.ComparisonComplete && result.FinalAmbiguityEvidence.IsAmbiguous, "Dense new-option workload failed.");
            Require(result.Coverage.Matches.All(match => match.SceneSampleOrder < 256), "Background points were claimed as an object.");
            var prepared = new DeterministicPreparedScenePreparationTool().Execute(scene.Select(p => p.Position).ToArray(), PreparedSceneSamplingSmokeSuite.Options(sceneCount, 32));
            Require(prepared.Success && prepared.Samples.Count <= 32, "Dense spatial preparation failed.");
            return new WorkOutcome { MatchedModelSampleCount = result.Coverage.MatchedModelSampleCount, Candidates = result.EvaluatedCandidateCount,
                RefinedCandidates = result.FinalAmbiguityEvidence.RefinedCandidateCount, PreparedSamples = prepared.Samples.Count };
        }
        private static void TestNativeLifetime()
        {
            var weak = Enumerable.Range(0, 8).SelectMany(NativeCycle).ToArray();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Require(weak.All(reference => !reference.IsAlive), "Native Tool owner remained strongly referenced after disposal.");
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference[] NativeCycle(int iteration)
        {
            using Mat source = LineGaugeSubpixelSmokeSuite.Profile(149, 121, true, iteration % 2 == 0 ? 47.25 : 73.875);
            using var gauge = new LineGaugeTool(); gauge.SetProperty(LineGaugeSubpixelSmokeSuite.Settings());
            using (var result = gauge.Execute(source, CancellationToken.None)) Require(result.Success, "Native gauge cycle failed.");
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel(); RequireThrows<OperationCanceledException>(() => gauge.Execute(source, cancellation.Token));
            }
            using (var recovered = gauge.Execute(source, CancellationToken.None)) Require(recovered.Success, "Native gauge recovery failed.");
            using Mat template = new Mat(31, 29, MatType.CV_8UC1, Scalar.All(0));
            Cv2.Rectangle(template, new Rect(2, 3, iteration % 2 == 0 ? 11 : 7, 7), Scalar.All(255), -1);
            Cv2.Circle(template, new Point(20, 20), 3, Scalar.All(255), -1);
            using Mat scene = new Mat(83, 91, MatType.CV_8UC1, Scalar.All(0));
            using (Mat region = scene.SubMat(new Rect(33, 27, template.Cols, template.Rows))) template.CopyTo(region);
            using var matcher = new EdgeBasedTemplateMatchingTool();
            matcher.SetProperty(new EdgeBasedTemplateMatchingToolProperty { SCORE_MIN = 0.6, SEARCH_STEP = 1, MAX_TEMPLATE_POINTS = 200, USE_DRAW_IMAGE = false });
            matcher.SetTemplateImage(template);
            var artifact = matcher.PrepareLearnedModel("local-cycle-" + iteration);
            matcher.SetLearnedModel(artifact);
            using (var matched = matcher.Execute(scene)) Require(matched.Success, "Native learned-model restoration failed: " + matched.Message);
            template.SetTo(Scalar.All(0));
            Cv2.Rectangle(template, new Rect(3, 14, 9, 10), Scalar.All(255), -1);
            Cv2.Circle(template, new Point(21, 6), 4, Scalar.All(255), -1);
            scene.SetTo(Scalar.All(0));
            using (Mat region = scene.SubMat(new Rect(33, 27, template.Cols, template.Rows))) template.CopyTo(region);
            matcher.SetTemplateImage(template); // A different template retires the restored native caches on the same owner.
            using (var replaced = matcher.Execute(scene)) Require(replaced.Success, "Native model replacement failed.");
            return new[] { new WeakReference(gauge), new WeakReference(matcher) };
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenVisionLab.Vision3D.FeatureExtraction;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class PreparedSceneSamplingSmokeSuite
    {
        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase("Prepared spatial scene reproduces even-index target loss and preserves geometric samples across permutations", TestPermutation);
            yield return new SmokeCase("Prepared spatial scene retains source IDs immutable evidence duplicates and scale behavior", TestMapping);
            yield return new SmokeCase("Prepared spatial scene enforces authored comparison budgets and recovers", TestBudget);
            yield return new SmokeCase("Prepared spatial scene cancels validation and sorting with valid recovery", TestCancellation);
        }
        internal static DeterministicPreparedScenePreparationOptions Options(int count, int samples) => new DeterministicPreparedScenePreparationOptions
        {
            MaximumSampleCount = samples, UseSpatiallyBalancedSampling = true, MaximumSpatialComparisonCount = (long)count * Math.Min(count, samples)
        };
        internal static ThreeDPoint[] Points(int count) => Enumerable.Range(0, count).Select(i => new ThreeDPoint(i * 0.1, Math.Sin(i * 0.13) * 3, Math.Cos(i * 0.07) * 2)).ToArray();
        private static ThreeDPoint[] Reorder(ThreeDPoint[] targets, ThreeDPoint[] background, HashSet<int> targetIndices)
        {
            int ti = 0, bi = 0;
            return Enumerable.Range(0, targets.Length + background.Length).Select(i => targetIndices.Contains(i) ? targets[ti++] : background[bi++]).ToArray();
        }
        private static void TestPermutation()
        {
            var selected = Enumerable.Range(0, 12).Select(i => (2 * i + 1) * 120 / 24).ToHashSet();
            var targetA = Enumerable.Range(0, 120).Where(i => !selected.Contains(i)).Take(24).ToHashSet();
            var targetB = selected.Concat(Enumerable.Range(0, 120).Where(i => !selected.Contains(i)).Take(12)).ToHashSet();
            var targets = Points(24).Select(p => new ThreeDPoint(p.X + 1000, p.Y, p.Z)).ToArray(); var background = Points(96);
            var a = Reorder(targets, background, targetA); var b = Reorder(targets, background, targetB);
            var tool = new DeterministicPreparedScenePreparationTool();
            var oldA = tool.Execute(a, new DeterministicPreparedScenePreparationOptions { MaximumSampleCount = 12 });
            var oldB = tool.Execute(b, new DeterministicPreparedScenePreparationOptions { MaximumSampleCount = 12 });
            Require(oldA.Success && oldB.Success && !oldA.Samples.Any(s => s.Position.X > 100)
                && oldB.Samples.Count(s => s.Position.X > 100) == 12, "Actual SDK did not reproduce the authored ordering boundary.");
            var spatialA = tool.Execute(a, Options(120, 12)); var spatialB = tool.Execute(b, Options(120, 12));
            Require(spatialA.Success && spatialB.Success && spatialA.Samples.Any(s => s.Position.X > 100), "Spatial preparation did not retain the separated target cluster.");
            Require(spatialA.Samples.Select(s => s.Position.X).SequenceEqual(spatialB.Samples.Select(s => s.Position.X))
                && spatialA.Samples.Select(s => s.Position.Y).SequenceEqual(spatialB.Samples.Select(s => s.Position.Y))
                && spatialA.Samples.Select(s => s.Position.Z).SequenceEqual(spatialB.Samples.Select(s => s.Position.Z)), "Geometric selection depended on source permutation.");
            Require(oldA.SamplingSemantics == DeterministicPreparedScenePreparationTool.Semantics
                && spatialA.SamplingSemantics == DeterministicPreparedScenePreparationTool.SpatialSamplingSemantics, "Sampling semantics were mislabeled.");
        }
        private static void TestMapping()
        {
            var tool = new DeterministicPreparedScenePreparationTool(); var original = Points(99);
            var baseline = tool.Execute(original, Options(99, 13));
            Require(baseline.Success && baseline.Samples.All(s => ReferenceEquals(s.Position, original[s.SourcePointIndex])), "Source index mapping failed.");
            RequireThrows<NotSupportedException>(() => ((IList<PreparedScenePointSample>)baseline.Samples)[0] = baseline.Samples[1]);
            foreach (double scale in new[] { 0.01, 1000.0 })
            {
                var points = original.Select(p => new ThreeDPoint(p.X * scale, p.Y * scale, p.Z * scale)).ToArray();
                var result = tool.Execute(points, Options(99, 13));
                Require(result.Success && result.Samples.Select(s => s.SourcePointIndex).SequenceEqual(baseline.Samples.Select(s => s.SourcePointIndex)), "Spatial scale changed selection.");
            }
            var duplicates = Enumerable.Repeat(new ThreeDPoint(1, 2, 3), 10).ToArray();
            var distinct = tool.Execute(duplicates, Options(10, 10));
            Require(distinct.Success && distinct.Samples.Count == 1 && distinct.Samples[0].SourcePointIndex == 0, "Duplicates became independent geometry.");
        }
        private static void TestBudget()
        {
            var tool = new DeterministicPreparedScenePreparationTool(); var points = Points(99); var options = Options(99, 13);
            options.MaximumSpatialComparisonCount--;
            Require(!tool.Execute(points, options).Success, "Budget overflow was admitted.");
            options.MaximumSpatialComparisonCount = null;
            Require(!tool.Execute(points, options).Success, "Missing spatial budget was admitted.");
            options.UseSpatiallyBalancedSampling = false; options.MaximumSpatialComparisonCount = 1;
            Require(!tool.Execute(points, options).Success, "Unused non-default policy was silently ignored.");
            options.MaximumSpatialComparisonCount = null;
            Require(tool.Execute(points, options).Success, "Default recovery failed.");
            var invalid = new[] { new ThreeDPoint(double.NaN, 0, 0) };
            Require(!tool.Execute(invalid, Options(1, 1)).Success, "Non-finite spatial input was admitted.");
            Require(tool.Execute(points, Options(99, 13)).Success, "Spatial recovery failed.");
        }
        private static void TestCancellation()
        {
            var points = Points(2048); var tool = new DeterministicPreparedScenePreparationTool();
            var baselineReads = new SurfaceQualitySmokeSuite.ReadProbe<ThreeDPoint>(points);
            Require(tool.Execute(baselineReads, new DeterministicPreparedScenePreparationOptions { MaximumSampleCount = 16 }).Success, "Default read-count control failed.");
            using var cancellation = new CancellationTokenSource();
            var spatialReads = new SurfaceQualitySmokeSuite.ReadProbe<ThreeDPoint>(points, baselineReads.Reads + 64, cancellation);
            RequireThrows<OperationCanceledException>(() => tool.Execute(spatialReads, Options(2048, 16), cancellation.Token));
            Require(spatialReads.Reads > baselineReads.Reads, "Cancellation did not reach sorting.");
            Require(tool.Execute(points, Options(2048, 16)).Success, "Preparation did not recover.");
        }
    }
}

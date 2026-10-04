using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenVisionLab.Vision3D.FeatureExtraction;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class SurfaceQualitySmokeSuite
    {
        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase("Surface final quality compares refined competitors separately from coarse evidence", TestFinalCompetition);
            yield return new SmokeCase("Surface final quality marks budget exhaustion and invalid policies with recovery", TestBudget);
            yield return new SmokeCase("Surface final quality maps both competitors through multiple claims", TestMultiple);
            yield return new SmokeCase("Surface final quality cancels during added comparison and recovers", TestCancellation);
            yield return new SmokeCase("Surface retained fit floor separates trimming from full support without changing defaults", TestFitFloor);
            yield return new SmokeCase("Surface retained fit floor validates independent limits and preserves evidence across scales", TestFitFloorLimits);
        }

        internal static SurfaceMatchSample[] Samples(IEnumerable<ThreeDPoint> points) => points.Select((p, i) => new SurfaceMatchSample(i, p)).ToArray();
        internal static RigidSurfacePose Pose(double x, double y, double z, double degrees = 0)
        {
            double angle = degrees * Math.PI / 180;
            return new RigidSurfacePose(Math.Cos(angle), -Math.Sin(angle), 0, Math.Sin(angle), Math.Cos(angle), 0, 0, 0, 1, x, y, z);
        }
        internal static ThreeDPoint Transform(ThreeDPoint p, RigidSurfacePose pose) => new ThreeDPoint(
            pose.M11 * p.X + pose.M12 * p.Y + pose.M13 * p.Z + pose.TranslationX,
            pose.M21 * p.X + pose.M22 * p.Y + pose.M23 * p.Z + pose.TranslationY,
            pose.M31 * p.X + pose.M32 * p.Y + pose.M33 * p.Z + pose.TranslationZ);
        internal static SurfaceMatchSample[] Tetrahedron() => Samples(new[]
        {
            new ThreeDPoint(0,0,0), new ThreeDPoint(2,0.3,0.2), new ThreeDPoint(0.4,2,0.1), new ThreeDPoint(0.1,0.5,2.4)
        });
        internal static DeterministicRigidSurfacePoseSearchOptions Options(int minimum = 4, double scale = 1) => new DeterministicRigidSurfacePoseSearchOptions
        {
            MinimumRotationXDegrees = 0, MaximumRotationXDegrees = 0, RotationStepXDegrees = 1,
            MinimumRotationYDegrees = 0, MaximumRotationYDegrees = 0, RotationStepYDegrees = 1,
            MinimumRotationZDegrees = -2, MaximumRotationZDegrees = 2, RotationStepZDegrees = 4,
            MinimumTranslationX = -20 * scale, MaximumTranslationX = 20 * scale,
            MinimumTranslationY = -20 * scale, MaximumTranslationY = 20 * scale,
            MinimumTranslationZ = -20 * scale, MaximumTranslationZ = 20 * scale,
            MaximumCorrespondenceDistance = 0.7 * scale, MinimumMatchedSampleCount = minimum, MaximumCandidateCount = 100000,
            UseSamplePairTranslationCandidates = true,
            RefinementOptions = new SurfacePoseRefinementOptions { MaximumIterations = 10, MinimumRmseImprovement = 1e-12 * scale },
            AmbiguityOptions = new SurfacePoseAmbiguityOptions
            {
                MinimumRotationSeparationDegrees = 10, MinimumTranslationSeparation = 1 * scale,
                MaximumTranslationSeparation = 20 * scale, MaximumMatchedSampleCountDifference = 0, MaximumRmseDifference = 1e-8 * scale
            }
        };
        internal static SurfaceMatchSample[] Competitors(SurfaceMatchSample[] model) => Samples(
            model.Select(p => Transform(p.Position, Pose(-4, 0, 0))).Concat(model.Select(p => Transform(p.Position, Pose(4, 0, 0, 1)))));

        private static void TestFinalCompetition()
        {
            SurfaceMatchSample[] model = Tetrahedron(), scene = Competitors(model);
            var tool = new DeterministicRigidSurfacePoseSearchTool();
            var options = Options();
            var baseline = tool.Execute(model, scene, options);
            Require(baseline.Success && baseline.Matched && baseline.FinalAmbiguityEvidence == null, "Default search failed.");
            options.AmbiguityOptions.MaximumRefinedCandidateCount = 1000;
            var result = tool.Execute(model, scene, options);
            var final = result.FinalAmbiguityEvidence;
            Require(result.Success && result.Matched && final != null && final.ComparisonComplete && final.IsRefinedComparison, "Final comparison did not complete.");
            Require(final.IsAmbiguous && !result.AmbiguityEvidence.IsAmbiguous, "An off-grid competitor must become equivalent after refinement, separately from coarse evidence.");
            RequireApproximately(result.Pose.TranslationX, baseline.Pose.TranslationX, 1e-12, "Optional evidence changed the selected pose.");
            Require(ReferenceEquals(final.ReferencePose, result.Pose) && ReferenceEquals(final.ReferenceCoverage, result.Coverage), "Final reference differs from the returned pose/coverage.");
            Require(final.AlternativeCoverage.MatchedModelSampleCount == 4 && final.AlternativeCoverage.InlierRmse < 1e-8, "Final alternative lacks full analytic support.");
            options.AmbiguityOptions.MaximumRefinedCandidateCount = 1;
            Require(final.MaximumRefinedCandidateCount == 1000, "Evidence retained a mutable policy reference.");
        }
        private static void TestBudget()
        {
            SurfaceMatchSample[] model = Tetrahedron(), scene = Competitors(model);
            var options = Options(); var tool = new DeterministicRigidSurfacePoseSearchTool();
            options.AmbiguityOptions.MaximumRefinedCandidateCount = 1;
            var result = tool.Execute(model, scene, options);
            Require(result.Success && result.Matched && !result.FinalAmbiguityEvidence.ComparisonComplete
                && result.FinalAmbiguityEvidence.RefinedCandidateCount == 1, "Budget exhaustion was interpreted as complete evidence or changed raw matching.");
            options.AmbiguityOptions.MaximumRefinedCandidateCount = 0;
            Require(!tool.Execute(model, scene, options).Success, "Zero final-comparison budget was accepted.");
            options.AmbiguityOptions.MaximumRefinedCandidateCount = 1000;
            var saved = options.RefinementOptions; options.RefinementOptions = null;
            Require(!tool.Execute(model, scene, options).Success, "Final comparison accepted missing refinement.");
            options.RefinementOptions = saved;
            Require(tool.Execute(model, scene, options).FinalAmbiguityEvidence.ComparisonComplete, "Valid recovery failed.");
        }
        private static void TestMultiple()
        {
            SurfaceMatchSample[] model = Tetrahedron(), scene = Competitors(model);
            var options = Options(); options.AmbiguityOptions.MaximumRefinedCandidateCount = 1000;
            var result = new DeterministicMultipleSurfaceMatchTool().Execute(model, scene, new DeterministicMultipleSurfaceMatchOptions
            {
                PoseSearchOptions = options, MaximumMatchCount = 2, MaximumExpandedCandidateCount = 2000
            });
            Require(result.Success && result.Matches.Count == 2, "Multiple matching failed.");
            var first = result.Matches[0]; var second = result.Matches[1];
            Require(first.FinalAmbiguityEvidence.IsAmbiguous && first.FinalAmbiguityEvidence.ComparisonComplete, "First instance lacks its refined competitor.");
            Require(second.FinalAmbiguityEvidence.ComparisonComplete && !second.FinalAmbiguityEvidence.IsAmbiguous, "Claimed instance remained a competitor.");
            var firstIds = first.Coverage.Matches.Select(p => p.SceneSampleOrder).ToArray();
            var secondIds = second.Coverage.Matches.Select(p => p.SceneSampleOrder).ToArray();
            Require(!firstIds.Intersect(secondIds).Any() && firstIds.Concat(secondIds).OrderBy(i => i).SequenceEqual(Enumerable.Range(0, 8)), "Claims lost original scene IDs.");
            Require(first.FinalAmbiguityEvidence.ReferenceCoverage.Matches.Select(p => p.SceneSampleOrder).SequenceEqual(firstIds), "Final reference IDs do not match instance IDs.");
            Require(first.FinalAmbiguityEvidence.AlternativeCoverage.Matches.Select(p => p.SceneSampleOrder).OrderBy(i => i).SequenceEqual(secondIds.OrderBy(i => i)), "Final alternative IDs were not mapped.");
        }
        private static void TestCancellation()
        {
            SurfaceMatchSample[] model = Tetrahedron(), scene = Competitors(model); var options = Options();
            var tool = new DeterministicRigidSurfacePoseSearchTool(); var baselineReads = new ReadProbe<SurfaceMatchSample>(model);
            Require(tool.Execute(baselineReads, scene, options).Success, "Read-count baseline failed.");
            using var cancellation = new CancellationTokenSource();
            var canceledReads = new ReadProbe<SurfaceMatchSample>(model, baselineReads.Reads + 50, cancellation);
            options.AmbiguityOptions.MaximumRefinedCandidateCount = 1000;
            RequireThrows<OperationCanceledException>(() => tool.Execute(canceledReads, scene, options, cancellation.Token));
            Require(canceledReads.Reads > baselineReads.Reads, "Cancellation did not reach the added path.");
            Require(tool.Execute(model, scene, options).FinalAmbiguityEvidence.ComparisonComplete, "Search did not recover after cancellation.");
        }

        internal sealed class ContaminationInput
        {
            internal SurfaceMatchSample[] Model, Scene;
            internal RigidSurfacePose Seed, Truth;
            internal int OutlierCount;
        }
        internal static ContaminationInput Contamination(int count, int seed, double fraction, double scale = 1)
        {
            Random random = new Random(seed);
            var model = Samples(Enumerable.Range(0, count).Select(i => new ThreeDPoint(
                (i % 13 * 3 + random.NextDouble() * 0.1) * scale,
                (i / 13 * 3 + random.NextDouble() * 0.1) * scale,
                (2 * Math.Sin(i * 0.47 + seed * 0.03) + Math.Cos(i * 0.19)) * scale)));
            var truth = Pose(0.2 * scale, -0.1 * scale, 0.15 * scale);
            int outliers = (int)Math.Round(count * fraction);
            var order = Enumerable.Range(0, count).OrderBy(_ => random.Next()).Take(outliers).ToHashSet();
            var scene = Samples(model.Select((p,i) =>
            {
                ThreeDPoint transformed = Transform(p.Position, truth);
                return new ThreeDPoint(transformed.X, transformed.Y, transformed.Z + (order.Contains(i) ? 0.35 * scale : 0));
            }));
            return new ContaminationInput { Model = model, Scene = scene, Truth = truth, Seed = Pose(0.23 * scale, -0.12 * scale, 0.16 * scale), OutlierCount = outliers };
        }
        internal static double PoseError(RigidSurfacePose actual, RigidSurfacePose truth) => Math.Sqrt(
            Math.Pow(actual.TranslationX - truth.TranslationX, 2) + Math.Pow(actual.TranslationY - truth.TranslationY, 2)
            + Math.Pow(actual.TranslationZ - truth.TranslationZ, 2));
        internal static double RotationCoefficientError(RigidSurfacePose actual, RigidSurfacePose truth) => new[] {
            Math.Abs(actual.M11-truth.M11), Math.Abs(actual.M12-truth.M12), Math.Abs(actual.M13-truth.M13),
            Math.Abs(actual.M21-truth.M21), Math.Abs(actual.M22-truth.M22), Math.Abs(actual.M23-truth.M23),
            Math.Abs(actual.M31-truth.M31), Math.Abs(actual.M32-truth.M32), Math.Abs(actual.M33-truth.M33)
        }.Max();
        private static void TestFitFloor()
        {
            var input = Contamination(100, 691, 0.4); var options = Options(65);
            options.RefinementOptions.RetainedCorrespondenceFraction = 0.6;
            var tool = new DeterministicSurfacePoseRefinementTool();
            var baseline = tool.Execute(input.Model, input.Scene, input.Seed, options);
            Require(baseline.Success && baseline.RetainedCorrespondenceCount == 65 && baseline.MinimumRetainedCorrespondenceCount == null, "Null floor changed legacy behavior.");
            options.RefinementOptions.MinimumRetainedCorrespondenceCount = 4;
            var result = tool.Execute(input.Model, input.Scene, input.Seed, options);
            Require(result.Success && result.RetainedCorrespondenceCount == 60 && result.MinimumRetainedCorrespondenceCount == 4, "Independent fixed-k floor was not applied.");
            Require(result.Coverage.MatchedModelSampleCount == result.InitialCoverage.MatchedModelSampleCount
                && result.Coverage.Matches.Count == 100 && result.LastFitCorrespondences.Count <= 64, "Raw support or bounded fit was changed.");
            Require(PoseError(result.Pose, input.Truth) < 1e-8 && RotationCoefficientError(result.Pose, input.Truth) < 1e-8
                && PoseError(result.Pose, input.Truth) < PoseError(baseline.Pose, input.Truth), "Independent floor did not recover analytic truth.");
            Require(result.Coverage.InlierRmse > 0.1, "Biased raw observations were hidden.");
            options.RefinementOptions.MinimumRetainedCorrespondenceCount = 40;
            Require(result.MinimumRetainedCorrespondenceCount == 4, "Retained-fit evidence changed after policy mutation.");
        }
        private static void TestFitFloorLimits()
        {
            var tool = new DeterministicSurfacePoseRefinementTool();
            foreach (double scale in new[] { 0.01, 1.0, 1000.0 })
            {
                var input = Contamination(100, 713, 0.4, scale); var options = Options(65, scale);
                options.RefinementOptions.MinimumRetainedCorrespondenceCount = 4;
                Require(!tool.Execute(input.Model, input.Scene, input.Seed, options).Success, "Independent floor accepted absent trimming.");
                options.RefinementOptions.RetainedCorrespondenceFraction = 0.6;
                options.RefinementOptions.MinimumRetainedCorrespondenceCount = 3;
                Require(!tool.Execute(input.Model, input.Scene, input.Seed, options).Success, "Fewer than four fit samples were admitted.");
                options.RefinementOptions.MinimumRetainedCorrespondenceCount = 101;
                Require(!tool.Execute(input.Model, input.Scene, input.Seed, options).Success, "Floor exceeding the model was accepted.");
                options.RefinementOptions.MinimumRetainedCorrespondenceCount = 4;
                var result = tool.Execute(input.Model, input.Scene, input.Seed, options);
                Require(result.Success && PoseError(result.Pose, input.Truth) < 1e-8 * scale
                    && RotationCoefficientError(result.Pose, input.Truth) < 1e-8, "Scaled truth/recovery failed.");
                using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
                RequireThrows<OperationCanceledException>(() => tool.Execute(input.Model, input.Scene, input.Seed, options, cancellation.Token));
                Require(tool.Execute(input.Model, input.Scene, input.Seed, options).Success, "Refinement did not recover.");
            }
        }

        internal sealed class ReadProbe<T> : IReadOnlyList<T>
        {
            private readonly IReadOnlyList<T> values;
            private readonly int cancelAt;
            private readonly CancellationTokenSource cancellation;
            internal ReadProbe(IReadOnlyList<T> values, int cancelAt = int.MaxValue, CancellationTokenSource cancellation = null)
            { this.values = values; this.cancelAt = cancelAt; this.cancellation = cancellation; }
            internal int Reads { get; private set; }
            public int Count => values.Count;
            public T this[int index] { get { if (++Reads == cancelAt) cancellation?.Cancel(); return values[index]; } }
            public IEnumerator<T> GetEnumerator() { for (int i = 0; i < Count; i++) yield return this[i]; }
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}

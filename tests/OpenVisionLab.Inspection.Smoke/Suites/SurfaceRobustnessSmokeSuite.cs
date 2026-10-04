using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using OpenVisionLab.Vision3D.FeatureExtraction;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class SurfaceRobustnessSmokeSuite
    {
        private static readonly int[] HeldOutSizes = { 379, 487 };
        private static readonly int[] HeldOutSeeds = { 907, 1009 };
        private static readonly string[] Challenges = { "clean", "outlier20", "outlier40", "coherent-shift", "competitors", "partial", "heavy", "absent" };
        private static readonly string[] Policies = { "ordered-null", "balanced-null", "trimmed-0.8" };
        private static readonly JsonSerializerOptions ReportOptions = new JsonSerializerOptions { WriteIndented = true };

        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase("Surface trimmed robustness reports same-orientation translated competitors and preserves null defaults", TestCompetition);
            yield return new SmokeCase("Surface trimmed robustness selects a qualifying competitor behind a stronger diagnostic alternative", TestQualifyingCompetition);
            yield return new SmokeCase("Surface trimmed robustness preserves full observations while independently improving the fixed objective", TestObjective);
            yield return new SmokeCase("Surface trimmed robustness maps single and multiple retained evidence to original scene IDs", TestCallers);
            yield return new SmokeCase("Surface trimmed robustness retains bounded seeds for support degeneracy and unmet objectives", TestRetainedSeeds);
            yield return new SmokeCase("Surface trimmed robustness validates nullable policies and recovers through shared callers", TestGuards);
            yield return new SmokeCase("Surface trimmed robustness orders residual ties and scales RMSE without overflow", TestResidualMath);
            yield return new SmokeCase("Surface trimmed robustness propagates copy sort and fit cancellation with valid recovery", TestCancellation);
        }

        private sealed class Input
        {
            internal string Id;
            internal string Challenge;
            internal string Hash;
            internal bool HeldOut;
            internal SurfaceMatchSample[] Model;
            internal SurfaceMatchSample[] Scene;
            internal int[] ModelIds;
            internal int[] SceneIds;
            internal double Yaw;
            internal ThreeDPoint Translation;
        }

        // The held-out equations and policy are frozen in PL-0035/work-contract.json before SDK edits or execution.
        private static ThreeDPoint Truth(ThreeDPoint point, double yaw, ThreeDPoint translation)
        {
            double radians = yaw * Math.PI / 180D;
            return new ThreeDPoint(point.X * Math.Cos(radians) - point.Y * Math.Sin(radians) + translation.X,
                point.X * Math.Sin(radians) + point.Y * Math.Cos(radians) + translation.Y, point.Z + translation.Z);
        }

        private static RigidSurfacePose Pose(double yaw, ThreeDPoint translation)
        {
            double radians = yaw * Math.PI / 180D;
            return new RigidSurfacePose(Math.Cos(radians), -Math.Sin(radians), 0, Math.Sin(radians), Math.Cos(radians), 0,
                0, 0, 1, translation.X, translation.Y, translation.Z);
        }

        private static Input CreateInput(int count, int seed, string challenge)
        {
            Random random = new Random(seed);
            SurfaceMatchSample[] model = Enumerable.Range(0, count).Select(i => new SurfaceMatchSample(i,
                new ThreeDPoint(i % 23 * 1.2 + (random.NextDouble() - 0.5) * 0.02,
                    i / 23 * 0.9 + (random.NextDouble() - 0.5) * 0.02, 2.1 * Math.Sin(0.47 * i) + Math.Cos(0.23 * i)))).ToArray();
            ThreeDPoint translation = new ThreeDPoint(-3, 6, 1.5);
            List<SurfaceMatchSample> scene = new List<SurfaceMatchSample>();
            List<int> ids = new List<int>();
            void Add(ThreeDPoint point, int id) { scene.Add(new SurfaceMatchSample(scene.Count, point)); ids.Add(id); }
            for (int i = 0; i < count; i++)
            {
                if (challenge == "absent" || challenge == "partial" && i % 4 == 1 || challenge == "heavy" && i % 3 != 0) continue;
                ThreeDPoint point = Truth(model[i].Position, -9.75, translation);
                double noise = challenge == "coherent-shift" ? 0.26
                    : challenge == "outlier20" && i % 5 == 0 || challenge == "outlier40" && i % 5 < 2 ? 0.28 : 0D;
                Add(new ThreeDPoint(point.X, point.Y, point.Z + noise), i);
            }
            if (challenge == "competitors")
            {
                foreach (SurfaceMatchSample sample in model)
                {
                    ThreeDPoint point = Truth(sample.Position, -9.75, translation);
                    Add(new ThreeDPoint(point.X, point.Y, point.Z - 0.28), -1);
                }
            }
            int background = Math.Max(17, (int)Math.Ceiling(count * 0.65) - scene.Count + 17);
            for (int i = 0; i < background; i++) Add(new ThreeDPoint(90 + i, -70 - i, 80 + i % 3), -1);
            int[] modelIds = Enumerable.Range(0, count).ToArray();
            string hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            {
                Model = model.Select(p => new[] { p.Position.X, p.Position.Y, p.Position.Z }),
                Scene = scene.Select(p => new[] { p.Position.X, p.Position.Y, p.Position.Z }), ModelIds = modelIds, SceneIds = ids
            })));
            return new Input
            {
                Id = "held/" + count + "/" + seed + "/" + challenge, Challenge = challenge, Hash = hash, HeldOut = true,
                Model = model, Scene = scene.ToArray(), ModelIds = modelIds, SceneIds = ids.ToArray(), Yaw = -9.75, Translation = translation
            };
        }

        private static Input Regression(int count, int seed, string challenge, bool heldOut)
        {
            SurfaceChallengeEvaluationSmokeSuite.Input old = SurfaceChallengeEvaluationSmokeSuite.CreateInput(count, seed, challenge, heldOut);
            return new Input
            {
                Id = "regression/" + old.Id, Challenge = challenge, Hash = old.Hash, HeldOut = false,
                Model = old.Model, Scene = old.Scene, ModelIds = old.ModelPhysicalIds, SceneIds = old.ScenePhysicalIds,
                Yaw = 7.25, Translation = new ThreeDPoint(4, -3, 2)
            };
        }

        private static DeterministicRigidSurfacePoseSearchOptions Options(int count, double yaw, string policy)
        {
            return new DeterministicRigidSurfacePoseSearchOptions
            {
                MinimumRotationXDegrees = -2, MaximumRotationXDegrees = 2, RotationStepXDegrees = 4,
                MinimumRotationYDegrees = -2, MaximumRotationYDegrees = 2, RotationStepYDegrees = 4,
                MinimumRotationZDegrees = yaw - 3, MaximumRotationZDegrees = yaw + 3, RotationStepZDegrees = 3,
                MinimumTranslationX = -30, MaximumTranslationX = 30, MinimumTranslationY = -30, MaximumTranslationY = 30,
                MinimumTranslationZ = -30, MaximumTranslationZ = 30, MaximumCorrespondenceDistance = 0.8,
                MinimumMatchedSampleCount = (int)Math.Ceiling(count * 0.65), MaximumCandidateCount = 100,
                RefinementOptions = new SurfacePoseRefinementOptions
                {
                    MaximumIterations = 5, UseSpatiallyBalancedFit = policy == "balanced-null",
                    RetainedCorrespondenceFraction = policy == "trimmed-0.8" ? 0.8 : (double?)null
                }
            };
        }

        private static RigidSurfacePose Seed(Input input)
        {
            return Pose(input.HeldOut ? -9 : 8, new ThreeDPoint(input.Translation.X, input.Translation.Y,
                input.Translation.Z - (input.HeldOut ? 0.22 : 0.3)));
        }

        private static double PoseError(Input input, RigidSurfacePose pose)
        {
            if (pose == null) return double.PositiveInfinity;
            return input.Model.Max(sample =>
            {
                ThreeDPoint expected = Truth(sample.Position, input.Yaw, input.Translation), actual = pose.Transform(sample.Position);
                return Math.Sqrt(Math.Pow(expected.X - actual.X, 2) + Math.Pow(expected.Y - actual.Y, 2) + Math.Pow(expected.Z - actual.Z, 2));
            });
        }

        private static SurfaceMatchSample[] SmallModel(double scale = 1)
        {
            return new[] { new ThreeDPoint(0, 0, 0), new ThreeDPoint(3, 0, 0), new ThreeDPoint(0, 4, 0),
                new ThreeDPoint(0, 0, 2), new ThreeDPoint(2, 1, 3) }
                .Select((p, i) => new SurfaceMatchSample(i, new ThreeDPoint(p.X * scale, p.Y * scale, p.Z * scale))).ToArray();
        }

        private static SurfacePoseAmbiguityOptions Competition(double scale = 1)
        {
            return new SurfacePoseAmbiguityOptions
            {
                MinimumRotationSeparationDegrees = 45, MinimumTranslationSeparation = 0.25 * scale,
                MaximumTranslationSeparation = 0.5 * scale, MaximumMatchedSampleCountDifference = 0, MaximumRmseDifference = 1e-10 * scale
            };
        }

        private static void TestCompetition()
        {
            foreach (double scale in new[] { 0.001, 1D, 1000D })
            {
                SurfaceMatchSample[] model = SmallModel(scale);
                SurfaceMatchSample[] scene = model.Select(m => m.Position)
                    .Concat(model.Select(m => new ThreeDPoint(m.Position.X, m.Position.Y, m.Position.Z + 0.25 * scale)))
                    .Select((p, i) => new SurfaceMatchSample(i, p)).ToArray();
                DeterministicRigidSurfacePoseSearchOptions options = SurfaceSearchRobustnessSmokeSuite.Options();
                options.MinimumMatchedSampleCount = 5;
                options.MinimumTranslationX = options.MinimumTranslationY = options.MinimumTranslationZ = -100 * scale;
                options.MaximumTranslationX = options.MaximumTranslationY = options.MaximumTranslationZ = 100 * scale;
                options.MaximumCorrespondenceDistance = 1e-8 * scale;
                options.UseSamplePairTranslationCandidates = true;
                options.AmbiguityOptions = Competition(scale);
                options.AmbiguityOptions.MinimumTranslationSeparation = null;
                DeterministicRigidSurfacePoseSearchTool tool = new DeterministicRigidSurfacePoseSearchTool();
                DeterministicRigidSurfacePoseSearchResult legacy = tool.Execute(model, scene, options);
                Require(legacy.Success && legacy.Matched && !legacy.AmbiguityEvidence.IsAmbiguous
                    && double.IsNaN(legacy.AmbiguityEvidence.TranslationSeparation), "Null translation policy changed orientation-only evidence.");
                options.AmbiguityOptions.MinimumTranslationSeparation = 0.24 * scale;
                DeterministicRigidSurfacePoseSearchResult result = tool.Execute(model, scene, options);
                Require(result.Success && result.Matched && result.AmbiguityEvidence.IsAmbiguous
                    && result.EvaluatedCandidateCount == legacy.EvaluatedCandidateCount, "Same-orientation competitors were hidden: scale=" + scale);
                RequireApproximately(result.Pose.TranslationZ, legacy.Pose.TranslationZ, 0, "Evidence changed raw ranking.");
                RequireApproximately(result.AmbiguityEvidence.TranslationSeparation, 0.25 * scale, 1e-12 * scale, "Model-origin distance changed units.");
                RequireApproximately(result.AmbiguityEvidence.RotationSeparationDegrees, 0, 0, "Translated copies have identical orientation.");
                options.AmbiguityOptions.MinimumTranslationSeparation = 0.3 * scale;
                Require(result.AmbiguityEvidence.MinimumTranslationSeparation == 0.24 * scale, "Evidence retained mutable options.");
                Require(!tool.Execute(model, scene, options).AmbiguityEvidence.IsAmbiguous, "Below-threshold translations were admitted.");
                options.AmbiguityOptions.MinimumTranslationSeparation = 0.25 * scale;
                options.AmbiguityOptions.MaximumTranslationSeparation = 0.25 * scale;
                if (scale == 1) Require(tool.Execute(model, scene, options).AmbiguityEvidence.IsAmbiguous, "Inclusive minimum/maximum separation boundary changed.");
            }
        }

        private static void TestQualifyingCompetition()
        {
            SurfaceMatchSample[] model = SmallModel();
            List<SurfaceMatchSample> scene = new List<SurfaceMatchSample>();
            void Add(SurfaceMatchSample m, double x, double noise)
            {
                scene.Add(new SurfaceMatchSample(scene.Count, new ThreeDPoint(m.Position.X + x, m.Position.Y, m.Position.Z + noise)));
            }
            foreach (SurfaceMatchSample m in model) Add(m, 5, m.Order % 2 == 0 ? 0.04 : -0.04);
            foreach (SurfaceMatchSample m in model.Take(4)) Add(m, 15, 0);
            foreach (SurfaceMatchSample m in model.Take(3)) Add(m, 25, m.Order % 2 == 0 ? 0.04 : -0.04);
            DeterministicRigidSurfacePoseSearchOptions options = SurfaceSearchRobustnessSmokeSuite.Options();
            options.UseSamplePairTranslationCandidates = true;
            options.MaximumCorrespondenceDistance = 0.1;
            options.AmbiguityOptions = new SurfacePoseAmbiguityOptions
            {
                MinimumRotationSeparationDegrees = 45, MinimumTranslationSeparation = 5,
                MaximumTranslationSeparation = 30, MaximumMatchedSampleCountDifference = 2, MaximumRmseDifference = 0.015
            };
            DeterministicRigidSurfacePoseSearchResult result = new DeterministicRigidSurfacePoseSearchTool().Execute(model, scene, options);
            Require(result.Success && result.Matched && result.Coverage.MatchedModelSampleCount == 5
                && result.AmbiguityEvidence.IsAmbiguous && result.AmbiguityEvidence.AlternativeCoverage.MatchedModelSampleCount == 3,
                "A stronger four-point alternative outside the RMSE gate hid the qualifying three-point competitor.");
        }

        private static void RequireFullEvidence(Input input, SurfacePoseRefinementResult result)
        {
            Require(result.Success && result.Coverage.MatchedModelSampleCount >= result.InitialCoverage.MatchedModelSampleCount,
                "Refinement lost raw support or execution failed: " + result.Message);
            DeterministicSurfaceCoverageResult scored = new DeterministicSurfaceCoverageTool().Execute(
                input.Model, input.Scene, result.Pose, result.Coverage.MaximumCorrespondenceDistance);
            Require(scored.Success && scored.Matches.Select(m => (m.ModelSampleOrder, m.SceneSampleOrder, m.Distance))
                .SequenceEqual(result.Coverage.Matches.Select(m => (m.ModelSampleOrder, m.SceneSampleOrder, m.Distance))), "Full coverage was replaced by retained fitting points.");
            RequireApproximately(scored.InlierRmse, result.Coverage.InlierRmse, 0, "Raw final RMSE changed.");
            Require(result.LastFitCorrespondences.Count <= 64 && result.IterationCount <= result.MaximumIterations, "Bounded fitting or iteration ceiling changed.");
            if (result.TrimmedRmse.HasValue)
            {
                double[] distances = result.Coverage.Matches.OrderBy(m => m.Distance).ThenBy(m => m.ModelSampleOrder).ThenBy(m => m.SceneSampleOrder)
                    .Take(result.RetainedCorrespondenceCount).Select(m => m.Distance).ToArray();
                double expected = Math.Sqrt(distances.Sum(d => d * d) / distances.Length);
                RequireApproximately(result.TrimmedRmse.Value, expected, 1e-12, "Fixed-count objective disagrees with independent residual calculation.");
                Require(result.TrimmedRmse <= result.InitialTrimmedRmse, "Selected objective increased.");
            }
        }

        private static void TestObjective()
        {
            Input input = Regression(521, 409, "biased-outliers", true);
            DeterministicSurfacePoseRefinementTool tool = new DeterministicSurfacePoseRefinementTool();
            SurfacePoseRefinementResult warm = tool.Execute(input.Model, input.Scene, Seed(input), Options(input.Model.Length, input.Yaw, "balanced-null"));
            DeterministicRigidSurfacePoseSearchOptions options = Options(input.Model.Length, input.Yaw, "trimmed-0.8");
            SurfacePoseRefinementResult result = tool.Execute(input.Model, input.Scene, warm.Pose, options);
            RequireFullEvidence(input, result);
            Require(result.Improved && PoseError(input, result.Pose) < 0.1 && result.TrimmedRmse < result.InitialTrimmedRmse
                && result.Coverage.InlierRmse > result.InitialCoverage.InlierRmse, "The selected objective must permit the measured full-RMSE conflict.");
            Require(result.Coverage.Matches.Count == input.Model.Length && result.Coverage.Matches.Count(m => m.Distance > 0.1) >= input.Model.Length / 5,
                "Outlier observations disappeared from raw evidence.");
            Require(result.RetainedCorrespondenceCount == (int)Math.Ceiling(0.8 * result.InitialCoverage.Matches.Count)
                && result.LastRetainedCorrespondences.Count == result.RetainedCorrespondenceCount
                && result.LastFitCorrespondences.All(f => result.LastRetainedCorrespondences.Any(r => r.ModelSampleOrder == f.ModelSampleOrder && r.SceneSampleOrder == f.SceneSampleOrder)),
                "Fit evidence or fixed support changed.");
            options.RefinementOptions.RetainedCorrespondenceFraction = null;
            Require(result.RetainedCorrespondenceFraction == 0.8 && !result.UseSpatiallyBalancedFit, "Options were not snapshotted or trim depended on the legacy spatial flag.");
            RequireThrows<NotSupportedException>(() => ((IList<SurfaceCoverageMatch>)result.LastRetainedCorrespondences).Clear());
            Input clean = Regression(257, 101, "clean", false);
            SurfacePoseRefinementResult legacy = tool.Execute(clean.Model, clean.Scene, Seed(clean), Options(clean.Model.Length, clean.Yaw, "ordered-null"));
            Require(legacy.RetainedCorrespondenceFraction == null && legacy.RetainedCorrespondenceCount == 0
                && legacy.InitialTrimmedRmse == null && legacy.TrimmedRmse == null && legacy.LastRetainedCorrespondences.Count == 0,
                "Null policy introduced retained-objective evidence.");
            SurfaceMatchSample[] reordered = input.Model.Reverse().Select((m, i) => new SurfaceMatchSample(i, m.Position)).ToArray();
            Input permuted = new Input { Model = reordered, Scene = input.Scene, Yaw = input.Yaw, Translation = input.Translation };
            SurfacePoseRefinementResult reorderedResult = tool.Execute(reordered, input.Scene, warm.Pose, Options(reordered.Length, input.Yaw, "trimmed-0.8"));
            Require(reorderedResult.Success && PoseError(permuted, reorderedResult.Pose) < 0.1, "This reordered regression input lost physical truth; no general permutation-invariance claim.");
        }

        private static void TestCallers()
        {
            SurfaceMatchSample[] model = SmallModel();
            ThreeDPoint[] first = model.Select(m => Truth(m.Position, 7.25, new ThreeDPoint(4, -3, 2))).ToArray();
            ThreeDPoint[] second = model.Select(m => Truth(m.Position, 7.25, new ThreeDPoint(14, -3, 2))).ToArray();
            ThreeDPoint[] third = model.Select(m => Truth(m.Position, 7.25, new ThreeDPoint(24, -3, 2))).ToArray();
            List<SurfaceMatchSample> scene = new List<SurfaceMatchSample>();
            for (int i = 0; i < model.Length; i++)
            {
                scene.Add(new SurfaceMatchSample(scene.Count, first[model.Length - 1 - i]));
                scene.Add(new SurfaceMatchSample(scene.Count, second[i]));
                scene.Add(new SurfaceMatchSample(scene.Count, third[i]));
            }
            DeterministicRigidSurfacePoseSearchOptions options = Options(model.Length, 7.25, "trimmed-0.8");
            options.UseSamplePairTranslationCandidates = true;
            options.AmbiguityOptions = Competition();
            options.AmbiguityOptions.MinimumTranslationSeparation = 5;
            options.AmbiguityOptions.MaximumTranslationSeparation = 20;
            options.AmbiguityOptions.MaximumRmseDifference = 0.2;
            options.MaximumCandidateCount = 1000;
            SurfaceMatchSample[] singleScene = first.Select((p, i) => new SurfaceMatchSample(i, p)).ToArray();
            DeterministicRigidSurfacePoseSearchResult single = new DeterministicRigidSurfacePoseSearchTool().Execute(model, singleScene, options);
            Require(single.Success && single.Matched && single.RefinementEvidence.RetainedCorrespondenceFraction == 0.8
                && single.RefinementEvidence.LastRetainedCorrespondences.Count == 4, "Actual single search bypassed trimmed refinement.");
            DeterministicMultipleSurfaceMatchOptions multi = new DeterministicMultipleSurfaceMatchOptions
            {
                PoseSearchOptions = options, MaximumMatchCount = 3, MaximumExpandedCandidateCount = 2700
            };
            DeterministicMultipleSurfaceMatchResult result = new DeterministicMultipleSurfaceMatchTool().Execute(model, scene, multi);
            Require(result.Success && result.Matches.Count == 3 && result.Matches[0].AmbiguityEvidence.IsAmbiguous
                && result.Matches[1].AmbiguityEvidence.IsAmbiguous && !result.Matches[2].AmbiguityEvidence.IsAmbiguous,
                "Multiple competition must use each available coarse scene domain: " + result.Message);
            HashSet<int> claimed = new HashSet<int>();
            foreach (DeterministicSurfaceMatchInstance instance in result.Matches)
            {
                SurfacePoseRefinementResult evidence = instance.RefinementEvidence;
                Require(evidence.Success && evidence.RetainedCorrespondenceFraction == 0.8 && evidence.LastRetainedCorrespondences.Count == 4
                    && evidence.Coverage.SceneSampleCount == scene.Count, "Multiple refinement lost original-count or retained evidence.");
                bool left = Math.Abs(instance.Pose.TranslationX - 4) < 0.01;
                bool middle = Math.Abs(instance.Pose.TranslationX - 14) < 0.01;
                foreach (SurfaceCoverageMatch pair in evidence.Coverage.Matches) Require(claimed.Add(pair.SceneSampleOrder), "Multiple raw observations overlapped.");
                foreach (SurfaceCoverageMatch pair in evidence.LastRetainedCorrespondences.Concat(evidence.LastFitCorrespondences))
                    Require(pair.SceneSampleOrder == (left ? 3 * (4 - pair.ModelSampleOrder) : 3 * pair.ModelSampleOrder + (middle ? 1 : 2)),
                        "Retained or fit pairs used compacted scene IDs.");
                if (instance.AmbiguityEvidence.AlternativeCoverage != null)
                {
                    Require(instance.AmbiguityEvidence.MinimumTranslationSeparation == 5 && instance.AmbiguityEvidence.TranslationSeparation > 9,
                        "Mapped ambiguity lost its translation snapshot.");
                    Require(instance.AmbiguityEvidence.AlternativeCoverage.Matches.All(p => p.SceneSampleOrder >= 0 && p.SceneSampleOrder < scene.Count), "Alternative IDs escaped the original scene.");
                    foreach (SurfaceCoverageMatch pair in instance.AmbiguityEvidence.AlternativeCoverage.Matches)
                    {
                        ThreeDPoint actual = instance.AmbiguityEvidence.AlternativePose.Transform(model[pair.ModelSampleOrder].Position);
                        ThreeDPoint expected = scene[pair.SceneSampleOrder].Position;
                        double distance = Math.Sqrt(Math.Pow(actual.X - expected.X, 2) + Math.Pow(actual.Y - expected.Y, 2) + Math.Pow(actual.Z - expected.Z, 2));
                        RequireApproximately(pair.Distance, distance, 1e-12, "Competing coverage retained compacted scene IDs after a prior claim.");
                    }
                }
            }
            Require(claimed.Count == 15, "A supported instance observation was removed.");
        }

        private static void TestRetainedSeeds()
        {
            DeterministicSurfacePoseRefinementTool tool = new DeterministicSurfacePoseRefinementTool();
            foreach (double scale in new[] { 0.001, 1D, 1000D })
            {
                SurfaceMatchSample[] model = SmallModel(scale);
                SurfaceMatchSample[] scene = model.Select(m => new SurfaceMatchSample(m.Order,
                    Truth(m.Position, 7.25, new ThreeDPoint(4 * scale, -3 * scale, 2 * scale)))).ToArray();
                DeterministicRigidSurfacePoseSearchOptions options = Options(5, 7.25, "trimmed-0.8");
                options.MinimumTranslationX = options.MinimumTranslationY = options.MinimumTranslationZ = -30 * scale;
                options.MaximumTranslationX = options.MaximumTranslationY = options.MaximumTranslationZ = 30 * scale;
                options.MaximumCorrespondenceDistance *= scale;
                RigidSurfacePose seed = Pose(8, new ThreeDPoint(4 * scale, -3 * scale, 1.7 * scale));
                SurfacePoseRefinementResult good = tool.Execute(model, scene, seed, options);
                Require(good.Success && good.Improved && good.TrimmedRmse < 1e-8 * scale, "Scale-aware bounded trim failed: " + scale);
                options.MinimumRotationZDegrees = 7.5;
                SurfacePoseRefinementResult bounds = tool.Execute(model, scene, seed, options);
                Require(bounds.Success && !bounds.Improved && bounds.StopReason == SurfacePoseRefinementStopReason.BoundsRejected
                    && ReferenceEquals(seed, bounds.Pose), "Trimming widened authored rotation bounds.");
                options.MinimumRotationZDegrees = 4.25;
                options.RefinementOptions.MinimumRmseImprovement = scale;
                SurfacePoseRefinementResult held = tool.Execute(model, scene, seed, options);
                Require(held.Success && !held.Improved && ReferenceEquals(seed, held.Pose)
                    && held.LastRetainedCorrespondences.Count == 4, "Unmet trimmed objective discarded a usable bounded seed.");
            }
            SurfaceMatchSample[] line = Enumerable.Range(0, 5).Select(i => new SurfaceMatchSample(i, new ThreeDPoint(i * 3, 0, 0))).ToArray();
            DeterministicRigidSurfacePoseSearchOptions lineOptions = Options(5, 0, "trimmed-0.8");
            SurfacePoseRefinementResult degenerate = tool.Execute(line, line, Pose(0, new ThreeDPoint(0, 0, 0)), lineOptions);
            Require(degenerate.Success && degenerate.StopReason == SurfacePoseRefinementStopReason.AlignmentRejected, "Trimmed pairs bypassed collinearity rejection.");
            SurfaceMatchSample[] three = SmallModel().Take(3).ToArray();
            DeterministicRigidSurfacePoseSearchOptions fewOptions = Options(3, 0, "trimmed-0.8");
            fewOptions.MinimumMatchedSampleCount = 3;
            SurfacePoseRefinementResult few = tool.Execute(three, three, Pose(0, new ThreeDPoint(0, 0, 0)), fewOptions);
            Require(few.Success && few.IterationCount == 0 && few.RetainedCorrespondenceCount == 4 && few.TrimmedRmse == null
                && few.StopReason == SurfacePoseRefinementStopReason.InsufficientCorrespondences, "Insufficient fixed-k evidence was fabricated.");

            SurfaceMatchSample[] supportModel = SmallModel();
            SurfaceMatchSample[] supportScene = supportModel.Select(m => new SurfaceMatchSample(m.Order,
                new ThreeDPoint(m.Position.X, m.Position.Y, m.Position.Z + (m.Order == 4 ? -0.28 : 0.05)))).ToArray();
            DeterministicRigidSurfacePoseSearchOptions supportOptions = Options(5, 0, "trimmed-0.8");
            supportOptions.MaximumCorrespondenceDistance = 0.3;
            SurfacePoseRefinementResult supportLoss = tool.Execute(supportModel, supportScene, Pose(0, new ThreeDPoint(0, 0, 0)), supportOptions);
            Require(supportLoss.Success && !supportLoss.Improved && supportLoss.StopReason == SurfacePoseRefinementStopReason.NoImprovement
                && supportLoss.Coverage.MatchedModelSampleCount == 5 && supportLoss.LastRetainedCorrespondences.Count == 4,
                "A lower trimmed residual was accepted by discarding full support.");
            SurfaceMatchSample[] expandingModel = supportModel.Concat(new[] { new SurfaceMatchSample(5, new ThreeDPoint(8, 8, 8)) }).ToArray();
            SurfaceMatchSample[] expandingScene = expandingModel.Select(m => new SurfaceMatchSample(m.Order,
                new ThreeDPoint(m.Position.X, m.Position.Y, m.Position.Z + (m.Order == 5 ? 0.29 : 0)))).ToArray();
            SurfacePoseRefinementResult expanding = tool.Execute(expandingModel, expandingScene, Pose(0, new ThreeDPoint(0, 0, -0.02)), supportOptions);
            Require(expanding.Success && expanding.Improved && expanding.InitialCoverage.MatchedModelSampleCount == 5
                && expanding.Coverage.MatchedModelSampleCount == 6 && expanding.RetainedCorrespondenceCount == 4,
                "Retained count changed after raw support expanded.");
        }

        private static void TestGuards()
        {
            SurfaceMatchSample[] model = SmallModel();
            DeterministicRigidSurfacePoseSearchTool search = new DeterministicRigidSurfacePoseSearchTool();
            DeterministicMultipleSurfaceMatchTool multiple = new DeterministicMultipleSurfaceMatchTool();
            DeterministicSurfacePoseRefinementTool refinement = new DeterministicSurfacePoseRefinementTool();
            DeterministicRigidSurfacePoseSearchOptions options = Options(5, 0, "trimmed-0.8");
            DeterministicMultipleSurfaceMatchOptions multi = new DeterministicMultipleSurfaceMatchOptions
            {
                PoseSearchOptions = options, MaximumMatchCount = 1, MaximumExpandedCandidateCount = 1000
            };
            foreach (double value in new[] { 0D, 0.5, 1D, -1, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                options.RefinementOptions.RetainedCorrespondenceFraction = value;
                Require(!search.Execute(model, model, options).Success && !multiple.Execute(model, model, multi).Success
                    && !refinement.Execute(model, model, Pose(0, new ThreeDPoint(0, 0, 0)), options).Success, "Malformed retained fraction escaped shared validation.");
            }
            options.RefinementOptions.RetainedCorrespondenceFraction = 0.8;
            options.AmbiguityOptions = Competition();
            foreach (double value in new[] { 0D, -1D, 0.6, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                options.AmbiguityOptions.MinimumTranslationSeparation = value;
                Require(!search.Execute(model, model, options).Success && !multiple.Execute(model, model, multi).Success
                    && !refinement.Execute(model, model, Pose(0, new ThreeDPoint(0, 0, 0)), options).Success, "Malformed translation policy escaped shared validation.");
            }
            options.AmbiguityOptions = Competition();
            Require(!search.Execute(Array.Empty<SurfaceMatchSample>(), model, options).Success
                && !refinement.Execute(model, Array.Empty<SurfaceMatchSample>(), Pose(0, new ThreeDPoint(0, 0, 0)), options).Success, "Empty input became a product refusal.");
            Require(search.Execute(model, model, options).Success && multiple.Execute(model, model, multi).Success
                && refinement.Execute(model, model, Pose(0, new ThreeDPoint(0, 0, 0)), options).Success, "Invalid input left caller state behind.");
            options.UseSamplePairTranslationCandidates = true;
            options.MaximumCandidateCount = 10;
            Require(!search.Execute(model, model, options).Success, "Competition options bypassed the authored coarse budget.");
            options.MaximumCandidateCount = 1000;
            Require(search.Execute(model, model, options).Success, "Budget failure prevented valid recovery.");
        }

        private static T ResidualCall<T>(string name, IReadOnlyList<SurfaceCoverageMatch> matches, int count, CancellationToken token)
        {
            MethodInfo method = typeof(DeterministicSurfacePoseRefinementTool).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
            try { return (T)method.Invoke(null, name == "SelectRetained" ? new object[] { matches, count, token } : new object[] { matches, token }); }
            catch (TargetInvocationException exception) when (exception.InnerException is OperationCanceledException)
            {
                throw (OperationCanceledException)exception.InnerException;
            }
        }

        private static void TestResidualMath()
        {
            SurfaceCoverageMatch[] ties = Enumerable.Range(0, 10).Reverse().Select(i => new SurfaceCoverageMatch(i, 20 - i, 0.25)).ToArray();
            SurfaceCoverageMatch[] selected = ResidualCall<SurfaceCoverageMatch[]>("SelectRetained", ties, 6, default);
            Require(selected.Select(m => m.ModelSampleOrder).SequenceEqual(Enumerable.Range(0, 6)), "Residual ties lost stable model/scene order.");
            foreach (double scale in new[] { 1e-200, 1D, 1e200 })
            {
                SurfaceCoverageMatch[] large = { new SurfaceCoverageMatch(0, 0, 3 * scale), new SurfaceCoverageMatch(1, 1, 4 * scale) };
                double actual = ResidualCall<double>("TrimmedRmse", large, 0, default);
                RequireApproximately(actual / scale, Math.Sqrt(12.5), 1e-14, "Residual norm overflowed or underflowed.");
            }
        }

        private sealed class CancellingList<T> : IReadOnlyList<T>
        {
            private readonly IReadOnlyList<T> source;
            private readonly CancellationTokenSource cancellation;
            private readonly int cancelAt;
            private int reads;
            internal CancellingList(IReadOnlyList<T> source, CancellationTokenSource cancellation, int cancelAt)
            {
                this.source = source; this.cancellation = cancellation; this.cancelAt = cancelAt;
            }
            public int Count => source.Count;
            public T this[int index] { get { if (++reads == cancelAt) cancellation.Cancel(); return source[index]; } }
            public IEnumerator<T> GetEnumerator() { for (int i = 0; i < Count; i++) yield return this[i]; }
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private static void TestCancellation()
        {
            SurfaceCoverageMatch[] matches = Enumerable.Range(0, 65).Select(i => new SurfaceCoverageMatch(i, i, 65 - i)).ToArray();
            foreach (int cancelAt in new[] { 1, 65 })
            {
                using CancellationTokenSource cancellation = new CancellationTokenSource();
                RequireThrows<OperationCanceledException>(() => ResidualCall<SurfaceCoverageMatch[]>("SelectRetained",
                    new CancellingList<SurfaceCoverageMatch>(matches, cancellation, cancelAt), 52, cancellation.Token));
            }
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                RequireThrows<OperationCanceledException>(() => ResidualCall<double>("TrimmedRmse",
                    new CancellingList<SurfaceCoverageMatch>(matches, cancellation, 65), 0, cancellation.Token));
            }
            Input input = Regression(257, 101, "clean", false);
            DeterministicSurfacePoseRefinementTool tool = new DeterministicSurfacePoseRefinementTool();
            int retainedCount = (int)Math.Ceiling(0.8 * input.Model.Length);
            foreach (int cancelAt in new[] { input.Model.Length * 2 + 10, input.Model.Length * 2 + retainedCount + 10,
                input.Model.Length * 2 + retainedCount + 64 + 10 })
            {
                using CancellationTokenSource cancellation = new CancellationTokenSource();
                RequireThrows<OperationCanceledException>(() => tool.Execute(
                    new CancellingList<SurfaceMatchSample>(input.Model, cancellation, cancelAt),
                    input.Scene, Seed(input), Options(input.Model.Length, input.Yaw, "trimmed-0.8"), cancellation.Token));
            }
            SurfacePoseRefinementResult recovered = tool.Execute(input.Model, input.Scene, Seed(input), Options(input.Model.Length, input.Yaw, "trimmed-0.8"));
            Require(recovered.Success && recovered.Improved && PoseError(input, recovered.Pose) < 1e-8, "Canceled refinement prevented same-instance recovery.");
            DeterministicRigidSurfacePoseSearchTool search = new DeterministicRigidSurfacePoseSearchTool();
            SurfaceMatchSample[] small = SmallModel();
            DeterministicRigidSurfacePoseSearchOptions options = Options(5, 0, "trimmed-0.8");
            options.AmbiguityOptions = Competition();
            options.UseSamplePairTranslationCandidates = true;
            options.MaximumCandidateCount = 1000;
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                RequireThrows<OperationCanceledException>(() => search.Execute(new CancellingList<SurfaceMatchSample>(small, cancellation, 30), small, options, cancellation.Token));
            }
            Require(search.Execute(small, small, options).Success, "Canceled competition prevented valid search recovery.");
            DeterministicMultipleSurfaceMatchTool multiple = new DeterministicMultipleSurfaceMatchTool();
            DeterministicMultipleSurfaceMatchOptions multi = new DeterministicMultipleSurfaceMatchOptions
            {
                PoseSearchOptions = options, MaximumMatchCount = 1, MaximumExpandedCandidateCount = 1000
            };
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                RequireThrows<OperationCanceledException>(() => multiple.Execute(new CancellingList<SurfaceMatchSample>(small, cancellation, 30), small, multi, cancellation.Token));
            }
            Require(multiple.Execute(small, small, multi).Success, "Canceled multiple competition prevented valid recovery.");
        }

        private sealed class Row
        {
            public string CaseId { get; set; }
            public string InputSha256 { get; set; }
            public bool NewHeldOut { get; set; }
            public string Challenge { get; set; }
            public string Policy { get; set; }
            public int Repeat { get; set; }
            public bool ExecutionFailed { get; set; }
            public bool Detected { get; set; }
            public bool ExpectedDetection { get; set; }
            public bool TruthViolation { get; set; }
            public double? MaximumPoseError { get; set; }
            public int WrongPhysicalCorrespondences { get; set; }
            public int InitialSupport { get; set; }
            public int Support { get; set; }
            public double? InitialRawRmse { get; set; }
            public double? RawRmse { get; set; }
            public double? InitialTrimmedRmse { get; set; }
            public double? TrimmedRmse { get; set; }
            public int RetainedCount { get; set; }
            public int FitPairs { get; set; }
            public int AcceptedIterations { get; set; }
            public string StopReason { get; set; }
            public bool EvidenceConsistent { get; set; }
            public double ElapsedMilliseconds { get; set; }
            public long ManagedAllocatedBytes { get; set; }
            public string Fingerprint { get; set; }
        }

        private static Row Evaluate(Input input, string policy, int repeat)
        {
            DeterministicSurfacePoseRefinementTool tool = new DeterministicSurfacePoseRefinementTool();
            RigidSurfacePose seed = Seed(input);
            if (!input.HeldOut)
            {
                // Already observed PL-0033/34 inputs are regression only. Reproduce PL-0034's balanced warm start before comparing the selected objective.
                seed = tool.Execute(input.Model, input.Scene, seed, Options(input.Model.Length, input.Yaw, "balanced-null")).Pose;
            }
            long allocated = GC.GetTotalAllocatedBytes(true);
            Stopwatch watch = Stopwatch.StartNew();
            SurfacePoseRefinementResult result = tool.Execute(input.Model, input.Scene, seed, Options(input.Model.Length, input.Yaw, policy));
            watch.Stop();
            long bytes = GC.GetTotalAllocatedBytes(true) - allocated;
            double error = PoseError(input, result.Pose);
            bool detected = result.Success && result.Coverage.MatchedModelSampleCount >= (int)Math.Ceiling(input.Model.Length * 0.65);
            bool expected = input.Challenge != "heavy" && input.Challenge != "absent";
            int wrong = result.Coverage?.Matches.Count(m => input.ModelIds[m.ModelSampleOrder] != input.SceneIds[m.SceneSampleOrder]) ?? 0;
            bool consistent = result.Success && result.Coverage.MatchedModelSampleCount >= result.InitialCoverage.MatchedModelSampleCount
                && result.LastFitCorrespondences.Count <= 64 && result.IterationCount <= 5;
            if (result.TrimmedRmse.HasValue) consistent &= result.TrimmedRmse <= result.InitialTrimmedRmse
                && result.RetainedCorrespondenceCount == Math.Max(4, Math.Max((int)Math.Ceiling(input.Model.Length * 0.65), (int)Math.Ceiling(0.8 * result.InitialCoverage.Matches.Count)));
            else consistent &= policy != "trimmed-0.8" || result.StopReason == SurfacePoseRefinementStopReason.InsufficientCorrespondences;
            Row row = new Row
            {
                CaseId = input.Id, InputSha256 = input.Hash, NewHeldOut = input.HeldOut, Challenge = input.Challenge, Policy = policy, Repeat = repeat,
                ExecutionFailed = !result.Success, Detected = detected, ExpectedDetection = expected,
                TruthViolation = detected && (!expected || error > 0.1 || wrong > 0) || !detected && expected,
                MaximumPoseError = double.IsInfinity(error) ? null : error, WrongPhysicalCorrespondences = wrong,
                InitialSupport = result.InitialCoverage?.MatchedModelSampleCount ?? 0, Support = result.Coverage?.MatchedModelSampleCount ?? 0,
                InitialRawRmse = result.InitialCoverage?.HasInlierRmse == true ? result.InitialCoverage.InlierRmse : null,
                RawRmse = result.Coverage?.HasInlierRmse == true ? result.Coverage.InlierRmse : null,
                InitialTrimmedRmse = result.InitialTrimmedRmse, TrimmedRmse = result.TrimmedRmse,
                RetainedCount = result.RetainedCorrespondenceCount, FitPairs = result.LastFitCorrespondences.Count,
                AcceptedIterations = result.AcceptedIterationCount, StopReason = result.StopReason.ToString(), EvidenceConsistent = consistent,
                ElapsedMilliseconds = watch.Elapsed.TotalMilliseconds, ManagedAllocatedBytes = bytes
            };
            // Absence has no RMSE: fingerprint only finite/null scalars plus immutable poses and pairs, never serialize NaN coverage fields.
            row.Fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            {
                result.Success, result.Pose, result.StopReason, result.IterationCount, result.AcceptedIterationCount,
                row.InitialSupport, row.Support, row.InitialRawRmse, row.RawRmse, result.InitialTrimmedRmse, result.TrimmedRmse,
                result.RetainedCorrespondenceCount, result.Coverage?.Matches, result.LastRetainedCorrespondences, result.LastFitCorrespondences
            })));
            return row;
        }

        internal static int RunEvaluation(string outputPath)
        {
            string path = Path.GetFullPath(outputPath);
            if (!path.StartsWith(@"D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Robustness evidence must use the project D: test root.");
            List<Input> inputs = new List<Input>();
            foreach (bool oldHeldOut in new[] { false, true })
            foreach (int count in oldHeldOut ? new[] { 389, 521 } : new[] { 257, 509 })
            foreach (int seed in oldHeldOut ? new[] { 307, 409 } : new[] { 101, 203 })
            foreach (string challenge in new[] { "biased-outliers", "near-competing-surface" }) inputs.Add(Regression(count, seed, challenge, oldHeldOut));
            foreach (int count in HeldOutSizes)
            foreach (int seed in HeldOutSeeds)
            foreach (string challenge in Challenges) inputs.Add(CreateInput(count, seed, challenge));
            List<Row> rows = new List<Row>();
            foreach (Input input in inputs)
            foreach (string policy in Policies)
            for (int repeat = 0; repeat < 3; repeat++) rows.Add(Evaluate(input, policy, repeat));
            bool stable = rows.GroupBy(r => new { r.CaseId, r.Policy }).All(g => g.Select(r => r.Fingerprint).Distinct().Count() == 1);
            var report = new
            {
                Schema = "surface-robustness-evaluation-v1", HeldOutSizes, HeldOutSeeds, Challenges, Policies,
                Scope = "Actual local SDK Execute from authored bounded seeds; old PL-0033/34 inputs are regression. New held-out equations/policies frozen before execution. Not global search, object identity, field accuracy or target Takt.",
                Policy = "65% full support, 0.8 correspondence distance, 5 iterations, <=64 fit pairs; optional retained fraction 0.8. Independent research gate: max pose error <=0.1 XYZ unit and correct physical IDs. No customer tolerances implied. Raw RMSE increases are permitted only with the selected trimmed objective.",
                Limitations = "Uniform bias and same-XYZ label swaps cannot be identified from coordinates alone; trimming does not guarantee robustness beyond the authored residual fraction or global convergence. Translation ambiguity has separate bounded coarse-search smoke evidence, not an identity guarantee.",
                Metrics = "Per-Execute elapsed wall time and managed allocations; excludes native heap, peak memory, acquisition and target hardware. Warm-up refinement is excluded from regression cost.",
                Runtime = Environment.Version.ToString(),
                Binary = new { Path = typeof(DeterministicSurfacePoseRefinementTool).Assembly.Location,
                    Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(DeterministicSurfacePoseRefinementTool).Assembly.Location))) },
                UniqueInputs = inputs.Count, NewHeldOutInputs = inputs.Count(i => i.HeldOut), Trials = rows.Count, RepeatedResultsConsistent = stable,
                Summary = rows.GroupBy(r => new { r.NewHeldOut, r.Challenge, r.Policy }).Select(g => new
                {
                    g.Key.NewHeldOut, g.Key.Challenge, g.Key.Policy, IndependentInputs = g.Select(r => r.CaseId).Distinct().Count(), Trials = g.Count(),
                    ExecutionErrors = g.Count(r => r.ExecutionFailed), TruthViolations = g.Count(r => r.TruthViolation),
                    Refusals = g.Count(r => !r.Detected), RawRmseIncreases = g.Count(r => r.RawRmse > r.InitialRawRmse),
                    MaximumPoseError = g.Where(r => r.MaximumPoseError.HasValue).Max(r => r.MaximumPoseError),
                    MedianElapsedMilliseconds = g.Select(r => r.ElapsedMilliseconds).OrderBy(v => v).ElementAt(g.Count() / 2),
                    MedianManagedAllocatedBytes = g.Select(r => r.ManagedAllocatedBytes).OrderBy(v => v).ElementAt(g.Count() / 2)
                }).ToArray(), Cases = rows
            };
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonSerializer.Serialize(report, ReportOptions));
            Console.WriteLine("Surface robustness | " + inputs.Count + " independent inputs / " + rows.Count + " trials; stable=" + stable
                + "; execution errors=" + rows.Count(r => r.ExecutionFailed) + "; retained truth violations=" + rows.Count(r => r.TruthViolation));
            return stable && rows.All(r => !r.ExecutionFailed && r.EvidenceConsistent) ? 0 : 1;
        }
    }
}

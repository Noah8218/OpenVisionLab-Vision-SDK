using OpenVisionLab.Vision3D.FeatureExtraction;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class SurfacePoseRefinementSmokeSuite
    {
        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase("Surface pose refinement recovers off-grid truth through the single search without changing defaults", TestSingleSearch);
            yield return new SmokeCase("Surface pose refinement rescans noisy partial support and background across coordinate scales", TestPartialNoise);
            yield return new SmokeCase("Surface pose refinement rejects rotation and translation outside the authored bounds", TestBounds);
            yield return new SmokeCase("Surface pose refinement retains supplementary singular and boundary Euler domains", TestEulerDomains);
            yield return new SmokeCase("Surface pose refinement retains usable seeds for insufficient degenerate or unimproved fits", TestRetainedSeeds);
            yield return new SmokeCase("Surface pose refinement maps disjoint multiple-instance evidence to original scene orders", TestMultipleSearch);
            yield return new SmokeCase("Surface pose refinement limits fitting to 64 pairs and scores all support before accepting", TestBoundedSampling);
            yield return new SmokeCase("Surface pose refinement snapshots evidence and recovers after invalid and canceled inputs", TestSnapshotAndRecovery);
        }

        private static SurfaceMatchSample[] Model(double scale = 1.0)
        {
            ThreeDPoint[] points =
            {
                new ThreeDPoint(0, 0, 0), new ThreeDPoint(3, 0, 0), new ThreeDPoint(0, 4, 0),
                new ThreeDPoint(0, 0, 2), new ThreeDPoint(2, 1, 3)
            };
            return points.Select((point, index) => new SurfaceMatchSample(index,
                new ThreeDPoint(point.X * scale, point.Y * scale, point.Z * scale))).ToArray();
        }

        // Independent fixture equations apply X, then Y, then Z to coordinates; no SDK rotation helper is used.
        private static ThreeDPoint Expected(ThreeDPoint point, double x, double y, double z, double tx = 4, double ty = -3, double tz = 2)
        {
            double radians = Math.PI / 180.0;
            double afterXy = point.Y * Math.Cos(x * radians) - point.Z * Math.Sin(x * radians);
            double afterXz = point.Y * Math.Sin(x * radians) + point.Z * Math.Cos(x * radians);
            double afterYx = point.X * Math.Cos(y * radians) + afterXz * Math.Sin(y * radians);
            double afterYz = -point.X * Math.Sin(y * radians) + afterXz * Math.Cos(y * radians);
            return new ThreeDPoint(
                afterYx * Math.Cos(z * radians) - afterXy * Math.Sin(z * radians) + tx,
                afterYx * Math.Sin(z * radians) + afterXy * Math.Cos(z * radians) + ty, afterYz + tz);
        }

        private static RigidSurfacePose Seed(double x = 0, double y = 0, double z = 10, double tx = 4, double ty = -3, double tz = 2)
        {
            ThreeDPoint first = Expected(new ThreeDPoint(1, 0, 0), x, y, z, 0, 0, 0);
            ThreeDPoint second = Expected(new ThreeDPoint(0, 1, 0), x, y, z, 0, 0, 0);
            ThreeDPoint third = Expected(new ThreeDPoint(0, 0, 1), x, y, z, 0, 0, 0);
            return new RigidSurfacePose(first.X, second.X, third.X, first.Y, second.Y, third.Y,
                first.Z, second.Z, third.Z, tx, ty, tz);
        }

        private static SurfaceMatchSample[] Scene(IReadOnlyList<SurfaceMatchSample> model, double x = 0, double y = 0, double z = 7.25, double tx = 4, double ty = -3, double tz = 2)
        {
            return model.Select(sample => new SurfaceMatchSample(sample.Order, Expected(sample.Position, x, y, z, tx, ty, tz))).ToArray();
        }

        private static DeterministicRigidSurfacePoseSearchOptions Options(bool refine = true)
        {
            return new DeterministicRigidSurfacePoseSearchOptions
            {
                MinimumRotationXDegrees = 0, MaximumRotationXDegrees = 0, RotationStepXDegrees = 10,
                MinimumRotationYDegrees = 0, MaximumRotationYDegrees = 0, RotationStepYDegrees = 10,
                MinimumRotationZDegrees = 0, MaximumRotationZDegrees = 10, RotationStepZDegrees = 10,
                MinimumTranslationX = -30, MaximumTranslationX = 30, MinimumTranslationY = -30,
                MaximumTranslationY = 30, MinimumTranslationZ = -30, MaximumTranslationZ = 30,
                MaximumCorrespondenceDistance = 0.8, MinimumMatchedSampleCount = 5, MaximumCandidateCount = 100,
                RefinementOptions = refine ? new SurfacePoseRefinementOptions() : null
            };
        }

        private static void RequirePose(RigidSurfacePose pose, IReadOnlyList<SurfaceMatchSample> model,
            double x, double y, double z, double tx = 4, double ty = -3, double tz = 2, double tolerance = 1e-9)
        {
            Require(pose != null && pose.IsRigid(1e-9), "A usable proper-rigid pose is required.");
            foreach (SurfaceMatchSample sample in model)
            {
                ThreeDPoint actual = pose.Transform(sample.Position);
                ThreeDPoint expected = Expected(sample.Position, x, y, z, tx, ty, tz);
                RequireApproximately(actual.X, expected.X, tolerance, "Independent transformed X changed.");
                RequireApproximately(actual.Y, expected.Y, tolerance, "Independent transformed Y changed.");
                RequireApproximately(actual.Z, expected.Z, tolerance, "Independent transformed Z changed.");
            }
        }

        private static void RequireScored(SurfacePoseRefinementResult result, IReadOnlyList<SurfaceMatchSample> model,
            IReadOnlyList<SurfaceMatchSample> scene)
        {
            Require(result.Success && result.Coverage.MatchedModelSampleCount >= result.InitialCoverage.MatchedModelSampleCount
                && result.Coverage.InlierRmse <= result.InitialCoverage.InlierRmse, "Refinement worsened full support or RMSE.");
            DeterministicSurfaceCoverageResult rescored = new DeterministicSurfaceCoverageTool().Execute(
                model, scene, result.Pose, result.Coverage.MaximumCorrespondenceDistance);
            Require(rescored.Success && rescored.MatchedModelSampleCount == result.Coverage.MatchedModelSampleCount
                && rescored.Matches.Select(match => match.SceneSampleOrder).SequenceEqual(result.Coverage.Matches.Select(match => match.SceneSampleOrder)),
                "Final evidence must rescore every model sample through the coverage contract.");
            RequireApproximately(result.Coverage.InlierRmse, rescored.InlierRmse, 0, "Final pose and coverage disagree.");
        }

        private static void TestSingleSearch()
        {
            SurfaceMatchSample[] model = Model();
            SurfaceMatchSample[] scene = Scene(model);
            DeterministicRigidSurfacePoseSearchTool tool = new DeterministicRigidSurfacePoseSearchTool();
            DeterministicRigidSurfacePoseSearchResult coarse = tool.Execute(model, scene, Options(false));
            DeterministicRigidSurfacePoseSearchResult refined = tool.Execute(model, scene, Options());
            Require(coarse.Success && coarse.Matched && coarse.RefinementEvidence == null && coarse.EvaluatedCandidateCount == 2,
                "The null-option grid path or its count changed.");
            RequireApproximately(Math.Atan2(coarse.Pose.M21, coarse.Pose.M11) * 180 / Math.PI, 10, 1e-12, "Legacy coarse yaw changed.");
            RequireApproximately(coarse.Coverage.InlierRmse, 0.09598389510951591, 1e-12, "The pinned coarse probe changed.");
            Require(refined.Success && refined.Matched && refined.RefinementEvidence.Improved
                && refined.EvaluatedCandidateCount == coarse.EvaluatedCandidateCount, "Actual single-search refinement was not used.");
            RequirePose(refined.Pose, model, 0, 0, 7.25);
            RequireScored(refined.RefinementEvidence, model, scene);
            RequireApproximately(refined.RefinementEvidence.InitialCoverage.InlierRmse, coarse.Coverage.InlierRmse, 0, "Seed coverage changed.");
            DeterministicRigidSurfacePoseSearchResult repeat = tool.Execute(model, scene, Options());
            Require(repeat.RefinementEvidence.IterationCount == refined.RefinementEvidence.IterationCount
                && repeat.RefinementEvidence.StopReason == refined.RefinementEvidence.StopReason, "Same-input refinement must be deterministic.");
            RequireApproximately(repeat.Pose.M21, refined.Pose.M21, 0, "Repeated pose changed.");
        }

        private static void TestPartialNoise()
        {
            foreach (double scale in new[] { 0.001, 1.0, 1000.0 })
            {
                List<SurfaceMatchSample> model = Model(scale).ToList();
                model.Add(new SurfaceMatchSample(5, new ThreeDPoint(8 * scale, 8 * scale, 8 * scale)));
                List<SurfaceMatchSample> scene = Scene(model.Take(5).ToArray(), tx: 4 * scale, ty: -3 * scale, tz: 2 * scale).Select((sample, index) =>
                    new SurfaceMatchSample(index, new ThreeDPoint(sample.Position.X + (index % 2 == 0 ? 0.003 : -0.002) * scale,
                        sample.Position.Y - 0.001 * scale, sample.Position.Z + 0.002 * scale))).ToList();
                scene.Add(new SurfaceMatchSample(5, new ThreeDPoint(100 * scale, 80 * scale, -90 * scale)));
                scene.Add(new SurfaceMatchSample(6, new ThreeDPoint(-100 * scale, -80 * scale, 90 * scale)));
                DeterministicRigidSurfacePoseSearchOptions options = Options();
                options.MaximumCorrespondenceDistance *= scale;
                options.MinimumTranslationX = options.MinimumTranslationY = options.MinimumTranslationZ = -30 * scale;
                options.MaximumTranslationX = options.MaximumTranslationY = options.MaximumTranslationZ = 30 * scale;
                DeterministicSurfacePoseRefinementTool tool = new DeterministicSurfacePoseRefinementTool();
                SurfacePoseRefinementResult fixedAxes = tool.Execute(
                    model, scene, Seed(tx: 4 * scale, ty: -3 * scale, tz: 2 * scale), options);
                Require(fixedAxes.Success && !fixedAxes.Improved && fixedAxes.StopReason == SurfacePoseRefinementStopReason.BoundsRejected,
                    "A noisy unconstrained fit must not silently relax fixed X/Y rotation axes.");
                options.MinimumRotationXDegrees = options.MinimumRotationYDegrees = -1;
                options.MaximumRotationXDegrees = options.MaximumRotationYDegrees = 1;
                SurfacePoseRefinementResult result = tool.Execute(
                    model, scene, Seed(tx: 4 * scale, ty: -3 * scale, tz: 2 * scale), options);
                Require(result.Success && result.Improved && result.Coverage.MatchedModelSampleCount == 5
                    && result.Coverage.UnmatchedModelSampleCount == 1 && result.Coverage.Matches.All(match => match.SceneSampleOrder < 5),
                    "Noise, missing support or background changed the authored matches: scale=" + scale + ", stop=" + result.StopReason + ", " + result.Message);
                RequireApproximately(Math.Atan2(result.Pose.M21, result.Pose.M11) * 180 / Math.PI, 7.25, 0.1, "Noisy off-grid yaw was not recovered.");
                Require(result.Coverage.InlierRmse < 0.01 * scale, "Noisy full-coverage residual exceeded the independent fixture bound.");
                RequireScored(result, model, scene);
            }
        }

        private static void TestBounds()
        {
            SurfaceMatchSample[] model = Model();
            DeterministicSurfacePoseRefinementTool tool = new DeterministicSurfacePoseRefinementTool();
            RigidSurfacePose seed = Seed();
            SurfacePoseRefinementResult angular = tool.Execute(model, Scene(model, z: 12), seed, Options());
            Require(angular.Success && !angular.Improved && angular.StopReason == SurfacePoseRefinementStopReason.BoundsRejected
                && ReferenceEquals(angular.Pose, seed), "A fitted yaw outside the domain must retain the seed.");
            DeterministicRigidSurfacePoseSearchOptions options = Options();
            options.MinimumRotationZDegrees = options.MaximumRotationZDegrees = 7.25;
            options.MinimumTranslationX = 3.7;
            options.MaximumTranslationX = 3.9;
            seed = Seed(z: 7.25, tx: 3.8);
            SurfacePoseRefinementResult translation = tool.Execute(model, Scene(model), seed, options);
            Require(translation.Success && !translation.Improved && translation.StopReason == SurfacePoseRefinementStopReason.BoundsRejected
                && ReferenceEquals(translation.Pose, seed), "Translation constraints must not be widened for a better fit.");
            RequireScored(angular, model, Scene(model, z: 12));
            RequireScored(translation, model, Scene(model));
        }

        private static void TestEulerDomains()
        {
            (double X, double Y, double Z, double SeedX, double SeedY, double SeedZ, double XMin, double XMax, double YMin, double YMax, double ZMin, double ZMax)[] cases =
            {
                (3.25, 120, 7.25, 3, 119.5, 7, 0, 10, 110, 130, 0, 10),
                (3.25, -120, 7.25, 3, -119.5, 7, 0, 10, -130, -110, 0, 10),
                (15, 90, 8, 14, 90, 8, 12, 20, 90, 90, 0, 10),
                (15, -90, 8, 14, -90, 8, 12, 20, -90, -90, 0, 10),
                (0, 0, 180, 0, 0, 179.5, 0, 0, 0, 0, 170, 180),
                (0, 0, -180, 0, 0, -179.5, 0, 0, 0, 0, -180, -170)
            };
            foreach (var item in cases)
            {
                SurfaceMatchSample[] model = Model();
                SurfaceMatchSample[] scene = Scene(model, item.X, item.Y, item.Z);
                DeterministicRigidSurfacePoseSearchOptions options = Options();
                options.MinimumRotationXDegrees = item.XMin;
                options.MaximumRotationXDegrees = item.XMax;
                options.MinimumRotationYDegrees = item.YMin;
                options.MaximumRotationYDegrees = item.YMax;
                options.MinimumRotationZDegrees = item.ZMin;
                options.MaximumRotationZDegrees = item.ZMax;
                SurfacePoseRefinementResult result = new DeterministicSurfacePoseRefinementTool().Execute(
                    model, scene, Seed(item.SeedX, item.SeedY, item.SeedZ), options);
                Require(result.Success && result.Improved, "An equivalent bounded Euler representation was lost: " + item);
                RequirePose(result.Pose, model, item.X, item.Y, item.Z);
                RequireScored(result, model, scene);
            }
        }

        private static void TestRetainedSeeds()
        {
            SurfaceMatchSample[] triangle = Model().Take(3).ToArray();
            DeterministicRigidSurfacePoseSearchOptions options = Options();
            options.MinimumMatchedSampleCount = 3;
            DeterministicRigidSurfacePoseSearchResult three = new DeterministicRigidSurfacePoseSearchTool().Execute(triangle, Scene(triangle), options);
            Require(three.Success && three.Matched && !three.RefinementEvidence.Improved
                && three.RefinementEvidence.IterationCount == 0
                && three.RefinementEvidence.StopReason == SurfacePoseRefinementStopReason.InsufficientCorrespondences,
                "Three-point coarse matching must remain usable without invoking the four-pair solver.");
            SurfaceMatchSample[] line = Enumerable.Range(0, 4).Select(index => new SurfaceMatchSample(index, new ThreeDPoint(3 * index, 0, 0))).ToArray();
            DeterministicSurfacePoseRefinementTool tool = new DeterministicSurfacePoseRefinementTool();
            SurfacePoseRefinementResult degenerate = tool.Execute(line, Scene(line), Seed(), options);
            Require(degenerate.Success && !degenerate.Improved && degenerate.StopReason == SurfacePoseRefinementStopReason.AlignmentRejected,
                "Collinear pairs must retain a usable seed with an alignment rejection.");
            SurfaceMatchSample[] duplicate = Model().Take(4).ToArray();
            duplicate[3] = new SurfaceMatchSample(3, duplicate[0].Position);
            SurfacePoseRefinementResult duplicated = tool.Execute(duplicate, Scene(duplicate), Seed(), options);
            Require(duplicated.Success && duplicated.StopReason == SurfacePoseRefinementStopReason.AlignmentRejected, "Duplicate fit geometry must not bypass solver validation.");
            options = Options();
            options.RefinementOptions.MinimumRmseImprovement = 1;
            RigidSurfacePose seed = Seed();
            SurfacePoseRefinementResult held = tool.Execute(Model(), Scene(Model()), seed, options);
            Require(held.Success && held.StopReason == SurfacePoseRefinementStopReason.NoImprovement && !held.Improved
                && ReferenceEquals(held.Pose, seed), "An unmet authored improvement must retain the seed.");
            options.RefinementOptions.MinimumRmseImprovement = 0;
            options.RefinementOptions.MaximumIterations = 1;
            SurfacePoseRefinementResult limited = tool.Execute(Model(), Scene(Model()), seed, options);
            Require(limited.Success && limited.Improved && limited.IterationCount == 1 && limited.AcceptedIterationCount == 1
                && limited.StopReason == SurfacePoseRefinementStopReason.IterationLimit, "The authored one-step limit was not honored.");
            options.RefinementOptions.MaximumIterations = 20;
            SurfacePoseRefinementResult maximum = tool.Execute(Model(), Scene(Model()), seed, options);
            Require(maximum.Success && maximum.IterationCount <= 20 && maximum.MaximumIterations == 20, "The supported iteration ceiling changed.");
            RequirePose(limited.Pose, Model(), 0, 0, 7.25);
            RequireScored(held, Model(), Scene(Model()));
        }

        private static void TestMultipleSearch()
        {
            SurfaceMatchSample[] model = Model();
            SurfaceMatchSample[] first = Scene(model);
            SurfaceMatchSample[] second = Scene(model, z: 2.25, tx: 14, ty: 1, tz: 4);
            List<SurfaceMatchSample> scene = new List<SurfaceMatchSample>();
            for (int index = 0; index < model.Length; index++)
            {
                scene.Add(new SurfaceMatchSample(scene.Count, second[index].Position));
                scene.Add(new SurfaceMatchSample(scene.Count, first[model.Length - 1 - index].Position));
            }

            scene.Add(new SurfaceMatchSample(scene.Count, new ThreeDPoint(100, 80, 90)));
            scene.Add(new SurfaceMatchSample(scene.Count, new ThreeDPoint(-100, -80, -90)));
            DeterministicMultipleSurfaceMatchOptions options = new DeterministicMultipleSurfaceMatchOptions
            {
                PoseSearchOptions = Options(), MaximumMatchCount = 2, MaximumExpandedCandidateCount = 1000
            };
            DeterministicMultipleSurfaceMatchTool tool = new DeterministicMultipleSurfaceMatchTool();
            DeterministicMultipleSurfaceMatchResult result = tool.Execute(model, scene, options);
            Require(result.Success && result.Matches.Count == 2, "Both off-grid instances must be found through the actual multiple-search path.");
            HashSet<int> claimed = new HashSet<int>();
            foreach (DeterministicSurfaceMatchInstance match in result.Matches)
            {
                Require(match.RefinementEvidence != null && match.RefinementEvidence.Improved
                    && match.Coverage.SceneSampleCount == scene.Count && match.RefinementEvidence.InitialCoverage.SceneSampleCount == scene.Count,
                    "Mapped instance evidence must retain original scene counts.");
                bool isFirst = Math.Abs(match.Pose.TranslationX - 4) < 0.01;
                RequirePose(match.Pose, model, 0, 0, isFirst ? 7.25 : 2.25, isFirst ? 4 : 14, isFirst ? -3 : 1, isFirst ? 2 : 4);
                foreach (SurfaceCoverageMatch pair in match.Coverage.Matches)
                {
                    Require(pair.SceneSampleOrder < 10 && claimed.Add(pair.SceneSampleOrder), "Refinement reused another instance or background evidence.");
                    ThreeDPoint expected = scene[pair.SceneSampleOrder].Position;
                    ThreeDPoint actual = match.Pose.Transform(model[pair.ModelSampleOrder].Position);
                    RequireApproximately(actual.X, expected.X, 1e-9, "Final original-order X mapping changed.");
                    RequireApproximately(actual.Y, expected.Y, 1e-9, "Final original-order Y mapping changed.");
                    RequireApproximately(actual.Z, expected.Z, 1e-9, "Final original-order Z mapping changed.");
                }

                foreach (SurfaceCoverageMatch pair in match.RefinementEvidence.LastFitCorrespondences)
                {
                    Require(pair.SceneSampleOrder == (isFirst ? 2 * (4 - pair.ModelSampleOrder) + 1 : 2 * pair.ModelSampleOrder),
                        "Last fit evidence was left in the compacted available-scene numbering.");
                }
            }

            Require(claimed.SetEquals(Enumerable.Range(0, 10)), "A supported object sample was lost or duplicated.");
            options.PoseSearchOptions.RefinementOptions = null;
            DeterministicMultipleSurfaceMatchResult coarse = tool.Execute(model, scene, options);
            Require(coarse.Success && coarse.Matches.Count == 2 && coarse.Matches.All(match => match.RefinementEvidence == null)
                && coarse.EvaluatedCandidateCount == result.EvaluatedCandidateCount, "Refinement changed expanded grid counts or null defaults.");
        }

        private static void TestBoundedSampling()
        {
            SurfaceMatchSample[] model = Enumerable.Range(0, 80).Select(index => new SurfaceMatchSample(index,
                new ThreeDPoint(3 * index, 4 * (index % 3), 2 * (index % 7)))).ToArray();
            DeterministicRigidSurfacePoseSearchOptions options = Options();
            options.MinimumRotationZDegrees = 0;
            options.MaximumRotationZDegrees = 0.2;
            options.RotationStepZDegrees = 0.1;
            DeterministicSurfacePoseRefinementTool tool = new DeterministicSurfacePoseRefinementTool();
            SurfacePoseRefinementResult recovered = tool.Execute(model, Scene(model, z: 0.07), Seed(z: 0.1), options);
            int[] selected = Enumerable.Range(0, 64).Select(index => index * 79 / 63).ToArray();
            Require(recovered.Success && recovered.Improved && recovered.Coverage.MatchedModelSampleCount == 80
                && recovered.LastFitCorrespondences.Select(match => match.ModelSampleOrder).SequenceEqual(selected), "Bounded fit sampling or full coverage changed.");
            RequirePose(recovered.Pose, model, 0, 0, 0.07);
            options.MaximumRotationZDegrees = 0;
            foreach (bool loseSupport in new[] { false, true })
            {
                HashSet<int> fitOrders = new HashSet<int>(selected);
                SurfaceMatchSample[] biased = model.Select(sample => new SurfaceMatchSample(sample.Order,
                    new ThreeDPoint(sample.Position.X + (fitOrders.Contains(sample.Order) ? (loseSupport ? -0.1 : 0.1) : (loseSupport ? 0.4 : -0.4)),
                        sample.Position.Y, sample.Position.Z))).ToArray();
                options.MaximumCorrespondenceDistance = loseSupport ? 0.45 : 0.8;
                RigidSurfacePose seed = Seed(z: 0, tx: 0, ty: 0, tz: 0);
                SurfacePoseRefinementResult rejected = tool.Execute(model, biased, seed, options);
                Require(rejected.Success && !rejected.Improved && rejected.StopReason == SurfacePoseRefinementStopReason.NoImprovement
                    && ReferenceEquals(rejected.Pose, seed) && rejected.Coverage.MatchedModelSampleCount == 80,
                    "A 64-pair improvement must not hide worse all-sample RMSE or lost support.");
                RequireApproximately(rejected.Coverage.InlierRmse, 0.2, 1e-12, "Independent full-support RMS changed.");
                RequireScored(rejected, model, biased);
            }
        }

        private static void TestSnapshotAndRecovery()
        {
            List<SurfaceMatchSample> model = Model().ToList();
            List<SurfaceMatchSample> scene = Scene(model).ToList();
            DeterministicRigidSurfacePoseSearchOptions options = Options();
            DeterministicSurfacePoseRefinementTool tool = new DeterministicSurfacePoseRefinementTool();
            SurfacePoseRefinementResult snapshot = tool.Execute(model, scene, Seed(), options);
            Require(snapshot.Success && snapshot.Improved, "A snapshot seed must improve.");
            options.MaximumRotationZDegrees = 99;
            options.RefinementOptions.MaximumIterations = 1;
            model.Clear();
            scene.Clear();
            Require(snapshot.MaximumIterations == 10 && snapshot.RotationBoundsDegrees[5] == 10
                && snapshot.InitialCoverage.MatchedModelSampleCount == 5 && snapshot.Coverage.Matches.Count == 5,
                "Caller mutation changed evidence snapshots.");
            RequireThrows<NotSupportedException>(() => ((IList<SurfaceCoverageMatch>)snapshot.InitialCoverage.Matches).Clear());
            RequireThrows<NotSupportedException>(() => ((IList<SurfaceCoverageMatch>)snapshot.Coverage.Matches).Clear());
            RequireThrows<NotSupportedException>(() => ((IList<SurfaceCoverageMatch>)snapshot.LastFitCorrespondences).Clear());
            RequireThrows<NotSupportedException>(() => ((IList<double>)snapshot.RotationBoundsDegrees)[0] = 1);
            Require(new DeterministicSurfaceMatchInstance(0, 0, null, null).RefinementEvidence == null, "The existing public instance constructor changed.");

            SurfaceMatchSample[] valid = Model();
            SurfaceMatchSample[] actual = Scene(valid);
            foreach (int limit in new[] { 0, 21 })
            {
                options = Options();
                options.RefinementOptions.MaximumIterations = limit;
                RequireFailed(tool.Execute(valid, actual, Seed(), options));
                Require(!new DeterministicRigidSurfacePoseSearchTool().Execute(valid, actual, options).Success, "Single search ignored invalid opt-in.");
                Require(!new DeterministicMultipleSurfaceMatchTool().Execute(valid, actual, new DeterministicMultipleSurfaceMatchOptions
                {
                    PoseSearchOptions = options, MaximumMatchCount = 1, MaximumExpandedCandidateCount = 100
                }).Success, "Multiple search ignored invalid opt-in.");
            }

            foreach (double improvement in new[] { -1.0, double.NaN, double.PositiveInfinity })
            {
                options = Options();
                options.RefinementOptions.MinimumRmseImprovement = improvement;
                RequireFailed(tool.Execute(valid, actual, Seed(), options));
            }

            SurfaceMatchSample[] invalid = Model();
            invalid[2] = new SurfaceMatchSample(2, new ThreeDPoint(double.NaN, 0, 0));
            RequireFailed(tool.Execute(invalid, actual, Seed(), Options()));
            RequireFailed(tool.Execute(null, actual, Seed(), Options()));
            RequireFailed(tool.Execute(valid, actual, null, Options()));
            RequireFailed(tool.Execute(valid, actual, Seed(z: 20), Options()));
            RequireFailed(tool.Execute(valid, actual, new RigidSurfacePose(-1, 0, 0, 0, 1, 0, 0, 0, 1, 4, -3, 2), Options()));
            RequireFailed(tool.Execute(valid, actual, Seed(), Options(false)));
            RequireFailed(tool.Execute(valid, actual, Seed(), null));
            using (CancellationTokenSource canceled = new CancellationTokenSource())
            {
                canceled.Cancel();
                RequireThrows<OperationCanceledException>(() => tool.Execute(valid, actual, Seed(), Options(), canceled.Token));
            }

            using (CancellationTokenSource canceled = new CancellationTokenSource())
            {
                CancelingSamples running = new CancelingSamples(valid, canceled, 17);
                RequireThrows<OperationCanceledException>(() => tool.Execute(running, actual, Seed(), Options(), canceled.Token));
                Require(running.ReadCount == 17, "The deterministic cancellation must occur after initial coverage, during fit-pair preparation.");
            }

            SurfacePoseRefinementResult recovered = tool.Execute(valid, actual, Seed(), Options());
            Require(recovered.Success && recovered.Improved, "Invalid or canceled execution poisoned subsequent valid calls.");
            RequirePose(recovered.Pose, valid, 0, 0, 7.25);
        }

        private static void RequireFailed(SurfacePoseRefinementResult result)
        {
            Require(!result.Success && result.Pose == null && result.Coverage == null
                && result.StopReason == SurfacePoseRefinementStopReason.ExecutionFailed && !string.IsNullOrWhiteSpace(result.Message),
                "Invalid execution must fail with typed evidence and no usable pose.");
        }

        private sealed class CancelingSamples : IReadOnlyList<SurfaceMatchSample>
        {
            private readonly IReadOnlyList<SurfaceMatchSample> samples;
            private readonly CancellationTokenSource cancellation;
            private readonly int cancelAt;

            internal CancelingSamples(IReadOnlyList<SurfaceMatchSample> samples, CancellationTokenSource cancellation, int cancelAt)
            {
                this.samples = samples;
                this.cancellation = cancellation;
                this.cancelAt = cancelAt;
            }

            internal int ReadCount { get; private set; }
            public int Count => samples.Count;
            public SurfaceMatchSample this[int index]
            {
                get
                {
                    ReadCount++;
                    if (ReadCount == cancelAt) cancellation.Cancel();
                    return samples[index];
                }
            }

            public IEnumerator<SurfaceMatchSample> GetEnumerator() => samples.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}

using OpenVisionLab.Vision3D.FeatureExtraction;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class SurfaceSearchRobustnessSmokeSuite
    {
        private static readonly int[] PartialSceneOrders = { 1, 3, 4, 5 };
        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase("Surface robustness anchors recover partial objects and centroid bias within budget", TestAnchors);
            yield return new SmokeCase("Surface robustness reports coarse orientation alternatives and preserves defaults", TestAmbiguity);
            yield return new SmokeCase("Surface robustness maps disjoint multiple ambiguity to original scene orders", TestMultipleAmbiguity);
            yield return new SmokeCase("Surface robustness validates options and recovers after cancellation", TestGuards);
            yield return new SmokeCase("Surface robustness indexed coverage equals exhaustive greedy order and residuals", TestIndexedCoverage);
            yield return new SmokeCase("Surface robustness adaptive axes preserve exhaustive ties boundaries and original IDs", TestAdaptiveIndexAxes);
            yield return new SmokeCase("Surface robustness cancels index construction without retaining state", TestIndexCancellation);
            yield return new SmokeCase("Surface proposals recover dense original coverage beyond the all-pair budget", TestDenseProposals);
            yield return new SmokeCase("Surface proposals preserve partial background support and disjoint original IDs", TestPartialAndMultipleProposals);
            yield return new SmokeCase("Surface proposals validate policy budgets ambiguity cancellation and recovery", TestProposalGuards);
        }

        internal static SurfaceMatchSample[] Points(params double[][] points)
        {
            return points.Select((point, index) => new SurfaceMatchSample(index, new ThreeDPoint(point[0], point[1], point[2]))).ToArray();
        }

        internal static DeterministicRigidSurfacePoseSearchOptions Options()
        {
            return new DeterministicRigidSurfacePoseSearchOptions
            {
                RotationStepXDegrees = 1, RotationStepYDegrees = 1, RotationStepZDegrees = 90,
                MinimumTranslationX = -100, MaximumTranslationX = 100,
                MinimumTranslationY = -100, MaximumTranslationY = 100,
                MinimumTranslationZ = -100, MaximumTranslationZ = 100,
                MaximumCorrespondenceDistance = 0.01, MinimumMatchedSampleCount = 3, MaximumCandidateCount = 10000
            };
        }

        private static SurfaceMatchSample[] Model()
        {
            return Points(new[] { 0.0, 0, 0 }, new[] { 3.0, 0, 0 }, new[] { 0.0, 4, 0 },
                new[] { 0.0, 0, 5 }, new[] { 2.0, 3, 1 }, new[] { 1.0, 2, 4 });
        }

        private static void TestAnchors()
        {
            SurfaceMatchSample[] model = Model();
            SurfaceMatchSample[] scene = Points(new[] { 10.0, -4, 2 }, new[] { 13.0, -4, 2 },
                new[] { 10.0, 0, 2 }, new[] { 10.0, -4, 7 }, new[] { 80.0, 70, 60 }, new[] { -20.0, 30, -40 });
            DeterministicRigidSurfacePoseSearchTool tool = new DeterministicRigidSurfacePoseSearchTool();
            DeterministicRigidSurfacePoseSearchOptions options = Options();
            DeterministicRigidSurfacePoseSearchResult legacy = tool.Execute(model, scene, options);
            Require(legacy.Success && !legacy.Matched && legacy.EvaluatedCandidateCount == 1, "Centroid default changed.");
            options.UseSamplePairTranslationCandidates = true;
            options.MaximumCandidateCount = 37;
            DeterministicRigidSurfacePoseSearchResult result = tool.Execute(model, scene, options);
            Require(result.Success && result.Matched && result.EvaluatedCandidateCount == 37
                && result.Coverage.MatchedModelSampleCount == 4, "Anchors must recover exactly four visible model points.");
            RequireApproximately(result.Pose.TranslationX, 10, 0, "Authored X translation changed.");
            RequireApproximately(result.Pose.TranslationY, -4, 0, "Authored Y translation changed.");
            RequireApproximately(result.Pose.TranslationZ, 2, 0, "Authored Z translation changed.");
            options.MaximumCandidateCount = 36;
            Require(!tool.Execute(model, scene, options).Success, "Expanded anchors must fail preflight rather than truncate the grid.");
            options.MaximumCandidateCount = 37;
            options.RefinementOptions = new SurfacePoseRefinementOptions { MaximumIterations = 2 };
            result = tool.Execute(model, scene, options);
            Require(result.Success && result.Matched && result.RefinementEvidence != null && result.Coverage.MatchedModelSampleCount == 4,
                "Existing refinement must remain available after anchor recovery.");
        }

        private static SurfaceMatchSample[] Square(double offset = 0)
        {
            return Points(new[] { offset - 1, -1.0, 0 }, new[] { offset + 1, -1.0, 0 },
                new[] { offset + 1, 1.0, 0 }, new[] { offset - 1, 1.0, 0 });
        }

        private static SurfacePoseAmbiguityOptions Ambiguity()
        {
            return new SurfacePoseAmbiguityOptions { MinimumRotationSeparationDegrees = 45, MaximumTranslationSeparation = 1e-9, MaximumRmseDifference = 1e-12 };
        }

        private static void TestAmbiguity()
        {
            DeterministicRigidSurfacePoseSearchTool tool = new DeterministicRigidSurfacePoseSearchTool();
            DeterministicRigidSurfacePoseSearchOptions options = Options();
            options.MaximumRotationZDegrees = 180;
            DeterministicRigidSurfacePoseSearchResult legacy = tool.Execute(Square(), Square(), options);
            Require(legacy.Success && legacy.Matched && legacy.AmbiguityEvidence == null, "Default must retain deterministic selection without an orientation claim.");
            options.AmbiguityOptions = Ambiguity();
            DeterministicRigidSurfacePoseSearchResult result = tool.Execute(Square(), Square(), options);
            SurfacePoseAmbiguityEvidence evidence = result.AmbiguityEvidence;
            Require(result.Success && result.Matched && evidence.IsAmbiguous && evidence.EvaluatedCandidateCount == 3,
                "A square must expose a competing orientation while retaining location evidence.");
            Require(evidence.ReferenceCoverage.MatchedModelSampleCount == 4 && evidence.AlternativeCoverage.MatchedModelSampleCount == 4,
                "Both symmetric modes must retain independently evaluated support.");
            RequireApproximately(result.Pose.M11, legacy.Pose.M11, 0, "Ambiguity evaluation must preserve default selection.");
            Require(evidence.RotationSeparationDegrees >= 45 && evidence.AlternativePose != null, "Distinct mode evidence missing.");
            options.AmbiguityOptions.MaximumRmseDifference = 0;
            Require(evidence.MaximumRmseDifference == 1e-12, "Returned ambiguity policy must be a snapshot.");
            options.AmbiguityOptions = Ambiguity();
            result = tool.Execute(Model(), Model(), options);
            Require(result.Success && result.Matched && !result.AmbiguityEvidence.IsAmbiguous,
                "An asymmetric full-XYZ constellation must preserve its single supported mode.");
            options.MaximumRotationZDegrees = 0;
            result = tool.Execute(Model(), Model(), options);
            Require(!result.AmbiguityEvidence.IsAmbiguous && result.AmbiguityEvidence.AlternativePose == null,
                "A single authored rotation has no evaluated distinct alternative; this is a bounded-domain result.");
        }

        private static void TestMultipleAmbiguity()
        {
            SurfaceMatchSample[] scene = Points(Square(10).Concat(Square(-10))
                .Select(sample => new[] { sample.Position.X, sample.Position.Y, sample.Position.Z }).ToArray());
            DeterministicRigidSurfacePoseSearchOptions pose = Options();
            pose.MaximumRotationZDegrees = 90;
            pose.AmbiguityOptions = Ambiguity();
            DeterministicMultipleSurfaceMatchOptions options = new DeterministicMultipleSurfaceMatchOptions
            {
                PoseSearchOptions = pose, MaximumMatchCount = 2, MaximumExpandedCandidateCount = 128
            };
            DeterministicMultipleSurfaceMatchResult result = new DeterministicMultipleSurfaceMatchTool().Execute(Square(), scene, options);
            Require(result.Success && result.Matches.Count == 2, "Two separated square instances must remain disjoint.");
            HashSet<int> claimed = new HashSet<int>();
            foreach (DeterministicSurfaceMatchInstance match in result.Matches)
            {
                Require(match.AmbiguityEvidence.IsAmbiguous, "Every symmetric instance must retain ambiguity evidence.");
                foreach (SurfaceCoverageMatch pair in match.Coverage.Matches) Require(claimed.Add(pair.SceneSampleOrder), "Instance evidence overlapped.");
                int[] orders = match.Coverage.Matches.Select(pair => pair.SceneSampleOrder).OrderBy(value => value).ToArray();
                Require(orders.SequenceEqual(match.AmbiguityEvidence.ReferenceCoverage.Matches.Select(pair => pair.SceneSampleOrder).OrderBy(value => value))
                    && orders.SequenceEqual(match.AmbiguityEvidence.AlternativeCoverage.Matches.Select(pair => pair.SceneSampleOrder).OrderBy(value => value)),
                    "Ambiguity reference and alternative must use the original scene IDs, including the second scene slice.");
            }
            SurfaceMatchSample[] asymmetric = Model();
            SurfaceMatchSample[] separated = Points(asymmetric.Select(sample => new[] { sample.Position.X + 10, sample.Position.Y, sample.Position.Z })
                .Concat(asymmetric.Select(sample => new[] { -sample.Position.Y - 10, sample.Position.X, sample.Position.Z })).ToArray());
            pose.MinimumMatchedSampleCount = 6;
            options.MaximumExpandedCandidateCount = 512;
            result = new DeterministicMultipleSurfaceMatchTool().Execute(asymmetric, separated, options);
            Require(result.Success && result.Matches.Count == 2 && result.Matches.All(match => !match.AmbiguityEvidence.IsAmbiguous),
                "A distinctly rotated object at another position must not be an orientation alternative of this instance.");
        }

        private static void TestGuards()
        {
            DeterministicRigidSurfacePoseSearchTool tool = new DeterministicRigidSurfacePoseSearchTool();
            DeterministicRigidSurfacePoseSearchOptions options = Options();
            foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, 0, -1, 181 })
            {
                options.AmbiguityOptions = new SurfacePoseAmbiguityOptions { MinimumRotationSeparationDegrees = invalid };
                Require(!tool.Execute(Model(), Model(), options).Success, "Invalid ambiguity separation must fail.");
            }
            foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, -1 })
            {
                options.AmbiguityOptions = Ambiguity();
                options.AmbiguityOptions.MaximumRmseDifference = invalid;
                Require(!tool.Execute(Model(), Model(), options).Success, "Invalid ambiguity RMSE tolerance must fail.");
            }
            foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, -1 })
            {
                options.AmbiguityOptions = Ambiguity();
                options.AmbiguityOptions.MaximumTranslationSeparation = invalid;
                Require(!tool.Execute(Model(), Model(), options).Success, "Invalid instance association distance must fail.");
            }
            options.AmbiguityOptions = Ambiguity();
            options.AmbiguityOptions.MaximumMatchedSampleCountDifference = 7;
            Require(!tool.Execute(Model(), Model(), options).Success, "Support difference cannot exceed model count.");
            options.AmbiguityOptions = Ambiguity();
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            RequireThrows<OperationCanceledException>(() => tool.Execute(Model(), Model(), options, cancellation.Token));
            Require(tool.Execute(Model(), Model(), options).Matched, "The same tool must recover after invalid policy and cancellation.");
        }

        private static void TestIndexedCoverage()
        {
            DeterministicSurfaceCoverageTool tool = new DeterministicSurfaceCoverageTool();
            RigidSurfacePose identity = new RigidSurfacePose(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0);
            foreach (int count in new[] { 63, 64, 129, 513 })
            foreach (double scale in new[] { 1e-150, 1.0, 1e150 })
            {
                Random random = new Random(7103 + count);
                SurfaceMatchSample[] scene = Points(Enumerable.Range(0, count).Select(index => index < 4
                    ? new[] { (index % 2 == 0 ? -1 : 1) * scale, 0.0, 0.0 }
                    : new[] { (random.Next(41) - 20) * scale, (random.Next(41) - 20) * scale, (random.Next(41) - 20) * scale }).ToArray());
                SurfaceMatchSample[] model = Points(new[] { 0.0, 0, 0 }, new[] { 0.0, 0, 0 },
                    new[] { 0.0, 0, 0 }, new[] { 0.0, 0, 0 }, new[] { 5 * scale, 6 * scale, 7 * scale });
                model = Points(model.Select(sample => new[] { sample.Position.X, sample.Position.Y, sample.Position.Z })
                    .Concat(Enumerable.Range(0, 27).Select(index => new[] { (index - 13) * scale, 2 * scale, 3 * scale })).ToArray());
                double radius = scale;
                DeterministicSurfaceCoverageResult actual = tool.Execute(model, scene, identity, radius);
                IReadOnlyList<SurfaceCoverageMatch> oracle = Exhaustive(model, scene, radius);
                Require(actual.Success && actual.Matches.Count == oracle.Count, "Indexed match count differs from exhaustive contract.");
                for (int index = 0; index < oracle.Count; index++)
                {
                    SurfaceCoverageMatch expected = oracle[index];
                    SurfaceCoverageMatch pair = actual.Matches[index];
                    Require(pair.ModelSampleOrder == expected.ModelSampleOrder && pair.SceneSampleOrder == expected.SceneSampleOrder
                        && BitConverter.DoubleToInt64Bits(pair.Distance) == BitConverter.DoubleToInt64Bits(expected.Distance),
                        "Inclusive boundary, greedy claims, distance bits, or tie order changed.");
                }
            }

            SurfaceMatchSample[] extremes = Points(Enumerable.Range(0, 64).Select(index => new[] { index == 0 ? 1e308 : -1e308, 0.0, 0.0 }).ToArray());
            SurfaceMatchSample[] largeModel = Enumerable.Range(0, 32).Select(index => new SurfaceMatchSample(index, new ThreeDPoint(1e308, 0, 0))).ToArray();
            Require(tool.Execute(largeModel, extremes, identity, 1).Matches[0].SceneSampleOrder == 0,
                "Extreme finite coordinate equality must remain searchable.");
            RigidSurfacePose overflow = new RigidSurfacePose(1, 0, 0, 0, 1, 0, 0, 0, 1, 1e308, 0, 0);
            Require(tool.Execute(largeModel, extremes, overflow, 1).MatchedModelSampleCount == 0,
                "A nonfinite transformed coordinate must retain the exhaustive no-correspondence result.");
        }

        // Frozen pre-index exhaustive contract oracle: no production index/search helper is called.
        private static List<SurfaceCoverageMatch> Exhaustive(SurfaceMatchSample[] model, SurfaceMatchSample[] scene, double radius)
        {
            List<SurfaceCoverageMatch> matches = new List<SurfaceCoverageMatch>();
            HashSet<int> claimed = new HashSet<int>();
            foreach (SurfaceMatchSample sample in model)
            {
                var nearest = scene.Where(candidate => !claimed.Contains(candidate.Order))
                    .Select(candidate => new { candidate.Order, Distance = Distance(sample.Position, candidate.Position) })
                    .OrderBy(candidate => candidate.Distance).ThenBy(candidate => candidate.Order).FirstOrDefault();
                if (nearest == null || nearest.Distance > radius || double.IsInfinity(nearest.Distance)) continue;
                claimed.Add(nearest.Order);
                matches.Add(new SurfaceCoverageMatch(sample.Order, nearest.Order, nearest.Distance));
            }
            return matches;
        }

        private static double Distance(ThreeDPoint first, ThreeDPoint second)
        {
            double[] delta = { Math.Abs(first.X - second.X), Math.Abs(first.Y - second.Y), Math.Abs(first.Z - second.Z) };
            double scale = delta.Max();
            return scale == 0 ? 0 : double.IsInfinity(scale) ? double.PositiveInfinity
                : scale * Math.Sqrt(delta[0] / scale * (delta[0] / scale) + delta[1] / scale * (delta[1] / scale) + delta[2] / scale * (delta[2] / scale));
        }


        private static void TestAdaptiveIndexAxes()
        {
            DeterministicSurfaceCoverageTool tool = new DeterministicSurfaceCoverageTool();
            RigidSurfacePose identity = new RigidSurfacePose(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0);
            foreach (int permutation in new[] { 0, 1, 2 })
            {
                List<double[]> coordinates = Enumerable.Range(0, 192).Select(i =>
                {
                    double narrow = i % 7 * 1e-6, wide = i % 32, other = i / 32;
                    return permutation == 0 ? new[] { narrow, wide, other }
                        : permutation == 1 ? new[] { wide, other, narrow } : new[] { other, narrow, wide };
                }).ToList();
                coordinates.Add(coordinates[0].ToArray());
                SurfaceMatchSample[] scene = Points(coordinates.ToArray());
                SurfaceMatchSample[] model = Points(Enumerable.Range(0, 32).Select(i =>
                {
                    double[] point = coordinates[i * 5].ToArray();
                    point[(permutation + 1) % 3] += i % 3 == 0 ? 0.5 : 0;
                    return point;
                }).ToArray());
                IReadOnlyList<SurfaceCoverageMatch> expected = Exhaustive(model, scene, 0.5);
                DeterministicSurfaceCoverageResult actual = tool.Execute(model, scene, identity, 0.5);
                Require(actual.Success && actual.Matches.Count == expected.Count, "Adaptive-axis coverage changed exhaustive support.");
                for (int i = 0; i < expected.Count; i++)
                {
                    Require(actual.Matches[i].ModelSampleOrder == expected[i].ModelSampleOrder
                        && actual.Matches[i].SceneSampleOrder == expected[i].SceneSampleOrder, "Adaptive-axis pruning changed an original ID or exact tie.");
                    RequireApproximately(actual.Matches[i].Distance, expected[i].Distance, 0, "Adaptive-axis pruning changed an inclusive residual.");
                }
            }
            SurfaceMatchSample[] line = Points(Enumerable.Range(0, 128).Select(i => new[] { 0.0, 0.0, i * 1.0 }).ToArray());
            SurfaceMatchSample[] same = Points(Enumerable.Range(0, 128).Select(i => new double[3]).ToArray());
            foreach (SurfaceMatchSample[] scene in new[] { line, same })
            {
                SurfaceMatchSample[] model = Points(Enumerable.Range(0, 32).Select(i => new[] { 0.0, 0.0, i * 1.0 }).ToArray());
                IReadOnlyList<SurfaceCoverageMatch> expected = Exhaustive(model, scene, 0.001);
                DeterministicSurfaceCoverageResult actual = tool.Execute(model, scene, identity, 0.001);
                Require(actual.Success && actual.Matches.Select(m => m.SceneSampleOrder).SequenceEqual(expected.Select(m => m.SceneSampleOrder)),
                    "Axis selection must preserve collinear and fully coincident behavior.");
            }
        }

        private static void TestIndexCancellation()
        {
            SurfaceMatchSample[] samples = Points(Enumerable.Range(0, 4096).Select(index => new[] { index * 1.0, 0.0, 0.0 }).ToArray());
            DeterministicSurfaceCoverageTool tool = new DeterministicSurfaceCoverageTool();
            RigidSurfacePose pose = new RigidSurfacePose(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0);
            foreach (int cancelAt in new[] { samples.Length + 128, 2 * samples.Length + 2 })
            {
                using CancellationTokenSource cancellation = new CancellationTokenSource();
                CancellingSamples input = new CancellingSamples(samples, cancellation, cancelAt);
                RequireThrows<OperationCanceledException>(() => tool.Execute(
                    Points(Enumerable.Range(0, 32).Select(index => new[] { 0.0, 0, 0 }).ToArray()), input, pose, 1, cancellation.Token));
                Require(input.ReadCount >= cancelAt && input.ReadCount <= cancelAt + 1024, "Cancellation must reach index copying or the first sort comparison after validation.");
                Require(tool.Execute(Points(new[] { 0.0, 0, 0 }), samples, pose, 1).MatchedModelSampleCount == 1,
                    "Cancelled index construction or sort must permit recovery.");
            }
        }

        private static void TestDenseProposals()
        {
            SurfaceMatchSample[] model = Points(Enumerable.Range(0, 256).Select(i => new[]
            {
                (i * 37 % 101) * 0.17, (i * 13 % 53) * 0.13, (i * 7 % 29) * 0.11
            }).ToArray());
            List<double[]> coordinates = model.Select(p => new[] { p.Position.X + 10, p.Position.Y - 4, p.Position.Z + 2 }).ToList();
            coordinates.AddRange(Enumerable.Range(0, 8192 - coordinates.Count).Select(i => new[]
            {
                1000.0 + (i % 128) * 7, 1200.0 + (i / 128) * 5, 100.0 + (i % 19) * 11
            }));
            SurfaceMatchSample[] scene = Points(coordinates.ToArray());
            DeterministicRigidSurfacePoseSearchTool tool = new DeterministicRigidSurfacePoseSearchTool();
            DeterministicRigidSurfacePoseSearchOptions options = Options();
            options.UseSamplePairTranslationCandidates = true;
            options.MaximumCandidateCount = DeterministicRigidSurfacePoseSearchTool.AbsoluteMaximumCandidateCount;
            Require(!tool.Execute(model, scene, options).Success, "The legacy all-pair budget must remain unchanged.");
            options.ProposalOptions = new SurfacePoseProposalOptions { MaximumModelAnchorCount = 8, MaximumSceneAnchorCount = 32 };
            options.MaximumCandidateCount = 257;
            DeterministicRigidSurfacePoseSearchResult result = tool.Execute(model, scene, options);
            Require(result.Success && result.Matched, result.Message + result.RejectionReason);
            Require(result.EvaluatedCandidateCount == 257 && result.Coverage.MatchedModelSampleCount == 256
                && result.Coverage.ModelSampleCount == 256 && result.Coverage.SceneSampleCount == 8192,
                "Reduced proposals must be followed by full original coverage.");
            RequireApproximately(result.Pose.TranslationX, 10, 1e-12, "Dense proposal X differs from independent truth.");
            RequireApproximately(result.Pose.TranslationY, -4, 1e-12, "Dense proposal Y differs from independent truth.");
            RequireApproximately(result.Pose.TranslationZ, 2, 1e-12, "Dense proposal Z differs from independent truth.");
            Require(result.Coverage.Matches.Select(m => m.SceneSampleOrder).SequenceEqual(Enumerable.Range(0, 256)),
                "Proposal selection must not replace original source IDs.");
            SurfacePoseProposalEvidence evidence = result.ProposalEvidence;
            Require(evidence.ModelSampleCount == 256 && evidence.SceneSampleCount == 8192
                && evidence.ModelSampleOrders.Count == 8 && evidence.SceneSampleOrders.Count == 32,
                "Proposal evidence lost the evaluated domain.");
            RequireThrows<NotSupportedException>(() => ((IList<int>)evidence.ModelSampleOrders)[0] = 999);
            options.MaximumCandidateCount = 256;
            Require(!tool.Execute(model, scene, options).Success, "Reduced preflight must refuse rather than truncate.");
        }

        private static void TestPartialAndMultipleProposals()
        {
            SurfaceMatchSample[] model = Model();
            SurfaceMatchSample[] partial = Points(new[] { -20.0, 30, -40 }, new[] { 10.0, -4, 2 },
                new[] { 80.0, 70, 60 }, new[] { 13.0, -4, 2 }, new[] { 10.0, 0, 2 }, new[] { 10.0, -4, 7 });
            DeterministicRigidSurfacePoseSearchOptions options = Options();
            options.UseSamplePairTranslationCandidates = true;
            options.ProposalOptions = new SurfacePoseProposalOptions { MaximumModelAnchorCount = 4, MaximumSceneAnchorCount = 4 };
            DeterministicRigidSurfacePoseSearchResult single = new DeterministicRigidSurfacePoseSearchTool().Execute(model, partial, options);
            Require(single.Success && single.Matched && single.Coverage.MatchedModelSampleCount == 4,
                "Reduced proposals lost authored partial/background support.");
            RequireApproximately(single.Pose.TranslationX, 10, 0, "Partial proposal pose changed.");
            Require(single.Coverage.Matches.Select(m => m.SceneSampleOrder).OrderBy(i => i).SequenceEqual(PartialSceneOrders),
                "Partial proposal source ID mapping changed.");
            List<double[]> scene = new List<double[]> { new[] { -20.0, 30, -40 } };
            scene.AddRange(model.Select(p => new[] { p.Position.X + 10, p.Position.Y - 4, p.Position.Z + 2 }));
            scene.Add(new[] { 80.0, 70, 60 });
            scene.AddRange(model.Select(p => new[] { p.Position.X + 30, p.Position.Y + 6, p.Position.Z - 3 }));
            options.ProposalOptions = new SurfacePoseProposalOptions { MaximumModelAnchorCount = 3, MaximumSceneAnchorCount = 32 };
            options.RefinementOptions = new SurfacePoseRefinementOptions { MaximumIterations = 2 };
            DeterministicMultipleSurfaceMatchResult multiple = new DeterministicMultipleSurfaceMatchTool().Execute(
                model, Points(scene.ToArray()), new DeterministicMultipleSurfaceMatchOptions
                { PoseSearchOptions = options, MaximumMatchCount = 2, MaximumExpandedCandidateCount = 84 });
            Require(multiple.Success && multiple.Matches.Count == 2, multiple.Message);
            int[] ids = multiple.Matches.SelectMany(m => m.Coverage.Matches.Select(pair => pair.SceneSampleOrder)).ToArray();
            Require(ids.Distinct().Count() == 12 && ids.OrderBy(i => i).SequenceEqual(Enumerable.Range(1, 6).Concat(Enumerable.Range(8, 6))),
                "Reduced multiple matching must preserve disjoint original evidence.");
            foreach (DeterministicSurfaceMatchInstance match in multiple.Matches)
                Require(match.Coverage.MatchedModelSampleCount == 6 && match.Coverage.SceneSampleCount == 14
                    && match.RefinementEvidence != null && match.ProposalEvidence.ModelSampleOrders.Count == 3,
                    "Reduced multiple coverage/refinement must use original full inputs.");
        }

        private static void TestProposalGuards()
        {
            DeterministicRigidSurfacePoseSearchTool tool = new DeterministicRigidSurfacePoseSearchTool();
            DeterministicRigidSurfacePoseSearchOptions options = Options();
            options.ProposalOptions = new SurfacePoseProposalOptions();
            Require(!tool.Execute(Model(), Model(), options).Success, "Single reduced proposals require explicit anchors.");
            options.UseSamplePairTranslationCandidates = true;
            foreach (int count in new[] { 0, 1025 })
            {
                options.ProposalOptions.MaximumSceneAnchorCount = count;
                Require(!tool.Execute(Model(), Model(), options).Success, "Invalid proposal counts must be refused.");
            }

            options.ProposalOptions = new SurfacePoseProposalOptions { MaximumModelAnchorCount = 4, MaximumSceneAnchorCount = 4 };
            options.MaximumRotationZDegrees = 180;
            options.AmbiguityOptions = Ambiguity();
            DeterministicRigidSurfacePoseSearchResult symmetric = tool.Execute(Square(), Square(), options);
            Require(symmetric.Success && symmetric.Matched && symmetric.AmbiguityEvidence.IsAmbiguous
                && symmetric.ProposalEvidence.ModelSampleOrders.Count == 4, "Proposal ambiguity must evaluate the same domain.");
            options = Options();
            options.UseSamplePairTranslationCandidates = true;
            options.ProposalOptions = new SurfacePoseProposalOptions { MaximumModelAnchorCount = 3, MaximumSceneAnchorCount = 4 };
            SurfaceMatchSample[] large = Points(Enumerable.Range(0, 512).Select(i => new[] { i * 1.0, (i % 7) * 2.0, (i % 11) * 3.0 }).ToArray());
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                RequireThrows<OperationCanceledException>(() => tool.Execute(Model(), large, options, cancellation.Token));
            }

            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                CancellingSamples input = new CancellingSamples(large, cancellation, large.Length + 5);
                RequireThrows<OperationCanceledException>(() => tool.Execute(Model(), input, options, cancellation.Token));
                Require(input.ReadCount <= large.Length + 5 + 1024, "Proposal-copy cancellation exceeded the read interval.");
            }

            Require(tool.Execute(Model(), Model(), options).Matched, "Reduced proposals did not recover after cancellation.");
        }

        private sealed class CancellingSamples : IReadOnlyList<SurfaceMatchSample>
        {
            private readonly SurfaceMatchSample[] samples;
            private readonly CancellationTokenSource cancellation;
            private readonly int cancelAt;
            internal int ReadCount { get; private set; }
            internal CancellingSamples(SurfaceMatchSample[] samples, CancellationTokenSource cancellation, int cancelAt)
            {
                this.samples = samples; this.cancellation = cancellation; this.cancelAt = cancelAt;
            }
            public int Count => samples.Length;
            public SurfaceMatchSample this[int index]
            {
                get { if (++ReadCount == cancelAt) cancellation.Cancel(); return samples[index]; }
            }
            public IEnumerator<SurfaceMatchSample> GetEnumerator() => ((IEnumerable<SurfaceMatchSample>)samples).GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}

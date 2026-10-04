using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using OpenVisionLab.Vision3D.FeatureExtraction;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class SurfaceChallengeEvaluationSmokeSuite
    {
        private static readonly int[] DevelopmentSizes = { 257, 509 };
        private static readonly int[] HeldOutSizes = { 389, 521 };
        private static readonly int[] DevelopmentSeeds = { 101, 203 };
        private static readonly int[] HeldOutSeeds = { 307, 409 };
        private static readonly string[] Challenges = { "clean", "correlated-noise", "density", "near-competing-surface", "partial", "heavy-occlusion", "biased-outliers", "permuted-order" };
        private static readonly JsonSerializerOptions ReportOptions = new JsonSerializerOptions { WriteIndented = true };

        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase("Surface challenge spatial fit reduces measured ordered noise bias while preserving default bounds full support and 64 pairs", TestSpatialFit);
            yield return new SmokeCase("Surface challenge spatial fit cancellation snapshots and valid recovery preserve existing owners", TestCancellation);
            yield return new SmokeCase("Surface challenge detects symmetric orientation alternatives separately from execution errors", TestSymmetry);
            yield return new SmokeCase("Surface challenge development and held-out inputs have independent identities and analytic truth", TestSplit);
        }

        private static DeterministicRigidSurfacePoseSearchOptions Options(int modelCount, bool balanced)
        {
            return new DeterministicRigidSurfacePoseSearchOptions
            {
                MinimumRotationXDegrees = -2, MaximumRotationXDegrees = 2, RotationStepXDegrees = 4,
                MinimumRotationYDegrees = -2, MaximumRotationYDegrees = 2, RotationStepYDegrees = 4,
                MinimumRotationZDegrees = 5, MaximumRotationZDegrees = 10, RotationStepZDegrees = 5,
                MinimumTranslationX = -30, MaximumTranslationX = 30, MinimumTranslationY = -30, MaximumTranslationY = 30,
                MinimumTranslationZ = -30, MaximumTranslationZ = 30, MaximumCorrespondenceDistance = 0.8,
                MinimumMatchedSampleCount = (int)Math.Ceiling(modelCount * 0.65), MaximumCandidateCount = 100,
                RefinementOptions = new SurfacePoseRefinementOptions { MaximumIterations = 5, UseSpatiallyBalancedFit = balanced }
            };
        }

        // Independent fixture equation; no production rotation/pose/search helper generates truth.
        private static ThreeDPoint Truth(ThreeDPoint point)
        {
            double radians = 7.25 * Math.PI / 180D;
            return new ThreeDPoint(point.X * Math.Cos(radians) - point.Y * Math.Sin(radians) + 4,
                point.X * Math.Sin(radians) + point.Y * Math.Cos(radians) - 3, point.Z + 2);
        }

        private static RigidSurfacePose Seed()
        {
            double radians = 8 * Math.PI / 180D;
            return new RigidSurfacePose(Math.Cos(radians), -Math.Sin(radians), 0, Math.Sin(radians), Math.Cos(radians), 0, 0, 0, 1, 4, -3, 1.7);
        }

        internal sealed class Input
        {
            internal string Id;
            internal string Challenge;
            internal bool HeldOut;
            internal SurfaceMatchSample[] Model;
            internal SurfaceMatchSample[] Scene;
            internal int[] ModelPhysicalIds;
            internal int[] ScenePhysicalIds;
            internal string Hash;
        }

        internal static Input CreateInput(int count, int seed, string challenge, bool heldOut)
        {
            Random random = new Random(seed);
            ThreeDPoint[] positions = Enumerable.Range(0, count).Select(i => new ThreeDPoint(
                i % 19 + (random.NextDouble() - 0.5) * 0.03, i / 19 + (random.NextDouble() - 0.5) * 0.03,
                2 * Math.Sin(i * (heldOut ? 0.61 : 0.7)) + Math.Cos(i * (heldOut ? 0.27 : 0.31)))).ToArray();
            HashSet<int> orderedStratum = new HashSet<int>(Enumerable.Range(0, 64).Select(i => (int)((long)i * (count - 1) / 63)));
            List<SurfaceMatchSample> scene = new List<SurfaceMatchSample>();
            List<int> physical = new List<int>();
            void Add(ThreeDPoint p, int id) { scene.Add(new SurfaceMatchSample(scene.Count, p)); physical.Add(id); }
            for (int i = 0; i < positions.Length; i++)
            {
                if (challenge == "partial" && i % 4 == 1 || challenge == "heavy-occlusion" && i % 3 != 0) continue;
                ThreeDPoint truth = Truth(positions[i]);
                double noise = challenge == "correlated-noise" || challenge == "permuted-order"
                    ? orderedStratum.Contains(i) ? 0.2 : -0.2 * 64 / (count - 64)
                    : challenge == "biased-outliers" && i % 5 == 0 ? 0.28 : 0D;
                Add(new ThreeDPoint(truth.X, truth.Y, truth.Z + noise), i);
            }
            if (challenge == "density")
            {
                for (int i = 0; i < positions.Length; i += 3)
                {
                    ThreeDPoint p = Truth(positions[i]);
                    for (int j = 1; j <= 4; j++) Add(new ThreeDPoint(p.X + j * 0.004, p.Y - j * 0.003, p.Z), i);
                }
            }
            if (challenge == "near-competing-surface")
            {
                foreach (ThreeDPoint p in positions.Select(Truth)) Add(new ThreeDPoint(p.X, p.Y, p.Z - 0.28), -1);
            }
            // Distant background is shared; new near-surface and correlated-noise cases are the added coverage.
            int backgroundCount = Math.Max(17, (int)Math.Ceiling(count * 0.65) - scene.Count + 17);
            for (int i = 0; i < backgroundCount; i++) Add(new ThreeDPoint(80 + i, -50 - 2 * i, 60 + i % 3), -1);
            int[] order = Enumerable.Range(0, count).ToArray();
            if (challenge == "permuted-order")
            {
                for (int i = order.Length - 1; i > 0; i--)
                {
                    int j = random.Next(i + 1);
                    (order[i], order[j]) = (order[j], order[i]);
                }
            }
            SurfaceMatchSample[] model = order.Select((id, i) => new SurfaceMatchSample(i, positions[id])).ToArray();
            byte[] serialized = JsonSerializer.SerializeToUtf8Bytes(new
            {
                Model = model.Select(p => new[] { p.Position.X, p.Position.Y, p.Position.Z }),
                Scene = scene.Select(p => new[] { p.Position.X, p.Position.Y, p.Position.Z }), ModelIds = order, SceneIds = physical
            });
            return new Input
            {
                Id = count + "/" + seed + "/" + challenge, Challenge = challenge, HeldOut = heldOut, Model = model, Scene = scene.ToArray(),
                ModelPhysicalIds = order, ScenePhysicalIds = physical.ToArray(), Hash = Convert.ToHexString(SHA256.HashData(serialized))
            };
        }

        private static double PoseError(Input input, RigidSurfacePose pose)
        {
            if (pose == null) return double.PositiveInfinity;
            return input.Model.Max(sample =>
            {
                ThreeDPoint expected = Truth(sample.Position), actual = pose.Transform(sample.Position);
                double x = expected.X - actual.X, y = expected.Y - actual.Y, z = expected.Z - actual.Z;
                return Math.Sqrt(x * x + y * y + z * z);
            });
        }

        private static void TestSpatialFit()
        {
            foreach (int count in DevelopmentSizes)
            {
                Input input = CreateInput(count, 101, "correlated-noise", false);
                DeterministicSurfacePoseRefinementTool tool = new DeterministicSurfacePoseRefinementTool();
                SurfacePoseRefinementResult ordered = tool.Execute(input.Model, input.Scene, Seed(), Options(count, false));
                SurfacePoseRefinementResult balanced = tool.Execute(input.Model, input.Scene, Seed(), Options(count, true));
                Require(ordered.Success && balanced.Success && ordered.Improved && balanced.Improved, "Both declared local fits must execute and improve their seed.");
                Require(PoseError(input, balanced.Pose) < PoseError(input, ordered.Pose) * 0.6
                    && balanced.Coverage.InlierRmse < ordered.Coverage.InlierRmse, "Measured ordered-stratum bias did not improve.");
                Require(balanced.UseSpatiallyBalancedFit && !ordered.UseSpatiallyBalancedFit
                    && balanced.LastFitCorrespondences.Count <= 64 && balanced.Coverage.MatchedModelSampleCount == count
                    && balanced.Coverage.Matches.All(m => input.ModelPhysicalIds[m.ModelSampleOrder] == input.ScenePhysicalIds[m.SceneSampleOrder]),
                    "Spatial fitting lost its bound, original correspondence or full support.");
                SurfacePoseRefinementResult repeat = tool.Execute(input.Model, input.Scene, Seed(), Options(count, true));
                Require(repeat.Pose.M11 == balanced.Pose.M11 && repeat.Pose.TranslationZ == balanced.Pose.TranslationZ
                    && repeat.LastFitCorrespondences.Select(m => m.ModelSampleOrder).SequenceEqual(balanced.LastFitCorrespondences.Select(m => m.ModelSampleOrder)),
                    "Spatial selection/refinement is not deterministic for stable input order.");
                DeterministicSurfaceCoverageResult rescored = new DeterministicSurfaceCoverageTool().Execute(input.Model, input.Scene, balanced.Pose, 0.8);
                Require(rescored.InlierRmse == balanced.Coverage.InlierRmse && rescored.Matches.Count == count, "Final evidence omitted full-model rescoring.");
            }
        }

        private sealed class CancellingSamples : IReadOnlyList<SurfaceMatchSample>
        {
            private readonly SurfaceMatchSample[] values;
            private readonly CancellationTokenSource cancellation;
            private readonly int cancelAt;
            private int reads;
            internal CancellingSamples(SurfaceMatchSample[] values, CancellationTokenSource cancellation, int cancelAt)
            { this.values = values; this.cancellation = cancellation; this.cancelAt = cancelAt; }
            public int Count => values.Length;
            public SurfaceMatchSample this[int index]
            {
                get { if (++reads == cancelAt) cancellation.Cancel(); return values[index]; }
            }
            public IEnumerator<SurfaceMatchSample> GetEnumerator() => ((IEnumerable<SurfaceMatchSample>)values).GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private static void TestCancellation()
        {
            Input input = CreateInput(257, 101, "clean", false);
            DeterministicSurfacePoseRefinementTool tool = new DeterministicSurfacePoseRefinementTool();
            DeterministicRigidSurfacePoseSearchOptions options = Options(input.Model.Length, true);
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            CancellingSamples model = new CancellingSamples(input.Model, cancellation, input.Model.Length * 2 + 10);
            RequireThrows<OperationCanceledException>(() => tool.Execute(model, input.Scene, Seed(), options, cancellation.Token));
            SurfacePoseRefinementResult valid = tool.Execute(input.Model, input.Scene, Seed(), options);
            Require(valid.Success && valid.Improved && PoseError(input, valid.Pose) < 1e-8, "Spatial-fit cancellation prevented valid recovery.");
            options.RefinementOptions.UseSpatiallyBalancedFit = false;
            Require(valid.UseSpatiallyBalancedFit, "Refinement evidence retained mutable options.");
        }

        private static DeterministicRigidSurfacePoseSearchResult SymmetryResult()
        {
            SurfaceMatchSample[] cube = Enumerable.Range(0, 8).Select(i => new SurfaceMatchSample(i,
                new ThreeDPoint((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))).ToArray();
            DeterministicRigidSurfacePoseSearchOptions options = SurfaceSearchRobustnessSmokeSuite.Options();
            options.MaximumRotationZDegrees = 90;
            options.MinimumMatchedSampleCount = 8;
            options.AmbiguityOptions = new SurfacePoseAmbiguityOptions
            {
                MinimumRotationSeparationDegrees = 45, MaximumMatchedSampleCountDifference = 0, MaximumRmseDifference = 1e-10
            };
            return new DeterministicRigidSurfacePoseSearchTool().Execute(cube, cube, options);
        }

        private static void TestSymmetry()
        {
            DeterministicRigidSurfacePoseSearchResult result = SymmetryResult();
            Require(result.Success && result.Matched && result.AmbiguityEvidence.IsAmbiguous
                && result.Coverage.MatchedModelSampleCount == 8, "Symmetric orientation must remain explicit evidence, not an execution failure.");
        }

        private static void TestSplit()
        {
            Require(!DevelopmentSizes.Intersect(HeldOutSizes).Any() && !DevelopmentSeeds.Intersect(HeldOutSeeds).Any(),
                "Development and held-out geometry sizes/seeds overlap.");
            Input dev = CreateInput(257, 101, "clean", false), held = CreateInput(389, 307, "clean", true);
            Require(dev.Hash != held.Hash && dev.Model.Length != held.Model.Length, "Held-out identity reused development inputs.");
            RigidSurfacePose truth = new RigidSurfacePose(Math.Cos(7.25 * Math.PI / 180), -Math.Sin(7.25 * Math.PI / 180), 0,
                Math.Sin(7.25 * Math.PI / 180), Math.Cos(7.25 * Math.PI / 180), 0, 0, 0, 1, 4, -3, 2);
            Require(PoseError(held, truth) == 0, "Evaluation truth is inconsistent with its independent analytic fixture.");
        }

        private sealed class Row
        {
            public string CaseId { get; set; }
            public string InputSha256 { get; set; }
            public string Challenge { get; set; }
            public bool HeldOut { get; set; }
            public bool BalancedFit { get; set; }
            public int Repeat { get; set; }
            public bool ExecutionFailed { get; set; }
            public bool Detected { get; set; }
            public bool ExpectedDetection { get; set; }
            public bool FalseAcceptance { get; set; }
            public bool FalseRejection { get; set; }
            public double? MaximumPoseError { get; set; }
            public double? InlierRmse { get; set; }
            public int WrongPhysicalCorrespondences { get; set; }
            public int Support { get; set; }
            public int FitPairs { get; set; }
            public string StopReason { get; set; }
            public double ElapsedMilliseconds { get; set; }
            public long ManagedAllocatedBytes { get; set; }
            public string Fingerprint { get; set; }
        }

        private static Row Evaluate(Input input, bool balanced, int repeat)
        {
            long allocated = GC.GetTotalAllocatedBytes(true);
            Stopwatch watch = Stopwatch.StartNew();
            SurfacePoseRefinementResult result = new DeterministicSurfacePoseRefinementTool().Execute(
                input.Model, input.Scene, Seed(), Options(input.Model.Length, balanced));
            watch.Stop();
            double error = PoseError(input, result.Pose);
            bool expected = input.Challenge != "heavy-occlusion";
            bool detected = result.Success && result.Coverage.MatchedModelSampleCount >= (int)Math.Ceiling(input.Model.Length * 0.65);
            int wrong = result.Coverage?.Matches.Count(m => input.ModelPhysicalIds[m.ModelSampleOrder] != input.ScenePhysicalIds[m.SceneSampleOrder]) ?? 0;
            string fingerprint = JsonSerializer.Serialize(new
            {
                result.Success, result.StopReason, result.AcceptedIterationCount, result.Pose, result.Coverage, result.LastFitCorrespondences
            });
            return new Row
            {
                CaseId = input.Id, InputSha256 = input.Hash, Challenge = input.Challenge, HeldOut = input.HeldOut,
                BalancedFit = balanced, Repeat = repeat, ExecutionFailed = !result.Success, Detected = detected, ExpectedDetection = expected,
                FalseAcceptance = result.Success && detected && (!expected || error > 0.1 || wrong > 0),
                FalseRejection = result.Success && !detected && expected, MaximumPoseError = double.IsInfinity(error) ? null : error,
                InlierRmse = result.Coverage?.HasInlierRmse == true ? result.Coverage.InlierRmse : null,
                WrongPhysicalCorrespondences = wrong, Support = result.Coverage?.MatchedModelSampleCount ?? 0,
                FitPairs = result.LastFitCorrespondences.Count, StopReason = result.StopReason.ToString(),
                ElapsedMilliseconds = watch.Elapsed.TotalMilliseconds, ManagedAllocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated,
                Fingerprint = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fingerprint)))
            };
        }

        internal static int RunEvaluation(string outputPath)
        {
            string path = Path.GetFullPath(outputPath);
            if (!path.StartsWith(@"D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Challenge evidence must use the project D: test root.");
            List<Row> rows = new List<Row>();
            foreach (bool heldOut in new[] { false, true })
            foreach (int count in heldOut ? HeldOutSizes : DevelopmentSizes)
            foreach (int seed in heldOut ? HeldOutSeeds : DevelopmentSeeds)
            foreach (string challenge in Challenges)
            {
                Input input = CreateInput(count, seed, challenge, heldOut);
                foreach (bool balanced in new[] { false, true })
                for (int repeat = 0; repeat < 3; repeat++) rows.Add(Evaluate(input, balanced, repeat));
            }
            DeterministicRigidSurfacePoseSearchResult symmetry = SymmetryResult();
            bool stable = rows.GroupBy(row => new { row.CaseId, row.BalancedFit }).All(group => group.Select(row => row.Fingerprint).Distinct().Count() == 1);
            var report = new
            {
                Schema = "surface-challenge-evaluation-v1", DevelopmentSizes, HeldOutSizes, DevelopmentSeeds, HeldOutSeeds, Challenges,
                Scope = "Analytic synthetic point-to-point local refinement from an authored bounded seed; not end-to-end acquisition, global search, sensor uncertainty or production qualification.",
                Policy = "Frozen before held-out evaluation: 65% support, 0.8 correspondence distance, 5 iterations, 64 fit pairs. Every challenge contains target samples; ExpectedDetection is false only for heavy occlusion below declared support, not physical absence. Research truth gate: maximum pose error <= 0.1 input XYZ unit and correct physical correspondence. These are comparison controls, not customer tolerances.",
                Limitation = "A support-only host acceptance gate can accept a near identical competing surface. Spatial sampling does not identify object identity, reject outliers or guarantee invariance under permuted input. Normals/datum/competing-translation policy need separate contracts before point-to-plane or robust global registration.",
                Metrics = "Unique inputs are separate from repeated trials and policies. Timing covers local Execute. Managed allocation excludes native heap and peak memory.",
                Runtime = Environment.Version.ToString(),
                Binary = new { Path = typeof(DeterministicSurfacePoseRefinementTool).Assembly.Location,
                    Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(DeterministicSurfacePoseRefinementTool).Assembly.Location))) },
                UniqueInputs = rows.Select(row => row.CaseId).Distinct().Count(), Trials = rows.Count, RepeatedResultsConsistent = stable,
                Summary = rows.GroupBy(row => new { row.HeldOut, row.BalancedFit, row.Challenge }).Select(group => new
                {
                    group.Key.HeldOut, group.Key.BalancedFit, group.Key.Challenge, Trials = group.Count(),
                    ExecutionErrors = group.Count(row => row.ExecutionFailed), FalseAcceptances = group.Count(row => row.FalseAcceptance),
                    FalseRejections = group.Count(row => row.FalseRejection), Refusals = group.Count(row => !row.Detected && !row.ExecutionFailed),
                    MaximumPoseError = group.Where(row => row.MaximumPoseError.HasValue).Max(row => row.MaximumPoseError),
                    MedianElapsedMilliseconds = group.Select(row => row.ElapsedMilliseconds).OrderBy(x => x).ElementAt(group.Count() / 2)
                }).ToArray(),
                Symmetry = new { symmetry.Success, symmetry.Matched, Ambiguous = symmetry.AmbiguityEvidence?.IsAmbiguous,
                    AcceptedBySupportAndOrientationGate = symmetry.Success && symmetry.Matched && symmetry.AmbiguityEvidence?.IsAmbiguous == false },
                Cases = rows
            };
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonSerializer.Serialize(report, ReportOptions));
            Console.WriteLine("Surface challenge | " + report.UniqueInputs + " independent inputs / " + rows.Count
                + " trials; stable=" + stable + "; execution errors=" + rows.Count(row => row.ExecutionFailed)
                + "; truth violations=" + rows.Count(row => row.FalseAcceptance || row.FalseRejection));
            return stable && rows.All(row => !row.ExecutionFailed) && symmetry.Success && symmetry.AmbiguityEvidence.IsAmbiguous ? 0 : 1;
        }
    }
}

using OpenCvSharp;
using OpenVisionLab.Vision2D;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Result;
using OpenVisionLab.Vision2D.Tool;
using OpenVisionLab.Vision3D.FeatureExtraction;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class MatchingQualificationSmokeSuite
    {
        private static readonly JsonSerializerOptions ReportJsonOptions = new JsonSerializerOptions { WriteIndented = true };
        private static readonly int[] DevelopmentShapes = { 101, 202 };
        private static readonly int[] ValidationShapes = { 303, 404 };
        private static readonly int[] DevelopmentSeeds = { 11, 29 };
        private static readonly int[] ValidationSeeds = { 73, 97 };

        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase("Matching qualification rejects target-absent teaching hits and restores borrowed images", TestNegativeTeaching);
            yield return new SmokeCase("Matching qualification chooses a fixed visible pattern from blur occlusion and negative evidence", TestVisibleTeaching);
            yield return new SmokeCase("Matching qualification validates negative inputs and recovers after cancellation", TestNegativeRecovery);
            yield return new SmokeCase("Matching qualification separates development geometry and seeds from held-out inputs", TestSplit);
            yield return new SmokeCase("Matching qualification distinguishes execution errors from correct refusals", TestEvaluationFailure);
        }

        private static AutoMPointToolProperty TeachingProperty(bool whole)
        {
            return new AutoMPointToolProperty
            {
                UseAnalysisRoi = true, AnalysisRoi = new Rect(32, 32, 64, 64),
                CandidateMode = whole ? AutoMPointCandidateMode.WholeAnalysisRoi : AutoMPointCandidateMode.Grid,
                PatternWidth = 32, PatternHeight = 32, CandidateStride = 16, MaximumFinalists = 16, MaximumResults = 1,
                MaximumCandidateOverlap = 0.8, MinimumContrastStdDev = 4, MinimumEdgeDensity = 0.005,
                MinimumQuadrantBalance = 0, MinimumOrientationBalance = 0, MinimumFeatureQuality = 0,
                MatchingMinimumScore = whole ? 0.55 : 0.8, SearchStep = 1, MinimumUniquenessMargin = 0.03,
                SyntheticTranslationPixels = whole ? 0 : 3,
                MaximumPositionErrorPixels = 4, MinimumSyntheticSuccessRate = 1,
                MinimumRepresentativeImageCount = 3, MinimumRepresentativeSuccessRate = 1,
                UseAngleSearch = false, UseScaleSearch = false, UseHybridVerify = true
            };
        }

        private static Mat Shape(int family, bool negative)
        {
            Mat image = new Mat(64, 64, MatType.CV_8UC1, Scalar.All(17));
            int inset = family < 300 ? 7 : 10;
            Cv2.Rectangle(image, new Rect(inset, 7, 50 - inset, 46), Scalar.All(225), -1);
            Cv2.Rectangle(image, new Rect(29, 7, 21, 16), Scalar.All(17), -1);
            Cv2.Circle(image, new Point(20, 37), family % 2 == 0 ? 8 : 7, Scalar.All(17), -1);
            if (family % 2 == 0) Cv2.Rectangle(image, new Rect(35, 39, 8, 14), Scalar.All(17), -1);
            if (negative)
            {
                Cv2.Rectangle(image, new Rect(29, 7, 21, 16), Scalar.All(225), -1);
                Cv2.Circle(image, new Point(20, 37), 10, Scalar.All(225), -1);
            }
            return image;
        }

        private static Mat Scene(int family, string challenge, int x, int y, int width = 128, int height = 128)
        {
            Mat scene = new Mat(height, width, MatType.CV_8UC1, Scalar.All(17));
            using Mat pattern = Shape(family, challenge == "similar");
            if (challenge != "blank")
            {
                using Mat patch = new Mat(scene, new Rect(x, y, 64, 64));
                pattern.CopyTo(patch);
            }
            if (challenge == "blur") Cv2.GaussianBlur(scene, scene, new Size(7, 7), 1.4);
            if (challenge == "partial") Cv2.Rectangle(scene, new Rect(x, y, 15, 64), Scalar.All(17), -1);
            if (challenge == "heavy") Cv2.Rectangle(scene, new Rect(x, y, 57, 64), Scalar.All(17), -1);
            if (challenge == "clutter")
            {
                // Background clutter crosses the source outside the stable taught datum.
                Cv2.Line(scene, new Point(2, 8), new Point(width - 3, 12), Scalar.All(210), 2);
                Cv2.Line(scene, new Point(7, 2), new Point(9, height - 3), Scalar.All(180), 2);
            }
            return scene;
        }

        private static AutoMPointCandidateResult Teach(AutoMPointTool tool, int family, bool whole, bool negative)
        {
            using Mat reference = Scene(family, "clean", 32, 32);
            using Mat clean = reference.Clone();
            using Mat blur = Scene(family, "blur", 32, 32);
            using Mat partial = Scene(family, "partial", 32, 32);
            using Mat absent = Scene(family, "similar", 32, 32);
            tool.SetProperty(TeachingProperty(whole));
            using VisionToolResult execution = negative
                ? tool.Execute(reference, new[] { clean, blur, partial }, new[] { absent }, CancellationToken.None)
                : tool.Execute(reference, new[] { clean, blur, partial });
            if (!execution.Success) Console.WriteLine("TEACH | " + family + " | " + execution.Message);
            return execution.Success ? tool.results.Single() : null;
        }

        private static void TestNegativeTeaching()
        {
            using Mat source = Scene(101, "clean", 32, 32);
            using AutoMPointTool tool = new AutoMPointTool();
            tool.SetProperty(TeachingProperty(true));
            using (VisionToolResult normal = tool.Execute(source))
                Require(normal.Success && tool.results.Count == 1, "Unchanged whole-ROI teaching must remain usable. " + normal.Message);
            using Mat negative = Scene(101, "similar", 32, 32);
            string before = ImageHash(negative);
            using (VisionToolResult guarded = tool.Execute(source, null, new[] { negative }, CancellationToken.None))
            {
                Require(!guarded.Success && tool.results.Count == 0 && tool.candidates.Any(candidate =>
                    candidate.NegativeRepresentativeMatches.Any(match => match.Outcome == "FalsePositive")),
                    "A target-absent hit must reject the fixed template, with the negative observation retained.");
            }
            Require(!negative.IsDisposed && ImageHash(negative) == before, "Borrowed negative input must remain unchanged and caller-owned.");
            using VisionToolResult recovered = tool.Execute(source);
            Require(recovered.Success && tool.results[0].NegativeRepresentativeMatches.Count == 0,
                "The next normal call must not retain the prior negative collection.");
        }

        private static void TestVisibleTeaching()
        {
            using AutoMPointTool tool = new AutoMPointTool();
            AutoMPointCandidateResult candidate = Teach(tool, 101, false, true);
            Require(candidate != null && candidate.RepresentativeSuccessCount == 3
                && candidate.NegativeRepresentativeMatches.Count == 1 && candidate.NegativeRepresentativeMatches[0].Success,
                "One fixed visible template must pass clean, blur and occlusion and reject the training negative.");
            Require(candidate.PatternRoi.X >= 32 && candidate.PatternRoi.Right <= 96,
                "The selected template must remain inside the authored analysis ROI.");
        }

        private static void TestNegativeRecovery()
        {
            using Mat source = Scene(101, "clean", 32, 32);
            using Mat wrongDepth = new Mat(8, 8, MatType.CV_16UC1, Scalar.All(1));
            using AutoMPointTool tool = new AutoMPointTool();
            tool.SetProperty(TeachingProperty(true));
            foreach (Mat invalid in new Mat[] { null, wrongDepth })
            {
                using VisionToolResult result = tool.Execute(source, null, new[] { invalid }, CancellationToken.None);
                Require(result.ErrorCode == VisionToolErrorCode.AutoMPointInvalidParameter && tool.results.Count == 0,
                    "Invalid negative input must fail before evaluation.");
            }
            using Mat small = new Mat(8, 8, MatType.CV_8UC1, Scalar.All(17));
            using (VisionToolResult result = tool.Execute(source, null, new[] { small }, CancellationToken.None))
                Require(!result.Success && tool.candidates.Any(candidate => candidate.NegativeRepresentativeMatches.Any(match => match.Outcome == "ExecutionError")),
                    "Unusable negative evidence must reject teaching rather than count as a correct negative.");
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            RequireThrows<OperationCanceledException>(() => tool.Execute(source, null, new[] { source }, cancellation.Token));
            using CancellationTokenSource midway = new CancellationTokenSource();
            RequireThrows<OperationCanceledException>(() => tool.Execute(source, null, new CancellingNegatives(source, midway), midway.Token));
            Require(midway.IsCancellationRequested && !source.IsDisposed, "Mid-evaluation cancellation must preserve borrowed input ownership.");
            using VisionToolResult recovered = tool.Execute(source);
            Require(recovered.Success && tool.results[0].NegativeRepresentativeMatches.Count == 0,
                "Cancellation must reset borrowed positive/negative collections before a new execution.");
            using VisionToolResult legacySignature = tool.Execute(source, Array.Empty<Mat>(), default);
            Require(legacySignature.Success, "The existing three-argument default-token overload must retain source compatibility.");
        }

        private static void TestSplit()
        {
            Require(!DevelopmentShapes.Intersect(ValidationShapes).Any() && !DevelopmentSeeds.Intersect(ValidationSeeds).Any(),
                "Development and validation identities must be disjoint.");
            using Mat development = Shape(101, false);
            using Mat validation = Shape(303, false);
            Require(ImageHash(development) != ImageHash(validation), "Validation must contain separately authored geometry.");
        }

        private static void TestEvaluationFailure()
        {
            using Mat blank = Scene(101, "blank", 32, 32);
            using Mat reference = Scene(101, "clean", 32, 32);
            using Mat template = reference.SubMat(new Rect(32, 32, 64, 64)).Clone();
            using EdgeBasedTemplateMatchingTool matcher = new EdgeBasedTemplateMatchingTool();
            matcher.SetProperty(new EdgeBasedTemplateMatchingToolProperty { SCORE_MIN = 0.8, USE_DRAW_IMAGE = false });
            using (VisionToolResult failed = matcher.Execute(blank))
            {
                Require(!failed.Success && failed.ErrorCode != VisionToolErrorCode.MatchingNoResult,
                    "A missing template must produce an execution error.");
                Evaluation observation = new Evaluation
                {
                    ExpectedDetection = false, Detected = failed.Success && matcher.results.Count > 0,
                    ExecutionFailed = !failed.Success && failed.ErrorCode != VisionToolErrorCode.MatchingNoResult,
                    CaseConstraintsSatisfied = true
                };
                Require(!observation.Passed, "An execution error on an absent target must fail qualification.");
            }
            matcher.SetTemplateImage(template);
            using (VisionToolResult refused = matcher.Execute(blank))
            {
                Require(refused.ErrorCode == VisionToolErrorCode.MatchingNoResult, "A valid blank search must be an expected refusal.");
                Evaluation observation = new Evaluation
                {
                    ExpectedDetection = false, Detected = refused.Success && matcher.results.Count > 0,
                    ExecutionFailed = !refused.Success && refused.ErrorCode != VisionToolErrorCode.MatchingNoResult,
                    CaseConstraintsSatisfied = true
                };
                Require(observation.Passed, "Recovery to an expected refusal must pass qualification.");
            }
            DeterministicRigidSurfacePoseSearchResult invalid = new DeterministicRigidSurfacePoseSearchTool().Execute(
                null, null, SurfaceSearchRobustnessSmokeSuite.Options());
            Evaluation invalidSurface = new Evaluation
            {
                ExpectedDetection = false, Detected = invalid.Success && invalid.Matched,
                ExecutionFailed = !invalid.Success, CaseConstraintsSatisfied = true
            };
            Require(!invalidSurface.Passed, "Invalid 3D execution must not become a correct target-absent refusal.");
        }

        internal static int RunQualification(string outputPath)
        {
            string fullPath = Path.GetFullPath(outputPath);
            string storage = @"D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\";
            if (!fullPath.StartsWith(storage, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Qualification output must be inside " + storage);
            List<Evaluation> cases = new List<Evaluation>();
            foreach (int family in DevelopmentShapes.Concat(ValidationShapes))
            {
                bool heldOut = ValidationShapes.Contains(family);
                foreach (int seed in heldOut ? ValidationSeeds : DevelopmentSeeds)
                {
                    Evaluate2D(cases, family, seed, heldOut);
                    Evaluate3D(cases, family, seed, heldOut);
                }
            }
            SurfaceModelWorkflowSmokeSuite.WorkflowEvaluation[] workflows = SurfaceModelWorkflowSmokeSuite.QualifyWorkflows();
            LabeledEvidenceStatisticsTool statistics = new LabeledEvidenceStatisticsTool();
            var report = new
            {
                Schema = "matching-qualification-v1", Scope = "Independent analytic/synthetic inputs; no sensor or field qualification",
                DevelopmentShapes, ValidationShapes, DevelopmentSeeds, ValidationSeeds,
                Policy = "Shape IDs are separate for policy development and validation. Each validation part has its own clean reference and fixed development teaching challenges at (32,32). Validation execution seeds/placements are never supplied to teaching. One fixed template per part; no runtime template switching or threshold tuning from held-out observations",
                Boundaries = "Managed allocations are caller-thread bytes; working set is process observation. Coarse pose ambiguity is bounded by the authored grid. No production Takt, calibration or false-rate confidence claim.",
                Binaries = new[] { typeof(Program).Assembly, typeof(AutoMPointTool).Assembly, typeof(DeterministicSurfaceCoverageTool).Assembly }
                    .Select(assembly => new { assembly.Location, Sha256 = FileHash(assembly.Location) }).ToArray(),
                Summary = new
                {
                    IndependentInputs = cases.Select(item => item.CaseId).Distinct().Count(), Trials = cases.Count,
                    NonDeterministicInputs = cases.GroupBy(item => item.CaseId).Count(group => group.Select(Fingerprint).Distinct().Count() != 1),
                    ByAlgorithm = cases.GroupBy(item => item.Algorithm).Select(group => new
                    {
                        Algorithm = group.Key, IndependentInputs = group.Select(item => item.CaseId).Distinct().Count(), Trials = group.Count(),
                        ExpectedDetections = group.Count(item => item.ExpectedDetection),
                        FalseDetections = group.Count(item => !item.ExpectedDetection && item.Detected),
                        Misses = group.Count(item => item.ExpectedDetection && !item.Detected && !item.ExecutionFailed),
                        ExecutionFailures = group.Count(item => item.ExecutionFailed),
                        Refusals = group.Count(item => !item.Detected && !item.ExecutionFailed),
                        RefusalRate = group.Count(item => !item.Detected && !item.ExecutionFailed) / (double)group.Count(),
                        Failures = group.Count(item => !item.Passed), PositionErrorUnit = group.First().PositionErrorUnit,
                        PositionError = statistics.Execute(group.Where(item => item.PositionError.HasValue).Select(item =>
                            new LabeledEvidenceStatisticsObservation(item.CaseId, item.HeldOut ? LabeledEvidenceRole.HeldOut : LabeledEvidenceRole.Good, item.PositionError.Value)).ToArray()),
                        ElapsedMilliseconds = statistics.Execute(group.Select(item => new LabeledEvidenceStatisticsObservation(
                            item.CaseId, item.HeldOut ? LabeledEvidenceRole.HeldOut : LabeledEvidenceRole.Good, item.ElapsedMilliseconds)).ToArray())
                    }).ToArray()
                },
                Workflows = new
                {
                    Schema = "sdk-workflow-qualification-v1",
                    Scope = "Synthetic contract controls; metadata units are host declarations; no sensor uncertainty, calibration, customer tolerances or Takt approval",
                    Metrics = "Per-case timed scope; process managed allocation delta excludes native heap/peak memory; medians are local observations",
                    Runtime = Environment.Version.ToString(), ProcessArchitecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                    Binaries = new[] { typeof(SurfaceModelArtifactCodec).Assembly, typeof(EdgeBasedTemplateMatchingTool).Assembly, typeof(Program).Assembly }
                        .Select(assembly => new { assembly.GetName().Name, Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))) }).ToArray(),
                    RepeatedResultsConsistent = workflows.GroupBy(row => row.CaseId).All(group => group.Select(row => row.Fingerprint).Distinct().Count() == 1),
                    Summary = workflows.GroupBy(row => row.CaseId).Select(group => new
                    {
                        CaseId = group.Key, Count = group.Count(), Passed = group.All(row => row.Passed),
                        MedianElapsedMilliseconds = group.Select(row => row.ElapsedMilliseconds).OrderBy(value => value).ElementAt(group.Count() / 2),
                        MedianManagedAllocatedBytes = group.Select(row => row.ManagedAllocatedBytes).OrderBy(value => value).ElementAt(group.Count() / 2)
                    }).ToArray(),
                    Results = workflows
                },
                Results = cases
            };
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, JsonSerializer.Serialize(report, ReportJsonOptions));
            Console.WriteLine("QUALIFY | cases=" + cases.Count + " failures=" + cases.Count(item => !item.Passed) + " | " + fullPath);
            return workflows.All(row => row.Passed) && workflows.GroupBy(row => row.CaseId).All(group => group.Select(row => row.Fingerprint).Distinct().Count() == 1)
                && cases.All(item => item.Passed) && cases.GroupBy(item => item.CaseId).All(group => group.Select(Fingerprint).Distinct().Count() == 1) ? 0 : 1;
        }

        private static void Evaluate2D(List<Evaluation> cases, int family, int seed, bool heldOut)
        {
            using AutoMPointTool teaching = new AutoMPointTool();
            // Validation parts are taught from their clean reference and fixed development challenges; their test seed is never supplied here.
            AutoMPointCandidateResult chosen = Teach(teaching, family, false, true);
            using Mat reference = Scene(family, "clean", 32, 32);
            using Mat template = chosen == null ? null : reference.SubMat(chosen.PatternRoi).Clone();
            using EdgeBasedTemplateMatchingTool matcher = new EdgeBasedTemplateMatchingTool();
            matcher.SetProperty(new EdgeBasedTemplateMatchingToolProperty
            {
                SCORE_MIN = 0.8, SEARCH_STEP = 1, MAX_TEMPLATE_POINTS = 300, CONTOUR_RETRIEVAL_MODE = RetrievalModes.List,
                USE_DRAW_IMAGE = false, USE_POSITION_REFINE = true, USE_SUBPIXEL_REFINE = true, USE_HYBRID_VERIFY = true
            });
            if (template != null) matcher.SetTemplateImage(template);
            Random random = new Random(seed);
            foreach (string challenge in new[] { "clean", "blur", "partial", "clutter", "similar", "blank", "heavy" })
            {
                int x = random.Next(20, 105), y = random.Next(18, 75);
                using Mat scene = Scene(family, challenge, x, y, 196, 160);
                bool expected = challenge != "similar" && challenge != "blank" && challenge != "heavy";
                string inputHash = ImageHash(scene);
                for (int repeat = 0; repeat < 3; repeat++)
                {
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    Stopwatch timer = Stopwatch.StartNew();
                    using VisionToolResult result = matcher.Execute(scene);
                    timer.Stop();
                    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                    MatchingResult match = matcher.results.FirstOrDefault();
                    bool detected = chosen != null && result.Success && match != null;
                    double? error = detected ? Math.Sqrt(
                        Math.Pow(match.Center.X + chosen.NativeToPatternOffsetX - (x + chosen.PatternRoi.X - 32 + chosen.PatternRoi.Width / 2.0), 2)
                        + Math.Pow(match.Center.Y + chosen.NativeToPatternOffsetY - (y + chosen.PatternRoi.Y - 32 + chosen.PatternRoi.Height / 2.0), 2)) : null;
                    cases.Add(new Evaluation
                    {
                        Id = "2d/" + family + "/" + seed + "/" + challenge + "/" + repeat,
                        CaseId = "2d/" + family + "/" + seed + "/" + challenge, Algorithm = "2D edge matching", Trial = repeat, HeldOut = heldOut,
                        InputLabel = challenge == "similar" || challenge == "blank" ? "target absent" : challenge == "heavy" ? "insufficient visible support" : "target present",
                        PositionErrorUnit = "pixel", SupportUnit = "edge score percent",
                        ExpectedPosition = chosen == null ? null : new[] { x + chosen.PatternRoi.X - 32 + chosen.PatternRoi.Width / 2.0, y + chosen.PatternRoi.Y - 32 + chosen.PatternRoi.Height / 2.0 },
                        ActualPosition = detected ? new[] { match.Center.X + chosen.NativeToPatternOffsetX, match.Center.Y + chosen.NativeToPatternOffsetY } : null,
                        TemplateRoi = chosen == null ? null : new[] { chosen.PatternRoi.X, chosen.PatternRoi.Y, chosen.PatternRoi.Width, chosen.PatternRoi.Height },
                        InputHash = inputHash, ModelHash = template == null ? "none" : ImageHash(template),
                        ExpectedDetection = expected, Detected = detected, PositionError = error,
                        Outcome = chosen == null ? "TeachingRejected" : result.ErrorName,
                        ExecutionFailed = chosen == null || !result.Success && result.ErrorCode != VisionToolErrorCode.MatchingNoResult,
                        CaseConstraintsSatisfied = (!error.HasValue || error.Value <= 2.5) && ImageHash(scene) == inputHash,
                        ElapsedMilliseconds = timer.Elapsed.TotalMilliseconds,
                        ManagedAllocatedBytes = allocated,
                        ObservedWorkingSetBytes = Environment.WorkingSet,
                        Support = detected ? match.EdgeScore : null, RejectionReason = result.Message
                    });
                }
            }
        }

        private static void Evaluate3D(List<Evaluation> cases, int family, int seed, bool heldOut)
        {
            Random geometry = new Random(family);
            SurfaceMatchSample[] model = SurfaceSearchRobustnessSmokeSuite.Points(Enumerable.Range(0, 12).Select(index =>
                new[] { geometry.NextDouble() * 15, geometry.NextDouble() * 11, geometry.NextDouble() * 9 }).ToArray());
            double angle = seed == 11 || seed == 73 ? 30 : 60;
            double radians = angle * Math.PI / 180;
            Random sceneNoise = new Random(seed);
            foreach (bool present in new[] { true, false })
            {
                List<double[]> coordinates = new List<double[]>();
                if (present) coordinates.AddRange(model.Take(9).Select(sample => new[]
                {
                    Math.Cos(radians) * sample.Position.X - Math.Sin(radians) * sample.Position.Y + 10 + (sceneNoise.NextDouble() - 0.5) * 0.002,
                    Math.Sin(radians) * sample.Position.X + Math.Cos(radians) * sample.Position.Y - 4 + (sceneNoise.NextDouble() - 0.5) * 0.002,
                    sample.Position.Z + 2 + (sceneNoise.NextDouble() - 0.5) * 0.002
                }));
                coordinates.AddRange(Enumerable.Range(0, 12).Select(index => new[]
                {
                    35 + sceneNoise.NextDouble() * 40, 30 + sceneNoise.NextDouble() * 40, 20 + sceneNoise.NextDouble() * 40
                }));
                SurfaceMatchSample[] scene = SurfaceSearchRobustnessSmokeSuite.Points(coordinates.ToArray());
                DeterministicRigidSurfacePoseSearchOptions options = SurfaceSearchRobustnessSmokeSuite.Options();
                options.MaximumRotationZDegrees = 90; options.RotationStepZDegrees = 30;
                options.MaximumCorrespondenceDistance = 0.01; options.MinimumMatchedSampleCount = 9;
                options.UseSamplePairTranslationCandidates = true;
                options.AmbiguityOptions = new SurfacePoseAmbiguityOptions { MinimumRotationSeparationDegrees = 20, MaximumTranslationSeparation = 0.01, MaximumRmseDifference = 0.002 };
                DeterministicRigidSurfacePoseSearchTool tool = new DeterministicRigidSurfacePoseSearchTool();
                for (int repeat = 0; repeat < 3; repeat++)
                {
                    long before = GC.GetAllocatedBytesForCurrentThread(); Stopwatch timer = Stopwatch.StartNew();
                    DeterministicRigidSurfacePoseSearchResult result = tool.Execute(model, scene, options);
                    timer.Stop();
                    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                    bool detected = result.Success && result.Matched && !result.AmbiguityEvidence.IsAmbiguous;
                    double? error = detected ? Math.Sqrt(Math.Pow(result.Pose.TranslationX - 10, 2)
                        + Math.Pow(result.Pose.TranslationY + 4, 2) + Math.Pow(result.Pose.TranslationZ - 2, 2)) : null;
                    double? rotationError = detected ? Math.Abs(Math.Atan2(result.Pose.M21, result.Pose.M11) * 180 / Math.PI - angle) : null;
                    cases.Add(new Evaluation
                    {
                        Id = "3d/" + family + "/" + seed + "/" + (present ? "partial-noisy-background" : "absent") + "/" + repeat,
                        CaseId = "3d/" + family + "/" + seed + "/" + (present ? "partial-noisy-background" : "absent"),
                        Algorithm = "3D rigid surface matching", Trial = repeat, PositionErrorUnit = "synthetic XYZ unit", SupportUnit = "matched model points",
                        InputLabel = present ? "target present" : "target absent", ExpectedPosition = new[] { 10.0, -4, 2 },
                        ActualPosition = detected ? new[] { result.Pose.TranslationX, result.Pose.TranslationY, result.Pose.TranslationZ } : null,
                        ExpectedRotationDegrees = angle,
                        HeldOut = heldOut, InputHash = PointHash(scene), ModelHash = PointHash(model), ExpectedDetection = present,
                        Detected = detected, PositionError = error, RotationErrorDegrees = rotationError,
                        Outcome = !result.Success ? "ExecutionError" : !result.Matched ? "NoMatch" : result.AmbiguityEvidence.IsAmbiguous ? "Ambiguous" : "Matched",
                        ExecutionFailed = !result.Success,
                        CaseConstraintsSatisfied = (!error.HasValue || error.Value <= 0.004) && (!rotationError.HasValue || rotationError.Value <= 1e-10),
                        ElapsedMilliseconds = timer.Elapsed.TotalMilliseconds, ManagedAllocatedBytes = allocated,
                        ObservedWorkingSetBytes = Environment.WorkingSet, Support = result.Coverage?.MatchedModelSampleCount,
                        RejectionReason = result.Success ? result.RejectionReason : result.Message
                    });
                }
            }
        }

        private static string Fingerprint(Evaluation item) => JsonSerializer.Serialize(new
        {
            item.InputHash, item.ModelHash, item.Detected, item.Outcome, item.ExecutionFailed, item.CaseConstraintsSatisfied,
            item.PositionError, item.RotationErrorDegrees, item.Support, item.ActualPosition
        });

        private static string ImageHash(Mat image) => Convert.ToHexString(SHA256.HashData(image.ToBytes(".png")));
        private static string FileHash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        private static string PointHash(SurfaceMatchSample[] points)
        {
            string text = string.Join(";", points.Select(sample => string.Join(",", new[] { sample.Position.X, sample.Position.Y, sample.Position.Z }
                .Select(value => value.ToString("R", CultureInfo.InvariantCulture)))));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        }

        private sealed class CancellingNegatives : IReadOnlyList<Mat>
        {
            private readonly Mat image;
            private readonly CancellationTokenSource cancellation;
            private int reads;
            internal CancellingNegatives(Mat image, CancellationTokenSource cancellation) { this.image = image; this.cancellation = cancellation; }
            public int Count => 1;
            public Mat this[int index] { get { if (++reads == 2) cancellation.Cancel(); return image; } }
            public IEnumerator<Mat> GetEnumerator() { yield return image; }
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private sealed class Evaluation
        {
            public string Id { get; set; }
            public string CaseId { get; set; }
            public string Algorithm { get; set; }
            public int Trial { get; set; }
            public string InputLabel { get; set; }
            public string PositionErrorUnit { get; set; }
            public string SupportUnit { get; set; }
            public double[] ExpectedPosition { get; set; }
            public double[] ActualPosition { get; set; }
            public int[] TemplateRoi { get; set; }
            public double? ExpectedRotationDegrees { get; set; }
            public bool HeldOut { get; set; }
            public string InputHash { get; set; }
            public string ModelHash { get; set; }
            public bool ExpectedDetection { get; set; }
            public bool Detected { get; set; }
            public string Outcome { get; set; }
            public bool ExecutionFailed { get; set; }
            public bool CaseConstraintsSatisfied { get; set; }
            public bool Passed => !ExecutionFailed && CaseConstraintsSatisfied && Detected == ExpectedDetection;
            public double? PositionError { get; set; }
            public double? RotationErrorDegrees { get; set; }
            public double? Support { get; set; }
            public double ElapsedMilliseconds { get; set; }
            public long ManagedAllocatedBytes { get; set; }
            public long ObservedWorkingSetBytes { get; set; }
            public string RejectionReason { get; set; }
        }
    }
}

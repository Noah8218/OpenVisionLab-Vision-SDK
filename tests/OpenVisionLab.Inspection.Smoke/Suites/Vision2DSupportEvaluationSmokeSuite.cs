using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;
using OpenCvSharp;
using OpenVisionLab.Vision2D.Blob;
using OpenVisionLab.Vision2D.Pipeline;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Result;
using OpenVisionLab.Vision2D.Tool;
using static OpenVisionLab.Core.FormulaUtil;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;

namespace OpenVisionLab.Inspection.Smoke
{
    // A bounded cross-Tool support study, separate from 2D/3D matching qualification.
    // Truth comes from authored geometry and gray response, never from an SDK result.
    internal static class Vision2DSupportEvaluationSmokeSuite
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };
        private static readonly string[] MatchingFailures = { "null", "roi", "property" };
        private static readonly string[] BlobFailures = { "null", "roi", "property", "area" };
        private static readonly string[] LineFailures = { "null", "roi", "property", "depth", "sampling", "cancel" };
        private static readonly (string Id, string Split, double Angle, double Scale, double Dx, double Dy, double Gain, double Offset, double Blur, string Challenge, bool Characterization)[] MatchInputs =
        {
            ("M00", "development", 1.25, 1.015, 19.5, 12.75, 1, 0, 0, "present", false),
            ("M01", "held-out", 3.25, 1.035, 39.25, 25.4, 1, 0, 0, "present", false),
            ("M02", "held-out", -3.75, 0.965, 55.6, 31.2, 1, 0, 0, "present", false),
            ("M03", "held-out", 3.25, 1.035, 39.25, 25.4, 0.72, 18, 0, "present", true),
            ("M04", "held-out", -3.75, 0.965, 55.6, 31.2, 1, 0, 1, "present", true),
            ("M05", "held-out", 3.25, 1.035, 39.25, 25.4, 1, 0, 0, "inverted", false),
            ("M06", "held-out", 0, 1, 29, 19, 0.9, 7, 0, "blank", false),
            ("M07", "held-out", 0, 1, 29, 19, 1, 0, 0, "similar", true),
            ("M08", "held-out", 0, 1, 29, 19, 1, 0, 0, "duplicate", false)
        };
        private static readonly (string Id, string Split, double Intercept, double Amplitude, string Response, bool Characterization)[] PixelInputs =
        {
            ("P00", "development", 43.625, 80, "tanh", false),
            ("P01", "held-out", 41.375, 80, "tanh", false),
            ("P02", "held-out", 67.625, 18, "tanh", false),
            ("P03", "held-out", 41.375, 1, "tanh", false),
            ("P04", "held-out", 67.625, 80, "asymmetric-ramp", true),
            ("P05", "held-out", 41.375, 80, "reflection", true),
            ("P06", "held-out", 4, 80, "border", false)
        };

        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase("2D support separates taught inputs from held-out pose light absence and identity observations", () => CheckGroup(EvaluateMatching));
            yield return new SmokeCase("2D support follows known rotation and scale through Blob and LineGauge with explicit color and XML restoration", () => CheckGroup(EvaluateRoiWorkflow));
            yield return new SmokeCase("2D support records faint asymmetric and reflected gray response against independent pixel truth", () => CheckGroup(EvaluatePixels));
            yield return new SmokeCase("2D support clears prior mutable Tool outputs after recipe input and cancellation failures with recovery", () => CheckGroup(EvaluateFailureRecovery));
        }

        internal static int RunEvaluation(string reportPath)
        {
            string fullPath = Path.GetFullPath(reportPath);
            string storage = @"D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\";
            if (!fullPath.StartsWith(storage, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("2D support output must be inside " + storage);
            string directory = Path.GetDirectoryName(fullPath);
            Directory.CreateDirectory(directory);
            // Freeze parameters, generated hashes and expectations before the first SDK execution.
            var manifest = CreateManifest();
            File.WriteAllText(Path.Combine(directory, "2d-support-input-manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions));
            var rows = new List<Observation>();
            foreach (var group in new (string Name, Action<List<Observation>, string> Run)[]
            {
                ("matching", EvaluateMatching), ("roi", EvaluateRoiWorkflow), ("pixels", EvaluatePixels), ("failure-recovery", EvaluateFailureRecovery)
            })
            {
                try { group.Run(rows, directory); }
                catch (Exception exception)
                {
                    rows.Add(new Observation { Id = group.Name + "/execution-error", Expected = "All declared group cases executed", Outcome = exception.GetType().Name, Message = exception.Message, ContractPassed = false });
                }
            }
            Assembly[] binaries = { typeof(Program).Assembly, typeof(EdgeBasedTemplateMatchingTool).Assembly, typeof(BlobTool).Assembly, typeof(OpenCvAlgorithmBase).Assembly, typeof(Mat).Assembly };
            string[] unexecuted = ExpectedCaseIds().Except(rows.Select(row => row.Id)).ToArray();
            bool duplicateIds = rows.GroupBy(row => row.Id).Any(group => group.Count() != 1);
            var report = new
            {
                Schema = "2d-support-evaluation-v1", At = DateTimeOffset.UtcNow, InputManifestSha256 = Hash(File.ReadAllBytes(Path.Combine(directory, "2d-support-input-manifest.json"))),
                Scope = "Independent synthetic pixel/pose/ROI contract evaluation; no sensor, mm accuracy, customer error policy, Takt or commercial-engine comparison",
                Runtime = Environment.Version.ToString(), Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                Binaries = binaries.Distinct().Select(assembly => new { assembly.GetName().Name, assembly.Location, Sha256 = Hash(File.ReadAllBytes(assembly.Location)) }).ToArray(),
                Summary = new
                {
                    Observations = rows.Count, ContractFailures = rows.Count(row => !row.ContractPassed), UnexecutedCases = unexecuted, DuplicateCaseIds = duplicateIds,
                    QualityGatesPassed = rows.Count(row => row.QualityGatePassed == true), QualityGatesFailed = rows.Count(row => row.QualityGatePassed == false),
                    CharacterizationFailures = rows.Count(row => row.CharacterizationOnly && row.QualityGatePassed == false),
                    FalseDetections = rows.Count(row => row.Expected == "absent" && row.Detected), Misses = rows.Count(row => row.Expected == "present" && !row.Detected),
                    RecoveryFailures = rows.Count(row => row.RecoveryPassed == false)
                },
                Results = rows
            };
            File.WriteAllText(fullPath, JsonSerializer.Serialize(report, JsonOptions));
            Console.WriteLine("2D SUPPORT | observations=" + rows.Count + " contract failures=" + rows.Count(row => !row.ContractPassed)
                + " quality failures=" + rows.Count(row => row.QualityGatePassed == false) + " | " + fullPath);
            return rows.Count > 0 && rows.All(row => row.ContractPassed) && unexecuted.Length == 0 && !duplicateIds ? 0 : 1;
        }

        private static string[] ExpectedCaseIds() => new[] { "T01", "T02", "M09", "M10", "R01", "R02", "F-line-radius", "F-line-nonfinite" }
            .Concat(MatchInputs.Select(item => item.Id)).Concat(PixelInputs.Select(item => item.Id))
            .Concat(MatchingFailures.Select(failure => "F-match-" + failure)).Concat(BlobFailures.Select(failure => "F-blob-" + failure))
            .Concat(new[] { false, true }.SelectMany(subpixel => LineFailures.Select(failure => "F-line-" + subpixel + "-" + failure))).ToArray();

        private static object CreateManifest()
        {
            using Mat template = EdgePoseRefinementSmokeSuite.CreateTemplate();
            var inputs = new List<object>();
            foreach (var item in MatchInputs)
            {
                using Mat scene = DatumScene(template, item.Angle, item.Scale, item.Dx, item.Dy, item.Gain, item.Offset, item.Blur, item.Challenge);
                inputs.Add(new { item.Id, item.Split, item.Angle, item.Scale, item.Dx, item.Dy, item.Gain, item.Offset, item.Blur, item.Challenge, item.Characterization, InputSha256 = ImageHash(scene) });
            }
            foreach (var item in PixelInputs)
            {
                using Mat scene = GrayResponse(item.Intercept, item.Amplitude, item.Response);
                inputs.Add(new { item.Id, item.Split, item.Intercept, item.Amplitude, item.Response, item.Characterization, InputSha256 = ImageHash(scene) });
            }
            foreach (var item in new[] { ("training-reference", 1d, 0d, 0d), ("training-light", 0.85, 10d, 0d), ("training-blur", 1d, 0d, 0.7) })
            {
                using Mat scene = DatumScene(template, 0, 1, 0, 0, item.Item2, item.Item3, item.Item4);
                inputs.Add(new { Id = item.Item1, Split = "training", InputSha256 = ImageHash(scene) });
            }
            using (Mat blank = DatumScene(template, 0, 1, 0, 0, challenge: "blank"))
                inputs.Add(new { Id = "training-blank", Split = "training-negative", InputSha256 = ImageHash(blank) });
            foreach (var item in MatchInputs.Where(item => item.Id == "M01" || item.Id == "M02"))
            {
                using Mat scene = WorkflowScene(template, item.Angle, item.Scale, item.Dx, item.Dy, out var pixels, out double slope, out double intercept);
                inputs.Add(new { Id = item.Id == "M01" ? "R01" : "R02", Split = "held-out", InputSha256 = ImageHash(scene), MarkArea = pixels.Count, Slope = slope, InterceptPx = intercept });
            }
            foreach (bool scale in new[] { false, true })
            {
                using Mat shape = AmbiguityTemplate(scale);
                using Mat scene = AmbiguityScene(shape, scale);
                inputs.Add(new { Id = scale ? "M10" : "M09", Split = "held-out", TemplateSha256 = ImageHash(shape), InputSha256 = ImageHash(scene), Expected = "MatchingAmbiguous" });
            }
            using (Mat scene = DatumScene(template, 0, 1, 19, 13))
                inputs.Add(new { Id = "failure-matching-control", Split = "failure-control", InputSha256 = ImageHash(scene) });
            using (Mat mark = PresenceControl())
                inputs.Add(new { Id = "failure-presence-control", Split = "failure-control", InputSha256 = ImageHash(mark) });
            using (Mat line = GrayResponse(41.375, 80, "tanh"))
                inputs.Add(new { Id = "failure-line-control", Split = "failure-control", InputSha256 = ImageHash(line) });
            using (Mat depth = new Mat(137, 173, MatType.CV_16UC1, Scalar.All(120)))
                inputs.Add(new { Id = "failure-depth-control", Split = "failure-control", InputSha256 = ImageHash(depth) });
            return new
            {
                Schema = "2d-support-input-manifest-v1", AuthoritativeTruth = "Authored shape, OpenCV downward-Y sin/cos similarity, inverse-mapped mark raster and analytic gray response",
                Training = "One clean 64x64 datum at (44,36), clean/gain .85 + offset 10/blur .7 positives and blank negative; only these enter AutoMPoint. No held-out input enters teaching or setting selection.",
                Development = "M00 and P00 are development controls; settings fixed in source before held-out execution. No automated runtime template switching.",
                Settings = MatchingSettings(),
                Gates = "Clear pose controls <= .3px / .15deg / .003 scale; smooth quantized gray max point error <= .125px; continuous similarity <= 1e-9; generated Blob count exact and centroid rounding <= .011px; pre-run/cancel never exposes prior successful mutable output; valid recovery required",
                Characterization = "M03/M04/M07 and asymmetric/reflection response remain observations, including quality failures. They do not certify an input region or physical edge location.",
                RoiCases = "R01=(3.25,1.035,39.25,25.4); R02=(-3.75,.965,55.6,31.2), fixed 384x320; mark (162,78,15,11), mark ROI (154,70,31,29); x=.03*y+216.93; gauge ROI (174,144,101,89). Host rounds AABB outward and refuses clipping.",
                FailureCases = "Each begins with accepted control: null input, out-of-frame ROI, missing property, 16-bit LineGauge, invalid radius/NaN contrast, opt-in -> legacy, pre-cancel; restore valid state and re-execute. Caller-owned accepted snapshots remain valid.",
                ExpectedObservationIds = ExpectedCaseIds(), Inputs = inputs
            };
        }

        private static void CheckGroup(Action<List<Observation>, string> run)
        {
            var rows = new List<Observation>();
            run(rows, null);
            Require(rows.Count > 0 && rows.All(row => row.ContractPassed), string.Join("; ", rows.Where(row => !row.ContractPassed).Select(row => row.Id + ": " + row.Message)));
        }

        private static EdgeBasedTemplateMatchingToolProperty MatchingSettings() => new EdgeBasedTemplateMatchingToolProperty
        {
            SCORE_MIN = 0.8, SEARCH_STEP = 2, MAX_TEMPLATE_POINTS = 400, USE_DRAW_IMAGE = false,
            USE_POSITION_REFINE = true, USE_SUBPIXEL_REFINE = true, USE_UNIQUE_MATCH_VALIDATION = true,
            USE_ROI = true, CvROI = new Rect(28, 20, 190, 170),
            USE_FIND_ANGLE = true, FIND_ANGLE_MIN = -5, FIND_ANGLE_MAX = 5, FIND_ANGLE = 5,
            USE_FIND_SCALE = true, FIND_SCALE_MIN = 0.95, FIND_SCALE_MAX = 1.05, FIND_SCALE_STEP = 0.05,
            MIN_GRADIENT_MAGNITUDE = 5
        };

        private static EdgeBasedTemplateMatchingTool Matcher(Mat template)
        {
            var tool = new EdgeBasedTemplateMatchingTool { UsePoseRefinement = true };
            tool.SetProperty(MatchingSettings());
            tool.SetTemplateImage(template);
            return tool;
        }

        private static Mat DatumScene(Mat template, double angle, double scale, double dx, double dy, double gain = 1, double offset = 0, double blur = 0, string challenge = "present")
        {
            Mat scene = new Mat(320, 384, MatType.CV_8UC1, Scalar.All(17));
            using Mat shape = template.Clone();
            if (challenge == "similar") Cv2.Rectangle(shape, new Rect(8, 7, 42, 46), Scalar.All(225), -1);
            if (challenge != "blank")
            {
                using Mat warp = Cv2.GetRotationMatrix2D(new Point2f(31.5f, 31.5f), angle, scale);
                warp.Set(0, 2, warp.At<double>(0, 2) + 44 + dx);
                warp.Set(1, 2, warp.At<double>(1, 2) + 36 + dy);
                Cv2.WarpAffine(shape, scene, warp, scene.Size(), InterpolationFlags.Linear, BorderTypes.Constant, Scalar.All(17));
            }
            if (challenge == "duplicate")
            {
                using Mat region = scene.SubMat(new Rect(142, 110, 64, 64));
                template.CopyTo(region);
            }
            if (gain != 1 || offset != 0) scene.ConvertTo(scene, MatType.CV_8UC1, gain, offset);
            if (blur > 0) Cv2.GaussianBlur(scene, scene, new Size(7, 7), blur);
            if (challenge == "inverted") Cv2.BitwiseNot(scene, scene);
            return scene;
        }

        private static Point2d AuthoredPoint(Point2d reference, double angle, double scale, double dx, double dy)
        {
            double cos = Math.Cos(angle * Math.PI / 180), sin = Math.Sin(angle * Math.PI / 180);
            double x = reference.X - 75.5, y = reference.Y - 67.5;
            return new Point2d(75.5 + dx + scale * (cos * x + sin * y), 67.5 + dy + scale * (-sin * x + cos * y));
        }

        private static void EvaluateMatching(List<Observation> rows, string directory)
        {
            using Mat template = EdgePoseRefinementSmokeSuite.CreateTemplate();
            using Mat reference = DatumScene(template, 0, 1, 0, 0);
            using Mat light = DatumScene(template, 0, 1, 0, 0, 0.85, 10);
            using Mat blur = DatumScene(template, 0, 1, 0, 0, blur: 0.7);
            using Mat blank = DatumScene(template, 0, 1, 0, 0, challenge: "blank");
            string[] trainingHashes = { ImageHash(reference), ImageHash(light), ImageHash(blur), ImageHash(blank) };
            using var teacher = new AutoMPointTool();
            teacher.SetProperty(new AutoMPointToolProperty
            {
                UseAnalysisRoi = true, AnalysisRoi = new Rect(44, 36, 64, 64), CandidateMode = AutoMPointCandidateMode.WholeAnalysisRoi,
                PatternWidth = 64, PatternHeight = 64, MaximumFinalists = 1, MaximumResults = 1,
                MinimumContrastStdDev = 4, MinimumEdgeDensity = 0.005, MinimumQuadrantBalance = 0, MinimumOrientationBalance = 0, MinimumFeatureQuality = 0,
                MatchingMinimumScore = 0.8, MaximumTemplatePoints = 400, SearchStep = 2,
                UsePyramidPositionProposal = false, UseHybridVerify = false, SyntheticTranslationPixels = 0,
                MinimumRepresentativeImageCount = 3, MinimumRepresentativeSuccessRate = 1, MaximumPositionErrorPixels = 1
            });
            using (VisionToolResult result = teacher.Execute(reference, new[] { reference, light, blur }, new[] { blank }, CancellationToken.None))
            {
                Observation row = Observe("T01", "training", "teach one fixed whole datum and reject blank", reference, result, teacher.results.Count, directory);
                row.ContractPassed = result.Success && teacher.results.Count == 1 && teacher.results[0].PatternRoi == new Rect(44, 36, 64, 64)
                    && teacher.results[0].RepresentativeSuccessCount == 3 && teacher.results[0].NegativeRepresentativeMatches.Single().Success;
                row.Actual = new { TrainingHashes = trainingHashes, Candidates = teacher.candidates.Count, Accepted = teacher.results.Count };
                rows.Add(row);
            }
            using var learner = Matcher(template);
            EdgeTemplateModelArtifact artifact = learner.PrepareLearnedModel("2d-support-fixed-datum");
            byte[] encoded = EdgeTemplateModelArtifactCodec.Encode(artifact);
            string modelHash = Hash(encoded);
            EdgeTemplateModelArtifact restored = EdgeTemplateModelArtifactCodec.Decode(encoded);
            using var worker = new EdgeBasedTemplateMatchingTool { UsePoseRefinement = true };
            worker.SetProperty(MatchingSettings());
            worker.SetLearnedModel(restored);
            if (directory != null)
            {
                File.WriteAllBytes(Path.Combine(directory, "2d-support-model.bin"), encoded);
                foreach (var input in new[] { ("training-reference", reference), ("training-light", light), ("training-blur", blur), ("training-blank", blank) }) SaveInput(directory, input.Item1, input.Item2);
            }
            rows.Add(new Observation
            {
                Id = "T02", Split = "training", Expected = "model restored without inspection; exact canonical round trip", Outcome = "restored",
                ModelSha256 = modelHash, ContractPassed = worker.imageSource.Empty() && worker.results.Count == 0
                    && encoded.SequenceEqual(EdgeTemplateModelArtifactCodec.Encode(restored)),
                Actual = new { artifact.RawPointCount, PointCount = artifact.Points.Count, artifact.LearningParameters }, Message = "No source/results on restore; learned feature counts retained."
            });
            foreach (var item in MatchInputs)
            {
                using Mat scene = DatumScene(template, item.Angle, item.Scale, item.Dx, item.Dy, item.Gain, item.Offset, item.Blur, item.Challenge);
                worker.property.ALLOW_GLOBAL_POLARITY_REVERSAL = item.Challenge == "inverted";
                string expectation = item.Challenge == "blank" || item.Challenge == "similar" ? "absent" : item.Challenge == "duplicate" ? "ambiguous" : "present";
                using VisionToolResult result = worker.Execute(scene);
                Observation row = Observe(item.Id, item.Split, expectation, scene, result, worker.results.Count, directory);
                row.ModelSha256 = modelHash;
                row.CharacterizationOnly = item.Characterization;
                row.ContractPassed = !trainingHashes.Contains(row.InputSha256) && (result.Success || result.ErrorCode == VisionToolErrorCode.MatchingNoResult || result.ErrorCode == VisionToolErrorCode.MatchingAmbiguous);
                Point2d truth = AuthoredPoint(new Point2d(76, 68), item.Angle, item.Scale, item.Dx, item.Dy);
                row.Truth = new { X = truth.X, Y = truth.Y, Angle = item.Angle, Scale = item.Scale, InputRole = expectation, ExplicitPolarityReversal = item.Challenge == "inverted" };
                if (row.Detected)
                {
                    MatchingResult match = worker.results.Single();
                    row.PositionErrorPixels = EdgePoseRefinementSmokeSuite.Distance(new Point2d(match.Center.X, match.Center.Y), truth);
                    row.AngleErrorDegrees = Math.Abs(match.Angle - item.Angle);
                    row.ScaleError = Math.Abs(match.Scale - item.Scale);
                    row.Actual = new { match.Center.X, match.Center.Y, match.Angle, match.Scale, match.Score, match.EdgeScore };
                }
                row.QualityGatePassed = expectation == "present" ? row.Detected && row.PositionErrorPixels <= 0.3 && row.AngleErrorDegrees <= 0.15 && row.ScaleError <= 0.003
                    : expectation == "ambiguous" ? result.ErrorCode == VisionToolErrorCode.MatchingAmbiguous && worker.results.Count == 0
                    : result.ErrorCode == VisionToolErrorCode.MatchingNoResult && worker.results.Count == 0;
                if (!item.Characterization) row.ContractPassed &= row.QualityGatePassed == true;
                worker.property.ALLOW_GLOBAL_POLARITY_REVERSAL = false;
                using VisionToolResult recovery = worker.Execute(reference);
                row.RecoveryPassed = recovery.Success && worker.results.Count == 1;
                row.ContractPassed &= row.RecoveryPassed == true;
                rows.Add(row);
            }
            EvaluatePoseAmbiguity(rows, directory);
        }

        private static void EvaluatePoseAmbiguity(List<Observation> rows, string directory)
        {
            using Mat square = AmbiguityTemplate(false);
            using Mat scene = AmbiguityScene(square, false);
            using var matcher = Matcher(square);
            matcher.property.USE_UNIQUE_MATCH_VALIDATION = false;
            matcher.property.USE_FIND_SCALE = false;
            matcher.property.FIND_ANGLE_MIN = 0; matcher.property.FIND_ANGLE_MAX = 270; matcher.property.FIND_ANGLE = 90;
            matcher.UsePoseRefinement = false; matcher.UsePoseUniquenessValidation = true;
            using (VisionToolResult rejected = matcher.Execute(scene))
            {
                Observation row = Observe("M09", "held-out", "ambiguous same-center quarter turns", scene, rejected, matcher.results.Count, directory);
                row.QualityGatePassed = rejected.ErrorCode == VisionToolErrorCode.MatchingAmbiguous && matcher.results.Count == 0;
                matcher.UsePoseUniquenessValidation = false;
                using VisionToolResult recovered = matcher.Execute(scene);
                row.RecoveryPassed = recovered.Success && matcher.results.Count == 1;
                row.ContractPassed = row.QualityGatePassed == true && row.RecoveryPassed == true;
                rows.Add(row);
            }
            using Mat small = AmbiguityTemplate(true);
            using Mat concentric = AmbiguityScene(small, true);
            matcher.SetTemplateImage(small);
            matcher.property.USE_FIND_ANGLE = false; matcher.property.USE_FIND_SCALE = true;
            matcher.property.FIND_SCALE_MIN = 1; matcher.property.FIND_SCALE_MAX = 2; matcher.property.FIND_SCALE_STEP = 1;
            matcher.UsePoseUniquenessValidation = true;
            using VisionToolResult scaleResult = matcher.Execute(concentric);
            Observation scaleRow = Observe("M10", "held-out", "ambiguous same-center scales", concentric, scaleResult, matcher.results.Count, directory);
            scaleRow.QualityGatePassed = scaleResult.ErrorCode == VisionToolErrorCode.MatchingAmbiguous && matcher.results.Count == 0;
            matcher.UsePoseUniquenessValidation = false;
            using VisionToolResult scaleRecovery = matcher.Execute(concentric);
            scaleRow.RecoveryPassed = scaleRecovery.Success;
            scaleRow.ContractPassed = scaleRow.QualityGatePassed == true && scaleRow.RecoveryPassed == true;
            rows.Add(scaleRow);
        }

        private static Mat AmbiguityTemplate(bool scale)
        {
            Mat shape = new Mat(64, 64, MatType.CV_8UC1, Scalar.All(17));
            Cv2.Rectangle(shape, scale ? new Rect(16, 16, 32, 32) : new Rect(12, 12, 40, 40), Scalar.All(scale ? 205 : 225), -1);
            return shape;
        }

        private static Mat AmbiguityScene(Mat shape, bool scale)
        {
            if (!scale) return DatumScene(shape, 0, 1, 19, 13);
            Mat scene = new Mat(320, 384, MatType.CV_8UC1, Scalar.All(17));
            Cv2.Rectangle(scene, new Rect(52, 30, 64, 64), Scalar.All(205), -1);
            Cv2.Rectangle(scene, new Rect(68, 46, 32, 32), Scalar.All(245), -1);
            return scene;
        }

        private static Rect Bounds(Point2d[] corners) => new Rect((int)Math.Floor(corners.Min(point => point.X)), (int)Math.Floor(corners.Min(point => point.Y)),
            (int)Math.Ceiling(corners.Max(point => point.X)) - (int)Math.Floor(corners.Min(point => point.X)),
            (int)Math.Ceiling(corners.Max(point => point.Y)) - (int)Math.Floor(corners.Min(point => point.Y)));

        private static Point2d[] Corners(Rect roi) => new[] { new Point2d(roi.X, roi.Y), new Point2d(roi.Right, roi.Y), new Point2d(roi.Right, roi.Bottom), new Point2d(roi.X, roi.Bottom) };

        private static Mat WorkflowScene(Mat template, double angle, double scale, double dx, double dy, out List<Point2d> markPixels, out double slope, out double intercept)
        {
            Mat scene = DatumScene(template, angle, scale, dx, dy);
            double cos = Math.Cos(angle * Math.PI / 180), sin = Math.Sin(angle * Math.PI / 180);
            double d = 0.03 * 67.5 + 216.93 - 75.5;
            slope = (0.03 * cos + sin) / (cos - 0.03 * sin);
            intercept = 75.5 + dx + scale * cos * d - slope * (67.5 + dy - scale * sin * d);
            Rect gaugePatch = Bounds(Corners(new Rect(154, 124, 141, 129)).Select(point => AuthoredPoint(point, angle, scale, dx, dy)).ToArray());
            for (int y = gaugePatch.Y; y < gaugePatch.Bottom; y++)
            for (int x = gaugePatch.X; x < gaugePatch.Right; x++)
                scene.Set(y, x, (byte)Math.Round(120 + 80 * Math.Tanh((x - slope * y - intercept) / 1.6)));
            markPixels = new List<Point2d>();
            for (int y = 0; y < scene.Rows; y++)
            for (int x = 0; x < scene.Cols; x++)
            {
                double tx = (x - 75.5 - dx) / scale, ty = (y - 67.5 - dy) / scale;
                double rx = 75.5 + cos * tx - sin * ty, ry = 67.5 + sin * tx + cos * ty;
                if (rx >= 162 && rx < 177 && ry >= 78 && ry < 89)
                {
                    scene.Set(y, x, (byte)230);
                    markPixels.Add(new Point2d(x, y));
                }
            }
            return scene;
        }

        private static void EvaluateRoiWorkflow(List<Observation> rows, string directory)
        {
            using Mat template = EdgePoseRefinementSmokeSuite.CreateTemplate();
            using Mat reference = WorkflowScene(template, 0, 1, 0, 0, out _, out _, out _);
            using var matcher = Matcher(template);
            // Restrict the datum search to its taught area; measurement ROIs are separate Host routing.
            matcher.property.CvROI = new Rect(28, 20, 150, 110);
            using VisionToolResult referenceExecution = matcher.Execute(reference);
            Require(referenceExecution.Success, "Workflow reference match failed: " + referenceExecution.Message);
            MatchingResult referenceMatch = matcher.results.Single();
            foreach (var item in MatchInputs.Where(item => item.Id == "M01" || item.Id == "M02"))
            {
                using Mat source = WorkflowScene(template, item.Angle, item.Scale, item.Dx, item.Dy, out var markPixels, out double slope, out double intercept);
                using Mat bgr = new Mat(); using Mat gray = new Mat();
                Cv2.CvtColor(source, bgr, ColorConversionCodes.GRAY2BGR);
                Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);
                matcher.property.CvROI = new Rect(28, 20, 190, 170);
                using VisionToolResult matched = matcher.Execute(gray);
                Require(matched.Success, "Workflow held-out match failed: " + matched.Message);
                MatchingResult currentMatch = matcher.results.Single();
                MatchingCoordinateTransform transform = MatchingCoordinateTransform.FromMatches(referenceMatch, currentMatch);
                var independent = new MatchingCoordinateTransform(new Point2d(75.5, 67.5), new Point2d(75.5 + item.Dx, 67.5 + item.Dy), item.Angle, item.Scale);
                Rect markReferenceRoi = new Rect(154, 70, 31, 29), gaugeReferenceRoi = new Rect(174, 144, 101, 89);
                double formulaError = Corners(gaugeReferenceRoi).Max(point => EdgePoseRefinementSmokeSuite.Distance(independent.TransformPoint(point), AuthoredPoint(point, item.Angle, item.Scale, item.Dx, item.Dy)));
                Rect markRoi = Bounds(transform.TransformRoi(new Rect2d(markReferenceRoi.X, markReferenceRoi.Y, markReferenceRoi.Width, markReferenceRoi.Height)));
                Rect gaugeRoi = Bounds(transform.TransformRoi(new Rect2d(gaugeReferenceRoi.X, gaugeReferenceRoi.Y, gaugeReferenceRoi.Width, gaugeReferenceRoi.Height)));
                bool inside = new Rect(0, 0, source.Cols, source.Rows).Contains(markRoi) && new Rect(0, 0, source.Cols, source.Rows).Contains(gaugeRoi);
                Require(inside, "Host must refuse an out-of-frame transformed ROI before executing downstream Tools.");
                using var blob = new BlobTool();
                blob.SetProperty(new BlobToolProperty { USE_ROI = true, CvROI = markRoi, THRESHOLD = 128, MIN_AREA = 80, MAX_AREA = 300 });
                using VisionToolResult presence = blob.Execute(gray);
                VisionPipelineStep gaugeStep = GaugeStep(gaugeRoi);
                VisionPipelineStep restoredStep = RoundTrip(gaugeStep);
                using var gauge = (LineGaugeTool)VisionPipelineToolFactory.Create(restoredStep);
                bool restoreQuiet = gauge.imageSource.Empty() && gauge.resultList.Count == 0;
                using VisionToolResult measured = gauge.Execute(gray);
                LineGaugeSubpixelEvidence evidence = gauge.resultList.SingleOrDefault()?.SubpixelEvidence;
                BlobResult found = blob.results.SingleOrDefault();
                double? pointError = evidence?.HasFit == true ? evidence.Points.Max(point => Math.Abs(point.X - slope * point.Y - intercept)) : null;
                double? centroidError = found == null ? null : EdgePoseRefinementSmokeSuite.Distance(found.Center, new Point2d(markPixels.Average(point => point.X), markPixels.Average(point => point.Y)));
                Observation row = Observe(item.Id == "M01" ? "R01" : "R02", "held-out", "matched datum -> explicit ROI -> presence and global pixel line", gray, measured, gauge.resultList.Count, directory);
                row.Truth = new { MarkArea = markPixels.Count, MarkCenterX = markPixels.Average(point => point.X), MarkCenterY = markPixels.Average(point => point.Y), Slope = slope, InterceptPx = intercept };
                row.MaximumPointErrorPixels = pointError;
                row.Actual = new { MarkRoi = new[] { markRoi.X, markRoi.Y, markRoi.Width, markRoi.Height }, GaugeRoi = new[] { gaugeRoi.X, gaugeRoi.Y, gaugeRoi.Width, gaugeRoi.Height }, Area = found?.Area, CentroidError = centroidError, Slope = evidence?.HasFit == true ? (double?)evidence.Slope : null, InterceptPx = evidence?.HasFit == true ? (double?)evidence.Intercept : null, FormulaError = formulaError, RestoreExecuted = !restoreQuiet };
                row.QualityGatePassed = presence.Success && found != null && found.Area == markPixels.Count && centroidError <= 0.011 && measured.Success && pointError <= 0.125;
                row.ContractPassed = row.QualityGatePassed == true && formulaError <= 1e-9 && restoreQuiet && ImageHash(gray) == ImageHash(source);
                rows.Add(row);
            }
        }

        private static VisionPipelineStep GaugeStep(Rect roi)
        {
            var step = new VisionPipelineStep { ToolType = "lineGauge" };
            step.Parameters[nameof(LineGaugeToolProperty.CvROI)] = FormattableString.Invariant($"{roi.X},{roi.Y},{roi.Width},{roi.Height}");
            step.Parameters[nameof(LineGaugeToolProperty.USE_ROI)] = "true";
            step.Parameters[nameof(LineGaugeToolProperty.UseSubpixelMeasurement)] = "true";
            step.Parameters[nameof(LineGaugeToolProperty.SubpixelProfileRadius)] = "6";
            step.Parameters[nameof(LineGaugeToolProperty.CONTRAST)] = "4";
            step.Parameters[nameof(LineGaugeToolProperty.THICKNESS)] = "2";
            step.Parameters[nameof(LineGaugeToolProperty.SAMPLING_STEP)] = "1";
            step.Parameters[nameof(LineGaugeToolProperty.PRJ_PORALITY)] = "ALL";
            return step;
        }

        private static VisionPipelineStep RoundTrip(VisionPipelineStep step)
        {
            var serializer = new XmlSerializer(typeof(VisionPipelineStep));
            using var writer = new StringWriter(CultureInfo.InvariantCulture);
            serializer.Serialize(writer, step);
            using var reader = new StringReader(writer.ToString());
            using var xml = XmlReader.Create(reader, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            return (VisionPipelineStep)serializer.Deserialize(xml);
        }

        private static Mat GrayResponse(double intercept, double amplitude, string response)
        {
            Mat image = new Mat(137, 173, MatType.CV_8UC1);
            for (int y = 0; y < image.Rows; y++)
            for (int x = 0; x < image.Cols; x++)
            {
                double t = x - intercept - (response == "border" ? 0 : 0.03) * y;
                double value = response == "asymmetric-ramp" ? 40 + 160 * Math.Max(0, Math.Min(1, (t + 2) / 6))
                    : response == "reflection" ? 120 + 60 * Math.Tanh(t / 0.8) + 20 * Math.Tanh((t - 3) / 0.8)
                    : 120 + amplitude * Math.Tanh(t / 1.6);
                image.Set(y, x, (byte)Math.Round(value));
            }
            return image;
        }

        private static void EvaluatePixels(List<Observation> rows, string directory)
        {
            using var gauge = new LineGaugeTool();
            var settings = LineGaugeSubpixelSmokeSuite.Settings(); settings.CvROI = new Rect(0, 0, 173, 137);
            gauge.SetProperty(settings);
            using Mat recoveryInput = GrayResponse(43.625, 80, "tanh");
            foreach (var item in PixelInputs)
            {
                using Mat scene = GrayResponse(item.Intercept, item.Amplitude, item.Response);
                using VisionToolResult result = gauge.Execute(scene);
                var evidence = gauge.resultList.SingleOrDefault()?.SubpixelEvidence;
                Observation row = Observe(item.Id, item.Split, item.Amplitude == 1 ? "below-contrast rejection" : item.Response == "border" ? "truncated gradient window rejection" : "analytic pixel edge", scene, result, gauge.resultList.Count, directory);
                row.CharacterizationOnly = item.Characterization;
                row.Truth = new { PrimaryInterceptPx = item.Intercept, Slope = item.Response == "border" ? 0 : 0.03, ExpectedResponseInterceptPx = item.Intercept + (item.Response == "asymmetric-ramp" ? 1 : item.Response == "reflection" ? 0.75 : 0), item.Response, item.Amplitude };
                if (result.Success && evidence?.HasFit == true)
                {
                    row.MaximumPointErrorPixels = evidence.Points.Max(point => Math.Abs(point.X - item.Intercept - 0.03 * point.Y));
                    row.ReportedBiasPixels = evidence.Intercept - item.Intercept;
                    double expectedResponse = item.Intercept + (item.Response == "asymmetric-ramp" ? 1 : item.Response == "reflection" ? 0.75 : 0);
                    row.ResponseErrorPixels = evidence.Points.Max(point => Math.Abs(point.X - expectedResponse - 0.03 * point.Y));
                    row.Actual = new { evidence.SampleCount, evidence.RejectedPointCount, evidence.Slope, evidence.Intercept, evidence.OrthogonalRmse };
                }
                row.QualityGatePassed = item.Amplitude == 1 ? result.ErrorCode == VisionToolErrorCode.LineGaugeEdgeNotFound && evidence?.HasFit != true
                    : item.Response == "border" ? result.ErrorCode == VisionToolErrorCode.LineGaugeFitFailed && evidence?.HasFit != true && evidence?.RejectedPointCount > 0
                    : result.Success && row.MaximumPointErrorPixels <= 0.125;
                row.ContractPassed = item.Characterization ? result.Success && evidence?.HasFit == true && row.ResponseErrorPixels <= 0.125 : row.QualityGatePassed == true;
                using VisionToolResult recovery = gauge.Execute(recoveryInput);
                row.RecoveryPassed = recovery.Success && gauge.resultList.Single().SubpixelEvidence.HasFit;
                row.ContractPassed &= row.RecoveryPassed == true;
                rows.Add(row);
            }
        }

        private static void EvaluateFailureRecovery(List<Observation> rows, string directory)
        {
            using Mat template = EdgePoseRefinementSmokeSuite.CreateTemplate();
            using Mat source = DatumScene(template, 0, 1, 19, 13);
            using var matcher = Matcher(template);
            foreach (string failure in MatchingFailures)
            {
                var valid = MatchingSettings(); matcher.SetProperty(valid);
                using VisionToolResult before = matcher.Execute(source);
                Require(before.Success, "Matching failure control must start with success.");
                MatchingResult snapshot = matcher.results.Single();
                if (failure == "roi") matcher.property.CvROI = new Rect(380, 310, 64, 64);
                if (failure == "property") matcher.SetProperty(null);
                using VisionToolResult failed = matcher.Execute(failure == "null" ? null : source);
                Observation row = Observe("F-match-" + failure, "failure-control", "failure without stale successful coordinates", failure == "null" ? null : source, failed, matcher.results.Count, directory);
                VisionToolErrorCode expectedCode = failure == "null" ? VisionToolErrorCode.InputImageInvalid : failure == "roi" ? VisionToolErrorCode.MatchingRoiInvalid : VisionToolErrorCode.ToolPropertyMissing;
                row.ExpectedError = expectedCode.ToString();
                row.ContractPassed = failed.ErrorCode == expectedCode && matcher.results.Count == 0 && snapshot.Center.X > 0;
                matcher.SetProperty(MatchingSettings());
                using VisionToolResult recovered = matcher.Execute(source);
                row.RecoveryPassed = recovered.Success && matcher.results.Count == 1;
                row.ContractPassed &= row.RecoveryPassed == true;
                rows.Add(row);
            }
            using Mat mark = PresenceControl();
            using var blob = new BlobTool();
            foreach (string failure in BlobFailures)
            {
                var valid = new BlobToolProperty { USE_ROI = true, CvROI = new Rect(10, 7, 37, 31), MIN_AREA = 100, MAX_AREA = 220 };
                blob.SetProperty(valid);
                using VisionToolResult before = blob.Execute(mark);
                Require(before.Success, "Presence failure control must start with success.");
                BlobResult snapshot = blob.results.Single();
                var oldCandidate = blob.candidates.Single();
                if (failure == "roi") valid.CvROI = new Rect(59, 45, 20, 20);
                if (failure == "property") blob.SetProperty(null);
                if (failure == "area") valid.MIN_AREA = 300;
                using VisionToolResult failed = blob.Execute(failure == "null" ? null : mark);
                Observation row = Observe("F-blob-" + failure, "failure-control", "failure without stale successful presence or candidates", failure == "null" ? null : mark, failed, blob.results.Count, directory);
                row.Actual = new { Results = blob.results.Count, Candidates = blob.candidates.Count };
                VisionToolErrorCode expectedCode = failure == "null" ? VisionToolErrorCode.InputImageInvalid : failure == "roi" ? VisionToolErrorCode.BlobRoiInvalid
                    : failure == "area" ? VisionToolErrorCode.BlobInvalidAreaRange : VisionToolErrorCode.ToolPropertyMissing;
                row.ExpectedError = expectedCode.ToString();
                row.ContractPassed = failed.ErrorCode == expectedCode && blob.results.Count == 0 && blob.candidates.Count == 0 && snapshot.Area == 165 && oldCandidate.Area == 165;
                blob.SetProperty(new BlobToolProperty { MIN_AREA = 100, MAX_AREA = 220 });
                using VisionToolResult recovered = blob.Execute(mark);
                row.RecoveryPassed = recovered.Success && blob.results.Single().Area == 165;
                row.ContractPassed &= row.RecoveryPassed == true;
                rows.Add(row);
            }
            using Mat line = GrayResponse(41.375, 80, "tanh");
            using Mat depth = new Mat(137, 173, MatType.CV_16UC1, Scalar.All(120));
            using var gauge = new LineGaugeTool();
            foreach (bool subpixel in new[] { false, true })
            foreach (string failure in LineFailures)
            {
                var valid = LineGaugeSubpixelSmokeSuite.Settings(); valid.CvROI = new Rect(0, 0, 173, 137); valid.UseSubpixelMeasurement = subpixel;
                gauge.SetProperty(valid);
                using VisionToolResult before = gauge.Execute(line);
                Require(before.Success, "Line failure control must start with success.");
                var snapshot = gauge.resultList.Single();
                if (failure == "roi") valid.CvROI = new Rect(170, 130, 20, 20);
                if (failure == "property") gauge.SetProperty(null);
                if (failure == "sampling") valid.SAMPLING_STEP = 0;
                bool failedExecution;
                Observation row;
                if (failure == "cancel")
                {
                    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
                    RequireThrows<OperationCanceledException>(() => gauge.Execute(line, cancelled.Token));
                    row = new Observation { Id = "F-line-" + subpixel + "-cancel", Split = "failure-control", Expected = "cancel without stale successful points", Outcome = "OperationCanceledException", InputSha256 = ImageHash(line), ResultCount = gauge.resultList.Count };
                    failedExecution = true;
                }
                else
                {
                    using VisionToolResult failed = gauge.Execute(failure == "null" ? null : failure == "depth" ? depth : line);
                    row = Observe("F-line-" + subpixel + "-" + failure, "failure-control", "failure without stale successful points", failure == "null" ? null : failure == "depth" ? depth : line, failed, gauge.resultList.Count, directory);
                    VisionToolErrorCode expectedCode = failure == "null" || failure == "depth" ? VisionToolErrorCode.InputImageInvalid : failure == "roi" ? VisionToolErrorCode.LineGaugeRoiInvalid
                        : failure == "sampling" ? VisionToolErrorCode.LineGaugeInvalidSampling : VisionToolErrorCode.ToolPropertyMissing;
                    row.ExpectedError = expectedCode.ToString();
                    failedExecution = failed.ErrorCode == expectedCode;
                }
                row.ContractPassed = failedExecution && gauge.resultList.Count == 0 && snapshot.edgeList.Count > 3 && (!subpixel || snapshot.SubpixelEvidence.HasFit);
                var recoverySettings = LineGaugeSubpixelSmokeSuite.Settings(); recoverySettings.CvROI = new Rect(0, 0, 173, 137); recoverySettings.UseSubpixelMeasurement = !subpixel;
                gauge.SetProperty(recoverySettings);
                using VisionToolResult recovered = gauge.Execute(line);
                row.RecoveryPassed = recovered.Success && (gauge.resultList.Single().SubpixelEvidence != null) == !subpixel;
                row.ContractPassed &= row.RecoveryPassed == true;
                rows.Add(row);
            }
            var extra = LineGaugeSubpixelSmokeSuite.Settings(); extra.CvROI = new Rect(0, 0, 173, 137); gauge.SetProperty(extra);
            foreach (bool radius in new[] { true, false })
            {
                using VisionToolResult before = gauge.Execute(line);
                Require(before.Success, "Subpixel setting control failed.");
                if (radius) extra.SubpixelProfileRadius = 1; else extra.CONTRAST = double.NaN;
                using VisionToolResult failed = gauge.Execute(line);
                Observation row = Observe(radius ? "F-line-radius" : "F-line-nonfinite", "failure-control", "invalid subpixel setting refusal", line, failed, gauge.resultList.Count, directory);
                row.ContractPassed = failed.ErrorCode == VisionToolErrorCode.LineGaugeInvalidSampling && gauge.resultList.Count == 0;
                extra.SubpixelProfileRadius = 6; extra.CONTRAST = 4;
                using VisionToolResult recovered = gauge.Execute(line);
                row.RecoveryPassed = recovered.Success;
                row.ContractPassed &= row.RecoveryPassed == true;
                rows.Add(row);
            }
        }

        private static Mat PresenceControl()
        {
            Mat mark = new Mat(47, 61, MatType.CV_8UC1, Scalar.All(17));
            Cv2.Rectangle(mark, new Rect(19, 13, 15, 11), Scalar.All(230), -1);
            return mark;
        }

        private static Observation Observe(string id, string split, string expectation, Mat source, VisionToolResult result, int count, string directory)
        {
            if (directory != null && source != null) SaveInput(directory, id, source);
            return new Observation
            {
                Id = id, Split = split, Expected = expectation, Outcome = result.ErrorName, Message = result.Message,
                InputSha256 = source == null ? "null" : ImageHash(source), Detected = result.Success && count > 0, ResultCount = count,
                Metrics = result.Metrics.Where(item => double.IsFinite(item.Value)).ToDictionary(item => item.Key, item => item.Value)
            };
        }

        private static string ImageHash(Mat image) => Hash(image.ToBytes(".png"));
        private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
        private static void SaveInput(string directory, string id, Mat image)
        {
            string inputs = Path.Combine(directory, "inputs");
            Directory.CreateDirectory(inputs);
            File.WriteAllBytes(Path.Combine(inputs, id + ".png"), image.ToBytes(".png"));
        }

        private sealed class Observation
        {
            public string Id { get; set; }
            public string Split { get; set; }
            public string Expected { get; set; }
            public string ExpectedError { get; set; }
            public string Outcome { get; set; }
            public string Message { get; set; }
            public string InputSha256 { get; set; }
            public string ModelSha256 { get; set; }
            public bool Detected { get; set; }
            public int ResultCount { get; set; }
            public bool CharacterizationOnly { get; set; }
            public bool ContractPassed { get; set; }
            public bool? QualityGatePassed { get; set; }
            public bool? RecoveryPassed { get; set; }
            public double? PositionErrorPixels { get; set; }
            public double? AngleErrorDegrees { get; set; }
            public double? ScaleError { get; set; }
            public double? MaximumPointErrorPixels { get; set; }
            public double? ReportedBiasPixels { get; set; }
            public double? ResponseErrorPixels { get; set; }
            public object Truth { get; set; }
            public object Actual { get; set; }
            public Dictionary<string, double> Metrics { get; set; }
        }
    }
}

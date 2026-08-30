using OpenVisionLab.Inspection;
using OpenVisionLab.Vision2D;
using OpenVisionLab.Vision2D.Pipeline;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Tool;
using OpenVisionLab.Vision3D.FeatureExtraction;
using OpenVisionLab.Vision3D.Geometry;
using OpenVisionLab.Vision3D.Inspection;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;
using static OpenVisionLab.Inspection.Smoke.SmokeFixtures;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class Vision2DSmokeSuite
    {
        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase("2D affine transform recovers a known matrix and drawings", TestAffineTransformKnownMatrix);
            yield return new SmokeCase("2D affine transform rejects collinear source teaching", TestAffineTransformDegenerateSource);
            yield return new SmokeCase("2D affine transform retains evidence on coverage failure", TestAffineTransformCoverageFailure);
            yield return new SmokeCase("Mean multi ROI measures each region and preserves result identity", TestMeanMultiRoi);
            yield return new SmokeCase("Corner detection publishes global points and handles no result", TestCornerResultContract);
            yield return new SmokeCase("Auto MPoint suggests a unique pattern deterministically", TestAutoMPointUniquePattern);
            yield return new SmokeCase("Auto MPoint rejects a repeated ambiguous pattern", TestAutoMPointRepeatedPattern);
            yield return new SmokeCase("Auto MPoint rejects invalid ROI and pattern size", TestAutoMPointInvalidDefinition);
            yield return new SmokeCase("Auto MPoint selects the best representative-image pattern", TestAutoMPointRepresentativeBestPattern);
            yield return new SmokeCase("Auto MPoint rejects an invalid representative set", TestAutoMPointInvalidRepresentativeSet);
            yield return new SmokeCase("Edge matcher preserves legacy single-result behavior", TestEdgeMatcherLegacySingleResult);
            yield return new SmokeCase("Edge matcher accepts one unique candidate", TestEdgeMatcherUniqueSuccess);
            yield return new SmokeCase("Edge matcher rejects repeated candidates as ambiguous", TestEdgeMatcherUniqueAmbiguous);
            yield return new SmokeCase("Edge matcher reports no match without a candidate", TestEdgeMatcherUniqueNoMatch);
            yield return new SmokeCase("Edge matcher global polarity is opt-in and reports the selected state", TestEdgeMatcherGlobalPolarity);
            yield return new SmokeCase("MorphologyTool direct Execute transforms a synthetic image", TestMorphologyDirectExecution);
            yield return new SmokeCase("FilterTool direct Execute transforms a synthetic image", TestFilterDirectExecution);
            yield return new SmokeCase("EdgeDetectionTool direct Execute finds synthetic edges", TestEdgeDetectionDirectExecution);
            yield return new SmokeCase("RotateScaleTool direct Execute applies the requested output size", TestRotateScaleDirectExecution);
            yield return new SmokeCase("LineGaugeTool rejects unsupported image depth explicitly", TestLineGaugeUnsupportedDepth);
            yield return new SmokeCase("2D tool and result disposal release only owned images", TestVisionToolResourceOwnership);
            yield return new SmokeCase("Pipeline runtime honors tool, input, result, and layer ownership", TestVisionPipelineResourceOwnership);
            yield return new SmokeCase("Pipeline routes only non-null images to named output layers", TestVisionPipelineOptionalOutputContract);
            yield return new SmokeCase("Pipeline factory creates every built-in tool from valid parameters", TestVisionPipelineFactoryBuiltIns);
            yield return new SmokeCase("Pipeline factory rejects malformed, unknown, and duplicate parameters", TestVisionPipelineFactoryRejectsInvalidParameters);
            yield return new SmokeCase("Pipeline rejects configurations without an executable step", TestVisionPipelineRejectsNoExecutableSteps);
            yield return new SmokeCase("Pipeline acceptance supports only a terminal expected failure", TestVisionPipelineExpectedFailureAcceptance);
        }

        private static void TestAffineTransformKnownMatrix()
        {
            using (Mat source = new Mat(new Size(160, 120), MatType.CV_8UC1, Scalar.All(0)))
            {
                Cv2.Rectangle(source, new Rect(20, 20, 50, 40), Scalar.All(255), -1);
                AffineTransformTool tool = new AffineTransformTool();
                tool.SetProperty(new AffineTransformToolProperty
                {
                    SourcePoint1X = 0,
                    SourcePoint1Y = 0,
                    SourcePoint2X = 100,
                    SourcePoint2Y = 0,
                    SourcePoint3X = 0,
                    SourcePoint3Y = 100,
                    DestinationPoint1X = 12,
                    DestinationPoint1Y = 18,
                    DestinationPoint2X = 132,
                    DestinationPoint2Y = 8,
                    DestinationPoint3X = 37,
                    DestinationPoint3Y = 108,
                    OutputWidth = 240,
                    OutputHeight = 180,
                    MinimumSourceTriangleArea = 100,
                    MinimumDestinationTriangleArea = 100,
                    MinimumValidPixelRatio = 0.4
                });

                VisionToolResult result = tool.Execute(source);
                try
                {
                    Require(result.Success, "Known 2D affine transform must pass. " + result.ErrorName + ": " + result.Message);
                    Require(result.ResultImage != null && result.ResultImage.Width == 240 && result.ResultImage.Height == 180,
                        "2D affine transform did not honor the taught output size.");
                    RequireApproximately(result.Metrics["AffineM11"], 1.2, 1e-6, "Unexpected affine M11.");
                    RequireApproximately(result.Metrics["AffineM12"], 0.25, 1e-6, "Unexpected affine M12.");
                    RequireApproximately(result.Metrics["AffineM13"], 12.0, 1e-6, "Unexpected affine M13.");
                    RequireApproximately(result.Metrics["AffineM21"], -0.1, 1e-6, "Unexpected affine M21.");
                    RequireApproximately(result.Metrics["AffineM22"], 0.9, 1e-6, "Unexpected affine M22.");
                    RequireApproximately(result.Metrics["AffineM23"], 18.0, 1e-6, "Unexpected affine M23.");
                    Require(result.Metrics["AffineValidPixelRatio"] >= 0.4,
                        "Known 2D affine transform did not retain the declared source coverage.");
                    Require(result.Overlays.Count == 10,
                        "2D affine transform must retain three destination points, three destination edges, and four frame edges.");
                }
                finally
                {
                    result.ResultImage?.Dispose();
                }
            }
        }

        private static void TestAffineTransformDegenerateSource()
        {
            using (Mat source = new Mat(new Size(64, 64), MatType.CV_8UC1, Scalar.All(255)))
            {
                AffineTransformTool tool = new AffineTransformTool();
                tool.SetProperty(new AffineTransformToolProperty
                {
                    SourcePoint1X = 0,
                    SourcePoint1Y = 0,
                    SourcePoint2X = 10,
                    SourcePoint2Y = 10,
                    SourcePoint3X = 20,
                    SourcePoint3Y = 20,
                    MinimumSourceTriangleArea = 0
                });

                VisionToolResult result = tool.Execute(source);
                Require(!result.Success && result.ErrorCode == VisionToolErrorCode.AffineDegenerateSource,
                    "Collinear source teaching must fail with AffineDegenerateSource even when the operator area gate is zero.");
                Require(result.ResultStatus == VisionToolResultStatus.InvalidParameter,
                    "Collinear source teaching must be classified as an invalid parameter.");
            }
        }

        private static void TestAffineTransformCoverageFailure()
        {
            using (Mat source = new Mat(new Size(64, 64), MatType.CV_8UC1, Scalar.All(255)))
            {
                AffineTransformTool tool = new AffineTransformTool();
                tool.SetProperty(new AffineTransformToolProperty
                {
                    DestinationPoint1X = 500,
                    DestinationPoint1Y = 500,
                    DestinationPoint2X = 600,
                    DestinationPoint2Y = 500,
                    DestinationPoint3X = 500,
                    DestinationPoint3Y = 600,
                    OutputWidth = 64,
                    OutputHeight = 64,
                    MinimumValidPixelRatio = 0.1
                });

                VisionToolResult result = tool.Execute(source);
                try
                {
                    Require(!result.Success && result.ErrorCode == VisionToolErrorCode.AffineInsufficientCoverage,
                        "Off-frame affine teaching must fail with AffineInsufficientCoverage.");
                    Require(result.ResultImage != null && !result.ResultImage.Empty(),
                        "Coverage failure must retain the transformed image for correction evidence.");
                    Require(result.Metrics.ContainsKey("AffineValidPixelRatio")
                        && result.Metrics["AffineValidPixelRatio"] == 0,
                        "Coverage failure must retain the measured valid-pixel ratio.");
                    Require(result.Overlays.Count == 10,
                        "Coverage failure must retain the taught geometry overlays.");
                }
                finally
                {
                    result.ResultImage?.Dispose();
                }
            }
        }

        private static void TestAutoMPointUniquePattern()
        {
            using (Mat source = CreateAutoMPointUniqueSource())
            {
                AutoMPointToolProperty property = CreateAutoMPointProperty(
                    new Rect(0, 0, source.Width, source.Height),
                    64,
                    64,
                    32);
                AutoMPointTool firstTool = new AutoMPointTool();
                firstTool.SetProperty(property);
                AutoMPointTool secondTool = new AutoMPointTool();
                secondTool.SetProperty(property);

                VisionToolResult first = firstTool.Execute(source);
                VisionToolResult second = secondTool.Execute(source);
                try
                {
                    Require(first.Success, "Unique Auto MPoint source must produce a suggestion. " + first.ErrorName + ": " + first.Message);
                    Require(second.Success, "Repeated Auto MPoint execution must produce a suggestion. " + second.ErrorName + ": " + second.Message);
                    Require(firstTool.results.Count > 0 && secondTool.results.Count == firstTool.results.Count,
                        "Auto MPoint must retain the same non-empty result count.");
                    Require(firstTool.results[0].Accepted && firstTool.results[0].Rank == 1,
                        "Auto MPoint best result must be accepted and ranked first.");
                    Require(firstTool.results[0].UniquenessMargin >= property.MinimumUniquenessMargin,
                        "Auto MPoint best result must satisfy the uniqueness gate.");
                    Require(firstTool.results[0].SyntheticSuccessRate >= property.MinimumSyntheticSuccessRate,
                        "Auto MPoint best result must satisfy the synthetic stability gate.");
                    Require(firstTool.results[0].PositionErrorMaxPixels <= property.MaximumPositionErrorPixels,
                        "Auto MPoint best result must satisfy the position precision gate.");
                    Require(double.IsFinite(firstTool.results[0].RuntimeMedianMilliseconds)
                        && double.IsFinite(firstTool.results[0].RuntimeP95Milliseconds),
                        "Auto MPoint must publish finite runtime measurements.");
                    Require(first.Overlays.Count == firstTool.results.Count * 2,
                        "Auto MPoint must publish one pattern rectangle and one MPoint overlay per result.");
                    Require(firstTool.results.Select(candidate => candidate.PatternRoi)
                        .SequenceEqual(secondTool.results.Select(candidate => candidate.PatternRoi)),
                        "Auto MPoint result ranking must be deterministic for the same source.");
                    Require(Cv2.Norm(first.ResultImage, second.ResultImage, NormTypes.L1) == 0d,
                        "Auto MPoint result drawing must be deterministic for the same source.");

                    SaveAutoMPointEvidence(
                        "unique",
                        source,
                        first,
                        new[]
                        {
                            "Status=Accepted",
                            "ResultCount=" + firstTool.results.Count,
                            "BestPatternRoi=" + firstTool.results[0].PatternRoi,
                            "BestScore=" + firstTool.results[0].Score.ToString("0.000"),
                            "BestUniquenessMargin=" + firstTool.results[0].UniquenessMargin.ToString("0.000000"),
                            "BestPositionErrorMaxPx=" + firstTool.results[0].PositionErrorMaxPixels.ToString("0.000"),
                            "BestRuntimeMedianMs=" + firstTool.results[0].RuntimeMedianMilliseconds.ToString("0.000"),
                            "BestRuntimeP95Ms=" + firstTool.results[0].RuntimeP95Milliseconds.ToString("0.000")
                        });
                }
                finally
                {
                    first.ResultImage?.Dispose();
                    second.ResultImage?.Dispose();
                }
            }
        }

        private static void TestAutoMPointRepeatedPattern()
        {
            using (Mat source = CreateAutoMPointRepeatedSource())
            {
                AutoMPointToolProperty property = CreateAutoMPointProperty(
                    new Rect(0, 0, 128, 64),
                    64,
                    64,
                    64);
                property.MaximumFinalists = 2;
                property.MaximumResults = 2;
                property.MinimumUniquenessMargin = 0.1;

                AutoMPointTool tool = new AutoMPointTool();
                tool.SetProperty(property);
                VisionToolResult result = tool.Execute(source);
                try
                {
                    Require(!result.Success && result.ErrorCode == VisionToolErrorCode.AutoMPointNoCandidate,
                        "Two identical patterns must fail with AutoMPointNoCandidate.");
                    Require(tool.candidates.Count == 2 && tool.results.Count == 0,
                        "Both repeated candidates must be evaluated and neither may be suggested.");
                    Require(tool.candidates.All(candidate =>
                            !candidate.Accepted
                            && candidate.RejectReason.IndexOf("UniquenessMargin", StringComparison.Ordinal) >= 0),
                        "Repeated patterns must fail specifically at the uniqueness gate.");

                    SaveAutoMPointEvidence(
                        "repeated",
                        source,
                        result,
                        new[]
                        {
                            "Status=Rejected",
                            "ErrorCode=" + result.ErrorCode,
                            "CandidateCount=" + tool.candidates.Count,
                            "AcceptedCount=" + tool.results.Count,
                            "Candidate1Reason=" + tool.candidates[0].RejectReason,
                            "Candidate2Reason=" + tool.candidates[1].RejectReason
                        });
                }
                finally
                {
                    result.ResultImage?.Dispose();
                }
            }
        }

        private static void TestAutoMPointInvalidDefinition()
        {
            using (Mat source = CreateAutoMPointUniqueSource())
            {
                AutoMPointTool invalidRoiTool = new AutoMPointTool();
                invalidRoiTool.SetProperty(CreateAutoMPointProperty(
                    new Rect(source.Width - 10, source.Height - 10, 64, 64),
                    64,
                    64,
                    32));
                VisionToolResult invalidRoi = invalidRoiTool.Execute(source);
                Require(!invalidRoi.Success && invalidRoi.ErrorCode == VisionToolErrorCode.AutoMPointInvalidRoi,
                    "Out-of-image Auto MPoint ROI must fail with AutoMPointInvalidRoi.");

                AutoMPointTool invalidPatternTool = new AutoMPointTool();
                invalidPatternTool.SetProperty(CreateAutoMPointProperty(
                    new Rect(0, 0, 80, 80),
                    96,
                    96,
                    16));
                VisionToolResult invalidPattern = invalidPatternTool.Execute(source);
                Require(!invalidPattern.Success && invalidPattern.ErrorCode == VisionToolErrorCode.AutoMPointInvalidPatternSize,
                    "Oversized Auto MPoint pattern must fail with AutoMPointInvalidPatternSize.");
            }
        }

        private static void TestAutoMPointRepresentativeBestPattern()
        {
            using (Mat reference = CreateAutoMPointRepresentativeReference())
            {
                AutoMPointToolProperty property = CreateAutoMPointProperty(
                    new Rect(0, 0, reference.Width, reference.Height),
                    64,
                    64,
                    32);
                property.MaximumFinalists = 8;
                property.MaximumResults = 5;
                property.MinimumFeatureQuality = 0.01;
                property.MatchingMinimumScore = 0.45;
                property.MinimumUniquenessMargin = 0.01;
                property.MinimumRepresentativeImageCount = 3;
                property.MinimumRepresentativeSuccessRate = 0.75;

                List<Mat> samples = Enumerable.Range(0, 4)
                    .Select(index => CreateAutoMPointRepresentativeSample(reference, index))
                    .ToList();
                try
                {
                    AutoMPointTool tool = new AutoMPointTool();
                    tool.SetProperty(property);
                    VisionToolResult result = tool.Execute(reference, samples);
                    try
                    {
                        Require(result.Success,
                            "Representative Auto MPoint analysis must produce one stable suggestion. "
                            + result.ErrorName + ": " + result.Message + " Candidates="
                            + string.Join(
                                " | ",
                                tool.candidates.Select(candidate =>
                                    candidate.PatternRoi + " "
                                    + candidate.RepresentativeSuccessCount + "/"
                                    + candidate.RepresentativeImageCount + " ["
                                    + candidate.RejectReason + "] "
                                    + string.Join(
                                        ",",
                                        candidate.RepresentativeMatches.Select(match =>
                                            match.Outcome + ":" + match.Score.ToString("0.0")
                                            + "/" + match.UniquenessMargin.ToString("0.000"))))));
                        Require(tool.results.Count >= 1
                            && tool.results[0].PatternRoi == new Rect(64, 64, 64, 64),
                            "The pattern preserved across representative images must rank first.");
                        Require(tool.results[0].RepresentativeImageCount == 4
                            && tool.results[0].RepresentativeSuccessCount == 4
                            && Math.Abs(tool.results[0].RepresentativeSuccessRate - 1d) < 0.000001d,
                            "The best pattern must publish 4/4 representative-image success.");
                        Require(tool.results[0].RepresentativeMatches.Count == 4
                            && tool.results[0].RepresentativeMatches.All(match => match.Success),
                            "Per-image representative outcomes must be retained.");
                        Require(result.Metrics["AutoMPoint.RepresentativeImageCount"] == 4d
                            && result.Metrics["AutoMPoint.BestRepresentativeSuccessRate"] == 1d,
                            "Representative-image count and best success rate must be public metrics.");
                        SaveAutoMPointEvidence(
                            "representative_best",
                            reference,
                            result,
                            new[]
                            {
                                "Status=Accepted",
                                "BestPatternRoi=" + tool.results[0].PatternRoi,
                                "RepresentativeImages=" + tool.results[0].RepresentativeImageCount,
                                "RepresentativeSuccess=" + tool.results[0].RepresentativeSuccessCount,
                                "RepresentativeSuccessRate=" + tool.results[0].RepresentativeSuccessRate.ToString("0.000"),
                                "RepresentativeMeanScore=" + tool.results[0].RepresentativeMeanScore.ToString("0.000"),
                                "RepresentativeMinimumUniquenessMargin="
                                    + tool.results[0].RepresentativeMinimumUniquenessMargin.ToString("0.000000")
                            });
                    }
                    finally
                    {
                        result.ResultImage?.Dispose();
                    }
                }
                finally
                {
                    foreach (Mat sample in samples)
                    {
                        sample.Dispose();
                    }
                }
            }
        }

        private static void TestAutoMPointInvalidRepresentativeSet()
        {
            using (Mat reference = CreateAutoMPointRepresentativeReference())
            using (Mat sample = reference.Clone())
            {
                AutoMPointToolProperty property = CreateAutoMPointProperty(
                    new Rect(0, 0, reference.Width, reference.Height),
                    64,
                    64,
                    32);
                property.MinimumRepresentativeImageCount = 3;
                AutoMPointTool tool = new AutoMPointTool();
                tool.SetProperty(property);
                VisionToolResult result = tool.Execute(reference, new[] { sample });
                Require(!result.Success
                    && result.ErrorCode == VisionToolErrorCode.AutoMPointRepresentativeImageInvalid,
                    "Too few representative images must fail closed with AutoMPointRepresentativeImageInvalid.");
            }
        }

        private static AutoMPointToolProperty CreateAutoMPointProperty(
            Rect analysisRoi,
            int patternWidth,
            int patternHeight,
            int stride)
        {
            return new AutoMPointToolProperty
            {
                UseAnalysisRoi = true,
                AnalysisRoi = analysisRoi,
                CandidateMode = AutoMPointCandidateMode.Grid,
                PatternWidth = patternWidth,
                PatternHeight = patternHeight,
                CandidateStride = stride,
                MaximumFinalists = 6,
                MaximumResults = 3,
                MaximumCandidateOverlap = 0.05,
                MinimumContrastStdDev = 2,
                MinimumEdgeDensity = 0.002,
                MinimumQuadrantBalance = 0.02,
                MinimumOrientationBalance = 0.05,
                MinimumFeatureQuality = 0.05,
                MatchingMinimumScore = 0.5,
                MinimumUniquenessMargin = 0.03,
                MaximumTemplatePoints = 250,
                SearchStep = 2,
                UsePositionRefine = true,
                UseSubpixelRefine = true,
                UsePyramidPositionProposal = true,
                UseHybridVerify = true,
                UseAngleSearch = false,
                UseScaleSearch = false,
                SyntheticTranslationPixels = 3,
                MinimumSyntheticSuccessRate = 1,
                MaximumPositionErrorPixels = 5,
                MaximumAngleErrorDegrees = 0.1,
                MaximumScaleErrorRatio = 0.001
            };
        }

        private static Mat CreateAutoMPointUniqueSource()
        {
            Mat source = new Mat(new Size(256, 192), MatType.CV_8UC1, Scalar.All(24));
            Cv2.Rectangle(source, new Rect(66, 66, 50, 50), Scalar.All(205), 3);
            Cv2.Line(source, new Point(72, 108), new Point(109, 73), Scalar.All(245), 3, LineTypes.AntiAlias);
            Cv2.Circle(source, new Point(101, 99), 8, Scalar.All(90), -1, LineTypes.AntiAlias);
            Cv2.Rectangle(source, new Rect(142, 38, 54, 14), Scalar.All(130), -1);
            Cv2.Line(source, new Point(154, 148), new Point(220, 148), Scalar.All(105), 4);
            return source;
        }

        private static Mat CreateAutoMPointRepeatedSource()
        {
            Mat source = new Mat(new Size(128, 64), MatType.CV_8UC1, Scalar.All(24));
            DrawRepeatedAutoMPointMark(source, 0);
            DrawRepeatedAutoMPointMark(source, 64);
            return source;
        }

        private static Mat CreateAutoMPointRepresentativeReference()
        {
            Mat source = CreateAutoMPointUniqueSource();
            Cv2.Rectangle(source, new Rect(166, 70, 50, 50), Scalar.All(215), 3);
            Cv2.Line(source, new Point(171, 114), new Point(211, 74), Scalar.All(250), 4, LineTypes.AntiAlias);
            Cv2.Circle(source, new Point(204, 106), 9, Scalar.All(70), -1, LineTypes.AntiAlias);
            Cv2.Line(source, new Point(166, 96), new Point(216, 96), Scalar.All(180), 2, LineTypes.AntiAlias);
            return source;
        }

        private static Mat CreateAutoMPointRepresentativeSample(Mat reference, int index)
        {
            Mat sample = reference.Clone();
            Cv2.Rectangle(sample, new Rect(160, 64, 64, 64), Scalar.All(24), -1);
            Cv2.Line(
                sample,
                new Point(166 + (index * 3), 72),
                new Point(214, 119 - (index * 4)),
                Scalar.All(48 + (index * 7)),
                2,
                LineTypes.AntiAlias);
            return sample;
        }

        private static void DrawRepeatedAutoMPointMark(Mat source, int offsetX)
        {
            Cv2.Rectangle(source, new Rect(offsetX + 8, 8, 46, 46), Scalar.All(205), 3);
            Cv2.Line(
                source,
                new Point(offsetX + 13, 49),
                new Point(offsetX + 48, 14),
                Scalar.All(245),
                3,
                LineTypes.AntiAlias);
            Cv2.Circle(source, new Point(offsetX + 42, 42), 6, Scalar.All(90), -1, LineTypes.AntiAlias);
        }

        private static void SaveAutoMPointEvidence(
            string name,
            Mat source,
            VisionToolResult result,
            IEnumerable<string> summary)
        {
            string directory = Environment.GetEnvironmentVariable("LIB_NOAH_AUTOMPOINT_EVIDENCE_DIR");
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            Directory.CreateDirectory(directory);
            Cv2.ImWrite(Path.Combine(directory, name + "_source.png"), source);
            if (result?.ResultImage != null && !result.ResultImage.Empty())
            {
                Cv2.ImWrite(Path.Combine(directory, name + "_result.png"), result.ResultImage);
            }

            File.WriteAllLines(Path.Combine(directory, name + "_summary.txt"), summary ?? Array.Empty<string>());
        }

        private static void TestEdgeMatcherLegacySingleResult()
        {
            using (Mat source = CreateAutoMPointRepeatedSource())
            using (Mat template = new Mat(source, new Rect(0, 0, 64, 64)).Clone())
            {
                EdgeBasedTemplateMatchingTool tool = CreateEdgeMatcher(template, false);
                VisionToolResult result = tool.Execute(source);
                try
                {
                    Require(result.Success && tool.results.Count == 1,
                        "The opt-in contract must not change a legacy NUM_MATCH=1 repeated-pattern result. "
                        + result.ErrorName + ": " + result.Message
                        + " Count=" + tool.results.Count);
                    Require(result.Metrics["UniqueMatch.Enabled"] == 0D,
                        "Legacy execution must report the unique-match option as disabled.");
                    Require(double.IsNaN(tool.results[0].ScoreMargin),
                        "Legacy MatchingResult must not publish a synthetic uniqueness margin.");
                    Require(result.EdgeBasedMatchingDiagnostics != null
                        && result.EdgeBasedMatchingDiagnostics.State == "Success"
                        && result.EdgeBasedMatchingDiagnostics.ModelPoints.Count > 0
                        && result.EdgeBasedMatchingDiagnostics.SelectedCandidate != null,
                        "Legacy success must retain read-only model and selected-candidate diagnostics.");
                    SaveUniqueMatchEvidence("legacy_repeated_success", source, result, tool);
                }
                finally
                {
                    result.ResultImage?.Dispose();
                }
            }
        }

        private static void TestEdgeMatcherUniqueSuccess()
        {
            using (Mat source = CreateAutoMPointUniqueSource())
            using (Mat template = new Mat(source, new Rect(60, 60, 64, 64)).Clone())
            {
                EdgeBasedTemplateMatchingTool tool = CreateEdgeMatcher(template, true);
                VisionToolResult result = tool.Execute(source);
                try
                {
                    Require(result.Success && tool.results.Count == 1,
                        "One distinct pattern must produce exactly one unique MatchingResult. "
                        + result.ErrorName + ": " + result.Message);
                    Require(result.Metrics["UniqueMatch.State"] == 2D,
                        "A unique result must publish UniqueMatch.State=Success.");
                    Require(tool.results[0].ScoreMargin >= 3D,
                        "A unique result must expose the score margin in percentage points.");
                    Require(tool.results[0].FinalScore >= tool.results[0].EdgeScore - 0.001D,
                        "Non-hybrid final score must preserve the edge score.");
                    Require(result.EdgeBasedMatchingDiagnostics != null
                        && result.EdgeBasedMatchingDiagnostics.State == "Success"
                        && result.EdgeBasedMatchingDiagnostics.ModelPoints.Count > 0
                        && result.EdgeBasedMatchingDiagnostics.SelectedCandidate != null
                        && result.EdgeBasedMatchingDiagnostics.Reason.StartsWith("Success:", StringComparison.Ordinal),
                        "Unique success must retain its exact read-only model, candidate, state, and reason.");
                    SaveUniqueMatchEvidence("unique_success", source, result, tool);
                }
                finally
                {
                    result.ResultImage?.Dispose();
                }
            }
        }

        private static void TestEdgeMatcherUniqueAmbiguous()
        {
            using (Mat source = CreateAutoMPointRepeatedSource())
            using (Mat template = new Mat(source, new Rect(0, 0, 64, 64)).Clone())
            {
                EdgeBasedTemplateMatchingTool tool = CreateEdgeMatcher(template, true);
                VisionToolResult result = tool.Execute(source);
                try
                {
                    Require(!result.Success
                        && result.ErrorCode == VisionToolErrorCode.MatchingAmbiguous
                        && tool.results.Count == 0,
                        "Two repeated patterns must fail closed with MatchingAmbiguous and no MatchingResult.");
                    Require(result.Metrics["UniqueMatch.State"] == 3D
                        && result.Metrics["UniqueMatch.PlausibleAlternativeCount"] >= 1D,
                        "Ambiguous execution must retain its state and alternative count.");
                    Require(result.Metrics["UniqueMatch.ScoreMargin"] < result.Metrics["UniqueMatch.MinimumScoreMargin"],
                        "Ambiguous execution must expose the failed normalized score-margin gate.");
                    Require(result.Message.IndexOf("PlausibleAlternatives=", StringComparison.Ordinal) >= 0,
                        "Ambiguous execution must expose the exact reject reason.");
                    Require(result.EdgeBasedMatchingDiagnostics != null
                        && result.EdgeBasedMatchingDiagnostics.State == "Ambiguous"
                        && result.EdgeBasedMatchingDiagnostics.ModelPoints.Count > 0
                        && result.EdgeBasedMatchingDiagnostics.SelectedCandidate != null
                        && result.EdgeBasedMatchingDiagnostics.StrongestSpatialAlternative != null
                        && result.EdgeBasedMatchingDiagnostics.Reason == result.Message,
                        "Ambiguous execution must retain the exact selected/alternative geometry and runtime reason.");
                    SaveUniqueMatchEvidence("repeated_ambiguous", source, result, tool);
                }
                finally
                {
                    result.ResultImage?.Dispose();
                }
            }
        }

        private static void TestEdgeMatcherUniqueNoMatch()
        {
            using (Mat templateSource = CreateAutoMPointRepeatedSource())
            using (Mat template = new Mat(templateSource, new Rect(0, 0, 64, 64)).Clone())
            using (Mat source = new Mat(new Size(128, 64), MatType.CV_8UC1, Scalar.All(24)))
            {
                EdgeBasedTemplateMatchingTool tool = CreateEdgeMatcher(template, true);
                VisionToolResult result = tool.Execute(source);
                try
                {
                    Require(!result.Success
                        && result.ErrorCode == VisionToolErrorCode.MatchingNoResult
                        && tool.results.Count == 0,
                        "A source without the pattern must fail closed with MatchingNoResult.");
                    Require(result.Metrics["UniqueMatch.State"] == 1D,
                        "No-match execution must publish UniqueMatch.State=NoMatch.");
                    Require(result.EdgeBasedMatchingDiagnostics != null
                        && result.EdgeBasedMatchingDiagnostics.State == "NoMatch"
                        && result.EdgeBasedMatchingDiagnostics.ModelPoints.Count > 0
                        && result.EdgeBasedMatchingDiagnostics.Reason == result.Message,
                        "No-match execution must retain the trained model and exact runtime reason.");
                    SaveUniqueMatchEvidence("no_match", source, result, tool);
                }
                finally
                {
                    result.ResultImage?.Dispose();
                }
            }
        }

        private static EdgeBasedTemplateMatchingTool CreateEdgeMatcher(Mat template, bool useUniqueMatchValidation)
        {
            EdgeBasedTemplateMatchingTool tool = new EdgeBasedTemplateMatchingTool();
            EdgeBasedTemplateMatchingToolProperty property = CreateEdgeMatcherProperty();
            property.USE_UNIQUE_MATCH_VALIDATION = useUniqueMatchValidation;
            tool.SetProperty(property);
            tool.SetTemplateImage(template);
            return tool;
        }

        private static void TestEdgeMatcherGlobalPolarity()
        {
            using (Mat sameSource = CreateAutoMPointUniqueSource())
            using (Mat template = new Mat(sameSource, new Rect(60, 60, 64, 64)).Clone())
            using (Mat reversedSource = new Mat())
            using (Mat noTargetSource = new Mat(sameSource.Size(), MatType.CV_8UC1, Scalar.All(24)))
            {
                Cv2.BitwiseNot(sameSource, reversedSource);

                EdgeBasedTemplateMatchingTool legacyTool = CreateEdgeMatcher(template, false);
                VisionToolResult legacyReversed = legacyTool.Execute(reversedSource);
                Require(!legacyReversed.Success && legacyTool.results.Count == 0,
                    "Missing polarity keys must preserve legacy Same-only rejection.");
                legacyReversed.ResultImage?.Dispose();

                EdgeBasedTemplateMatchingTool sameTool = CreateEdgeMatcher(template, false, true);
                VisionToolResult sameResult = sameTool.Execute(sameSource);
                Require(sameResult.Success
                    && sameTool.results.Count == 1
                    && !sameTool.results[0].PolarityReversed
                    && sameResult.Metrics["GlobalPolarity.AllowReversal"] == 1D
                    && sameResult.Metrics["GlobalPolarity.Reversed"] == 0D,
                    "Opt-in same-polarity execution must retain Same state.");
                sameResult.ResultImage?.Dispose();

                EdgeBasedTemplateMatchingTool reversedTool = CreateEdgeMatcher(template, false, true);
                VisionToolResult reversedResult = reversedTool.Execute(reversedSource);
                Require(reversedResult.Success
                    && reversedTool.results.Count == 1
                    && reversedTool.results[0].PolarityReversed
                    && reversedResult.Metrics["GlobalPolarity.Reversed"] == 1D,
                    "Opt-in globally reversed execution must accept and report Reversed state.");
                reversedResult.ResultImage?.Dispose();

                EdgeBasedTemplateMatchingTool noTargetTool = CreateEdgeMatcher(template, false, true);
                VisionToolResult noTargetResult = noTargetTool.Execute(noTargetSource);
                Require(!noTargetResult.Success && noTargetTool.results.Count == 0,
                    "Global polarity reversal must not turn a no-target image into a match.");
                noTargetResult.ResultImage?.Dispose();
            }
        }

        private static EdgeBasedTemplateMatchingTool CreateEdgeMatcher(
            Mat template,
            bool useUniqueMatchValidation,
            bool allowGlobalPolarityReversal)
        {
            EdgeBasedTemplateMatchingTool tool = new EdgeBasedTemplateMatchingTool();
            EdgeBasedTemplateMatchingToolProperty property = CreateEdgeMatcherProperty();
            property.USE_UNIQUE_MATCH_VALIDATION = useUniqueMatchValidation;
            property.ALLOW_GLOBAL_POLARITY_REVERSAL = allowGlobalPolarityReversal;
            tool.SetProperty(property);
            tool.SetTemplateImage(template);
            return tool;
        }

        private static EdgeBasedTemplateMatchingToolProperty CreateEdgeMatcherProperty()
        {
            return new EdgeBasedTemplateMatchingToolProperty
            {
                NAME = "Unique match smoke",
                ADAPTIVE_THRESHOLD = 5d,
                ADAPTIVE_THRESHOLD_ALGORITHM = AdaptiveThresholdTypes.MeanC,
                BlockSize = 11,
                Weight = 2,
                SCORE_MIN = 0.5d,
                CANNY_HIGH = 100,
                USE_L2_GRADIENT = false,
                CONTOUR_APPROXIMATION_MODE = ContourApproximationModes.ApproxSimple,
                FIND_ANGLE = 0.5d,
                FIND_ANGLE_MAX = 5,
                FIND_ANGLE_MIN = -5,
                COARSE_ANGLE_STEP = 2d,
                GREEDINESS = 0.8d,
                SEARCH_STEP = 1,
                USE_POSITION_REFINE = true,
                USE_SUBPIXEL_REFINE = true,
                PYRAMID_POSITION_TOP_N = 3,
                PYRAMID_POSITION_MIN_SCORE = 0.35d,
                HYBRID_VERIFY_TOP_N = 6,
                MAX_TEMPLATE_POINTS = 500,
                MIN_GRADIENT_MAGNITUDE = 5d
            };
        }

        private static void SaveUniqueMatchEvidence(
            string name,
            Mat source,
            VisionToolResult result,
            EdgeBasedTemplateMatchingTool tool)
        {
            string directory = Environment.GetEnvironmentVariable("LIB_NOAH_UNIQUE_MATCH_EVIDENCE_DIR");
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            Directory.CreateDirectory(directory);
            Cv2.ImWrite(Path.Combine(directory, name + "_source.png"), source);
            if (result?.ResultImage != null && !result.ResultImage.Empty())
            {
                Cv2.ImWrite(Path.Combine(directory, name + "_result.png"), result.ResultImage);
            }

            List<string> summary = new List<string>
            {
                "Success=" + result.Success,
                "ErrorCode=" + result.ErrorCode,
                "Message=" + result.Message,
                "MatchingResultCount=" + tool.results.Count
            };
            foreach (KeyValuePair<string, double> metric in result.Metrics
                .Where(metric => metric.Key.StartsWith("UniqueMatch.", StringComparison.Ordinal))
                .OrderBy(metric => metric.Key, StringComparer.Ordinal))
            {
                summary.Add(metric.Key + "=" + metric.Value.ToString("0.######"));
            }

            if (tool.results.Count > 0)
            {
                summary.Add("EdgeScore=" + tool.results[0].EdgeScore.ToString("0.###"));
                summary.Add("ImageScore=" + tool.results[0].ImageScore.ToString("0.###"));
                summary.Add("FinalScore=" + tool.results[0].FinalScore.ToString("0.###"));
                summary.Add("ScoreMargin=" + tool.results[0].ScoreMargin.ToString("0.###"));
            }

            File.WriteAllLines(Path.Combine(directory, name + "_summary.txt"), summary);
        }

        private static void TestMorphologyDirectExecution()
        {
            using (Mat source = new Mat(new Size(9, 9), MatType.CV_8UC1, Scalar.Black))
            using (MorphologyTool tool = new MorphologyTool())
            {
                Cv2.Rectangle(source, new Rect(4, 4, 1, 1), Scalar.White, Cv2.FILLED);
                tool.SetProperty(new MorphologyToolProperty
                {
                    Operator = MorphTypes.Dilate,
                    KernelWidth = 3,
                    KernelHeight = 3,
                    Iterations = 1
                });

                using (VisionToolResult result = tool.Execute(source))
                {
                    Require(result.Success,
                        "MorphologyTool direct Execute failed: " + result.ErrorName + ": " + result.Message);
                    Require(result.ResultImage != null
                        && result.ResultImage.Size() == source.Size()
                        && Cv2.CountNonZero(result.ResultImage) == 9,
                        "MorphologyTool direct Execute did not apply the 3x3 dilation.");
                }
            }
        }

        private static void TestFilterDirectExecution()
        {
            using (Mat source = new Mat(new Size(9, 9), MatType.CV_8UC1, Scalar.Black))
            using (FilterTool tool = new FilterTool())
            {
                Cv2.Rectangle(source, new Rect(4, 4, 1, 1), Scalar.White, Cv2.FILLED);
                tool.SetProperty(new FilterToolProperty
                {
                    FilterType = FilterToolType.Blur,
                    KernelWidth = 3,
                    KernelHeight = 3
                });

                using (VisionToolResult result = tool.Execute(source))
                {
                    Require(result.Success,
                        "FilterTool direct Execute failed: " + result.ErrorName + ": " + result.Message);
                    Require(result.ResultImage != null
                        && result.ResultImage.Size() == source.Size()
                        && Cv2.CountNonZero(result.ResultImage) > 1
                        && Cv2.Norm(source, result.ResultImage, NormTypes.L1) > 0d,
                        "FilterTool direct Execute did not apply the 3x3 blur.");
                }
            }
        }

        private static void TestEdgeDetectionDirectExecution()
        {
            using (Mat source = new Mat(new Size(32, 32), MatType.CV_8UC1, Scalar.Black))
            using (EdgeDetectionTool tool = new EdgeDetectionTool())
            {
                Cv2.Rectangle(source, new Rect(8, 8, 16, 16), Scalar.White, Cv2.FILLED);
                tool.SetProperty(new EdgeDetectionToolProperty
                {
                    EdgeType = EdgeDetectionToolType.Canny,
                    CannyThresholdLow = 50,
                    CannyThresholdHigh = 100
                });

                using (VisionToolResult result = tool.Execute(source))
                {
                    Require(result.Success,
                        "EdgeDetectionTool direct Execute failed: " + result.ErrorName + ": " + result.Message);
                    Require(result.ResultImage != null
                        && result.ResultImage.Size() == source.Size()
                        && Cv2.CountNonZero(result.ResultImage) > 0,
                        "EdgeDetectionTool direct Execute did not publish the synthetic rectangle edges.");
                }
            }
        }

        private static void TestRotateScaleDirectExecution()
        {
            using (Mat source = new Mat(new Size(20, 10), MatType.CV_8UC1, Scalar.White))
            using (RotateScaleTool tool = new RotateScaleTool())
            {
                tool.SetProperty(new RotateScaleToolProperty
                {
                    ScaleXPercent = 50,
                    ScaleYPercent = 200
                });

                using (VisionToolResult result = tool.Execute(source))
                {
                    Require(result.Success,
                        "RotateScaleTool direct Execute failed: " + result.ErrorName + ": " + result.Message);
                    Require(result.ResultImage != null
                        && result.ResultImage.Width == 10
                        && result.ResultImage.Height == 20,
                        "RotateScaleTool direct Execute did not apply the requested 50% x 200% size.");
                }
            }
        }

        private static void TestLineGaugeUnsupportedDepth()
        {
            using (Mat supported = new Mat(new Size(128, 64), MatType.CV_8UC1, Scalar.Black))
            using (Mat unsupported = new Mat(new Size(128, 64), MatType.CV_16UC1, Scalar.Black))
            using (LineGaugeTool tool = new LineGaugeTool())
            {
                Cv2.Rectangle(supported, new Rect(64, 0, 64, 64), Scalar.All(255), Cv2.FILLED);
                Cv2.Rectangle(unsupported, new Rect(64, 0, 64, 64), Scalar.All(ushort.MaxValue), Cv2.FILLED);
                tool.SetProperty(new LineGaugeToolProperty
                {
                    USE_ROI = true,
                    CvROI = new Rect(0, 0, 128, 64),
                    PRJ_DIR = OpenVisionLab.Core.FormulaUtil.PROJECTION_DIR.X_LTOR,
                    PRJ_PORALITY = OpenVisionLab.Core.FormulaUtil.PROJECTION_POLARITY.BTOW,
                    CONTRAST = 30,
                    THICKNESS = 3,
                    SAMPLING_STEP = 8
                });

                using (VisionToolResult supportedResult = tool.Execute(supported))
                {
                    Require(supportedResult.Success,
                        "LineGaugeTool CV_8UC1 control execution failed: "
                        + supportedResult.ErrorName + ": " + supportedResult.Message);
                }

                using (VisionToolResult unsupportedResult = tool.Execute(unsupported))
                {
                    Require(!unsupportedResult.Success
                        && unsupportedResult.ErrorCode == VisionToolErrorCode.InputImageInvalid
                        && unsupportedResult.ResultStatus == VisionToolResultStatus.InvalidInput
                        && unsupportedResult.Exception == null
                        && unsupportedResult.Message.Contains("8-bit unsigned", StringComparison.OrdinalIgnoreCase),
                        "LineGaugeTool CV_16UC1 input must fail explicitly as InputImageInvalid.");
                }
            }
        }

        private static void TestVisionToolResourceOwnership()
        {
            using (Mat source = new Mat(4, 4, MatType.CV_8UC1, new Scalar(10)))
            {
                ThresholdTool tool = new ThresholdTool();
                tool.SetProperty(new ThresholdToolProperty { Threshold = 5 });

                VisionToolResult result = tool.Execute(source);
                Require(result.Success, "The ownership fixture must produce a passing result.");

                Mat ownedSource = tool.imageSource;
                Mat ownedResult = tool.imageResult;
                Mat ownedTemplate = tool.imageTemplate;
                Mat resultSnapshot = result.ResultImage;

                tool.Dispose();

                Require(ownedSource.IsDisposed, "Disposing a 2D tool did not release its source image.");
                Require(ownedResult.IsDisposed, "Disposing a 2D tool did not release its result image.");
                Require(ownedTemplate.IsDisposed, "Disposing a 2D tool did not release its template image.");
                Require(!resultSnapshot.IsDisposed, "Disposing a tool released the caller-owned result snapshot.");
                Require(!source.IsDisposed, "Disposing a tool released the caller-owned source image.");

                result.Dispose();
                Require(resultSnapshot.IsDisposed, "Disposing a tool result did not release its result snapshot.");
                Require(result.ResultImage == null, "A disposed tool result retained its released image reference.");

                tool.Dispose();
                result.Dispose();
            }
        }

        private static void TestMeanMultiRoi()
        {
            using (Mat source = new Mat(4, 8, MatType.CV_8UC1, Scalar.All(0d)))
            {
                using (Mat left = source.SubMat(new Rect(0, 0, 4, 4)))
                using (Mat right = source.SubMat(new Rect(4, 0, 4, 4)))
                {
                    left.SetTo(Scalar.All(10d));
                    right.SetTo(Scalar.All(100d));
                }

                MeanToolProperty multiProperty = new MeanToolProperty
                {
                    USE_MULTI_ROI = true,
                    USE_ROI = false,
                    MEAN_TYPES = MeanType.Mean,
                    CvROIS = new List<Rect>
                    {
                        new Rect(0, 0, 4, 4),
                        new Rect(4, 0, 4, 4)
                    }
                };

                using (MeanTool multiTool = new MeanTool())
                {
                    multiTool.SetProperty(multiProperty);
                    using (VisionToolResult result = multiTool.Execute(source))
                    {
                        Require(result.Success, "The mean multi-ROI fixture must pass.");
                        Require(multiTool.results.Count == 2, "Mean multi ROI did not produce one result per region.");
                        RequireApproximately(multiTool.results[0].meanValue, 10d, 0d, "Unexpected first ROI mean.");
                        RequireApproximately(multiTool.results[1].meanValue, 100d, 0d, "Unexpected second ROI mean.");
                        Require(multiTool.results[0].index == 0 && multiTool.results[1].index == 1, "Mean multi ROI did not preserve result identity.");
                        Require(multiTool.results[0].Bounding.X == 0 && multiTool.results[1].Bounding.X == 4, "Mean multi ROI did not preserve result bounds.");
                    }
                }

                MeanToolProperty deviationProperty = new MeanToolProperty
                {
                    MEAN_TYPES = MeanType.MeanStdDev
                };

                using (MeanTool deviationTool = new MeanTool())
                {
                    deviationTool.SetProperty(deviationProperty);
                    using (VisionToolResult result = deviationTool.Execute(source))
                    {
                        Require(result.Success, "The standard-deviation fixture must pass.");
                        RequireApproximately(deviationTool.results[0].meanValue, 45d, 0d, "Unexpected standard deviation.");
                    }
                }
            }
        }

        private static void TestCornerResultContract()
        {
            ContourToolProperty property = new ContourToolProperty
            {
                USE_MULTI_ROI = true,
                USE_ROI = false,
                CvROIS = new List<Rect>
                {
                    new Rect(0, 0, 40, 40),
                    new Rect(40, 0, 40, 40)
                }
            };

            using (CornerTool tool = new CornerTool())
            using (Mat source = new Mat(40, 80, MatType.CV_8UC1, Scalar.All(0d)))
            {
                tool.SetProperty(property);
                Cv2.Rectangle(source, new Rect(10, 10, 20, 20), Scalar.White, Cv2.FILLED);
                Cv2.Rectangle(source, new Rect(50, 10, 20, 20), Scalar.White, Cv2.FILLED);

                using (VisionToolResult result = tool.Execute(source))
                {
                    Require(result.Success, "The corner fixture must pass.");
                    Require(tool.results.Count == 8, "Corner detection did not publish all detected points.");
                    Require(tool.results.Count(item => item.Center.X < 40d) == 4, "Left ROI corner coordinates are incorrect.");
                    Require(tool.results.Count(item => item.Center.X >= 40d) == 4, "Right ROI corner coordinates are not global.");
                    Require(tool.results.All(item => item.Bounding.Width == 1 && item.Bounding.Height == 1), "Corner point bounds are not stable.");
                }

                using (Mat blank = new Mat(source.Size(), source.Type(), Scalar.All(0d)))
                using (VisionToolResult result = tool.Execute(blank))
                {
                    Require(!result.Success, "A blank corner image must return a controlled no-result failure.");
                    Require(result.Exception == null, "A blank corner image must not fail through an exception.");
                    Require(result.ErrorCode == VisionToolErrorCode.CornerNoResult, "A blank corner image returned the wrong error code.");
                    Require(tool.results.Count == 0, "A blank corner image retained stale points.");
                }

                property.CvROIS = new List<Rect> { new Rect(70, 0, 20, 20) };
                using (VisionToolResult result = tool.Execute(source))
                {
                    Require(!result.Success, "An out-of-bounds corner ROI must fail.");
                    Require(result.ErrorCode == VisionToolErrorCode.CornerRoiInvalid, "An invalid corner ROI returned the wrong error code.");
                    Require(result.ResultStatus == VisionToolResultStatus.InvalidRoi, "An invalid corner ROI returned the wrong result status.");
                    Require(result.Exception == null, "An invalid corner ROI must fail through validation.");
                }
            }
        }

        private static void TestVisionPipelineResourceOwnership()
        {
            VisionPipeline pipeline = new VisionPipeline { Name = "Ownership fixture" };
            pipeline.Steps.Add(new VisionPipelineStep
            {
                Name = "Clone input",
                ToolType = "tracking",
                InputLayer = "input",
                OutputLayer = "output"
            });

            using (Mat source = new Mat(4, 4, MatType.CV_8UC1, new Scalar(7)))
            using (VisionPipelineContext context = new VisionPipelineContext())
            {
                context.SetLayer("input", source);
                TrackingDisposableVisionTool ownedTool = new TrackingDisposableVisionTool();
                VisionPipelineRuntime runtime = new VisionPipelineRuntime(_ => ownedTool, true);
                VisionPipelineRunResult result = runtime.Run(pipeline, context);

                Require(result.Success, "The ownership pipeline must pass.");
                Require(ownedTool.WasDisposed, "The runtime did not dispose a factory-created owned tool.");
                Require(ownedTool.LastSource != null && ownedTool.LastSource.IsDisposed, "The runtime did not dispose the cloned input layer.");

                Mat resultSnapshot = result.StepResults[0].ToolResult.ResultImage;
                Require(!resultSnapshot.IsDisposed, "The runtime released a returned result before its owner disposed it.");
                using (Mat output = context.GetLayer("output"))
                {
                    Require(output != null && !output.Empty(), "The output layer was not retained independently.");
                }

                result.Dispose();
                Require(resultSnapshot.IsDisposed, "Disposing a pipeline result did not release its step image.");
                using (Mat output = context.GetLayer("output"))
                {
                    Require(output != null && !output.Empty(), "Disposing a pipeline result invalidated the context-owned output layer.");
                }

                TrackingDisposableVisionTool sharedTool = new TrackingDisposableVisionTool();
                VisionPipelineRunResult sharedResult = new VisionPipelineRuntime(_ => sharedTool).Run(pipeline, context);
                Require(!sharedTool.WasDisposed, "The compatibility factory overload disposed a caller-owned shared tool.");
                sharedResult.Dispose();
                sharedTool.Dispose();

                VisionPipeline failingPipeline = new VisionPipeline { Name = "Exception ownership fixture" };
                failingPipeline.Steps.Add(new VisionPipelineStep
                {
                    Name = "Produce intermediate",
                    ToolType = "tracking",
                    InputLayer = "input",
                    OutputLayer = "intermediate"
                });
                failingPipeline.Steps.Add(new VisionPipelineStep
                {
                    Name = "Throw",
                    ToolType = "throwing",
                    InputLayer = "intermediate",
                    OutputLayer = "unused"
                });

                TrackingDisposableVisionTool firstTool = new TrackingDisposableVisionTool();
                ThrowingDisposableVisionTool throwingTool = new ThrowingDisposableVisionTool();
                bool exceptionObserved = false;
                try
                {
                    new VisionPipelineRuntime(
                        step => step.ToolType == "tracking" ? (IVisionTool)firstTool : throwingTool,
                        true).Run(failingPipeline, context);
                }
                catch (InvalidOperationException exception)
                {
                    exceptionObserved = exception.Message == "Controlled pipeline exception.";
                }

                Require(exceptionObserved, "The controlled pipeline exception was not propagated.");
                Require(firstTool.WasDisposed && throwingTool.WasDisposed, "The exception path did not dispose every factory-owned tool.");
                Require(firstTool.ResultSnapshot != null && firstTool.ResultSnapshot.IsDisposed, "The exception path did not dispose a completed step result.");
                Require(throwingTool.LastSource != null && throwingTool.LastSource.IsDisposed, "The exception path did not dispose the active input layer clone.");
            }
        }

        private static void TestVisionPipelineOptionalOutputContract()
        {
            VisionPipeline unnamedOutput = new VisionPipeline { Name = "Unnamed output fixture" };
            unnamedOutput.Steps.Add(new VisionPipelineStep
            {
                Name = "No output layer",
                ToolType = "image",
                InputLayer = "input"
            });

            VisionPipeline nullOutput = new VisionPipeline { Name = "Null output fixture" };
            nullOutput.Steps.Add(new VisionPipelineStep
            {
                Name = "No result image",
                ToolType = "pass-through",
                InputLayer = "input",
                OutputLayer = "preserved"
            });

            using (Mat source = new Mat(4, 4, MatType.CV_8UC1, Scalar.All(7)))
            using (Mat preservedSource = new Mat(4, 4, MatType.CV_8UC1, Scalar.All(23)))
            using (VisionPipelineContext context = new VisionPipelineContext())
            {
                context.SetLayer("input", source);
                context.SetLayer("preserved", preservedSource);

                using (VisionPipelineRunResult unnamedResult =
                    new VisionPipelineRuntime(_ => new ImageReturningVisionTool()).Run(unnamedOutput, context))
                {
                    Require(unnamedResult.Success
                        && unnamedResult.StepResults[0].ToolResult.ResultImage != null,
                        "A successful pipeline step with an unnamed output must complete without routing an image.");
                }

                using (VisionPipelineRunResult nullResult =
                    new VisionPipelineRuntime(_ => new PassThroughVisionTool()).Run(nullOutput, context))
                using (Mat preserved = context.GetLayer("preserved"))
                {
                    Require(nullResult.Success
                        && nullResult.StepResults[0].ToolResult.ResultImage == null,
                        "A successful pipeline step may complete without a result image.");
                    Require(preserved != null
                        && !preserved.Empty()
                        && Cv2.Mean(preserved).Val0 == 23d,
                        "A null pipeline result image must not replace the existing named output layer.");
                }
            }
        }

        private static void TestVisionPipelineFactoryBuiltIns()
        {
            VisionPipelineStep thresholdStep = CreatePipelineStep("threshold");
            thresholdStep.Parameters[nameof(ThresholdToolProperty.Threshold)] = "123.5";
            thresholdStep.Parameters[nameof(ThresholdToolProperty.Invert)] = "true";
            thresholdStep.Parameters[nameof(ThresholdToolProperty.ThresholdType)] = "BinaryInv, Otsu";
            using (ThresholdTool threshold = (ThresholdTool)VisionPipelineToolFactory.Create(thresholdStep))
            {
                Require(threshold.property.Threshold == 123.5
                    && threshold.property.Invert
                    && threshold.property.ThresholdType == (ThresholdTypes.BinaryInv | ThresholdTypes.Otsu),
                    "Threshold factory parsing changed valid numeric, Boolean, or flags parameters.");
            }

            VisionPipelineStep morphologyStep = CreatePipelineStep("morphology");
            morphologyStep.Parameters[nameof(MorphologyToolProperty.KernelWidth)] = "5";
            using (MorphologyTool morphology = (MorphologyTool)VisionPipelineToolFactory.Create(morphologyStep))
            {
                Require(morphology.property.KernelWidth == 5, "Morphology factory did not retain KernelWidth.");
            }

            VisionPipelineStep filterStep = CreatePipelineStep("filter");
            filterStep.Parameters[nameof(FilterToolProperty.FilterType)] = nameof(FilterToolType.GaussianBlur);
            using (FilterTool filter = (FilterTool)VisionPipelineToolFactory.Create(filterStep))
            {
                Require(filter.property.FilterType == FilterToolType.GaussianBlur,
                    "Filter factory did not retain FilterType.");
            }

            VisionPipelineStep edgeStep = CreatePipelineStep("edge");
            edgeStep.Parameters[nameof(EdgeDetectionToolProperty.UseL2Gradient)] = "false";
            using (EdgeDetectionTool edge = (EdgeDetectionTool)VisionPipelineToolFactory.Create(edgeStep))
            {
                Require(!edge.property.UseL2Gradient, "Edge factory did not retain UseL2Gradient.");
            }

            VisionPipelineStep rotateStep = CreatePipelineStep("rotatescale");
            rotateStep.Parameters[nameof(RotateScaleToolProperty.Angle)] = "-12.5";
            using (RotateScaleTool rotate = (RotateScaleTool)VisionPipelineToolFactory.Create(rotateStep))
            {
                Require(rotate.property.Angle == -12.5, "Rotate/scale factory did not retain Angle.");
            }

            VisionPipelineStep affineStep = CreatePipelineStep("affine");
            affineStep.Parameters[nameof(AffineTransformToolProperty.OutputWidth)] = "64";
            using (AffineTransformTool affine = (AffineTransformTool)VisionPipelineToolFactory.Create(affineStep))
            {
                Require(affine.property.OutputWidth == 64, "Affine factory did not retain OutputWidth.");
            }
        }

        private static void TestVisionPipelineFactoryRejectsInvalidParameters()
        {
            RequireFactoryArgumentError(nameof(ThresholdToolProperty.Threshold), "not-a-number", "Threshold");
            RequireFactoryArgumentError(nameof(ThresholdToolProperty.RangeMin), "1.5", "RangeMin");
            RequireFactoryArgumentError(nameof(ThresholdToolProperty.Invert), "yes", "Invert");
            RequireFactoryArgumentError(nameof(ThresholdToolProperty.Mode), "999", "Mode");
            RequireFactoryArgumentError(nameof(ThresholdToolProperty.ThresholdType), "1024", "ThresholdType");
            RequireFactoryArgumentError(nameof(ThresholdToolProperty.Threshold), "NaN", "Threshold");
            RequireFactoryArgumentError("ThresholdTypo", "50", "ThresholdTypo");
            RequireFactoryArgumentError(string.Empty, "50", "cannot be empty");

            VisionPipelineStep duplicate = CreatePipelineStep("threshold");
            duplicate.Parameters[nameof(ThresholdToolProperty.Threshold)] = "10";
            duplicate.Parameters[nameof(ThresholdToolProperty.Threshold).ToLowerInvariant()] = "20";
            RequireFactoryArgumentError(duplicate, "duplicated");

            bool serializedDuplicateRejected = false;
            try
            {
                new VisionPipelineStep
                {
                    XmlParameters = new[]
                    {
                        new VisionPipelineParameter("Threshold", "10"),
                        new VisionPipelineParameter("threshold", "20")
                    }
                };
            }
            catch (ArgumentException exception)
            {
                serializedDuplicateRejected = exception.Message.IndexOf("duplicated", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            Require(serializedDuplicateRejected,
                "Serialized pipeline parameters must reject case-insensitive duplicate keys.");
        }

        private static void TestVisionPipelineRejectsNoExecutableSteps()
        {
            using (VisionPipelineContext context = new VisionPipelineContext())
            {
                VisionPipelineRuntime runtime = new VisionPipelineRuntime(_ => new PassThroughVisionTool());

                using (VisionPipelineRunResult empty = runtime.Run(new VisionPipeline(), context))
                {
                    Require(!empty.Success && empty.StepResults.Count == 0,
                        "An empty pipeline must fail without fabricating step results.");
                }

                VisionPipeline disabledPipeline = new VisionPipeline();
                disabledPipeline.Steps.Add(new VisionPipelineStep { Name = "Disabled", Enabled = false });
                using (VisionPipelineRunResult disabled = runtime.Run(disabledPipeline, context))
                {
                    Require(!disabled.Success && disabled.StepResults.Count == 1 && disabled.StepResults[0].Skipped,
                        "A disabled-only pipeline must not pass.");
                }

                VisionPipeline nullStepPipeline = new VisionPipeline();
                nullStepPipeline.Steps.Add(null);
                bool nullRejected = false;
                try
                {
                    runtime.Run(nullStepPipeline, context);
                }
                catch (InvalidOperationException exception)
                {
                    nullRejected = exception.Message.IndexOf("null", StringComparison.OrdinalIgnoreCase) >= 0;
                }

                Require(nullRejected, "A null pipeline step must be rejected before execution.");
            }
        }

        private static void TestVisionPipelineExpectedFailureAcceptance()
        {
            VisionPipeline expectedFailure = new VisionPipeline();
            expectedFailure.Steps.Add(new VisionPipelineStep
            {
                Name = "Expected failure",
                ToolType = "failing",
                InputLayer = "input",
                UseAcceptance = true,
                ExpectedSuccess = false,
                RequiredMessageText = "Controlled"
            });

            using (Mat source = new Mat(2, 2, MatType.CV_8UC1, Scalar.All(1)))
            using (VisionPipelineContext context = new VisionPipelineContext())
            {
                context.SetLayer("input", source);

                using (VisionPipelineRunResult accepted =
                    new VisionPipelineRuntime(_ => new FailingVisionTool()).Run(expectedFailure, context))
                {
                    Require(accepted.Success
                        && accepted.StepResults.Count == 1
                        && accepted.StepResults[0].AcceptancePassed
                        && accepted.StepResults[0].Success,
                        "A terminal failure matching ExpectedSuccess=false must pass acceptance.");
                    Require(context.GetLayer("output") == null,
                        "An accepted failed step must not fabricate an output image layer.");
                }

                using (VisionPipelineRunResult unexpectedSuccess =
                    new VisionPipelineRuntime(_ => new PassThroughVisionTool()).Run(expectedFailure, context))
                {
                    Require(!unexpectedSuccess.Success && !unexpectedSuccess.StepResults[0].AcceptancePassed,
                        "A successful tool must not satisfy ExpectedSuccess=false.");
                }

                VisionPipeline nonTerminal = new VisionPipeline();
                nonTerminal.Steps.Add(expectedFailure.Steps[0]);
                nonTerminal.Steps.Add(CreatePipelineStep("threshold"));
                bool nonTerminalRejected = false;
                try
                {
                    new VisionPipelineRuntime(_ => new FailingVisionTool()).Run(nonTerminal, context);
                }
                catch (InvalidOperationException exception)
                {
                    nonTerminalRejected = exception.Message.IndexOf("final", StringComparison.OrdinalIgnoreCase) >= 0;
                }

                Require(nonTerminalRejected,
                    "ExpectedSuccess=false must be rejected when a later enabled step depends on its output.");
            }
        }

        private static VisionPipelineStep CreatePipelineStep(string toolType)
        {
            return new VisionPipelineStep
            {
                Name = toolType,
                ToolType = toolType,
                InputLayer = "input",
                OutputLayer = "output"
            };
        }

        private static void RequireFactoryArgumentError(string key, string value, string expectedMessage)
        {
            VisionPipelineStep step = CreatePipelineStep("threshold");
            step.Parameters[key] = value;
            RequireFactoryArgumentError(step, expectedMessage);
        }

        private static void RequireFactoryArgumentError(VisionPipelineStep step, string expectedMessage)
        {
            bool rejected = false;
            try
            {
                IVisionTool tool = VisionPipelineToolFactory.Create(step);
                (tool as IDisposable)?.Dispose();
            }
            catch (ArgumentException exception)
            {
                rejected = exception.Message.IndexOf(expectedMessage, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            Require(rejected, $"Invalid pipeline parameter '{expectedMessage}' was not rejected.");
        }

    }
}

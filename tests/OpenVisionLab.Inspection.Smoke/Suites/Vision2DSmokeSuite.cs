using OpenVisionLab.Inspection;
using OpenVisionLab.Vision2D;
using OpenVisionLab.Vision2D.Blob;
using OpenVisionLab.Vision2D.Pipeline;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Tool;
using OpenVisionLab.Vision3D.FeatureExtraction;
using OpenVisionLab.Vision3D.Geometry;
using OpenVisionLab.Vision3D.Inspection;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;
using static OpenVisionLab.Inspection.Smoke.SmokeFixtures;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class Vision2DSmokeSuite
    {
        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase("Smoke numerical assertions reject non-finite comparisons", TestNumericalAssertionBoundary);
            yield return new SmokeCase("SIFT diagnostics survive success failure and repeated ROI execution", TestSiftExecutionDiagnostics);
            yield return new SmokeCase("Preprocessing releases failed clones and preserves caller images", TestPreprocessingFailureRecovery);
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
            yield return new SmokeCase("ThresholdTool rejects non-finite numeric parameters", TestThresholdRejectsNonFiniteValues);
            yield return new SmokeCase("OpenCV execution errors retain type-based classification", TestExecutionErrorClassification);
            yield return new SmokeCase("EdgeDetectionTool direct Execute finds synthetic edges", TestEdgeDetectionDirectExecution);
            yield return new SmokeCase("RotateScaleTool direct Execute applies the requested output size", TestRotateScaleDirectExecution);
            yield return new SmokeCase("LineGaugeTool rejects unsupported image depth explicitly", TestLineGaugeUnsupportedDepth);
            yield return new SmokeCase("2D tool and result disposal release only owned images", TestVisionToolResourceOwnership);
            yield return new SmokeCase("Pipeline runtime honors tool, input, result, and layer ownership", TestVisionPipelineResourceOwnership);
            yield return new SmokeCase("Pipeline routes only non-null images to named output layers", TestVisionPipelineOptionalOutputContract);
            yield return new SmokeCase("Pipeline XML preserves versioned settings and rejects unsupported input", TestVisionPipelineSerialization);
            yield return new SmokeCase("Pipeline failure-result execution classifies infrastructure failures", TestVisionPipelineFailureResults);
            yield return new SmokeCase("Pipeline descriptors cover every non-legacy 2D Tool", TestVisionPipelineDescriptors);
            yield return new SmokeCase("Pipeline factories create all 15 non-legacy 2D Tools", TestVisionPipelineFactoryBuiltIns);
            yield return new SmokeCase("Pipeline model artifacts enforce identity format hash and ownership", TestVisionPipelineModelArtifacts);
            yield return new SmokeCase("Pipeline factory rejects malformed, unknown, and duplicate parameters", TestVisionPipelineFactoryRejectsInvalidParameters);
            yield return new SmokeCase("Pipeline rejects configurations without an executable step", TestVisionPipelineRejectsNoExecutableSteps);
            yield return new SmokeCase("Pipeline acceptance supports only a terminal expected failure", TestVisionPipelineExpectedFailureAcceptance);
            yield return new SmokeCase("Pipeline acceptance checks finite inclusive metric and elapsed boundaries", TestAcceptanceBoundaries);
        }

        private static void TestNumericalAssertionBoundary()
        {
            foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                RequireThrows<InvalidOperationException>(() => RequireApproximately(invalid, 1.0, 0.01, "actual"));
                RequireThrows<InvalidOperationException>(() => RequireApproximately(1.0, invalid, 0.01, "expected"));
                RequireThrows<InvalidOperationException>(() => RequireApproximately(1.0, 1.0, invalid, "tolerance"));
            }

            RequireThrows<InvalidOperationException>(() => RequireApproximately(1.0, 1.0, -1.0, "negative tolerance"));
            RequireApproximately(2.0, 1.0, 1.0, "The finite tolerance boundary must remain inclusive.");
        }

        private static void TestSiftExecutionDiagnostics()
        {
            using (Mat template = new Mat(180, 180, MatType.CV_8UC1))
            using (Mat source = new Mat(230, 260, MatType.CV_8UC1, Scalar.All(0)))
            using (Mat blank = new Mat(source.Size(), MatType.CV_8UC1, Scalar.All(0)))
            using (SiftTool tool = new SiftTool())
            {
                Random random = new Random(173);
                for (int y = 0; y < template.Height; y++)
                {
                    for (int x = 0; x < template.Width; x++)
                    {
                        template.Set(y, x, (byte)random.Next(256));
                    }
                }

                Rect roi = new Rect(37, 21, template.Width, template.Height);
                using (Mat target = source.SubMat(roi))
                {
                    template.CopyTo(target);
                }

                SiftToolProperty property = new SiftToolProperty { USE_ROI = true, CvROI = roi };
                tool.SetProperty(property);
                tool.SetTemplateImage(template);
                for (int run = 0; run < 3; run++)
                {
                    bool expectSuccess = run != 1;
                    using (VisionToolResult result = tool.Execute(expectSuccess ? source : blank))
                    {
                        Require(result.Success == expectSuccess, "SIFT execution outcome changed: " + result.Message);
                        Require(result.Metrics.TryGetValue("FeatureDetector.Sift", out double sift)
                            && result.Metrics.TryGetValue("FeatureDetector.OrbFallback", out double orb)
                            && sift + orb == 1.0, "SIFT must retain exactly one selected detector after every attempted match.");
                        Require(tool.results.Count == (expectSuccess ? 1 : 0), "SIFT retained results from a previous execution.");
                        if (expectSuccess)
                        {
                            RequireApproximately(tool.results[0].Center.X, roi.X + 89.5, 1.0, "SIFT center must be in source coordinates.");
                            RequireApproximately(tool.results[0].Center.Y, roi.Y + 89.5, 1.0, "SIFT center must retain the ROI offset.");
                        }
                        else
                        {
                            Require(result.ErrorCode == VisionToolErrorCode.FeatureNoKeypoints, "Blank SIFT input must have an explicit no-keypoint result.");
                        }
                    }
                }

                property.USE_MULTI_ROI = true;
                property.CvROIS.Add(roi);
                property.CvROIS.Add(roi);
                using (VisionToolResult multiple = tool.Execute(source))
                {
                    Require(multiple.Success && tool.results.Count == 2, "SIFT multi ROI must retain each match.");
                    Require(multiple.Metrics["FeatureDetector.Sift"] + multiple.Metrics["FeatureDetector.OrbFallback"] == 1.0,
                        "The last successful ROI must not clear the selected detector.");
                }

                tool.SetTemplateImage(null);
                using (VisionToolResult invalid = tool.Execute(source))
                {
                    Require(!invalid.Success && invalid.ErrorCode == VisionToolErrorCode.FeatureTemplateMissing,
                        "Missing template must fail validation before detection.");
                    Require(!invalid.Metrics.ContainsKey("FeatureDetector.OrbFallback"), "Validation failure must not publish stale detector evidence.");
                }
            }
        }

        private static void TestPreprocessingFailureRecovery()
        {
            using (Mat source = new Mat(48, 64, MatType.CV_8UC1, Scalar.All(37)))
            using (PreprocessingProbe tool = new PreprocessingProbe())
            {
                tool.SetSourceImage(source);
                foreach (bool useRoi in new[] { false, true })
                {
                    OpenCvToolPropertyBase property = new MeanToolProperty
                    {
                        USE_ADAPTIVE_THRESHOLD = true,
                        BlockSize = 3,
                        ADAPTIVE_THRESHOLD_TYPES = ThresholdTypes.Trunc
                    };
                    RequireThrows<OpenCVException>(() => { using (tool.Prepare(useRoi, property)) { } });
                    property.USE_ADAPTIVE_THRESHOLD = false;
                    using (Mat prepared = tool.Prepare(useRoi, property))
                    {
                        Require(prepared.Width == (useRoi ? 20 : 64), "Preprocessing recovery lost the requested dimensions.");
                        prepared.SetTo(Scalar.All(0));
                        Require(source.At<byte>(4, 4) == 37 && tool.imageSource.At<byte>(4, 4) == 37,
                            "A prepared clone must not mutate the caller or tool source.");
                    }
                }

                using (SiftTool sift = new SiftTool())
                using (EdgeBasedTemplateMatchingTool edge = new EdgeBasedTemplateMatchingTool())
                {
                    SiftToolProperty siftProperty = new SiftToolProperty
                    {
                        USE_ADAPTIVE_THRESHOLD = true, BlockSize = 3, ADAPTIVE_THRESHOLD_TYPES = ThresholdTypes.Trunc
                    };
                    EdgeBasedTemplateMatchingToolProperty edgeProperty = new EdgeBasedTemplateMatchingToolProperty
                    {
                        USE_ADAPTIVE_THRESHOLD = true, BlockSize = 3, ADAPTIVE_THRESHOLD_TYPES = ThresholdTypes.Trunc
                    };
                    sift.SetProperty(siftProperty);
                    sift.SetTemplateImage(source);
                    edge.SetProperty(edgeProperty);
                    edge.SetTemplateImage(source);
                    foreach (IVisionTool matcher in new IVisionTool[] { sift, edge })
                    {
                        using (VisionToolResult failed = matcher.Execute(source))
                        {
                            Require(!failed.Success && failed.Exception is OpenCVException,
                                "Invalid template preprocessing must preserve the native failure.");
                        }
                    }
                    siftProperty.USE_ADAPTIVE_THRESHOLD = false;
                    edgeProperty.USE_ADAPTIVE_THRESHOLD = false;
                    foreach (IVisionTool matcher in new IVisionTool[] { sift, edge })
                    {
                        using (VisionToolResult recovered = matcher.Execute(source))
                        {
                            Require(recovered.Exception == null, "Template preprocessing failed to recover after corrected options.");
                        }
                    }
                }
            }
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
                            "BestScore=" + firstTool.results[0].Score.ToString("0.000", CultureInfo.InvariantCulture),
                            "BestUniquenessMargin=" + firstTool.results[0].UniquenessMargin.ToString("0.000000", CultureInfo.InvariantCulture),
                            "BestPositionErrorMaxPx=" + firstTool.results[0].PositionErrorMaxPixels.ToString("0.000", CultureInfo.InvariantCulture),
                            "BestRuntimeMedianMs=" + firstTool.results[0].RuntimeMedianMilliseconds.ToString("0.000", CultureInfo.InvariantCulture),
                            "BestRuntimeP95Ms=" + firstTool.results[0].RuntimeP95Milliseconds.ToString("0.000", CultureInfo.InvariantCulture)
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
                            && candidate.RejectReason.Contains("UniquenessMargin", StringComparison.Ordinal)),
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
                                            match.Outcome + ":" + match.Score.ToString("0.0", CultureInfo.InvariantCulture)
                                            + "/" + match.UniquenessMargin.ToString("0.000", CultureInfo.InvariantCulture))))));
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
                                "RepresentativeSuccessRate=" + tool.results[0].RepresentativeSuccessRate.ToString("0.000", CultureInfo.InvariantCulture),
                                "RepresentativeMeanScore=" + tool.results[0].RepresentativeMeanScore.ToString("0.000", CultureInfo.InvariantCulture),
                                "RepresentativeMinimumUniquenessMargin="
                                    + tool.results[0].RepresentativeMinimumUniquenessMargin.ToString("0.000000", CultureInfo.InvariantCulture)
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
                    Require(result.Message.Contains("PlausibleAlternatives=", StringComparison.Ordinal),
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
                summary.Add(metric.Key + "=" + metric.Value.ToString("0.######", CultureInfo.InvariantCulture));
            }

            if (tool.results.Count > 0)
            {
                summary.Add("EdgeScore=" + tool.results[0].EdgeScore.ToString("0.###", CultureInfo.InvariantCulture));
                summary.Add("ImageScore=" + tool.results[0].ImageScore.ToString("0.###", CultureInfo.InvariantCulture));
                summary.Add("FinalScore=" + tool.results[0].FinalScore.ToString("0.###", CultureInfo.InvariantCulture));
                summary.Add("ScoreMargin=" + tool.results[0].ScoreMargin.ToString("0.###", CultureInfo.InvariantCulture));
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

                tool.SetProperty(new FilterToolProperty
                {
                    FilterType = FilterToolType.BilateralFilter,
                    Diameter = 5,
                    SigmaColor = 15,
                    SigmaSpace = 15
                });

                using (VisionToolResult result = tool.Execute(source))
                {
                    Require(result.Success,
                        "FilterTool bilateral Execute failed: " + result.ErrorName + ": " + result.Message);
                    Require(result.ResultImage != null
                        && result.ResultImage.Size() == source.Size(),
                        "FilterTool bilateral Execute did not publish a result image.");
                }
            }
        }

        private static void TestThresholdRejectsNonFiniteValues()
        {
            using (Mat source = new Mat(new Size(4, 4), MatType.CV_8UC1, Scalar.All(10)))
            using (ThresholdTool tool = new ThresholdTool())
            {
                foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                {
                    tool.SetProperty(new ThresholdToolProperty { Threshold = invalid });
                    using (VisionToolResult thresholdResult = tool.Execute(source))
                    {
                        Require(!thresholdResult.Success && thresholdResult.ErrorCode == VisionToolErrorCode.InvalidParameter
                            && thresholdResult.Exception == null, "ThresholdTool must reject non-finite Threshold before OpenCV execution.");
                    }
                    tool.SetProperty(new ThresholdToolProperty { Threshold = 5, MaxValue = invalid });
                    using (VisionToolResult maxValueResult = tool.Execute(source))
                    {
                        Require(!maxValueResult.Success && maxValueResult.ErrorCode == VisionToolErrorCode.ThresholdInvalidMaxValue
                            && maxValueResult.Exception == null, "ThresholdTool must reject non-finite MaxValue before OpenCV execution.");
                    }
                }
            }
        }

        private static void TestExecutionErrorClassification()
        {
            using (Mat source = new Mat(new Size(4, 4), MatType.CV_8UC1, Scalar.All(10)))
            using (ThrowingOpenCvTool directionFailure = new ThrowingOpenCvTool(
                new InvalidOperationException("Direction calculation failed.")))
            using (ThrowingOpenCvTool openCvFailure = new ThrowingOpenCvTool(
                new OpenCVException("native execution failed")))
            using (VisionToolResult directionResult = directionFailure.Execute(source))
            using (VisionToolResult openCvResult = openCvFailure.Execute(source))
            {
                Require(directionResult.ErrorCode == VisionToolErrorCode.ToolExecutionException,
                    "A generic exception containing 'direction' must not be classified as InvalidRoi.");
                Require(openCvResult.ErrorCode == VisionToolErrorCode.OpenCvExecutionFailed,
                    "An OpenCV exception must be classified as OpenCvExecutionFailed.");
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

        private static void TestVisionPipelineSerialization()
        {
            VisionPipeline pipeline = new VisionPipeline { Name = "Serialized fixture" };
            pipeline.Steps.Add(new VisionPipelineStep
            {
                Name = "Matching",
                ToolType = "matching",
                Enabled = true,
                InputLayer = "input",
                OutputLayer = "matched",
                UseAcceptance = true,
                ExpectedSuccess = true,
                MaxElapsedMilliseconds = 12.5,
                AcceptanceMetricName = "Coverage",
                UseAcceptanceMetricMinimum = true,
                AcceptanceMetricMinimum = 0.75
            });
            pipeline.Steps[0].Parameters[nameof(MatchingToolProperty.SCORE_MIN)] = "0.8";
            pipeline.Steps[0].Artifacts.Add(new VisionPipelineArtifactReference(
                VisionPipelineToolFactory.TemplateArtifactRole,
                "templates/part-a",
                VisionPipelineToolFactory.EncodedImageArtifactFormat,
                VisionPipelineToolFactory.EncodedImageArtifactFormatVersion,
                new string('A', 64)));

            string serialized = VisionPipelineSerializer.Serialize(pipeline);
            Require(serialized.Contains("schemaVersion=\"2\"", StringComparison.Ordinal)
                && serialized.Contains("role=\"template\"", StringComparison.Ordinal)
                && serialized.Contains("id=\"templates/part-a\"", StringComparison.Ordinal)
                && !serialized.Contains("xmlns", StringComparison.Ordinal),
                "Pipeline serialization did not publish the current schema without default namespaces.");

            VisionPipeline restored = VisionPipelineSerializer.Deserialize(serialized);
            Require(restored.SchemaVersion == VisionPipeline.CurrentSchemaVersion
                && restored.Name == pipeline.Name
                && restored.Steps.Count == 1
                && restored.Steps[0].Name == "Matching"
                && restored.Steps[0].InputLayer == "input"
                && restored.Steps[0].OutputLayer == "matched"
                && restored.Steps[0].UseAcceptance
                && restored.Steps[0].MaxElapsedMilliseconds == 12.5
                && restored.Steps[0].AcceptanceMetricMinimum == 0.75
                && restored.Steps[0].Parameters[nameof(MatchingToolProperty.SCORE_MIN)] == "0.8"
                && restored.Steps[0].Artifacts.Count == 1
                && restored.Steps[0].Artifacts[0].Id == "templates/part-a"
                && restored.Steps[0].Artifacts[0].Sha256 == new string('A', 64),
                "Pipeline serialization did not preserve the semantic contract.");

            const string versionOneXml =
                "<VisionPipeline schemaVersion=\"1\"><Name>Version one</Name><Steps><Step>"
                + "<Name>Threshold</Name><ToolType>threshold</ToolType><Enabled>true</Enabled>"
                + "<InputLayer>input</InputLayer><OutputLayer>binary</OutputLayer>"
                + "<Parameters><Parameter><Key>Threshold</Key><Value>50</Value></Parameter></Parameters>"
                + "</Step></Steps></VisionPipeline>";
            VisionPipeline versionOne = VisionPipelineSerializer.Deserialize(versionOneXml);
            Require(versionOne.SchemaVersion == 1
                && versionOne.Steps.Count == 1
                && versionOne.Steps[0].Parameters[nameof(ThresholdToolProperty.Threshold)] == "50",
                "Schema version 1 Pipeline XML must remain readable.");

            string unversionedXml = versionOneXml.Replace(" schemaVersion=\"1\"", string.Empty);
            Require(VisionPipelineSerializer.Deserialize(unversionedXml).SchemaVersion == 1,
                "Unversioned original Pipeline XML must load as schema version 1.");

            VisionPipeline legacyWritable = new VisionPipeline { SchemaVersion = 1, Name = "Legacy write" };
            Require(VisionPipelineSerializer.Serialize(legacyWritable).Contains("schemaVersion=\"1\"", StringComparison.Ordinal),
                "Schema version 1 pipelines without artifacts must remain serializable.");

            string futureXml = serialized.Replace("schemaVersion=\"2\"", "schemaVersion=\"3\"");
            RequireThrows<NotSupportedException>(() => VisionPipelineSerializer.Deserialize(futureXml));

            string invalidVersionXml = serialized.Replace("schemaVersion=\"2\"", "schemaVersion=\"current\"");
            RequireThrows<NotSupportedException>(() => VisionPipelineSerializer.Deserialize(invalidVersionXml));

            string versionOneWithArtifact = serialized.Replace("schemaVersion=\"2\"", "schemaVersion=\"1\"");
            RequireThrows<NotSupportedException>(() => VisionPipelineSerializer.Deserialize(versionOneWithArtifact));

            string invalidHashXml = serialized.Replace(new string('A', 64), "not-a-sha256");
            RequireThrows<ArgumentException>(() => VisionPipelineSerializer.Deserialize(invalidHashXml));

            VisionPipelineArtifactReference artifact = pipeline.Steps[0].Artifacts[0];
            string artifactRole = artifact.Role;
            artifact.Role = " ";
            RequireThrows<ArgumentException>(() => VisionPipelineSerializer.Serialize(pipeline));
            artifact.Role = artifactRole;
            string artifactId = artifact.Id;
            artifact.Id = string.Empty;
            RequireThrows<ArgumentException>(() => VisionPipelineSerializer.Serialize(pipeline));
            artifact.Id = artifactId;
            string artifactFormat = artifact.Format;
            artifact.Format = string.Empty;
            RequireThrows<ArgumentException>(() => VisionPipelineSerializer.Serialize(pipeline));
            artifact.Format = artifactFormat;
            int artifactFormatVersion = artifact.FormatVersion;
            artifact.FormatVersion = 0;
            RequireThrows<ArgumentException>(() => VisionPipelineSerializer.Serialize(pipeline));
            artifact.FormatVersion = artifactFormatVersion;

            string duplicateXml = serialized.Replace(
                "</Parameters>",
                "<Parameter><Key>score_min</Key><Value>0.7</Value></Parameter></Parameters>");
            RequireThrows<InvalidOperationException>(() => VisionPipelineSerializer.Deserialize(duplicateXml));

            string dtdXml = "<!DOCTYPE VisionPipeline [<!ENTITY injected 'blocked'>]>" + serialized;
            RequireThrows<InvalidOperationException>(() => VisionPipelineSerializer.Deserialize(dtdXml));
            RequireThrows<ArgumentException>(() => VisionPipelineSerializer.Deserialize(" "));

            pipeline.SchemaVersion = 3;
            RequireThrows<NotSupportedException>(() => VisionPipelineSerializer.Serialize(pipeline));

            legacyWritable.Steps.Add(new VisionPipelineStep());
            legacyWritable.Steps[0].Artifacts.Add(pipeline.Steps[0].Artifacts[0]);
            RequireThrows<NotSupportedException>(() => VisionPipelineSerializer.Serialize(legacyWritable));
        }

        private static void TestVisionPipelineFailureResults()
        {
            VisionPipeline pipeline = new VisionPipeline { Name = "Failure-result fixture" };
            pipeline.Steps.Add(CreatePipelineStep("threshold"));

            using (Mat successSource = new Mat(2, 2, MatType.CV_8UC1, Scalar.All(1)))
            using (VisionPipelineContext successContext = new VisionPipelineContext())
            {
                successContext.SetLayer("input", successSource);
                using (VisionPipelineRunResult success = new VisionPipelineRuntime(_ => new ImageReturningVisionTool())
                    .RunWithFailureResults(pipeline, successContext))
                using (Mat output = successContext.GetLayer("output"))
                {
                    Require(success.Success && output != null && !output.Empty(),
                        "Failure-result execution did not preserve the normal output path.");
                }
            }

            VisionPipeline missingPipeline = new VisionPipeline { Name = "Missing-layer fixture" };
            VisionPipelineStep missingStep = CreatePipelineStep("threshold");
            missingStep.UseAcceptance = true;
            missingStep.ExpectedSuccess = false;
            missingPipeline.Steps.Add(missingStep);

            bool factoryCalled = false;
            using (VisionPipelineContext missingContext = new VisionPipelineContext())
            using (VisionPipelineRunResult missing = new VisionPipelineRuntime(_ =>
            {
                factoryCalled = true;
                return new PassThroughVisionTool();
            }).RunWithFailureResults(missingPipeline, missingContext))
            {
                Require(!missing.Success
                    && !factoryCalled
                    && missing.StepResults.Count == 1
                    && !missing.StepResults[0].AcceptancePassed
                    && missing.StepResults[0].ToolResult.ErrorCode == VisionToolErrorCode.InputLayerMissing
                    && missing.StepResults[0].ToolResult.ResultStatus == VisionToolResultStatus.InvalidInput
                    && missing.StepResults[0].ToolResult.Exception == null,
                    "A missing Pipeline layer was not returned as a typed infrastructure failure.");
            }

            using (Mat source = new Mat(2, 2, MatType.CV_8UC1, Scalar.All(1)))
            using (VisionPipelineContext context = new VisionPipelineContext())
            {
                context.SetLayer("input", source);
                pipeline.Steps[0].UseAcceptance = true;
                pipeline.Steps[0].ExpectedSuccess = false;
                InvalidOperationException factoryException = new InvalidOperationException("Controlled factory failure.");
                using (VisionPipelineRunResult factoryFailure = new VisionPipelineRuntime(_ => throw factoryException)
                    .RunWithFailureResults(pipeline, context))
                {
                    Require(!factoryFailure.Success
                        && !factoryFailure.StepResults[0].AcceptancePassed
                        && factoryFailure.StepResults[0].ToolResult.ErrorCode == VisionToolErrorCode.ToolFactoryFailed
                        && factoryFailure.StepResults[0].ToolResult.ResultStatus == VisionToolResultStatus.ConfigurationError
                        && ReferenceEquals(factoryFailure.StepResults[0].ToolResult.Exception, factoryException),
                        "A Pipeline factory exception was not preserved as ToolFactoryFailed.");
                }

                using (VisionPipelineRunResult nullTool = new VisionPipelineRuntime(_ => null)
                    .RunWithFailureResults(pipeline, context))
                {
                    Require(!nullTool.Success
                        && !nullTool.StepResults[0].AcceptancePassed
                        && nullTool.StepResults[0].ToolResult.ErrorCode == VisionToolErrorCode.ToolFactoryFailed
                        && nullTool.StepResults[0].ToolResult.Exception == null,
                        "A null factory result was not preserved as ToolFactoryFailed.");
                }

                using (VisionPipelineRunResult nullResult = new VisionPipelineRuntime(_ => new NullResultVisionTool())
                    .RunWithFailureResults(pipeline, context))
                {
                    Require(!nullResult.Success
                        && !nullResult.StepResults[0].AcceptancePassed
                        && nullResult.StepResults[0].ToolResult.ErrorCode == VisionToolErrorCode.ToolExecutionException
                        && nullResult.StepResults[0].ToolResult.ResultStatus == VisionToolResultStatus.Exception
                        && nullResult.StepResults[0].ToolResult.Exception == null,
                        "A null Tool execution result was not preserved as ToolExecutionException.");
                }

                ThrowingDisposableVisionTool throwingTool = new ThrowingDisposableVisionTool();
                using (VisionPipelineRunResult executionFailure = new VisionPipelineRuntime(_ => throwingTool, true)
                    .RunWithFailureResults(pipeline, context))
                {
                    Require(!executionFailure.Success
                        && !executionFailure.StepResults[0].AcceptancePassed
                        && executionFailure.StepResults[0].ToolResult.ErrorCode == VisionToolErrorCode.ToolExecutionException
                        && executionFailure.StepResults[0].ToolResult.ResultStatus == VisionToolResultStatus.Exception
                        && executionFailure.StepResults[0].ToolResult.Exception is InvalidOperationException
                        && throwingTool.WasDisposed
                        && throwingTool.LastSource != null
                        && throwingTool.LastSource.IsDisposed,
                        "A thrown Tool failure did not preserve its typed result and owned lifetimes.");
                }

                bool existingRunThrew = false;
                try
                {
                    new VisionPipelineRuntime(_ => throw factoryException).Run(pipeline, context);
                }
                catch (InvalidOperationException exception)
                {
                    existingRunThrew = ReferenceEquals(exception, factoryException);
                }

                Require(existingRunThrew, "The existing Run factory-exception contract changed.");
            }
        }

        private static void TestVisionPipelineDescriptors()
        {
            string[] expectedToolTypes =
            {
                "threshold",
                "morphology",
                "filter",
                "edgeDetection",
                "rotateScale",
                "affineTransform",
                "contour",
                "corner",
                "matching",
                "edgeBasedTemplateMatching",
                "autoMPoint",
                "sift",
                "lineGauge",
                "mean",
                "blob"
            };
            Require(VisionPipelineToolFactory.Descriptors.Count == 14,
                "The base Vision2D descriptor catalog must contain its 14 owned Tools.");
            Require(VisionPipelineBlobToolFactory.Descriptors.Select(item => item.ToolType).SequenceEqual(expectedToolTypes),
                "The Blob composite descriptor catalog does not contain the exact 15-Tool contract.");

            foreach (VisionPipelineToolDescriptor descriptor in VisionPipelineBlobToolFactory.Descriptors)
            {
                Require(!string.IsNullOrWhiteSpace(descriptor.ToolType)
                    && !string.IsNullOrWhiteSpace(descriptor.ToolTypeName)
                    && !string.IsNullOrWhiteSpace(descriptor.PropertyTypeName)
                    && !string.IsNullOrWhiteSpace(descriptor.PackageId),
                    $"Pipeline descriptor '{descriptor.ToolType}' has incomplete identity metadata.");
                Require(descriptor.Parameters.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                    == descriptor.Parameters.Count,
                    $"Pipeline descriptor '{descriptor.ToolType}' has duplicate parameter names.");
                Require(descriptor.Parameters.All(item => !string.IsNullOrWhiteSpace(item.Name)
                    && !string.IsNullOrWhiteSpace(item.ValueType)
                    && item.DefaultValue != null),
                    $"Pipeline descriptor '{descriptor.ToolType}' has incomplete parameter metadata.");

                Type propertyType = typeof(ThresholdToolProperty).Assembly.GetType(descriptor.PropertyTypeName)
                    ?? typeof(BlobToolProperty).Assembly.GetType(descriptor.PropertyTypeName);
                Require(propertyType != null,
                    $"Pipeline descriptor '{descriptor.ToolType}' names an unavailable property type.");

                Dictionary<string, PropertyInfo> writableProperties = propertyType.GetProperties()
                    .Where(item => item.CanWrite)
                    .ToDictionary(item => item.Name, StringComparer.Ordinal);
                writableProperties.Remove(nameof(MatchingToolProperty.PATTERN_PATH));
                Require(writableProperties.Keys.OrderBy(item => item).SequenceEqual(
                    descriptor.Parameters.Select(item => item.Name).OrderBy(item => item)),
                    $"Pipeline descriptor '{descriptor.ToolType}' does not cover its exact writable property contract.");

                foreach (VisionPipelineParameterDescriptor parameter in descriptor.Parameters)
                {
                    PropertyInfo property = writableProperties[parameter.Name];
                    Require(parameter.ValueKind == GetPipelineParameterValueKind(property.PropertyType)
                        && parameter.ValueType == GetPipelineParameterValueType(property.PropertyType),
                        $"Pipeline descriptor '{descriptor.ToolType}.{parameter.Name}' has incorrect type metadata.");
                }

                foreach (string alias in descriptor.Aliases)
                {
                    Require(VisionPipelineBlobToolFactory.TryGetDescriptor(alias, out VisionPipelineToolDescriptor resolved)
                        && ReferenceEquals(resolved, descriptor),
                        $"Pipeline descriptor alias '{alias}' does not resolve to '{descriptor.ToolType}'.");
                }
            }

            Require(VisionPipelineToolFactory.TryGetDescriptor("rotate_and_scale_tool", out VisionPipelineToolDescriptor rotate)
                && rotate.ToolType == "rotateScale",
                "Pipeline descriptor lookup did not normalize a documented alias.");
            Require(VisionPipelineBlobToolFactory.TryGetDescriptor("BlobTool", out VisionPipelineToolDescriptor blob)
                && blob.PackageId == "OpenVisionLab.Vision2D.Blob",
                "Blob descriptor lookup did not preserve the package owner.");

            VisionPipelineToolDescriptor matching = VisionPipelineToolFactory.Descriptors.Single(item => item.ToolType == "matching");
            Require(matching.Artifacts.Count == 1
                && matching.Artifacts[0].Required
                && matching.Artifacts[0].Role == VisionPipelineToolFactory.TemplateArtifactRole
                && matching.Artifacts[0].Format == VisionPipelineToolFactory.EncodedImageArtifactFormat
                && matching.Artifacts[0].FormatVersion == VisionPipelineToolFactory.EncodedImageArtifactFormatVersion
                && matching.Parameters.All(item => item.Name != nameof(MatchingToolProperty.PATTERN_PATH)),
                "Matching descriptor must require the versioned template artifact and exclude host paths.");

            VisionPipelineToolDescriptor lineGauge = VisionPipelineToolFactory.Descriptors.Single(item => item.ToolType == "lineGauge");
            Require(lineGauge.Parameters.Single(item => item.Name == nameof(LineGaugeToolProperty.CvROI)).Required,
                "LineGauge descriptor must identify its taught ROI as required.");
        }

        private static VisionPipelineParameterValueKind GetPipelineParameterValueKind(Type type)
        {
            if (type == typeof(string)) return VisionPipelineParameterValueKind.Text;
            if (type == typeof(bool)) return VisionPipelineParameterValueKind.Boolean;
            if (type == typeof(int)) return VisionPipelineParameterValueKind.WholeNumber;
            if (type == typeof(double)) return VisionPipelineParameterValueKind.Number;
            if (type.IsEnum) return VisionPipelineParameterValueKind.Enum;
            if (type == typeof(Rect)) return VisionPipelineParameterValueKind.Rectangle;
            if (type == typeof(List<Rect>)) return VisionPipelineParameterValueKind.RectangleList;
            if (type == typeof(System.Drawing.Color)) return VisionPipelineParameterValueKind.Color;
            throw new InvalidOperationException($"Unsupported Pipeline parameter type '{type.FullName}'.");
        }

        private static string GetPipelineParameterValueType(Type type)
        {
            return type == typeof(List<Rect>)
                ? "System.Collections.Generic.List<OpenCvSharp.Rect>"
                : type.FullName;
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

            VisionPipelineStep contourStep = CreatePipelineStep("contour");
            contourStep.Parameters[nameof(ContourToolProperty.USE_ROI)] = "true";
            contourStep.Parameters[nameof(ContourToolProperty.CvROI)] = "1,2,30,31";
            contourStep.Parameters[nameof(ContourToolProperty.CvROIS)] = "1,2,3,4;5,6,7,8";
            contourStep.Parameters[nameof(ContourToolProperty.DrawColor)] = "#112233";
            using (ContourTool contour = (ContourTool)VisionPipelineToolFactory.Create(contourStep))
            {
                Require(contour.property.CvROI == new Rect(1, 2, 30, 31)
                    && contour.property.CvROIS.SequenceEqual(new[] { new Rect(1, 2, 3, 4), new Rect(5, 6, 7, 8) })
                    && contour.property.DrawColor.R == 0x11
                    && contour.property.DrawColor.G == 0x22
                    && contour.property.DrawColor.B == 0x33,
                    "Contour factory did not retain rectangle, rectangle-list, or color parameters.");
            }

            VisionPipelineStep autoStep = CreatePipelineStep("autoMPoint");
            autoStep.Parameters[nameof(AutoMPointToolProperty.AnalysisRoi)] = "2,3,40,41";
            autoStep.Parameters[nameof(AutoMPointToolProperty.MaximumResults)] = "3";
            using (AutoMPointTool auto = (AutoMPointTool)VisionPipelineToolFactory.Create(autoStep))
            {
                Require(auto.property.AnalysisRoi == new Rect(2, 3, 40, 41)
                    && auto.property.MaximumResults == 3,
                    "Auto MPoint factory did not retain its typed settings.");
            }

            using (Mat template = new Mat(32, 32, MatType.CV_8UC1, Scalar.Black))
            {
                Cv2.Rectangle(template, new Rect(5, 7, 12, 15), Scalar.White, Cv2.FILLED);
                byte[] encoded = template.ToBytes(".png", Array.Empty<int>());
                int resolverCalls = 0;
                foreach (VisionPipelineToolDescriptor descriptor in VisionPipelineBlobToolFactory.Descriptors)
                {
                    VisionPipelineStep step = CreatePipelineStep(descriptor.ToolType);
                    if (descriptor.ToolType == "lineGauge")
                    {
                        step.Parameters[nameof(LineGaugeToolProperty.CvROI)] = "1,1,20,20";
                    }

                    if (descriptor.Artifacts.Count > 0)
                    {
                        step.Artifacts.Add(CreateTemplateArtifact(encoded, descriptor.ToolType + "/template"));
                    }

                    IVisionTool tool = VisionPipelineBlobToolFactory.Create(step, artifact =>
                    {
                        resolverCalls++;
                        Require(ReferenceEquals(artifact, step.Artifacts[0]),
                            "The factory must pass the serialized artifact identity to the host resolver.");
                        return encoded;
                    });
                    try
                    {
                        Require(tool.GetType().FullName == descriptor.ToolTypeName,
                            $"Pipeline factory created '{tool.GetType().FullName}' for '{descriptor.ToolType}'.");
                    }
                    finally
                    {
                        (tool as IDisposable)?.Dispose();
                    }
                }

                Require(resolverCalls == 3,
                    "Only Matching, edge-based matching, and SIFT should resolve a template artifact.");
            }

            VisionPipelineStep blobStep = CreatePipelineStep("blob");
            blobStep.Parameters[nameof(BlobToolProperty.CvROI)] = "3,4,20,21";
            blobStep.Parameters[nameof(BlobToolProperty.MIN_AREA)] = "33";
            using (BlobTool blobTool = (BlobTool)VisionPipelineBlobToolFactory.Create(blobStep))
            {
                Require(blobTool.property.CvROI == new Rect(3, 4, 20, 21)
                    && blobTool.property.MIN_AREA == 33,
                    "Blob factory did not retain its typed settings.");
            }
        }

        private static void TestVisionPipelineModelArtifacts()
        {
            using (Mat template = new Mat(40, 40, MatType.CV_8UC1, Scalar.Black))
            {
                Cv2.Rectangle(template, new Rect(6, 8, 18, 15), Scalar.White, Cv2.FILLED);
                Cv2.Circle(template, new OpenCvSharp.Point(29, 29), 5, new Scalar(127), Cv2.FILLED);
                byte[] encoded = template.ToBytes(".png", Array.Empty<int>());
                VisionPipelineStep matchingStep = CreatePipelineStep("matching");
                matchingStep.Parameters[nameof(MatchingToolProperty.USE_FIND_ANGLE)] = "false";
                matchingStep.Parameters[nameof(MatchingToolProperty.NUM_MATCH)] = "1";
                matchingStep.Artifacts.Add(CreateTemplateArtifact(encoded, "templates/verified"));

                int resolverCalls = 0;
                using (MatchingTool tool = (MatchingTool)VisionPipelineToolFactory.Create(matchingStep, artifact =>
                {
                    resolverCalls++;
                    Require(artifact.Id == "templates/verified", "The resolver received the wrong artifact ID.");
                    return encoded;
                }))
                {
                    Array.Clear(encoded, 0, encoded.Length);
                    using (VisionToolResult result = tool.Execute(template))
                    {
                        Require(result.Success && tool.property.PATTERN_PATH == string.Empty,
                            "The reconstructed Matching Tool did not own a usable template or retained a host path.");
                    }
                }

                Require(resolverCalls == 1, "A template artifact must be resolved exactly once during Tool creation.");
            }

            using (Mat template = new Mat(12, 12, MatType.CV_8UC1, Scalar.White))
            {
                byte[] encoded = template.ToBytes(".png", Array.Empty<int>());
                VisionPipelineStep missing = CreatePipelineStep("sift");
                RequireThrows<ArgumentException>(() => VisionPipelineToolFactory.Create(missing, _ => encoded));

                VisionPipelineStep noResolver = CreatePipelineStep("sift");
                noResolver.Artifacts.Add(CreateTemplateArtifact(encoded, "templates/no-resolver"));
                RequireThrows<InvalidOperationException>(() => VisionPipelineToolFactory.Create(noResolver));

                VisionPipelineStep wrongHash = CreatePipelineStep("sift");
                wrongHash.Artifacts.Add(CreateTemplateArtifact(encoded, "templates/wrong-hash"));
                wrongHash.Artifacts[0].Sha256 = new string('0', 64);
                RequireThrows<InvalidOperationException>(() => VisionPipelineToolFactory.Create(wrongHash, _ => encoded));

                VisionPipelineStep emptyBytes = CreatePipelineStep("sift");
                emptyBytes.Artifacts.Add(CreateTemplateArtifact(encoded, "templates/empty-bytes"));
                RequireThrows<InvalidOperationException>(() => VisionPipelineToolFactory.Create(emptyBytes, _ => Array.Empty<byte>()));

                VisionPipelineStep invalidMetadata = CreatePipelineStep("sift");
                invalidMetadata.Artifacts.Add(CreateTemplateArtifact(encoded, "templates/invalid-metadata"));
                invalidMetadata.Artifacts[0].Sha256 = "invalid";
                int invalidMetadataResolverCalls = 0;
                RequireThrows<ArgumentException>(() => VisionPipelineToolFactory.Create(invalidMetadata, _ =>
                {
                    invalidMetadataResolverCalls++;
                    return encoded;
                }));
                Require(invalidMetadataResolverCalls == 0,
                    "Invalid artifact metadata must be rejected before calling the host resolver.");

                VisionPipelineStep wrongFormat = CreatePipelineStep("sift");
                wrongFormat.Artifacts.Add(CreateTemplateArtifact(encoded, "templates/wrong-format"));
                wrongFormat.Artifacts[0].Format = "raw-image";
                RequireThrows<NotSupportedException>(() => VisionPipelineToolFactory.Create(wrongFormat, _ => encoded));

                byte[] invalidBytes = { 1, 2, 3, 4, 5 };
                VisionPipelineStep invalidImage = CreatePipelineStep("sift");
                invalidImage.Artifacts.Add(CreateTemplateArtifact(invalidBytes, "templates/invalid-image"));
                RequireThrows<InvalidOperationException>(() => VisionPipelineToolFactory.Create(invalidImage, _ => invalidBytes));

                VisionPipelineStep duplicate = CreatePipelineStep("sift");
                duplicate.Artifacts.Add(CreateTemplateArtifact(encoded, "templates/one"));
                duplicate.Artifacts.Add(CreateTemplateArtifact(encoded, "templates/two"));
                RequireThrows<ArgumentException>(() => VisionPipelineToolFactory.Create(duplicate, _ => encoded));

                VisionPipelineStep pathParameter = CreatePipelineStep("sift");
                pathParameter.Artifacts.Add(CreateTemplateArtifact(encoded, "templates/no-path"));
                pathParameter.Parameters[nameof(SiftToolProperty.PATTERN_PATH)] = "C:\\host\\template.png";
                bool pathRejected = false;
                try
                {
                    IVisionTool tool = VisionPipelineToolFactory.Create(pathParameter, _ => encoded);
                    (tool as IDisposable)?.Dispose();
                }
                catch (ArgumentException exception)
                {
                    pathRejected = exception.ParamName == "parameters"
                        && exception.Message.Contains(nameof(SiftToolProperty.PATTERN_PATH), StringComparison.Ordinal);
                }

                Require(pathRejected, "Pipeline model reconstruction must reject host path parameters.");

                VisionPipelineStep nonModel = CreatePipelineStep("threshold");
                nonModel.Artifacts.Add(CreateTemplateArtifact(encoded, "templates/unexpected"));
                RequireThrows<ArgumentException>(() => VisionPipelineToolFactory.Create(nonModel, _ => encoded));

                using (VisionPipelineContext context = new VisionPipelineContext())
                using (Mat source = template.Clone())
                {
                    context.SetLayer("input", source);
                    VisionPipeline pipeline = new VisionPipeline();
                    pipeline.Steps.Add(wrongHash);
                    VisionPipelineRuntime runtime = new VisionPipelineRuntime(
                        step => VisionPipelineToolFactory.Create(step, _ => encoded),
                        true);
                    using (VisionPipelineRunResult result = runtime.RunWithFailureResults(pipeline, context))
                    {
                        Require(!result.Success
                            && result.StepResults.Count == 1
                            && result.StepResults[0].ToolResult.ErrorCode == VisionToolErrorCode.ToolFactoryFailed,
                            "Artifact reconstruction failures must cross the Pipeline boundary as ToolFactoryFailed.");
                    }
                }
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
                _ = new VisionPipelineStep
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
                serializedDuplicateRejected = exception.Message.Contains("duplicated", StringComparison.OrdinalIgnoreCase);
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

                VisionPipeline futureSchema = new VisionPipeline { SchemaVersion = VisionPipeline.CurrentSchemaVersion + 1 };
                RequireThrows<NotSupportedException>(() => runtime.Run(futureSchema, context));

                VisionPipeline nullStepPipeline = new VisionPipeline();
                nullStepPipeline.Steps.Add(null);
                bool nullRejected = false;
                try
                {
                    runtime.Run(nullStepPipeline, context);
                }
                catch (InvalidOperationException exception)
                {
                    nullRejected = exception.Message.Contains("null", StringComparison.OrdinalIgnoreCase);
                }

                Require(nullRejected, "A null pipeline step must be rejected before execution.");
            }
        }

        private static void TestAcceptanceBoundaries()
        {
            VisionPipelineStep step = new VisionPipelineStep
            {
                UseAcceptance = true, ExpectedSuccess = true, AcceptanceMetricName = "Score",
                UseAcceptanceMetricMinimum = true, AcceptanceMetricMinimum = 1.0,
                UseAcceptanceMetricMaximum = true, AcceptanceMetricMaximum = 2.0, MaxElapsedMilliseconds = 10.0
            };
            using (VisionToolResult result = new VisionToolResult { Success = true, Elapsed = TimeSpan.FromMilliseconds(10) })
            {
                foreach (double boundary in new[] { 1.0, 2.0 })
                {
                    result.Metrics["Score"] = boundary;
                    Require(VisionPipelineAcceptanceEvaluator.Evaluate(step, result).Passed, "Metric and elapsed limits must be inclusive.");
                }
                foreach (double outside in new[] { 0.999, 2.001, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                {
                    result.Metrics["Score"] = outside;
                    Require(!VisionPipelineAcceptanceEvaluator.Evaluate(step, result).Passed, "Out-of-range or non-finite metrics must fail.");
                }
                result.Metrics["Score"] = 1.5;
                foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                {
                    step.AcceptanceMetricMinimum = invalid;
                    Require(!VisionPipelineAcceptanceEvaluator.Evaluate(step, result).Passed, "Enabled minimum must be finite.");
                    step.AcceptanceMetricMinimum = 1.0;
                    step.AcceptanceMetricMaximum = invalid;
                    Require(!VisionPipelineAcceptanceEvaluator.Evaluate(step, result).Passed, "Enabled maximum must be finite.");
                    step.AcceptanceMetricMaximum = 2.0;
                    step.MaxElapsedMilliseconds = invalid;
                    Require(!VisionPipelineAcceptanceEvaluator.Evaluate(step, result).Passed, "Elapsed limit must be finite.");
                    step.MaxElapsedMilliseconds = 10.0;
                }
                result.Elapsed = TimeSpan.FromMilliseconds(10.001);
                Require(!VisionPipelineAcceptanceEvaluator.Evaluate(step, result).Passed, "Elapsed above the limit must fail.");
                result.Elapsed = TimeSpan.Zero;
                step.AcceptanceMetricName = "Missing";
                Require(!VisionPipelineAcceptanceEvaluator.Evaluate(step, result).Passed, "Missing metrics must fail.");
                step.AcceptanceMetricName = " ";
                Require(!VisionPipelineAcceptanceEvaluator.Evaluate(step, result).Passed, "An empty enabled metric name must fail.");
                step.UseAcceptanceMetricMinimum = false;
                step.UseAcceptanceMetricMaximum = false;
                step.AcceptanceMetricMinimum = double.NaN;
                step.AcceptanceMetricMaximum = double.PositiveInfinity;
                Require(VisionPipelineAcceptanceEvaluator.Evaluate(step, result).Passed, "Disabled metric limits must not affect acceptance.");
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

                VisionPipelineStep metricStep = new VisionPipelineStep
                {
                    UseAcceptance = true,
                    AcceptanceMetricName = "Score",
                    UseAcceptanceMetricMinimum = true,
                    AcceptanceMetricMinimum = 1.0,
                    UseAcceptanceMetricMaximum = true,
                    AcceptanceMetricMaximum = 2.0
                };
                VisionToolResult nonFiniteMetric = new VisionToolResult { Success = true };
                nonFiniteMetric.Metrics["Score"] = double.NaN;
                Require(!VisionPipelineAcceptanceEvaluator.Evaluate(metricStep, nonFiniteMetric).Passed,
                    "A non-finite acceptance metric must fail closed.");

                metricStep.UseAcceptanceMetricMinimum = false;
                metricStep.AcceptanceMetricMaximum = double.NaN;
                VisionToolResult finiteMetric = new VisionToolResult { Success = true };
                finiteMetric.Metrics["Score"] = 1.5;
                Require(!VisionPipelineAcceptanceEvaluator.Evaluate(metricStep, finiteMetric).Passed,
                    "A non-finite acceptance limit must fail closed.");

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
                    nonTerminalRejected = exception.Message.Contains("final", StringComparison.OrdinalIgnoreCase);
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

        private static VisionPipelineArtifactReference CreateTemplateArtifact(byte[] encoded, string id)
        {
            string sha256 = BitConverter.ToString(SHA256.HashData(encoded)).Replace("-", string.Empty);

            return new VisionPipelineArtifactReference(
                VisionPipelineToolFactory.TemplateArtifactRole,
                id,
                VisionPipelineToolFactory.EncodedImageArtifactFormat,
                VisionPipelineToolFactory.EncodedImageArtifactFormatVersion,
                sha256);
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
                rejected = exception.ParamName == "parameters"
                    && exception.Message.Contains(expectedMessage, StringComparison.OrdinalIgnoreCase);
            }

            Require(rejected, $"Invalid pipeline parameter '{expectedMessage}' was not rejected.");
        }

        private sealed class PreprocessingProbe : OpenCvAlgorithmBase
        {
            internal Mat Prepare(bool useRoi, IOpenCVPropertyBase property) => CreatePreprocessedImage(new Rect(4, 4, 20, 20), useRoi, property);
            public override void Run() { }
        }

        private sealed class ThrowingOpenCvTool : OpenCvAlgorithmBase
        {
            public object property = new object();
            private readonly Exception exception;

            internal ThrowingOpenCvTool(Exception exception)
            {
                this.exception = exception;
            }

            public override void Run()
            {
                throw exception;
            }
        }

    }
}

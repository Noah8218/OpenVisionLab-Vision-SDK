using OpenVisionLab.Inspection;
using OpenVisionLab.Vision2D;
using OpenVisionLab.Vision2D.Blob;
using OpenVisionLab.Vision2D.Pipeline;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Result;
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
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Threading;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;
using static OpenVisionLab.Inspection.Smoke.SmokeFixtures;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class Vision2DSmokeSuite
    {
        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase("Smoke numerical assertions reject non-finite comparisons", TestNumericalAssertionBoundary);
            yield return new SmokeCase("Edge learning cache preserves exact settings and fresh-instance equivalence", TestEdgeLearningCachePrecision);
            yield return new SmokeCase("SIFT diagnostics survive success failure and repeated ROI execution", TestSiftExecutionDiagnostics);
            yield return new SmokeCase("Preprocessing releases failed clones and preserves caller images", TestPreprocessingFailureRecovery);
            yield return new SmokeCase("2D affine transform recovers a known matrix and drawings", TestAffineTransformKnownMatrix);
            yield return new SmokeCase("2D affine transform rejects collinear source teaching", TestAffineTransformDegenerateSource);
            yield return new SmokeCase("2D affine transform retains evidence on coverage failure", TestAffineTransformCoverageFailure);
            yield return new SmokeCase("Matching coordinates recover independent points corners and inverse", TestMatchingCoordinatesKnownGeometry);
            yield return new SmokeCase("Matching coordinates use relative reference pose and circular angles", TestMatchingCoordinatesRelativePose);
            yield return new SmokeCase("Matching coordinates retain teaching offset pose and immutable snapshots", TestMatchingCoordinatesTeachingSnapshot);
            yield return new SmokeCase("Matching coordinates reject invalid values and unrepresentable results", TestMatchingCoordinatesInvalidValues);
            yield return new SmokeCase("Matching coordinates preserve local precision and continuous rotated ROI boundaries", TestMatchingCoordinatesPrecision);
            yield return new SmokeCase("Matching coordinates consume successful teaching and global ROI poses and recover after failure", TestMatchingCoordinatesExecution);
            yield return new SmokeCase("Matching coordinates reuse the existing three-point affine image path", TestMatchingCoordinatesAffine);
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
            yield return new SmokeCase("Edge matcher recovers independent pixel locations after ambiguity absence and cancellation", TestEdgeMatcherUniqueRecovery);
            yield return new SmokeCase("Edge matcher global polarity is opt-in and reports the selected state", TestEdgeMatcherGlobalPolarity);
            yield return new SmokeCase("Edge gradient matches full OpenCV bits across batch seams depth stride and threshold boundaries", TestEdgeGradientBits);
            yield return new SmokeCase("Edge gradient cancels active creation and recovers without modifying source buffers", TestEdgeGradientCancellation);
            yield return new SmokeCase("Edge gradient preserves padded subpixel inputs and recovers after threshold rejection", TestEdgeGradientSubpixelRecovery);
            yield return new SmokeCase("Edge gradient preserves independent ROI coordinates and retained result images", TestEdgeGradientMultiRoi);
            yield return new SmokeCase("Edge threshold rejects invalid settings and recovers without stale matches", TestEdgeThresholdInvalidRecovery);
            yield return new SmokeCase("Edge threshold preserves finite boundaries and restored factory settings", TestEdgeThresholdFiniteBoundaries);
            yield return new SmokeCase("MorphologyTool direct Execute transforms a synthetic image", TestMorphologyDirectExecution);
            yield return new SmokeCase("FilterTool direct Execute transforms a synthetic image", TestFilterDirectExecution);
            yield return new SmokeCase("BoxFilter preserves selected borders through direct and factory execution", TestBoxFilterBorderExecution);
            yield return new SmokeCase("ThresholdTool rejects non-finite numeric parameters", TestThresholdRejectsNonFiniteValues);
            yield return new SmokeCase("OpenCV execution errors retain type-based classification", TestExecutionErrorClassification);
            yield return new SmokeCase("EdgeDetectionTool direct Execute finds synthetic edges", TestEdgeDetectionDirectExecution);
            yield return new SmokeCase("Canny color execution preserves gradients recovery and ownership", TestCannyColorExecution);
            yield return new SmokeCase("RotateScaleTool direct Execute applies the requested output size", TestRotateScaleDirectExecution);
            yield return new SmokeCase("LineGaugeTool rejects unsupported image depth explicitly", TestLineGaugeUnsupportedDepth);
            yield return new SmokeCase("2D tool and result disposal release only owned images", TestVisionToolResourceOwnership);
            yield return new SmokeCase("Pipeline runtime honors tool, input, result, and layer ownership", TestVisionPipelineResourceOwnership);
            yield return new SmokeCase("Pose uniqueness rejects same-location rotations without changing legacy spatial matching", TestPoseUniquenessRotations);
            yield return new SmokeCase("Pose uniqueness accepts asymmetric teaching and ignores local and circular angle neighbors", TestPoseUniquenessObservableAngles);
            yield return new SmokeCase("Pose uniqueness rejects competing scales at the same authored center", TestPoseUniquenessScales);
            yield return new SmokeCase("Pose uniqueness validates options and recovers after absence invalid settings and cancellation", TestPoseUniquenessRecovery);
            yield return new SmokeCase("Pose uniqueness revisits the full grid after coarse pyramid and hybrid proposals and retains ROI evidence", TestPoseUniquenessSearchPaths);
            yield return new SmokeCase("Pose uniqueness restores XML settings and owned template before explicit execution", TestPoseUniquenessTeachingReplay);
            yield return new SmokeCase("Pose uniqueness rejects unavailable hybrid comparisons and recovers in a sufficient ROI", TestPoseUniquenessUnverifiedHybrid);
            yield return new SmokeCase("Matching reliability recovers authored rotation scale brightness and polarity", TestMatchingReliabilityTransforms);
            yield return new SmokeCase("Matching reliability replays teaching settings and model after rejection", TestMatchingReliabilityTeachingReplay);
            yield return new SmokeCase("Pipeline reliability preserves the last valid layer after clone failure", TestPipelineReliabilityCloneFailure);
            yield return new SmokeCase("Pipeline reliability preserves owned snapshots through repeated disposal and recovery", TestPipelineReliabilityRepeatedLifetime);
            yield return new SmokeCase("Pipeline routes only non-null images to named output layers", TestVisionPipelineOptionalOutputContract);
            yield return new SmokeCase("Pipeline XML preserves versioned settings and rejects unsupported input", TestVisionPipelineSerialization);
            yield return new SmokeCase("Pipeline failure-result execution classifies infrastructure failures", TestVisionPipelineFailureResults);
            yield return new SmokeCase("Pipeline descriptors cover every non-legacy 2D Tool", TestVisionPipelineDescriptors);
            yield return new SmokeCase("Pipeline factories create all 16 non-legacy 2D Tools", TestVisionPipelineFactoryBuiltIns);
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

        private static void TestMatchingCoordinatesKnownGeometry()
        {
            MatchingCoordinateTransform transform = new MatchingCoordinateTransform(new Point2d(10, 20), new Point2d(100, 200), 90, 2);
            RequireTransformPoint(transform.TransformPoint(new Point2d(13, 24)), new Point2d(108, 194), 1e-12, "Independent 90-degree point");
            RequireTransformPoint(transform.TransformReferenceOffset(new Point2d(3, 4)), new Point2d(108, 194), 1e-12, "Reference offset");
            RequireTransformPoint(transform.InverseTransformPoint(new Point2d(108, 194)), new Point2d(13, 24), 1e-12, "Independent inverse point");
            Point2d[] expected = { new Point2d(98, 204), new Point2d(98, 192), new Point2d(106, 192), new Point2d(106, 204) };
            Point2d[] original = { new Point2d(8, 19), new Point2d(14, 19), new Point2d(14, 23), new Point2d(8, 23) };
            Point2d[] actual = transform.TransformRoi(new Rect2d(8, 19, 6, 4));
            Require(actual.Length == 4, "ROI must retain four corresponding corners.");
            for (int index = 0; index < actual.Length; index++)
            {
                RequireTransformPoint(actual[index], expected[index], 1e-12, "ROI corner " + index);
                RequireTransformPoint(transform.InverseTransformPoint(expected[index]), original[index], 1e-12, "ROI inverse " + index);
            }
        }

        private static void TestMatchingCoordinatesRelativePose()
        {
            MatchingResult reference = new MatchingResult(0, 95, new Point2f(10, 20), new Rect2f(), 30, 2);
            MatchingResult current = new MatchingResult(0, 95, new Point2f(100, 200), new Rect2f(), 120, 3);
            MatchingCoordinateTransform transform = MatchingCoordinateTransform.FromMatches(reference, current);
            RequireApproximately(transform.AngleDegrees, 90, 0, "Relative angle must subtract the reference pose.");
            RequireApproximately(transform.Scale, 1.5, 0, "Relative scale must divide by the reference pose.");
            RequireTransformPoint(transform.TransformPoint(new Point2d(13, 24)), new Point2d(106, 195.5), 1e-12, "Relative pose");
            RequireTransformPoint(transform.TransformPoint(new Point2d(10, 20)), new Point2d(100, 200), 0, "Reference anchor");
            current.Angle = 480;
            RequireTransformPoint(MatchingCoordinateTransform.FromMatches(reference, current).TransformPoint(new Point2d(13, 24)), new Point2d(106, 195.5), 1e-12, "Circular equivalent pose");
            reference.Angle = 359;
            current.Angle = 1;
            RequireApproximately(MatchingCoordinateTransform.FromMatches(reference, current).AngleDegrees, 2, 0, "Circular seam must use a two-degree rotation.");
            reference.Angle = 180;
            current.Angle = -180;
            RequireApproximately(MatchingCoordinateTransform.FromMatches(reference, current).AngleDegrees, 0, 0, "Equivalent half turns must not rotate twice.");
            reference.Angle = 1e308;
            current.Angle = -1e308;
            RequireApproximately(MatchingCoordinateTransform.FromMatches(reference, current).AngleDegrees, 128, 0, "Large finite pose angles must reduce before subtraction.");
            reference.Scale = double.Epsilon;
            current.Scale = double.Epsilon;
            RequireApproximately(MatchingCoordinateTransform.FromMatches(reference, current).Scale, 1, 0, "Finite identical small scales must retain a representable relative scale.");
            reference.Angle = 0;
            current.Angle = 0;
            reference.Scale = 1;
            current.Scale = 1;
            RequireTransformPoint(MatchingCoordinateTransform.FromMatches(reference, reference).TransformPoint(new Point2d(-0.25, 7.5)), new Point2d(-0.25, 7.5), 0, "Identity reference pose");
        }

        private static void TestMatchingCoordinatesTeachingSnapshot()
        {
            AutoMPointCandidateResult teaching = new AutoMPointCandidateResult
            {
                Accepted = true,
                NativeMatchCenter = new Point2f(10, 20),
                NativeMatchAngle = 30,
                NativeMatchScale = 2,
                PatternCenter = new Point2f(13, 24),
                NativeToPatternOffsetX = 3,
                NativeToPatternOffsetY = 4
            };
            MatchingResult current = new MatchingResult(0, 90, new Point2f(100, 200), new Rect2f(), 120, 3);
            MatchingCoordinateTransform transform = MatchingCoordinateTransform.FromAutoMPoint(teaching, current);
            RequireTransformPoint(transform.TransformReferenceOffset(new Point2d(teaching.NativeToPatternOffsetX, teaching.NativeToPatternOffsetY)), new Point2d(106, 195.5), 1e-12, "Authored Teaching datum");
            System.Text.Json.JsonSerializerOptions options = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
            string json = System.Text.Json.JsonSerializer.Serialize(teaching, options);
            AutoMPointCandidateResult restored = System.Text.Json.JsonSerializer.Deserialize<AutoMPointCandidateResult>(json, options);
            RequireTransformPoint(MatchingCoordinateTransform.FromAutoMPoint(restored, current).TransformPoint(new Point2d(13, 24)), new Point2d(106, 195.5), 1e-12, "Caller-owned in-memory Teaching round trip");
            AutoMPointCandidateResult legacy = new AutoMPointCandidateResult { Accepted = true, NativeMatchCenter = new Point2f(10, 20) };
            current.Angle = 90;
            current.Scale = 2;
            RequireTransformPoint(MatchingCoordinateTransform.FromAutoMPoint(legacy, current).TransformPoint(new Point2d(13, 24)), new Point2d(108, 194), 1e-12, "Legacy neutral Teaching defaults");
            teaching.NativeMatchCenter = new Point2f(999, 999);
            teaching.NativeMatchAngle = double.NaN;
            teaching.NativeMatchScale = 0;
            current.Center = new Point2f(0, 0);
            current.Angle = -180;
            current.Scale = 9;
            RequireTransformPoint(transform.TransformPoint(new Point2d(13, 24)), new Point2d(106, 195.5), 1e-12, "Immutable input snapshot");
            Rect2d roi = new Rect2d(8, 19, 6, 4);
            Point2d[] first = transform.TransformRoi(roi);
            first[0] = new Point2d(-999, -999);
            RequireTransformPoint(transform.TransformRoi(roi)[0], new Point2d(98.5, 203), 1e-12, "Fresh ROI corner array");
            Require(typeof(MatchingCoordinateTransform).GetProperties().All(property => !property.CanWrite), "Mapping must not expose mutable properties.");
            teaching.Accepted = false;
            RequireThrows<InvalidOperationException>(() => MatchingCoordinateTransform.FromAutoMPoint(teaching, current));
        }

        private static void TestMatchingCoordinatesInvalidValues()
        {
            MatchingResult reference = new MatchingResult(0, 95, new Point2f(), new Rect2f());
            MatchingResult current = new MatchingResult(0, 95, new Point2f(), new Rect2f());
            MatchingCoordinateTransform transform = MatchingCoordinateTransform.FromMatches(reference, current);
            RequireThrows<ArgumentNullException>(() => MatchingCoordinateTransform.FromMatches(null, current));
            RequireThrows<ArgumentNullException>(() => MatchingCoordinateTransform.FromMatches(reference, null));
            RequireThrows<ArgumentNullException>(() => MatchingCoordinateTransform.FromAutoMPoint(null, current));
            RequireThrows<ArgumentNullException>(() => MatchingCoordinateTransform.FromAutoMPoint(new AutoMPointCandidateResult(), null));
            foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                RequireThrows<ArgumentOutOfRangeException>(() => _ = new MatchingCoordinateTransform(new Point2d(invalid, 0), new Point2d(), 0, 1));
                RequireThrows<ArgumentOutOfRangeException>(() => _ = new MatchingCoordinateTransform(new Point2d(), new Point2d(0, invalid), 0, 1));
                RequireThrows<ArgumentOutOfRangeException>(() => _ = new MatchingCoordinateTransform(new Point2d(), new Point2d(), invalid, 1));
                RequireThrows<ArgumentOutOfRangeException>(() => _ = new MatchingCoordinateTransform(new Point2d(), new Point2d(), 0, invalid));
                RequireThrows<ArgumentOutOfRangeException>(() => transform.TransformPoint(new Point2d(invalid, 0)));
                RequireThrows<ArgumentOutOfRangeException>(() => transform.TransformReferenceOffset(new Point2d(0, invalid)));
                RequireThrows<ArgumentOutOfRangeException>(() => transform.InverseTransformPoint(new Point2d(0, invalid)));
                RequireThrows<ArgumentOutOfRangeException>(() => transform.TransformRoi(new Rect2d(invalid, 0, 1, 1)));
                reference.Angle = invalid;
                RequireThrows<ArgumentOutOfRangeException>(() => MatchingCoordinateTransform.FromMatches(reference, current));
                reference.Angle = 0;
                current.Angle = invalid;
                RequireThrows<ArgumentOutOfRangeException>(() => MatchingCoordinateTransform.FromMatches(reference, current));
                current.Angle = 0;
                reference.Scale = invalid;
                RequireThrows<ArgumentOutOfRangeException>(() => MatchingCoordinateTransform.FromMatches(reference, current));
                reference.Scale = 1;
                current.Scale = invalid;
                RequireThrows<ArgumentOutOfRangeException>(() => MatchingCoordinateTransform.FromMatches(reference, current));
                current.Scale = 1;
            }
            foreach (double invalid in new[] { 0d, -1d, double.Epsilon })
            {
                RequireThrows<ArgumentOutOfRangeException>(() => _ = new MatchingCoordinateTransform(new Point2d(), new Point2d(), 0, invalid));
            }
            foreach (double invalid in new[] { 0d, -1d })
            {
                reference.Scale = invalid;
                RequireThrows<ArgumentOutOfRangeException>(() => MatchingCoordinateTransform.FromMatches(reference, current));
                reference.Scale = 1;
                current.Scale = invalid;
                RequireThrows<ArgumentOutOfRangeException>(() => MatchingCoordinateTransform.FromMatches(reference, current));
                current.Scale = 1;
                AutoMPointCandidateResult invalidTeaching = new AutoMPointCandidateResult { Accepted = true, NativeMatchScale = invalid };
                RequireThrows<ArgumentOutOfRangeException>(() => MatchingCoordinateTransform.FromAutoMPoint(invalidTeaching, current));
            }
            RequireThrows<ArgumentOutOfRangeException>(() => MatchingCoordinateTransform.FromAutoMPoint(new AutoMPointCandidateResult { Accepted = true, NativeMatchAngle = double.NaN }, current));
            current.Center = new Point2f(float.NaN, 0);
            RequireThrows<ArgumentOutOfRangeException>(() => MatchingCoordinateTransform.FromMatches(reference, current));
            current.Center = new Point2f();
            foreach (Rect2d invalid in new[]
            {
                new Rect2d(), new Rect2d(0, 0, -1, 1), new Rect2d(0, 0, 1, -1),
                new Rect2d(0, double.NaN, 1, 1), new Rect2d(0, 0, double.PositiveInfinity, 1), new Rect2d(0, 0, 1, double.NaN),
                new Rect2d(double.MaxValue, 0, double.MaxValue, 1), new Rect2d(0, double.MaxValue, 1, double.MaxValue), new Rect2d(1e20, 0, 1, 1)
            })
            {
                RequireThrows<ArgumentOutOfRangeException>(() => transform.TransformRoi(invalid));
            }
            reference.Scale = double.Epsilon;
            current.Scale = double.MaxValue;
            RequireThrows<ArgumentOutOfRangeException>(() => MatchingCoordinateTransform.FromMatches(reference, current));
            reference.Scale = double.MaxValue;
            current.Scale = double.Epsilon;
            RequireThrows<ArgumentOutOfRangeException>(() => MatchingCoordinateTransform.FromMatches(reference, current));
            MatchingCoordinateTransform largeScale = new MatchingCoordinateTransform(new Point2d(), new Point2d(), 0, double.MaxValue);
            RequireThrows<OverflowException>(() => largeScale.TransformPoint(new Point2d(2, 0)));
            MatchingCoordinateTransform smallScale = new MatchingCoordinateTransform(new Point2d(), new Point2d(), 0, 1e-308);
            RequireThrows<OverflowException>(() => smallScale.InverseTransformPoint(new Point2d(2, 0)));
            MatchingCoordinateTransform distant = new MatchingCoordinateTransform(new Point2d(double.MaxValue, 0), new Point2d(), 0, 1);
            RequireThrows<OverflowException>(() => distant.TransformPoint(new Point2d(-double.MaxValue, 0)));
            RequireTransformPoint(transform.TransformPoint(new Point2d(3, 4)), new Point2d(3, 4), 0, "Valid recovery after all rejected inputs");
        }

        private static void TestMatchingCoordinatesPrecision()
        {
            MatchingCoordinateTransform largeOrigin = new MatchingCoordinateTransform(new Point2d(1e12, -1e12), new Point2d(2e12, 3e12), 90, 2);
            RequireTransformPoint(largeOrigin.TransformPoint(new Point2d(1e12 + 3, -1e12 + 4)), new Point2d(2e12 + 8, 3e12 - 6), 0, "Large-origin local mapping");
            RequireTransformPoint(largeOrigin.InverseTransformPoint(new Point2d(2e12 + 8, 3e12 - 6)), new Point2d(1e12 + 3, -1e12 + 4), 0, "Large-origin inverse mapping");
            MatchingCoordinateTransform diagonal = new MatchingCoordinateTransform(new Point2d(), new Point2d(), 45, Math.Sqrt(2));
            RequireTransformPoint(diagonal.TransformPoint(new Point2d(1.5, -2.5)), new Point2d(-1, -4), 1e-12, "Independent diagonal coefficients");
            RequireTransformPoint(diagonal.InverseTransformPoint(new Point2d(-1, -4)), new Point2d(1.5, -2.5), 1e-12, "Diagonal inverse");
            Point2d[] expected = { new Point2d(-3.75, -1.25), new Point2d(-2.75, -2.25), new Point2d(0.25, 0.75), new Point2d(-0.75, 1.75) };
            Point2d[] actual = diagonal.TransformRoi(new Rect2d(-1.25, -2.5, 1, 3));
            for (int index = 0; index < expected.Length; index++) RequireTransformPoint(actual[index], expected[index], 1e-12, "Continuous rotated boundary " + index);
            MatchingCoordinateTransform identity = new MatchingCoordinateTransform(new Point2d(), new Point2d(), 0, 1);
            Point2d[] unit = identity.TransformRoi(new Rect2d(-0.25, 0.5, 1, 1));
            RequireTransformPoint(unit[2], new Point2d(0.75, 1.5), 0, "One-pixel ROI retains its continuous extent");
        }

        private static void TestMatchingCoordinatesExecution()
        {
            using Mat reference = CreateAutoMPointUniqueSource();
            using AutoMPointTool teacher = new AutoMPointTool();
            AutoMPointToolProperty definition = CreateAutoMPointProperty(new Rect(0, 0, reference.Width, reference.Height), 64, 64, 32);
            definition.MaximumResults = 1;
            teacher.SetProperty(definition);
            using VisionToolResult taught = teacher.Execute(reference);
            Require(taught.Success && teacher.results.Count == 1, "Single Teaching ROI must produce one accepted candidate. " + taught.Message);
            AutoMPointCandidateResult teaching = teacher.results[0];
            Require(teaching.PatternRoi == new Rect(64, 64, 64, 64), "Teaching must select the independently authored reference ROI.");
            RequireApproximately(teaching.NativeMatchAngle, 0, 0, "Teaching must preserve the self-match angle.");
            RequireApproximately(teaching.NativeMatchScale, 1, 0, "Teaching must preserve the self-match scale.");
            using Mat template = new Mat(reference, teaching.PatternRoi).Clone();
            MatchingCoordinateTransform retained = null;
            foreach ((int angle, double scale) in new[] { (0, 1d), (90, 1d), (180, 1.25d) })
            {
                using Mat target = new Mat((int)(64 * scale), (int)(64 * scale), MatType.CV_8UC1);
                for (int row = 0; row < target.Rows; row++)
                {
                    for (int column = 0; column < target.Cols; column++)
                    {
                        int x = Math.Min(63, (int)(column / scale));
                        int y = Math.Min(63, (int)(row / scale));
                        (int sourceX, int sourceY) = angle == 90 ? (63 - y, x) : angle == 180 ? (63 - x, 63 - y) : (x, y);
                        target.Set(row, column, template.At<byte>(sourceY, sourceX));
                    }
                }
                using Mat scene = new Mat(128, 160, MatType.CV_8UC1, Scalar.All(24));
                using (Mat destination = new Mat(scene, new Rect(43, 29, target.Cols, target.Rows))) target.CopyTo(destination);
                using EdgeBasedTemplateMatchingTool matcher = CreateEdgeMatcher(template, true);
                matcher.UsePoseUniquenessValidation = true;
                matcher.property.CONTOUR_RETRIEVAL_MODE = RetrievalModes.List;
                matcher.property.CONTOUR_APPROXIMATION_MODE = ContourApproximationModes.ApproxNone;
                matcher.property.USE_FIND_ANGLE = true;
                matcher.property.FIND_ANGLE_MIN = 0;
                matcher.property.FIND_ANGLE_MAX = 180;
                matcher.property.FIND_ANGLE = 90;
                matcher.property.USE_FIND_SCALE = true;
                matcher.property.FIND_SCALE_MIN = 1;
                matcher.property.FIND_SCALE_MAX = 1.25;
                matcher.property.FIND_SCALE_STEP = 0.25;
                matcher.property.USE_ROI = true;
                matcher.property.CvROI = new Rect(20, 10, 120, 110);
                using VisionToolResult result = matcher.Execute(scene);
                Require(result.Success && matcher.results.Count == 1, "Authored coordinate scene must match. " + angle + "/" + scale + ": " + result.Message);
                MatchingCoordinateTransform mapping = MatchingCoordinateTransform.FromAutoMPoint(teaching, matcher.results[0]);
                Point2d point = mapping.TransformPoint(new Point2d(103, 91));
                Point2d expected = angle == 90 ? new Point2d(70, 54) : angle == 180 ? new Point2d(74.25, 75.25) : new Point2d(82, 56);
                RequireTransformPoint(point, expected, 1.5, "Independent authored datum angle=" + angle);
                RequireTransformPoint(mapping.TransformReferenceOffset(new Point2d(teaching.NativeToPatternOffsetX, teaching.NativeToPatternOffsetY)),
                    new Point2d(43 + target.Cols / 2d, 29 + target.Rows / 2d), 1.5, "Authored pattern datum with one global ROI offset");
                RequireApproximately(mapping.AngleDegrees, angle == 180 ? 180 : angle, 1e-9, "Actual coordinate angle changed.");
                RequireApproximately(mapping.Scale, scale, 1e-9, "Actual coordinate scale changed.");
                RequireTransformPoint(mapping.InverseTransformPoint(point), new Point2d(103, 91), 1e-9, "Actual pose inverse");
                Point2d[] expectedCorners = angle == 90
                    ? new[] { new Point2d(71, 67), new Point2d(71, 58), new Point2d(78, 58), new Point2d(78, 67) }
                    : angle == 180
                        ? new[] { new Point2d(90.5, 74), new Point2d(79.25, 74), new Point2d(79.25, 65.25), new Point2d(90.5, 65.25) }
                        : new[] { new Point2d(69, 57), new Point2d(78, 57), new Point2d(78, 64), new Point2d(69, 64) };
                Point2d[] corners = mapping.TransformRoi(new Rect2d(90, 92, 9, 7));
                for (int index = 0; index < corners.Length; index++) RequireTransformPoint(corners[index], expectedCorners[index], 1.5, "Authored scene ROI corner " + index);
                Console.WriteLine(FormattableString.Invariant($"COORD | authored angle={angle} scale={scale}; native=({matcher.results[0].Center.X:R},{matcher.results[0].Center.Y:R}); datum=({point.X:R},{point.Y:R}); expected=({expected.X:R},{expected.Y:R}); teaching=({teaching.NativeMatchAngle:R},{teaching.NativeMatchScale:R})"));
                retained = mapping;

                using Mat repeated = new Mat(scene.Size(), MatType.CV_8UC1, Scalar.All(24));
                using (Mat first = new Mat(repeated, new Rect(10, 29, 64, 64))) template.CopyTo(first);
                using (Mat second = new Mat(repeated, new Rect(85, 29, 64, 64))) template.CopyTo(second);
                matcher.property.USE_ROI = false;
                using (VisionToolResult ambiguous = matcher.Execute(repeated))
                {
                    MatchingCoordinateTransform published = ambiguous.Success ? MatchingCoordinateTransform.FromAutoMPoint(teaching, matcher.results[0]) : null;
                    Require(ambiguous.ErrorCode == VisionToolErrorCode.MatchingAmbiguous && published == null, "Host must not publish coordinates for repeated ambiguous targets.");
                }
                matcher.property.USE_ROI = true;
                using Mat absent = new Mat(scene.Size(), MatType.CV_8UC1, Scalar.All(24));
                using (VisionToolResult missing = matcher.Execute(absent))
                {
                    MatchingCoordinateTransform published = missing.Success ? MatchingCoordinateTransform.FromAutoMPoint(teaching, matcher.results[0]) : null;
                    Require(missing.ErrorCode == VisionToolErrorCode.MatchingNoResult && published == null, "Host must not publish coordinates for an absent target.");
                }
                using (VisionToolResult recovered = matcher.Execute(scene)) Require(recovered.Success, "Matching must recover after absence.");
                matcher.PoseMatchMinScoreMargin = double.NaN;
                using (VisionToolResult invalid = matcher.Execute(scene))
                {
                    MatchingCoordinateTransform published = invalid.Success ? MatchingCoordinateTransform.FromAutoMPoint(teaching, matcher.results[0]) : null;
                    Require(invalid.ErrorCode == VisionToolErrorCode.InvalidParameter && published == null, "A previous row must not bypass the current execution failure.");
                }
                matcher.PoseMatchMinScoreMargin = 0.03;
                using CancellationTokenSource cancellation = new CancellationTokenSource();
                cancellation.Cancel();
                RequireThrows<OperationCanceledException>(() => matcher.Execute(scene, cancellation.Token));
                using VisionToolResult final = matcher.Execute(scene);
                Require(final.Success, "Current valid matching must recover after invalid settings and cancellation.");
                RequireTransformPoint(MatchingCoordinateTransform.FromAutoMPoint(teaching, matcher.results[0]).TransformPoint(new Point2d(103, 91)), point, 0, "Deterministic recovery coordinates");
            }
            Require(retained != null && double.IsFinite(retained.TransformPoint(new Point2d(103, 91)).X), "Mapping must remain usable after its producing matching tool was disposed.");
        }

        private static void TestMatchingCoordinatesAffine()
        {
            MatchingCoordinateTransform mapping = new MatchingCoordinateTransform(new Point2d(10, 20), new Point2d(60, 80), 90, 2);
            Point2d p1 = mapping.TransformPoint(new Point2d(10, 20));
            Point2d p2 = mapping.TransformPoint(new Point2d(30, 20));
            Point2d p3 = mapping.TransformPoint(new Point2d(10, 40));
            using Mat source = new Mat(64, 64, MatType.CV_8UC1, Scalar.All(0));
            source.Set(24, 13, (byte)255);
            using AffineTransformTool affine = new AffineTransformTool();
            affine.SetProperty(new AffineTransformToolProperty
            {
                SourcePoint1X = 10,
                SourcePoint1Y = 20,
                SourcePoint2X = 30,
                SourcePoint2Y = 20,
                SourcePoint3X = 10,
                SourcePoint3Y = 40,
                DestinationPoint1X = p1.X,
                DestinationPoint1Y = p1.Y,
                DestinationPoint2X = p2.X,
                DestinationPoint2Y = p2.Y,
                DestinationPoint3X = p3.X,
                DestinationPoint3Y = p3.Y,
                OutputWidth = 128,
                OutputHeight = 128,
                Interpolation = InterpolationFlags.Nearest
            });
            using VisionToolResult result = affine.Execute(source);
            Require(result.Success, "Coordinate correspondences must run through the existing affine Tool. " + result.Message);
            RequireApproximately(result.Metrics["AffineM11"], 0, 1e-6, "Independent affine M11.");
            RequireApproximately(result.Metrics["AffineM12"], 2, 1e-6, "Independent affine M12.");
            RequireApproximately(result.Metrics["AffineM13"], 20, 1e-6, "Independent affine M13.");
            RequireApproximately(result.Metrics["AffineM21"], -2, 1e-6, "Independent affine M21.");
            RequireApproximately(result.Metrics["AffineM22"], 0, 1e-6, "Independent affine M22.");
            RequireApproximately(result.Metrics["AffineM23"], 100, 1e-6, "Independent affine M23.");
            Require(result.ResultImage.At<byte>(74, 68) == 255, "Authored pixel (13,24) must reach (68,74).");
            Require(result.Metrics["AffineValidPixelRatio"] > 0 && result.Overlays.Count == 10, "Existing coverage and drawing owner must remain active.");
        }

        private static void RequireTransformPoint(Point2d actual, Point2d expected, double tolerance, string label)
        {
            RequireApproximately(actual.X, expected.X, tolerance, label + " X");
            RequireApproximately(actual.Y, expected.Y, tolerance, label + " Y");
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

        private static void TestEdgeMatcherUniqueRecovery()
        {
            using (Mat reference = CreateAutoMPointRepeatedSource())
            using (Mat template = new Mat(reference, new Rect(0, 0, 64, 64)).Clone())
            using (Mat firstSource = new Mat(new Size(192, 128), MatType.CV_8UC1, Scalar.All(24)))
            using (Mat ambiguousSource = new Mat(firstSource.Size(), MatType.CV_8UC1, Scalar.All(24)))
            using (Mat emptySource = new Mat(firstSource.Size(), MatType.CV_8UC1, Scalar.All(24)))
            using (Mat recoveredSource = new Mat(firstSource.Size(), MatType.CV_8UC1, Scalar.All(24)))
            using (EdgeBasedTemplateMatchingTool tool = CreateEdgeMatcher(template, true))
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                Rect firstLocation = new Rect(73, 31, template.Width, template.Height);
                Rect recoveredLocation = new Rect(91, 49, template.Width, template.Height);
                using (Mat region = new Mat(firstSource, firstLocation)) template.CopyTo(region);
                using (Mat region = new Mat(recoveredSource, recoveredLocation)) template.CopyTo(region);
                using (Mat region = new Mat(ambiguousSource, new Rect(7, 11, template.Width, template.Height))) template.CopyTo(region);
                using (Mat region = new Mat(ambiguousSource, new Rect(103, 11, template.Width, template.Height))) template.CopyTo(region);

                tool.property.USE_ROI = true;
                tool.property.CvROI = new Rect(51, 17, 137, 108);
                using (VisionToolResult first = tool.Execute(firstSource))
                {
                    Require(first.Success && tool.results.Count == 1, "A single independently placed pattern must produce one unique location.");
                    RequireApproximately(tool.results[0].Bounding.X, firstLocation.X, 1.1, "The translated pattern's absolute X coordinate changed.");
                    RequireApproximately(tool.results[0].Bounding.Y, firstLocation.Y, 1.1, "The translated pattern's absolute Y coordinate changed.");
                    Require(first.EdgeBasedMatchingDiagnostics.SearchRoi.X == 51 && first.EdgeBasedMatchingDiagnostics.SearchRoi.Y == 17,
                        "The decision evidence must retain the actual offset search ROI.");
                }

                tool.property.USE_ROI = false;
                using (VisionToolResult ambiguous = tool.Execute(ambiguousSource))
                {
                    Require(!ambiguous.Success && ambiguous.ErrorCode == VisionToolErrorCode.MatchingAmbiguous && tool.results.Count == 0,
                        "An ambiguous execution must reject both candidates and clear the preceding accepted location.");
                    Require(ambiguous.EdgeBasedMatchingDiagnostics.State == "Ambiguous"
                        && ambiguous.EdgeBasedMatchingDiagnostics.StrongestSpatialAlternative != null,
                        "The failed decision must expose the competing location.");
                }
                using (VisionToolResult empty = tool.Execute(emptySource))
                {
                    Require(!empty.Success && empty.ErrorCode == VisionToolErrorCode.MatchingNoResult && tool.results.Count == 0,
                        "An absent target must reject without reusing the preceding ambiguous or accepted output.");
                    Require(empty.EdgeBasedMatchingDiagnostics.State == "NoMatch"
                        && empty.Metrics["UniqueMatch.PlausibleAlternativeCount"] == 0D
                        && (empty.EdgeBasedMatchingDiagnostics.StrongestSpatialAlternative == null
                            || empty.EdgeBasedMatchingDiagnostics.StrongestSpatialAlternative.Score < tool.property.SCORE_MIN),
                        "No-match evidence may retain a weak current-image candidate but must not retain a plausible competitor from the preceding image.");
                }

                cancellation.Cancel();
                OperationCanceledException canceled = RequireThrows<OperationCanceledException>(() => tool.Execute(recoveredSource, cancellation.Token));
                Require(canceled.CancellationToken == cancellation.Token, "Cancellation must retain the caller's token.");
                using (VisionToolResult recovered = tool.Execute(recoveredSource))
                {
                    Require(recovered.Success && tool.results.Count == 1 && recovered.Metrics["UniqueMatch.State"] == 2D,
                        "A valid input after cancellation must restore a unique current result.");
                    RequireApproximately(tool.results[0].Bounding.X, recoveredLocation.X, 1.1, "Recovery reused the preceding pattern's X location.");
                    RequireApproximately(tool.results[0].Bounding.Y, recoveredLocation.Y, 1.1, "Recovery reused the preceding pattern's Y location.");
                }
                Require(!firstSource.IsDisposed && !ambiguousSource.IsDisposed && !emptySource.IsDisposed && !recoveredSource.IsDisposed,
                    "The matcher must preserve caller-owned inputs throughout acceptance rejection cancellation and recovery.");
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

        private static void TestEdgeGradientBits()
        {
            Type gradientType = typeof(EdgeBasedTemplateMatchingTool).GetNestedType("GradientImage", BindingFlags.NonPublic);
            MethodInfo create = gradientType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
            PropertyInfo unitX = gradientType.GetProperty("UnitDxValues"), unitY = gradientType.GetProperty("UnitDyValues");
            PropertyInfo retained = gradientType.GetProperty("MagnitudeValues", BindingFlags.Instance | BindingFlags.NonPublic);
            MatType[] depths = { MatType.CV_8UC1, MatType.CV_16UC1, MatType.CV_32FC1, MatType.CV_64FC1 };
            Size[] sizes = { new Size(1, 1), new Size(1, 63), new Size(67, 1), new Size(65, 73), new Size(1025, 1030) };
            foreach (MatType depth in depths)
            foreach (Size size in sizes)
            foreach (bool padded in new[] { false, true })
            {
                using Mat bytes = new Mat(size.Height + 4, size.Width + 8, MatType.CV_8UC1);
                byte[] row = new byte[bytes.Cols];
                for (int y = 0; y < bytes.Rows; y++)
                {
                    for (int x = 0; x < row.Length; x++) row[x] = (byte)((x * 17 + y * 31 + (x * y) % 73) % 256);
                    Marshal.Copy(row, 0, bytes.Ptr(y), row.Length);
                }
                using Mat parent = new Mat();
                bytes.ConvertTo(parent, depth, depth == MatType.CV_8UC1 ? 1D : 1D / 3D);
                using Mat roi = new Mat(parent, new Rect(3, 2, size.Width, size.Height));
                using Mat source = padded ? new Mat(roi, new Rect(0, 0, roi.Cols, roi.Rows)) : roi.Clone();
                using Mat before = parent.Clone();
                using Mat dx = new Mat(); using Mat dy = new Mat(); using Mat magnitude = new Mat();
                Cv2.Sobel(source, dx, MatType.CV_64F, 1, 0, 3);
                Cv2.Sobel(source, dy, MatType.CV_64F, 0, 1, 3);
                Cv2.Magnitude(dx, dy, magnitude);
                dx.GetArray(out double[] expectedX); dy.GetArray(out double[] expectedY); magnitude.GetArray(out double[] expectedMagnitude);
                double boundary = expectedMagnitude.FirstOrDefault(value => value > 0D);
                double[] thresholds = { 0D, 5D, boundary, boundary > 0D ? Math.BitDecrement(boundary) : 0D };
                foreach (double threshold in thresholds)
                foreach (bool retainMagnitude in new[] { false, true })
                {
                    using IDisposable gradients = (IDisposable)create.Invoke(null, new object[] { source, threshold, CancellationToken.None, retainMagnitude });
                    double[] actualX = (double[])unitX.GetValue(gradients), actualY = (double[])unitY.GetValue(gradients);
                    double[] actualMagnitude = (double[])retained.GetValue(gradients);
                    Require(actualX.Length == expectedMagnitude.Length && actualY.Length == expectedMagnitude.Length
                        && actualMagnitude.Length == (retainMagnitude ? expectedMagnitude.Length : 0), "Gradient buffers must keep their original public-caller shape.");
                    for (int i = 0; i < expectedMagnitude.Length; i++)
                    {
                        double value = expectedMagnitude[i];
                        double inverse = 1D / value;
                        double x = value <= threshold ? 0D : expectedX[i] * inverse;
                        double y = value <= threshold ? 0D : expectedY[i] * inverse;
                        if (BitConverter.DoubleToInt64Bits(actualX[i]) != BitConverter.DoubleToInt64Bits(x)
                            || BitConverter.DoubleToInt64Bits(actualY[i]) != BitConverter.DoubleToInt64Bits(y)
                            || (retainMagnitude && BitConverter.DoubleToInt64Bits(actualMagnitude[i]) != BitConverter.DoubleToInt64Bits(value)))
                            throw new InvalidOperationException("Gradient IEEE bits differ: " + depth + " " + size + " padded=" + padded + " threshold=" + threshold + " index=" + i);
                    }
                }
                Require(Cv2.Norm(parent, before, NormTypes.L1) == 0D, "Gradient creation must preserve caller pixels and padding.");
            }
        }

        private static void TestEdgeGradientCancellation()
        {
            Type gradientType = typeof(EdgeBasedTemplateMatchingTool).GetNestedType("GradientImage", BindingFlags.NonPublic);
            MethodInfo create = gradientType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
            using Mat source = new Mat(4096, 4096, MatType.CV_8UC1, Scalar.All(17));
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            using ManualResetEventSlim started = new ManualResetEventSlim();
            Task work = Task.Run(() =>
            {
                started.Set();
                using IDisposable gradients = (IDisposable)create.Invoke(null, new object[] { source, 5D, cancellation.Token, false });
            });
            started.Wait();
            cancellation.CancelAfter(20);
            TargetInvocationException error = RequireThrows<TargetInvocationException>(() => work.GetAwaiter().GetResult());
            Require(error.InnerException is OperationCanceledException canceled && canceled.CancellationToken == cancellation.Token,
                "Active gradient creation must propagate the caller cancellation token.");
            using Mat recovery = new Mat(13, 17, MatType.CV_8UC1, Scalar.All(31));
            using IDisposable recovered = (IDisposable)create.Invoke(null, new object[] { recovery, 5D, CancellationToken.None, false });
            double[] values = (double[])gradientType.GetProperty("UnitDxValues").GetValue(recovered);
            Cv2.MinMaxLoc(source, out double sourceMinimum, out double sourceMaximum);
            Cv2.MinMaxLoc(recovery, out double recoveryMinimum, out double recoveryMaximum);
            Require(values.All(value => BitConverter.DoubleToInt64Bits(value) == 0L)
                && sourceMinimum == 17D && sourceMaximum == 17D && recoveryMinimum == 31D && recoveryMaximum == 31D,
                "Cancellation must preserve caller pixels and allow zero-gradient retry results.");
        }

        private static void TestEdgeGradientSubpixelRecovery()
        {
            using Mat reference = CreateAutoMPointUniqueSource();
            using Mat template = new Mat(reference, new Rect(60, 60, 64, 64)).Clone();
            using Mat padded = new Mat(147, 211, MatType.CV_8UC1, Scalar.All(24));
            using Mat source = new Mat(padded, new Rect(3, 2, 197, 137));
            using (Mat destination = new Mat(source, new Rect(37, 27, 64, 64))) template.CopyTo(destination);
            using Mat translation = new Mat(2, 3, MatType.CV_64FC1, new double[] { 1, 0, 0.375, 0, 1, 0.25 });
            using Mat shifted = new Mat();
            Cv2.WarpAffine(source, shifted, translation, source.Size(), InterpolationFlags.Linear, BorderTypes.Constant, Scalar.All(24));
            shifted.CopyTo(source);
            using Mat original = padded.Clone();
            using Mat blank = new Mat(source.Size(), MatType.CV_8UC1, Scalar.All(24));
            using EdgeBasedTemplateMatchingTool tool = CreateEdgeMatcher(template, true);
            Require(!source.IsContinuous(), "The fixture must exercise a padded caller image.");
            tool.property.USE_SUBPIXEL_REFINE = false;
            using VisionToolResult integer = tool.Execute(source);
            Require(integer.Success && tool.results.Count == 1, "Integer baseline must find the authored target: " + integer.Message);
            Point2f integerCenter = tool.results[0].Center;
            tool.property.USE_SUBPIXEL_REFINE = true;
            using VisionToolResult retained = tool.Execute(source);
            Require(retained.Success && tool.results.Count == 1, "Subpixel path must find the authored target: " + retained.Message);
            Point2f subpixelCenter = tool.results[0].Center;
            Require(Math.Abs(subpixelCenter.X - integerCenter.X) > 0.00001 || Math.Abs(subpixelCenter.Y - integerCenter.Y) > 0.00001,
                "The fractional fixture must exercise a nonzero subpixel correction.");
            for (int thresholdIndex = 0; thresholdIndex < 3; thresholdIndex++)
            {
                tool.property.MIN_GRADIENT_MAGNITUDE = thresholdIndex == 0 ? 0 : thresholdIndex == 1 ? 5 : 64;
                using VisionToolResult valid = tool.Execute(source);
                Require(valid.Success && tool.results.Count == 1, "Finite threshold must recover the authored target: " + valid.Message);
                RequireApproximately(tool.results[0].Center.X, 69.375, 1.5, "Fractional authored center X changed.");
                RequireApproximately(tool.results[0].Center.Y, 59.25, 1.5, "Fractional authored center Y changed.");
                using VisionToolResult absent = tool.Execute(blank);
                Require(!absent.Success && absent.ErrorCode == VisionToolErrorCode.MatchingNoResult && tool.results.Count == 0,
                    "Zero-gradient source must report no match without stale rows.");
            }

            tool.property.MIN_GRADIENT_MAGNITUDE = 1000000;
            using VisionToolResult unusable = tool.Execute(source);
            Require(!unusable.Success && unusable.ErrorCode == VisionToolErrorCode.MatchingTemplateInvalid && tool.results.Count == 0,
                "A template with every magnitude below threshold must reject its model.");
            tool.property.MIN_GRADIENT_MAGNITUDE = 5;
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            bool canceled = false;
            try { using VisionToolResult unused = tool.Execute(source, cancellation.Token); }
            catch (OperationCanceledException exception) { canceled = exception.CancellationToken == cancellation.Token; }
            Require(canceled, "Cancellation must preserve the caller token.");
            using VisionToolResult recovered = tool.Execute(source);
            Require(recovered.Success && tool.results.Count == 1, "Threshold rejection and cancellation must permit recovery: " + recovered.Message);
            RequireApproximately(tool.results[0].Center.X, subpixelCenter.X, 0, "Recovered subpixel X changed.");
            RequireApproximately(tool.results[0].Center.Y, subpixelCenter.Y, 0, "Recovered subpixel Y changed.");
            tool.Dispose();
            Require(!retained.ResultImage.IsDisposed && !recovered.ResultImage.IsDisposed && retained.ResultImage.Data != source.Data,
                "Result snapshots must remain owned and readable after Tool disposal.");
            Require(Cv2.Norm(padded, original, NormTypes.L1) == 0, "Execution must preserve caller pixels and padding.");
        }

        private static void TestEdgeGradientMultiRoi()
        {
            using Mat reference = CreateAutoMPointUniqueSource();
            using Mat template = new Mat(reference, new Rect(60, 60, 64, 64)).Clone();
            using Mat source = new Mat(137, 197, MatType.CV_8UC1, Scalar.All(24));
            using (Mat first = new Mat(source, new Rect(37, 27, 64, 64))) template.CopyTo(first);
            using (Mat second = new Mat(source, new Rect(119, 59, 64, 64))) template.CopyTo(second);
            using Mat original = source.Clone();
            using EdgeBasedTemplateMatchingTool tool = CreateEdgeMatcher(template, false);
            tool.property.USE_MULTI_ROI = true;
            tool.property.CvROIS = new List<Rect> { new Rect(23, 13, 78, 78), new Rect(105, 45, 78, 78) };
            using VisionToolResult retained = tool.Execute(source);
            Require(retained.Success && tool.results.Count == 2, "Each search ROI must produce its authored result: " + retained.Message);
            MatchingResult[] ordered = tool.results.OrderBy(match => match.Center.X).ToArray();
            RequireApproximately(ordered[0].Center.X, 69, 1.1, "First ROI global center X changed.");
            RequireApproximately(ordered[0].Center.Y, 59, 1.1, "First ROI global center Y changed.");
            RequireApproximately(ordered[1].Center.X, 151, 1.1, "Second ROI global center X changed.");
            RequireApproximately(ordered[1].Center.Y, 91, 1.1, "Second ROI global center Y changed.");
            tool.property.CvROIS.Reverse();
            using VisionToolResult reversed = tool.Execute(source);
            Require(reversed.Success && tool.results.Count == 2, "Reordered ROI execution must retain both results.");
            RequireApproximately(tool.results[0].Center.X, ordered[1].Center.X, 0, "A later ROI must not reuse the previous gradient plane.");
            RequireApproximately(tool.results[1].Center.X, ordered[0].Center.X, 0, "Reordered ROI must retain independent coordinates.");
            tool.Dispose();
            Require(!retained.ResultImage.IsDisposed && !reversed.ResultImage.IsDisposed && Cv2.Norm(source, original, NormTypes.L1) == 0,
                "Repeated ROI execution/disposal must preserve retained results and caller pixels.");
        }


        private static void TestEdgeLearningCachePrecision()
        {
            using Mat template = new Mat(48, 48, MatType.CV_8UC1, Scalar.All(17));
            Cv2.Rectangle(template, new Rect(9, 9, 29, 24), Scalar.All(230), -1);
            Cv2.Circle(template, new Point(19, 18), 5, Scalar.All(65), -1);
            Cv2.Line(template, new Point(15, 28), new Point(32, 20), Scalar.All(120), 2);
            using Mat edges = new Mat();
            using Mat dx = new Mat();
            using Mat dy = new Mat();
            Cv2.Canny(template, edges, 30, 90, 3, true);
            Cv2.Sobel(template, dx, MatType.CV_64F, 1, 0, 3);
            Cv2.Sobel(template, dy, MatType.CV_64F, 0, 1, 3);
            Cv2.FindContours(edges, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxNone);
            double[] magnitudes = contours.SelectMany(contour => contour).Distinct().Select(point =>
                Math.Sqrt(Math.Pow(dx.At<double>(point.Y, point.X), 2) + Math.Pow(dy.At<double>(point.Y, point.X), 2))).ToArray();
            double lower = 851.99999, upper = 852.00001;
            int lowerCount = magnitudes.Count(value => value > lower);
            int upperCount = magnitudes.Count(value => value > upper);
            Require(lowerCount > upperCount, "Independent fixture must cross a real Sobel feature boundary.");
            using EdgeBasedTemplateMatchingTool tool = new EdgeBasedTemplateMatchingTool();
            EdgeBasedTemplateMatchingToolProperty property = new EdgeBasedTemplateMatchingToolProperty
            {
                SCORE_MIN = 0.01, USE_DRAW_IMAGE = false, SEARCH_STEP = 1,
                MAX_TEMPLATE_POINTS = 10000, MIN_GRADIENT_MAGNITUDE = lower
            };
            tool.SetProperty(property);
            tool.SetTemplateImage(template);
            object store = typeof(EdgeBasedTemplateMatchingTool).GetField("modelStore", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(tool);
            PropertyInfo current = store.GetType().GetProperty("Current");
            using (VisionToolResult initial = tool.Execute(template))
                Require(initial.Success && initial.Metrics["Model.RawEdgePointCount"] == lowerCount, "Initial learning disagrees with the independent contour oracle.");
            object cached = current.GetValue(store);
            using (VisionToolResult unchanged = tool.Execute(template))
                Require(unchanged.Success && ReferenceEquals(cached, current.GetValue(store)), "Unchanged settings must retain the learned model.");
            property.MIN_GRADIENT_MAGNITUDE = upper;
            using (VisionToolResult changed = tool.Execute(template))
                Require(changed.Success && changed.Metrics["Model.RawEdgePointCount"] == upperCount
                    && !ReferenceEquals(cached, current.GetValue(store)), "A changed finite threshold must invalidate the model without rounding collisions.");
            using EdgeBasedTemplateMatchingTool fresh = new EdgeBasedTemplateMatchingTool();
            fresh.SetProperty(property);
            fresh.SetTemplateImage(template);
            using (VisionToolResult result = fresh.Execute(template))
                Require(result.Success && result.Metrics["Model.RawEdgePointCount"] == upperCount, "Fresh and reused instances must learn the same points.");
            property.MIN_GRADIENT_MAGNITUDE = double.NaN;
            using (VisionToolResult invalid = tool.Execute(template))
                Require(!invalid.Success && invalid.ErrorCode == VisionToolErrorCode.InvalidParameter, "Invalid settings must not expose stale success.");
            property.MIN_GRADIENT_MAGNITUDE = lower;
            using (VisionToolResult recovered = tool.Execute(template))
                Require(recovered.Success && recovered.Metrics["Model.RawEdgePointCount"] == lowerCount, "Valid restored settings must rebuild the original model.");
        }

        private static void TestEdgeThresholdInvalidRecovery()
        {
            using Mat reference = CreateAutoMPointUniqueSource();
            using Mat template = new Mat(reference, new Rect(60, 60, 64, 64)).Clone();
            using Mat source = new Mat(137, 197, MatType.CV_8UC1, Scalar.All(24));
            using (Mat destination = new Mat(source, new Rect(37, 27, 64, 64))) template.CopyTo(destination);
            using Mat original = source.Clone();
            using Mat blank = new Mat(source.Size(), source.Type(), Scalar.All(24));
            using EdgeBasedTemplateMatchingTool tool = CreateEdgeMatcher(template, false);
            using VisionToolResult retained = tool.Execute(source);
            Require(retained.Success && tool.results.Count == 1, "The initial target must match: " + retained.Message);
            MatchingResult retainedMatch = tool.results[0];
            List<MatchingResult> resultRows = tool.results;
            foreach (bool gradient in new[] { true, false })
            {
                string parameter = gradient ? nameof(tool.property.MIN_GRADIENT_MAGNITUDE) : nameof(tool.property.SCORE_MIN);
                double[] invalidValues = gradient
                    ? new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -double.MaxValue, -1, -double.Epsilon }
                    : new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, Math.BitDecrement(-1), Math.BitIncrement(1) };
                foreach (double value in invalidValues)
                {
                    if (gradient) tool.property.MIN_GRADIENT_MAGNITUDE = value;
                    else tool.property.SCORE_MIN = value;
                    for (int imageIndex = 0; imageIndex < 2; imageIndex++)
                    {
                        using VisionToolResult rejected = imageIndex == 0 ? tool.Execute(source) : tool.Execute(blank, CancellationToken.None);
                        Require(!rejected.Success && rejected.ErrorCode == VisionToolErrorCode.InvalidParameter && rejected.Message.Contains(parameter),
                            "Invalid setting must be named and rejected before searching: " + parameter + "=" + value);
                        Require(ReferenceEquals(tool.results, resultRows) && resultRows.Count == 0 && rejected.Metrics.Count == 0
                            && rejected.Overlays.Count == 0 && rejected.ResultImage == null && rejected.EdgeBasedMatchingDiagnostics.SelectedCandidate == null,
                            "Parameter rejection must not publish current or historical matching rows, scores, drawings or selection.");
                    }
                    if (gradient) tool.property.MIN_GRADIENT_MAGNITUDE = 5;
                    else tool.property.SCORE_MIN = 0.5;
                    using VisionToolResult recovered = tool.Execute(source, CancellationToken.None);
                    Require(recovered.Success && resultRows.Count == 1, "The same instance must recover after parameter rejection: " + recovered.Message);
                    RequireApproximately(resultRows[0].Center.X, retainedMatch.Center.X, 0, "Recovery changed the selected X.");
                    RequireApproximately(resultRows[0].Center.Y, retainedMatch.Center.Y, 0, "Recovery changed the selected Y.");
                    RequireApproximately(resultRows[0].Score, retainedMatch.Score, 0, "Recovery changed the selected score.");
                }
            }
            tool.property.SCORE_MIN = double.NaN;
            tool.property.MIN_GRADIENT_MAGNITUDE = double.NaN;
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            OperationCanceledException canceled = RequireThrows<OperationCanceledException>(() => { using VisionToolResult unused = tool.Execute(source, cancellation.Token); });
            Require(canceled.CancellationToken == cancellation.Token, "Pre-cancellation must retain the caller token and precede invalid settings.");
            tool.property.SCORE_MIN = 0.5;
            tool.property.MIN_GRADIENT_MAGNITUDE = 5;
            using VisionToolResult final = tool.Execute(source);
            Require(final.Success && resultRows.Count == 1, "Restored settings must recover after cancellation.");
            tool.Dispose();
            Require(!retained.ResultImage.IsDisposed && !final.ResultImage.IsDisposed && Cv2.Norm(source, original, NormTypes.L1) == 0,
                "Rejection, recovery and disposal must preserve retained snapshots and caller pixels.");
        }

        private static void TestEdgeThresholdFiniteBoundaries()
        {
            using Mat reference = CreateAutoMPointUniqueSource();
            using Mat template = new Mat(reference, new Rect(60, 60, 64, 64)).Clone();
            using Mat source = new Mat(137, 197, MatType.CV_8UC1, Scalar.All(24));
            using (Mat destination = new Mat(source, new Rect(37, 27, 64, 64))) template.CopyTo(destination);
            using Mat blank = new Mat(source.Size(), source.Type(), Scalar.All(24));
            using EdgeBasedTemplateMatchingTool tool = CreateEdgeMatcher(template, false);
            foreach (double magnitude in new[] { -0.0, 0.0, double.Epsilon, 1, 5, 64 })
            {
                tool.property.MIN_GRADIENT_MAGNITUDE = magnitude;
                using VisionToolResult valid = tool.Execute(source);
                Require(valid.Success && tool.results.Count == 1, "Finite nonnegative magnitude must accept the authored target: " + valid.Message);
                RequireApproximately(tool.results[0].Center.X, 69, 1.5, "Authored target X changed.");
                RequireApproximately(tool.results[0].Center.Y, 59, 1.5, "Authored target Y changed.");
                Require(double.IsFinite(tool.results[0].Score) && double.IsFinite(tool.results[0].EdgeScore) && double.IsFinite(tool.results[0].FinalScore),
                    "Selected scores must remain finite at the magnitude boundary.");
                using VisionToolResult absent = tool.Execute(blank);
                Require(!absent.Success && absent.ErrorCode == VisionToolErrorCode.MatchingNoResult && tool.results.Count == 0,
                    "A positive minimum score must reject a blank source.");
            }
            tool.property.MIN_GRADIENT_MAGNITUDE = double.MaxValue;
            using VisionToolResult unusable = tool.Execute(source);
            Require(!unusable.Success && unusable.ErrorCode == VisionToolErrorCode.MatchingTemplateInvalid && tool.results.Count == 0,
                "A large finite magnitude is valid input but may reject a model with no usable points.");
            tool.property.MIN_GRADIENT_MAGNITUDE = 5;
            foreach (double score in new[] { -1, -double.Epsilon, 0, 0.5, 1 })
            {
                tool.property.SCORE_MIN = score;
                using VisionToolResult valid = tool.Execute(source);
                Require(valid.Success && tool.results.Count == 1 && double.IsFinite(tool.results[0].FinalScore), "Finite score in [-1,1] must retain its contract.");
                using VisionToolResult absent = tool.Execute(blank);
                Require(absent.Success == (score <= 0), "Finite nonpositive minimum scores intentionally permit a blank score of zero.");
                if (absent.Success) RequireApproximately(tool.results.Single().Score, 0, 0, "Blank score must be finite zero.");
                else Require(absent.ErrorCode == VisionToolErrorCode.MatchingNoResult && tool.results.Count == 0, "Positive minimum score must report no match.");
            }
            byte[] encoded = template.ToBytes(".png");
            VisionPipelineToolDescriptor descriptor = VisionPipelineToolFactory.Descriptors.Single(item => item.ToolTypeName == typeof(EdgeBasedTemplateMatchingTool).FullName);
            VisionPipelineStep step = CreatePipelineStep(descriptor.ToolType);
            step.Parameters[nameof(EdgeBasedTemplateMatchingToolProperty.SCORE_MIN)] = "0.5";
            step.Parameters[nameof(EdgeBasedTemplateMatchingToolProperty.MIN_GRADIENT_MAGNITUDE)] = "NaN";
            step.Artifacts.Add(CreateTemplateArtifact(encoded, "teaching/threshold"));
            RequireThrows<ArgumentException>(() => { using EdgeBasedTemplateMatchingTool unused = (EdgeBasedTemplateMatchingTool)VisionPipelineToolFactory.Create(step, artifact => encoded); });
            step.Parameters[nameof(EdgeBasedTemplateMatchingToolProperty.MIN_GRADIENT_MAGNITUDE)] = "-1";
            using EdgeBasedTemplateMatchingTool restored = (EdgeBasedTemplateMatchingTool)VisionPipelineToolFactory.Create(step, artifact => encoded);
            using VisionToolResult rejected = restored.Execute(source);
            Require(!rejected.Success && rejected.ErrorCode == VisionToolErrorCode.InvalidParameter, "Finite negative factory settings must reach the same Tool guard.");
            step.Parameters[nameof(EdgeBasedTemplateMatchingToolProperty.MIN_GRADIENT_MAGNITUDE)] = "5";
            VisionPipeline pipeline = new VisionPipeline();
            pipeline.Steps.Add(step);
            VisionPipeline replay = VisionPipelineSerializer.Deserialize(VisionPipelineSerializer.Serialize(pipeline));
            using EdgeBasedTemplateMatchingTool reopened = (EdgeBasedTemplateMatchingTool)VisionPipelineToolFactory.Create(replay.Steps[0], artifact => encoded);
            using VisionToolResult recovered = reopened.Execute(source);
            Require(recovered.Success && reopened.results.Count == 1, "Corrected finite settings must survive teaching serialization and explicit execution.");
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

        private static void TestBoxFilterBorderExecution()
        {
            // Independent 3x3 normalized sums: Replicate counts the corner impulse 4/2/1 times.
            int[] replicated = { 40, 20, 0, 20, 10, 0, 0, 0, 0 };
            int[] reflected = { 10, 10, 0, 10, 10, 0, 0, 0, 0 };
            foreach (MatType type in new[] { MatType.CV_8UC1, MatType.CV_8UC3 })
            {
                using (Mat source = new Mat(3, 3, type, Scalar.Black))
                using (FilterTool direct = new FilterTool())
                {
                    if (source.Channels() == 1) source.Set<byte>(0, 0, 90);
                    else source.Set(0, 0, new Vec3b(90, 90, 90));
                    using (Mat original = source.Clone())
                    {
                        direct.SetProperty(new FilterToolProperty { FilterType = FilterToolType.BoxFilter, BorderType = BorderTypes.Replicate });
                        using (VisionToolResult retained = direct.Execute(source))
                        {
                            RequireFilterPixels(retained, source, replicated, "Direct Replicate");
                            direct.property.KernelWidth = 0;
                            using (VisionToolResult rejected = direct.Execute(source))
                            {
                                Require(!rejected.Success && rejected.ErrorCode == VisionToolErrorCode.FilterInvalidKernel
                                    && rejected.ResultImage == null, "BoxFilter invalid kernel did not return an empty failure result.");
                            }

                            direct.property.KernelWidth = 3;
                            direct.property.BorderType = new FilterToolProperty().BorderType;
                            using (VisionToolResult recovered = direct.Execute(source))
                            {
                                RequireFilterPixels(recovered, source, reflected, "Default Reflect101 recovery");
                            }
                            RequireFilterPixels(retained, source, replicated, "Retained result after recovery");
                        }

                        VisionPipelineStep step = CreatePipelineStep("filter");
                        step.Parameters[nameof(FilterToolProperty.FilterType)] = nameof(FilterToolType.BoxFilter);
                        step.Parameters[nameof(FilterToolProperty.BorderType)] = nameof(BorderTypes.Replicate);
                        using (FilterTool factory = (FilterTool)VisionPipelineToolFactory.Create(step))
                        using (VisionToolResult result = factory.Execute(source))
                        {
                            Require(factory.property.BorderType == BorderTypes.Replicate, "Filter factory lost the selected border.");
                            RequireFilterPixels(result, source, replicated, "Factory Replicate");
                        }
                        Require(Cv2.Norm(source, original, NormTypes.L1) == 0d, "BoxFilter modified the caller-owned source.");
                    }
                }
            }
        }

        private static void RequireFilterPixels(VisionToolResult result, Mat source, int[] values, string caseName)
        {
            Require(result.Success && result.ResultImage != null && result.ResultImage.Type() == source.Type(), caseName + " did not return the source image type.");
            using (Mat expected = new Mat(3, 3, source.Type(), Scalar.Black))
            {
                for (int row = 0; row < 3; row++)
                {
                    for (int column = 0; column < 3; column++)
                    {
                        byte value = (byte)values[row * 3 + column];
                        if (source.Channels() == 1) expected.Set(row, column, value);
                        else expected.Set(row, column, new Vec3b(value, value, value));
                    }
                }
                Require(Cv2.Norm(result.ResultImage, expected, NormTypes.L1) == 0d, caseName + " differs from independent normalized box sums.");
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

        private static void TestCannyColorExecution()
        {
            using (Mat parent = new Mat(31, 43, MatType.CV_8UC3, Scalar.White))
            using (Mat source = parent.SubMat(new Rect(3, 4, 31, 23)))
            using (Mat gray = new Mat())
            using (Mat expectedRetained = new Mat())
            {
                for (int row = 0; row < source.Rows; row++)
                    for (int column = 0; column < source.Cols; column++)
                        source.Set(row, column, column < 15 ? new Vec3b(255, 0, 0) : new Vec3b(0, 49, 0));
                Cv2.CvtColor(source, gray, ColorConversionCodes.BGR2GRAY);
                using (Mat before = parent.Clone())
                using (Mat expectedSource = source.Clone())
                {
                    Cv2.Canny(expectedSource, expectedRetained, 100, 200, 3, false);
                    VisionToolResult retained = null;
                    try
                    {
                        using (EdgeDetectionTool tool = new EdgeDetectionTool())
                        {
                            tool.SetProperty(new EdgeDetectionToolProperty { UseL2Gradient = false });
                            retained = tool.Execute(source);
                            Require(retained.Success && retained.ResultImage.Type() == MatType.CV_8UC1,
                                "Color Canny must succeed with a one-channel result: " + retained.Message);
                            foreach (Mat input in new[] { source, gray })
                                foreach (int aperture in new[] { 3, 5, 7 })
                                    foreach (bool l2 in new[] { false, true })
                                    {
                                        tool.SetProperty(new EdgeDetectionToolProperty { CannyApertureSize = aperture, UseL2Gradient = l2 });
                                        using (Mat referenceSource = input.Clone())
                                        using (Mat expected = new Mat())
                                        using (VisionToolResult result = tool.Execute(input))
                                        {
                                            Cv2.Canny(referenceSource, expected, 100, 200, aperture, l2);
                                            Require(result.Success && result.ResultImage.Type() == MatType.CV_8UC1
                                                && result.ResultImage.Size() == input.Size() && Cv2.Norm(result.ResultImage, expected, NormTypes.L1) == 0,
                                                "Canny pixels/type differ from the distinct-output CPU reference.");
                                            Require(result.Metrics["SourceImageChannels"] == input.Channels() && result.Metrics["ResultImageChannels"] == 1,
                                                "Canny result channel metrics must describe source and output separately.");
                                            Require(input == source ? Cv2.CountNonZero(result.ResultImage) > 0 : Cv2.CountNonZero(result.ResultImage) == 0,
                                                "Canny must preserve color-channel gradients at equal grayscale intensity.");
                                        }
                                    }
                            tool.SetProperty(new EdgeDetectionToolProperty { CannyThresholdLow = 400, CannyThresholdHigh = 200 });
                            using (VisionToolResult invalid = tool.Execute(source))
                                Require(!invalid.Success && invalid.ErrorCode == VisionToolErrorCode.EdgeDetectionInvalidThreshold
                                    && invalid.Exception == null && invalid.ResultImage == null, "Invalid Canny settings must retain typed failure.");
                            tool.SetProperty(new EdgeDetectionToolProperty { UseL2Gradient = false });
                            using (Mat unsupported = new Mat(23, 31, MatType.CV_16UC3, Scalar.All(17)))
                            using (Mat unsupportedBefore = unsupported.Clone())
                            using (VisionToolResult nativeFailure = tool.Execute(unsupported))
                                Require(!nativeFailure.Success && nativeFailure.ErrorCode == VisionToolErrorCode.OpenCvExecutionFailed
                                    && nativeFailure.Exception != null && nativeFailure.ResultImage == null
                                    && Cv2.Norm(unsupported, unsupportedBefore, NormTypes.L1) == 0,
                                    "The color Canny exception path must preserve caller pixels and typed failure.");
                            using (VisionToolResult recovered = tool.Execute(source))
                                Require(recovered.Success && Cv2.Norm(recovered.ResultImage, expectedRetained, NormTypes.L1) == 0,
                                    "The same Canny instance must recover after invalid settings.");
                        }
                        Require(!retained.ResultImage.IsDisposed && Cv2.Norm(retained.ResultImage, expectedRetained, NormTypes.L1) == 0,
                            "A retained color Canny result must survive later calls and Tool disposal.");
                        Require(Cv2.Norm(parent, before, NormTypes.L1) == 0, "Canny must preserve the caller image and ROI padding.");
                    }
                    finally { retained?.Dispose(); }
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

        private static Mat CreatePoseSquareSource()
        {
            Mat source = new Mat(160, 176, MatType.CV_8UC1, Scalar.All(24));
            Cv2.Rectangle(source, new Rect(60, 50, 40, 40), Scalar.All(205), -1);
            return source;
        }

        private static EdgeBasedTemplateMatchingTool CreatePoseMatcher(Mat template)
        {
            EdgeBasedTemplateMatchingTool tool = CreateEdgeMatcher(template, true);
            tool.property.USE_FIND_ANGLE = true;
            tool.property.FIND_ANGLE_MIN = 0;
            tool.property.FIND_ANGLE_MAX = 270;
            tool.property.FIND_ANGLE = 90;
            tool.property.USE_SUBPIXEL_REFINE = false;
            return tool;
        }

        private static void RequirePoseAmbiguous(EdgeBasedTemplateMatchingTool tool, VisionToolResult result, string caseId)
        {
            Require(!result.Success && result.ErrorCode == VisionToolErrorCode.MatchingAmbiguous && tool.results.Count == 0,
                caseId + " must reject competing authored poses: " + result.Message);
            Require(result.Metrics["PoseMatch.State"] == 3 && result.Metrics["PoseMatch.PlausibleAlternativeCount"] > 0,
                caseId + " must retain its pose-specific decision metrics.");
            Require(result.EdgeBasedMatchingDiagnostics.SelectedCandidate != null
                && result.EdgeBasedMatchingDiagnostics.StrongestPoseAlternative != null,
                caseId + " must retain both selected and alternative poses after rejecting result rows.");
            Console.WriteLine(caseId + " " + result.Message);
        }

        private static void TestPoseUniquenessRotations()
        {
            using Mat source = CreatePoseSquareSource();
            using Mat template = new Mat(source, new Rect(48, 38, 64, 64)).Clone();
            using EdgeBasedTemplateMatchingTool tool = CreatePoseMatcher(template);
            using (VisionToolResult legacy = tool.Execute(source))
            {
                Require(legacy.Success && tool.results.Count == 1 && legacy.Metrics["PoseMatch.Enabled"] == 0,
                    "Default-off pose validation must preserve existing spatial unique matching: " + legacy.Message);
                RequireApproximately(tool.results[0].Center.X, 80, 1.5, "Authored square center X changed.");
                RequireApproximately(tool.results[0].Center.Y, 70, 1.5, "Authored square center Y changed.");
            }
            tool.UsePoseUniquenessValidation = true;
            using VisionToolResult rejected = tool.Execute(source);
            RequirePoseAmbiguous(tool, rejected, "P1-quarter-turns");
            Require(rejected.Metrics["UniqueMatch.State"] == 2 && rejected.Metrics["PoseMatch.CheckedModelCount"] == 4,
                "Pose competition must not be relabeled as a spatial alternative or hidden by Top-K.");
            Require(rejected.Metrics["PoseMatch.AngleDifferenceDegrees"] >= tool.PoseMatchMinAngleDifferenceDegrees,
                "The retained rotation must be distinct under the configured angular contract.");
            tool.PoseMatchMinScoreMargin = 0;
            using (VisionToolResult boundary = tool.Execute(source))
                Require(boundary.Success, "A zero score margin permits an exact tie under the strict margin contract.");
            tool.UsePoseUniquenessValidation = false;
            using VisionToolResult recovered = tool.Execute(source);
            Require(recovered.Success && recovered.EdgeBasedMatchingDiagnostics.StrongestPoseAlternative == null,
                "Disabling the optional gate must restore legacy matching with fresh evidence.");
        }

        private static void TestPoseUniquenessObservableAngles()
        {
            using Mat source = CreateAutoMPointUniqueSource();
            using Mat template = new Mat(source, new Rect(60, 60, 64, 64)).Clone();
            using EdgeBasedTemplateMatchingTool tool = CreatePoseMatcher(template);
            tool.property.CONTOUR_RETRIEVAL_MODE = RetrievalModes.List;
            tool.property.CONTOUR_APPROXIMATION_MODE = ContourApproximationModes.ApproxNone;
            tool.UsePoseUniquenessValidation = true;
            using (VisionToolResult distinct = tool.Execute(source))
                Require(distinct.Success && distinct.Metrics["PoseMatch.State"] == 2,
                    "Authored asymmetric internal features must distinguish quarter-turn poses: " + distinct.Message);

            tool.property.FIND_ANGLE_MIN = -1;
            tool.property.FIND_ANGLE_MAX = 1;
            tool.property.FIND_ANGLE = 0.5;
            using (VisionToolResult local = tool.Execute(source))
                Require(local.Success && local.Metrics["PoseMatch.CheckedModelCount"] == 5 && local.Metrics["PoseMatch.EligibleAlternativeCount"] == 0,
                    "Grid samples inside the configured angle tolerance belong to one local pose.");

            using Mat square = CreatePoseSquareSource();
            using Mat squareTemplate = new Mat(square, new Rect(48, 38, 64, 64)).Clone();
            using EdgeBasedTemplateMatchingTool circular = CreatePoseMatcher(squareTemplate);
            circular.UsePoseUniquenessValidation = true;
            circular.property.FIND_ANGLE_MAX = 360;
            circular.property.FIND_ANGLE = 360;
            using VisionToolResult wrap = circular.Execute(square);
            Require(wrap.Success && wrap.Metrics["PoseMatch.CheckedModelCount"] == 2 && wrap.Metrics["PoseMatch.EligibleAlternativeCount"] == 0,
                "0 and 360 degrees denote the same orientation and must not compete.");
            Console.WriteLine("P2-asymmetric/local and P3-circular passed.");
        }

        private static void TestPoseUniquenessScales()
        {
            using Mat template = new Mat(64, 64, MatType.CV_8UC1, Scalar.All(24));
            Cv2.Rectangle(template, new Rect(16, 16, 32, 32), Scalar.All(205), -1);
            using Mat source = new Mat(160, 160, MatType.CV_8UC1, Scalar.All(24));
            Cv2.Rectangle(source, new Rect(48, 48, 64, 64), Scalar.All(205), -1);
            Cv2.Rectangle(source, new Rect(64, 64, 32, 32), Scalar.All(245), -1);
            using EdgeBasedTemplateMatchingTool tool = CreatePoseMatcher(template);
            tool.property.USE_FIND_ANGLE = false;
            tool.property.USE_FIND_SCALE = true;
            tool.property.FIND_SCALE_MIN = 1;
            tool.property.FIND_SCALE_MAX = 2;
            tool.property.FIND_SCALE_STEP = 1;
            tool.UsePoseUniquenessValidation = true;
            using (VisionToolResult competing = tool.Execute(source))
            {
                RequirePoseAmbiguous(tool, competing, "P4-concentric-scales");
                Require(competing.Metrics["PoseMatch.CheckedModelCount"] == 2 && competing.Metrics["PoseMatch.RelativeScaleDifference"] == 1,
                    "The authored 1x/2x squares must retain their relative scale competition.");
                RequireApproximately(competing.EdgeBasedMatchingDiagnostics.SelectedCandidate.Center.X, 80, 1.5, "Selected scale center X changed.");
                RequireApproximately(competing.EdgeBasedMatchingDiagnostics.StrongestPoseAlternative.Center.X, 80, 1.5, "Alternative scale center X changed.");
            }
            tool.PoseMatchMinRelativeScaleDifference = 1;
            using (VisionToolResult boundary = tool.Execute(source)) RequirePoseAmbiguous(tool, boundary, "P4-inclusive-scale-difference");
            tool.PoseMatchMinRelativeScaleDifference = 2;
            using VisionToolResult grouped = tool.Execute(source);
            Require(grouped.Success && grouped.Metrics["PoseMatch.EligibleAlternativeCount"] == 0,
                "The configured relative scale threshold must define which models are distinct.");
        }

        private static void TestPoseUniquenessRecovery()
        {
            using Mat source = CreatePoseSquareSource();
            using Mat template = new Mat(source, new Rect(48, 38, 64, 64)).Clone();
            using Mat blank = new Mat(source.Size(), MatType.CV_8UC1, Scalar.All(24));
            using EdgeBasedTemplateMatchingTool tool = CreatePoseMatcher(template);
            tool.UsePoseUniquenessValidation = true;
            using (VisionToolResult ambiguous = tool.Execute(source)) RequirePoseAmbiguous(tool, ambiguous, "P5-initial-rejection");
            using (VisionToolResult absent = tool.Execute(blank))
                Require(absent.ErrorCode == VisionToolErrorCode.MatchingNoResult && absent.Metrics["PoseMatch.State"] == 1
                    && absent.EdgeBasedMatchingDiagnostics.StrongestPoseAlternative == null, "Absent input must replace the rejected pose evidence.");
            tool.property.USE_FIND_ANGLE = false;
            foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, -0.01, 1.01 })
            {
                tool.PoseMatchMinScoreMargin = invalid;
                using VisionToolResult failed = tool.Execute(source);
                Require(failed.ErrorCode == VisionToolErrorCode.InvalidParameter && failed.EdgeBasedMatchingDiagnostics.SelectedCandidate == null,
                    "Invalid pose margin must fail before search and retain no stale candidate.");
            }
            tool.PoseMatchMinScoreMargin = 0.03;
            foreach (double invalid in new[] { double.NaN, double.NegativeInfinity, 0, 181 })
            {
                tool.PoseMatchMinAngleDifferenceDegrees = invalid;
                using VisionToolResult failed = tool.Execute(source);
                Require(failed.ErrorCode == VisionToolErrorCode.InvalidParameter, "Invalid pose angular threshold must be rejected.");
            }
            tool.PoseMatchMinAngleDifferenceDegrees = 5;
            foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, 0, -1 })
            {
                tool.PoseMatchMinRelativeScaleDifference = invalid;
                using VisionToolResult failed = tool.Execute(source);
                Require(failed.ErrorCode == VisionToolErrorCode.InvalidParameter, "Invalid pose scale threshold must be rejected.");
            }
            tool.PoseMatchMinRelativeScaleDifference = 0.05;
            tool.property.NUM_MATCH = 2;
            using (VisionToolResult invalid = tool.Execute(source)) Require(invalid.ErrorCode == VisionToolErrorCode.InvalidParameter, "Pose gate requires one result.");
            tool.property.NUM_MATCH = 1;
            tool.property.USE_MULTI_ROI = true;
            using (VisionToolResult invalid = tool.Execute(source)) Require(invalid.ErrorCode == VisionToolErrorCode.InvalidParameter, "Pose gate requires one search region.");
            tool.property.USE_MULTI_ROI = false;
            tool.property.USE_FIND_ANGLE = true;
            foreach (double invalidStep in new[] { double.NaN, double.PositiveInfinity, double.Epsilon })
            {
                tool.property.FIND_ANGLE = invalidStep;
                using VisionToolResult invalid = tool.Execute(source);
                Require(invalid.ErrorCode == VisionToolErrorCode.InvalidParameter, "Unrepresentable active pose grid must be rejected before enumeration.");
            }
            tool.property.USE_FIND_ANGLE = false;
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            RequireThrows<OperationCanceledException>(() => tool.Execute(source, cancellation.Token));
            using VisionToolResult recovered = tool.Execute(source);
            Require(recovered.Success && tool.results.Count == 1 && recovered.Metrics["PoseMatch.State"] == 2
                && recovered.EdgeBasedMatchingDiagnostics.StrongestPoseAlternative == null, "Valid input must recover after absence invalid options and cancellation.");
            Console.WriteLine("P5-invalid/absent/cancel -> valid recovery passed.");
        }

        private static void TestPoseUniquenessSearchPaths()
        {
            using Mat source = CreatePoseSquareSource();
            using Mat template = new Mat(source, new Rect(48, 38, 64, 64)).Clone();
            foreach (string path in new[] { "coarse", "pyramid", "hybrid" })
            {
                using EdgeBasedTemplateMatchingTool tool = CreatePoseMatcher(template);
                tool.UsePoseUniquenessValidation = true;
                tool.property.USE_COARSE_TO_FINE_ANGLE_SEARCH = path == "coarse";
                tool.property.COARSE_ANGLE_STEP = 180;
                tool.property.COARSE_ANGLE_TOP_K = 1;
                tool.property.USE_PYRAMID_POSITION_PROPOSAL = path == "pyramid";
                tool.property.USE_HYBRID_VERIFY = path == "hybrid";
                tool.property.USE_ROI = true;
                tool.property.CvROI = new Rect(30, 20, 116, 110);
                using VisionToolResult result = tool.Execute(source);
                RequirePoseAmbiguous(tool, result, "P6-" + path);
                Require(result.Metrics["PoseMatch.CheckedModelCount"] == 4 && result.Metrics["PoseMatch.UnverifiedCandidateCount"] == 0,
                    "Proposal pruning must not replace the configured full pose grid or silently accept an unverified candidate.");
                EdgeBasedMatchingDiagnosticEvidence evidence = result.EdgeBasedMatchingDiagnostics;
                RequireApproximately(evidence.SelectedCandidate.Center.X, 80, 1.5, "Selected evidence must include the ROI X offset.");
                RequireApproximately(evidence.StrongestPoseAlternative.Center.Y, 70, 1.5, "Pose evidence must include the ROI Y offset.");
                EdgeBasedMatchingDiagnosticEvidence clone = evidence.Clone();
                clone.StrongestPoseAlternative.Angle = 12345;
                clone.ModelPoints.Clear();
                Require(evidence.StrongestPoseAlternative.Angle != 12345 && evidence.ModelPoints.Count > 0,
                    "Diagnostic Clone must own its alternative and model point snapshots.");
            }
        }

        private static void TestPoseUniquenessUnverifiedHybrid()
        {
            using Mat source = CreatePoseSquareSource();
            using Mat template = new Mat(source, new Rect(48, 38, 64, 64)).Clone();
            using EdgeBasedTemplateMatchingTool tool = CreatePoseMatcher(template);
            tool.UsePoseUniquenessValidation = true;
            tool.property.USE_UNIQUE_MATCH_VALIDATION = false;
            tool.property.USE_HYBRID_VERIFY = true;
            tool.property.USE_ROI = true;
            tool.property.CvROI = new Rect(56, 46, 48, 48);
            using (VisionToolResult insufficient = tool.Execute(source))
            {
                Require(insufficient.ErrorCode == VisionToolErrorCode.MatchingAmbiguous && tool.results.Count == 0
                    && insufficient.Metrics["PoseMatch.UnverifiedCandidateCount"] > 0,
                    "An edge-supported pose without a complete hybrid comparison must fail closed: " + insufficient.Message);
                Console.WriteLine("P7-unavailable-hybrid " + insufficient.Message);
            }
            tool.property.CvROI = new Rect(30, 20, 116, 110);
            using VisionToolResult recovered = tool.Execute(source);
            RequirePoseAmbiguous(tool, recovered, "P7-recovered-comparison");
            Require(recovered.Metrics["PoseMatch.UnverifiedCandidateCount"] == 0, "A sufficient ROI must restore the comparison evidence.");
        }

        private static void TestPoseUniquenessTeachingReplay()
        {
            using Mat source = CreatePoseSquareSource();
            using Mat template = new Mat(source, new Rect(48, 38, 64, 64)).Clone();
            byte[] encoded = template.ToBytes(".png");
            VisionPipelineToolDescriptor descriptor = VisionPipelineToolFactory.Descriptors.Single(item => item.ToolTypeName == typeof(EdgeBasedTemplateMatchingTool).FullName);
            VisionPipelineStep step = CreatePipelineStep(descriptor.ToolType);
            step.Parameters[nameof(EdgeBasedTemplateMatchingTool.UsePoseUniquenessValidation)] = "true";
            step.Parameters[nameof(EdgeBasedTemplateMatchingTool.PoseMatchMinScoreMargin)] = "0.07";
            step.Parameters[nameof(EdgeBasedTemplateMatchingTool.PoseMatchMinAngleDifferenceDegrees)] = "45";
            step.Parameters[nameof(EdgeBasedTemplateMatchingTool.PoseMatchMinRelativeScaleDifference)] = "0.12";
            step.Parameters[nameof(EdgeBasedTemplateMatchingToolProperty.SCORE_MIN)] = "0.5";
            step.Parameters[nameof(EdgeBasedTemplateMatchingToolProperty.USE_FIND_ANGLE)] = "true";
            step.Parameters[nameof(EdgeBasedTemplateMatchingToolProperty.FIND_ANGLE_MIN)] = "0";
            step.Parameters[nameof(EdgeBasedTemplateMatchingToolProperty.FIND_ANGLE_MAX)] = "270";
            step.Parameters[nameof(EdgeBasedTemplateMatchingToolProperty.FIND_ANGLE)] = "90";
            step.Parameters[nameof(EdgeBasedTemplateMatchingToolProperty.SEARCH_STEP)] = "1";
            step.Artifacts.Add(CreateTemplateArtifact(encoded, "teaching/pose"));
            VisionPipeline pipeline = new VisionPipeline { Name = "Pose teaching replay" };
            pipeline.Steps.Add(step);
            string serialized = VisionPipelineSerializer.Serialize(pipeline);
            VisionPipeline restored = VisionPipelineSerializer.Deserialize(serialized);
            using EdgeBasedTemplateMatchingTool tool = (EdgeBasedTemplateMatchingTool)VisionPipelineToolFactory.Create(restored.Steps[0], artifact => encoded);
            Require(VisionPipelineSerializer.Serialize(restored) == serialized && tool.results.Count == 0,
                "XML/model restoration must preserve settings and must not execute matching.");
            Require(tool.UsePoseUniquenessValidation && tool.PoseMatchMinScoreMargin == 0.07
                && tool.PoseMatchMinAngleDifferenceDegrees == 45 && tool.PoseMatchMinRelativeScaleDifference == 0.12,
                "Factory must restore every declared pose parameter.");
            using VisionToolResult rejected = tool.Execute(source);
            RequirePoseAmbiguous(tool, rejected, "P6-teaching-replay");
            step.Parameters.Remove(nameof(EdgeBasedTemplateMatchingTool.UsePoseUniquenessValidation));
            using EdgeBasedTemplateMatchingTool legacy = (EdgeBasedTemplateMatchingTool)VisionPipelineToolFactory.Create(step, artifact => encoded);
            using VisionToolResult accepted = legacy.Execute(source);
            Require(!legacy.UsePoseUniquenessValidation && accepted.Success,
                "Existing serialized configurations missing the opt-in key must retain legacy success.");
        }

        private static void TestMatchingReliabilityTransforms()
        {
            using Mat reference = CreateAutoMPointUniqueSource();
            using Mat template = new Mat(reference, new Rect(60, 60, 64, 64)).Clone();
            foreach ((int angle, double scale, double gain, int offset) in new[]
            {
                (0, 1.0, 1.0, 0), (180, 1.0, 1.0, 0), (0, 1.25, 1.0, 0),
                (180, 0.75, 1.0, 0), (0, 1.0, 0.7, 30), (180, 1.25, 0.7, 30), (0, 1.0, -1.0, 255)
            })
            {
                using Mat transformed = new Mat((int)(64 * scale), (int)(64 * scale), MatType.CV_8UC1);
                for (int row = 0; row < transformed.Rows; row++)
                {
                    for (int column = 0; column < transformed.Cols; column++)
                    {
                        int x = Math.Min(63, (int)(column / scale));
                        int y = Math.Min(63, (int)(row / scale));
                        if (angle == 180) { x = 63 - x; y = 63 - y; }
                        transformed.Set(row, column, (byte)Math.Clamp(Math.Round(template.At<byte>(y, x) * gain + offset), 0, 255));
                    }
                }
                using Mat scene = new Mat(128, 160, MatType.CV_8UC1, Scalar.All(Math.Round(24 * gain + offset)));
                using (Mat destination = new Mat(scene, new Rect(43, 29, transformed.Cols, transformed.Rows))) transformed.CopyTo(destination);
                using EdgeBasedTemplateMatchingTool tool = CreateEdgeMatcher(template, true, gain < 0);
                tool.property.CONTOUR_RETRIEVAL_MODE = RetrievalModes.List;
                tool.property.CONTOUR_APPROXIMATION_MODE = ContourApproximationModes.ApproxNone;
                tool.property.USE_FIND_ANGLE = true;
                tool.property.FIND_ANGLE_MIN = 0;
                tool.property.FIND_ANGLE_MAX = 180;
                tool.property.FIND_ANGLE = 90;
                tool.property.USE_FIND_SCALE = true;
                tool.property.FIND_SCALE_MIN = 0.75;
                tool.property.FIND_SCALE_MAX = 1.25;
                tool.property.FIND_SCALE_STEP = 0.25;
                using VisionToolResult result = tool.Execute(scene);
                Require(result.Success && tool.results.Count == 1,
                    "Authored transform must recover one distinct target. Angle=" + angle + ", Scale=" + scale + ", Gain=" + gain + ": " + result.Message);
                Console.WriteLine(FormattableString.Invariant($"MEASURE | expected angle={angle} scale={scale} gain={gain}; actual angle={tool.results[0].Angle:R} scale={tool.results[0].Scale:R} center=({tool.results[0].Center.X:R},{tool.results[0].Center.Y:R}) bounds={tool.results[0].Bounding} score={tool.results[0].Score:R}"));
                RequireApproximately(tool.results[0].Center.X, 43 + transformed.Cols / 2.0, 1.5, "Authored transformed center X changed.");
                RequireApproximately(tool.results[0].Center.Y, 29 + transformed.Rows / 2.0, 1.5, "Authored transformed center Y changed.");
                RequireApproximately(tool.results[0].Angle, angle, 1e-9, "Authored grid angle changed.");
                RequireApproximately(tool.results[0].Scale, scale, 1e-9, "Authored grid scale changed.");
                Require(tool.results[0].PolarityReversed == (gain < 0), "Authored polarity evidence changed.");
            }
        }

        private static void TestMatchingReliabilityTeachingReplay()
        {
            using Mat reference = CreateAutoMPointRepeatedSource();
            using Mat template = new Mat(reference, new Rect(0, 0, 64, 64)).Clone();
            byte[] encoded = template.ToBytes(".png");
            VisionPipelineToolDescriptor descriptor = VisionPipelineToolFactory.Descriptors.Single(item => item.ToolTypeName == typeof(EdgeBasedTemplateMatchingTool).FullName);
            VisionPipelineStep step = CreatePipelineStep(descriptor.ToolType);
            step.Parameters[nameof(EdgeBasedTemplateMatchingToolProperty.USE_UNIQUE_MATCH_VALIDATION)] = "true";
            step.Parameters[nameof(EdgeBasedTemplateMatchingToolProperty.SCORE_MIN)] = "0.5";
            step.Parameters[nameof(EdgeBasedTemplateMatchingToolProperty.SEARCH_STEP)] = "1";
            step.Parameters[nameof(EdgeBasedTemplateMatchingToolProperty.USE_POSITION_REFINE)] = "true";
            step.Parameters[nameof(EdgeBasedTemplateMatchingToolProperty.UNIQUE_MATCH_MIN_SCORE_MARGIN)] = "0.03";
            step.Artifacts.Add(CreateTemplateArtifact(encoded, "teaching/reliability"));
            VisionPipeline pipeline = new VisionPipeline { Name = "Teaching reliability" };
            pipeline.Steps.Add(step);
            string serialized = VisionPipelineSerializer.Serialize(pipeline);
            VisionPipeline reloaded = VisionPipelineSerializer.Deserialize(serialized);
            int resolutions = 0;
            using EdgeBasedTemplateMatchingTool first = (EdgeBasedTemplateMatchingTool)VisionPipelineToolFactory.Create(step, artifact => { resolutions++; return encoded; });
            using EdgeBasedTemplateMatchingTool replay = (EdgeBasedTemplateMatchingTool)VisionPipelineToolFactory.Create(reloaded.Steps[0], artifact => { resolutions++; return encoded; });
            Require(resolutions == 2 && VisionPipelineSerializer.Serialize(reloaded) == serialized,
                "Restore must preserve the frozen settings/artifact and must not execute during loading.");
            using Mat one = new Mat(80, 144, MatType.CV_8UC1, Scalar.All(24));
            using (Mat destination = new Mat(one, new Rect(65, 8, 64, 64))) template.CopyTo(destination);
            using Mat absent = new Mat(80, 144, MatType.CV_8UC1, Scalar.All(24));
            foreach (Mat source in new[] { reference, absent, one })
            {
                using VisionToolResult actual = first.Execute(source);
                using VisionToolResult restored = replay.Execute(source);
                Require(actual.Success == restored.Success && actual.ErrorCode == restored.ErrorCode
                    && actual.EdgeBasedMatchingDiagnostics.State == restored.EdgeBasedMatchingDiagnostics.State,
                    "Reopened model/settings must preserve failure and selection state.");
                if (ReferenceEquals(source, reference)) Require(actual.ErrorCode == VisionToolErrorCode.MatchingAmbiguous && first.results.Count == 0, "Repeated marks must remain ambiguous after teaching replay.");
                else if (ReferenceEquals(source, absent)) Require(actual.ErrorCode == VisionToolErrorCode.MatchingNoResult && first.results.Count == 0, "Absent target must remain rejected after teaching replay.");
                else
                {
                    Require(actual.Success && first.results.Count == 1 && replay.results.Count == 1, "Both original and restored tools must recover after rejected inputs.");
                    RequireApproximately(first.results[0].Center.X, 97, 1.5, "Replayed target center X changed.");
                    RequireApproximately(first.results[0].Center.Y, 40, 1.5, "Replayed target center Y changed.");
                    RequireApproximately(first.results[0].Center.X, replay.results[0].Center.X, 0, "Original and restored X results differ.");
                    RequireApproximately(first.results[0].Score, replay.results[0].Score, 0, "Original and restored scores differ.");
                }
            }
            byte[] corrupted = (byte[])encoded.Clone();
            corrupted[corrupted.Length / 2] ^= 1;
            InvalidOperationException rejected = RequireThrows<InvalidOperationException>(() => VisionPipelineToolFactory.Create(reloaded.Steps[0], artifact => corrupted));
            Require(rejected.Message.Contains("SHA-256 mismatch", StringComparison.Ordinal), "Corrupted teaching bytes must fail the hash contract before decoding.");
            using VisionToolResult recovered = replay.Execute(one);
            Require(recovered.Success, "Corrupted external teaching bytes must not invalidate an already owned model.");
        }

        private static void TestPipelineReliabilityCloneFailure()
        {
            using VisionPipelineContext context = new VisionPipelineContext();
            using Mat valid = new Mat(5, 7, MatType.CV_8UC1, Scalar.All(123));
            context.SetLayer("image", valid);
            Mat disposed = new Mat(5, 7, MatType.CV_8UC1, Scalar.All(77));
            disposed.Dispose();
            RequireThrows<ObjectDisposedException>(() => context.SetLayer("image", disposed));
            using Mat retained = context.GetLayer("image");
            Require(retained != null && retained.At<byte>(2, 3) == 123,
                "Failed replacement must preserve the last successfully owned layer.");
            context.SetLayer("image", valid);
            using Mat recovered = context.GetLayer("image");
            Require(recovered.At<byte>(2, 3) == 123, "A failed clone must not prevent the next valid replacement.");
        }

        private static void TestPipelineReliabilityRepeatedLifetime()
        {
            using Mat source = new Mat(32, 48, MatType.CV_8UC1, Scalar.All(123));
            VisionPipeline pipeline = new VisionPipeline();
            VisionPipelineStep step = CreatePipelineStep("threshold");
            step.Parameters[nameof(ThresholdToolProperty.Threshold)] = "100";
            step.Parameters[nameof(ThresholdToolProperty.MaxValue)] = "255";
            pipeline.Steps.Add(step);
            using VisionPipelineContext context = new VisionPipelineContext();
            context.SetLayer("input", source);
            VisionPipelineRuntime runtime = new VisionPipelineRuntime();
            for (int index = 0; index < 64; index++)
            {
                VisionPipelineRunResult run = runtime.Run(pipeline, context);
                Require(run.Success && run.StepResults.Count == 1, "Repeated pipeline must retain the same valid step result.");
                Mat resultImage = run.StepResults[0].ToolResult.ResultImage;
                Require(resultImage != null && resultImage.At<byte>(1, 1) == 255, "Repeated output snapshot changed.");
                run.Dispose();
                run.Dispose();
                Require(resultImage.IsDisposed, "Run result must release its owned output snapshot.");
                using Mat layer = context.GetLayer("output");
                Require(layer.At<byte>(1, 1) == 255 && source.At<byte>(1, 1) == 123,
                    "Result disposal must preserve the independent context layer and caller input.");
            }
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            RequireThrows<OperationCanceledException>(() => runtime.Run(pipeline, context, cancellation.Token));
            step.InputLayer = "absent";
            using (VisionPipelineRunResult failed = runtime.RunWithFailureResults(pipeline, context))
                Require(!failed.Success && failed.StepResults[0].ToolResult.ErrorCode == VisionToolErrorCode.InputLayerMissing, "Missing layer must return its declared failure.");
            step.InputLayer = "input";
            using VisionPipelineRunResult recovered = runtime.Run(pipeline, context);
            Require(recovered.Success, "Pipeline must recover after cancellation and missing input.");
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
                "qrcode",
                "blob"
            };
            Require(VisionPipelineToolFactory.Descriptors.Count == 15,
                "The base Vision2D descriptor catalog must contain its 15 owned Tools.");
            Require(VisionPipelineBlobToolFactory.Descriptors.Select(item => item.ToolType).SequenceEqual(expectedToolTypes),
                "The Blob composite descriptor catalog does not contain the exact 16-Tool contract.");

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
                if (descriptor.ToolTypeName == typeof(QrCodeTool).FullName)
                {
                    // QR intentionally exposes only its supported subset of the inherited common property model.
                    string[] supported = { "NAME", "USE_ROI", "CvROI", "ExpectedCodeCount" };
                    writableProperties = writableProperties.Where(item => supported.Contains(item.Key)).ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
                }
                if (descriptor.ToolTypeName == typeof(EdgeBasedTemplateMatchingTool).FullName)
                {
                    // These additive Tool-owned settings preserve the required Property interface.
                    foreach (string name in new[]
                    {
                        nameof(EdgeBasedTemplateMatchingTool.UsePoseRefinement),
                        nameof(EdgeBasedTemplateMatchingTool.PoseRefinementMaximumIterations),
                        nameof(EdgeBasedTemplateMatchingTool.PoseRefinementEpsilon),
                        nameof(EdgeBasedTemplateMatchingTool.UsePoseUniquenessValidation),
                        nameof(EdgeBasedTemplateMatchingTool.PoseMatchMinScoreMargin),
                        nameof(EdgeBasedTemplateMatchingTool.PoseMatchMinAngleDifferenceDegrees),
                        nameof(EdgeBasedTemplateMatchingTool.PoseMatchMinRelativeScaleDifference)
                    })
                        writableProperties.Add(name, typeof(EdgeBasedTemplateMatchingTool).GetProperty(name));
                }
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

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
using System.Threading;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;
using static OpenVisionLab.Inspection.Smoke.SmokeFixtures;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class HeightMapAndGeometrySmokeSuite
    {
        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase("Height map keeps legacy unit compatibility", TestHeightMapLegacyUnitCompatibility);
            yield return new SmokeCase("Height map array factory preserves row-major values and declared metadata", TestHeightMapArrayFactory);
            yield return new SmokeCase("Height map rejects infinity and non-finite coordinate extents", TestHeightMapRejectsInvalidValues);
            yield return new SmokeCase("Height-map crop preserves values, missing cells, and source-frame origin", TestHeightMapCrop);
            yield return new SmokeCase("Height-map crop rejects invalid regions and honors cancellation", TestHeightMapCropGuards);
            yield return new SmokeCase("Height-map domain mask preserves foreground values and reduces background to missing", TestHeightMapDomainMask);
            yield return new SmokeCase("Height-map domain mask rejects invalid masks and honors cancellation", TestHeightMapDomainMaskGuards);
            yield return new SmokeCase("Height-map threshold removal preserves inclusive foreground and missing values", TestHeightMapThresholdBackgroundRemoval);
            yield return new SmokeCase("Height-map threshold removal rejects invalid input and honors cancellation", TestHeightMapThresholdBackgroundRemovalGuards);
            yield return new SmokeCase("Thickness pass preserves declared metadata", TestThicknessPass);
            yield return new SmokeCase("Thickness rejects a unit contract mismatch", TestThicknessUnitContractMismatch);
            yield return new SmokeCase("Thickness rejects a frame contract mismatch", TestThicknessFrameContractMismatch);
            yield return new SmokeCase("Thickness rejects insufficient valid coverage with quality evidence", TestThicknessInsufficientCoverage);
            yield return new SmokeCase("Thickness tolerance failure retains measurement", TestThicknessToleranceFailure);
            yield return new SmokeCase("Thickness rejects an invalid ROI", TestThicknessInvalidRoi);
            yield return new SmokeCase("Thickness rejects insufficient valid samples", TestThicknessInsufficientSamples);
            yield return new SmokeCase("Warpage fits an analytic plane", TestWarpageAnalyticPlane);
            yield return new SmokeCase("Warpage tolerance failure retains measurement", TestWarpageToleranceFailure);
            yield return new SmokeCase("Warpage rejects insufficient valid samples", TestWarpageInsufficientSamples);
            yield return new SmokeCase("Warpage rejects collinear geometry", TestWarpageDegenerateGeometry);
            yield return new SmokeCase("Warpage rejects an invalid limit", TestWarpageInvalidParameter);
            yield return new SmokeCase("Datum plane evaluates an analytic raw-height surface", TestDatumPlaneAnalyticSurface);
            yield return new SmokeCase("Datum plane retains measurement for a local-limit failure", TestDatumPlaneToleranceFailure);
            yield return new SmokeCase("Datum plane rejects a near-vertical height-field orientation", TestDatumPlaneNearVertical);
            yield return new SmokeCase("Datum plane treats missing cells separately from valid samples", TestDatumPlaneMissingSamples);
            yield return new SmokeCase("Datum plane rejects mixed planar and height units", TestDatumPlaneMixedUnits);
            yield return new SmokeCase("Two-point line constructs an ordered full-XYZ segment", TestTwoPointLine);
            yield return new SmokeCase("Two-point line rejects a zero-length segment", TestTwoPointLineZeroLength);
            yield return new SmokeCase("Three-point plane preserves authored normal orientation", TestThreePointPlane);
            yield return new SmokeCase("Three-point plane reverses normal when pick order reverses", TestThreePointPlaneOrder);
            yield return new SmokeCase("Three-point plane rejects collinear and near-collinear support", TestThreePointPlaneDegenerate);
            yield return new SmokeCase("Line intersection recovers a perpendicular corner", TestLineIntersection);
            yield return new SmokeCase("Line intersection rejects parallel geometry", TestLineIntersectionParallel);
            yield return new SmokeCase("Full XYZ affine solve recovers an analytic matrix", TestFullXyzAffineSolve);
            yield return new SmokeCase("Full XYZ affine solve rejects a taught condition limit", TestFullXyzAffineCondition);
            yield return new SmokeCase("Full XYZ affine apply preserves locator order and exact transformed XYZ", TestFullXyzAffineApply);
            yield return new SmokeCase("Full XYZ affine apply rejects duplicate source locators", TestFullXyzAffineApplyDuplicateLocator);
            yield return new SmokeCase("Reference-grid re-sampling projects U/V/H cells and preserves holes", TestReferenceGridProjectionAndHoles);
            yield return new SmokeCase("Reference-grid re-sampling chooses deterministic collision winners", TestReferenceGridCollisionTieBreak);
            yield return new SmokeCase("Reference-grid re-sampling rejects half-open upper-bound overflow", TestReferenceGridOutOfBounds);
            yield return new SmokeCase("Reference-grid re-sampling rejects invalid frame axes", TestReferenceGridInvalidAxes);
            yield return new SmokeCase("Median filter removes a spike with the declared kernel", TestDeterministicMedianFilterSpike);
            yield return new SmokeCase("Median filter preserves missing cells and clipped borders", TestDeterministicMedianFilterMissingAndBorder);
            yield return new SmokeCase("Local-median outlier filter excludes the center and preserves the strict threshold", TestDeterministicLocalMedianOutlierFilter);
            yield return new SmokeCase("Level Surface detrends unique reference cells and preserves region evidence", TestLevelSurfaceDetrend);
            yield return new SmokeCase("Level Surface fails closed on insufficient unique reference support", TestLevelSurfaceInsufficientSupport);
            yield return new SmokeCase("Level Frame constructs a deterministic right-handed orthonormal basis", TestLevelFrameBasis);
            yield return new SmokeCase("Level Frame maps fitted-plane samples to zero signed height", TestLevelFramePlaneMapping);
            yield return new SmokeCase("Level Frame rejects non-finite input and honors cancellation", TestLevelFrameGuards);
        }

        private static void TestHeightMapLegacyUnitCompatibility()
        {
            HeightMap3D map = new HeightMap3D(1, 1, 0.0, 0.0, 1.0, 1.0, new[] { 2.0 }, "mm", "legacy-frame", "legacy-source");

            Require(map.Unit == "mm" && map.PlanarUnit == "mm" && map.HeightUnit == "mm", "The legacy unit must populate both explicit units.");
            Require(map.FrameId == "legacy-frame" && map.SourceId == "legacy-source", "Legacy metadata was not preserved.");
            Require(map.CoordinateConvention == "GridXGridYScalarHeight", "The fixed height-map coordinate convention was not exposed.");
        }

        private static void TestHeightMapCrop()
        {
            HeightMap3D source = new HeightMap3D(
                3,
                4,
                10.0,
                20.0,
                0.5,
                0.25,
                new[]
                {
                    1.0, 2.0, 3.0, 4.0,
                    5.0, 6.0, double.NaN, 8.0,
                    9.0, 10.0, 11.0, 12.0
                },
                "mm",
                "raw-height",
                "fixture-top",
                "source.crop");

            HeightMapCropResult result = new HeightMapCropTool().Execute(
                source,
                new HeightMapRoi(1, 1, 2, 3));

            Require(result.Success && result.Output != null, "A valid crop must produce a typed output.");
            Require(result.ValidSampleCount == 5 && result.MissingSampleCount == 1,
                "The crop did not preserve finite and missing sample counts.");
            Require(result.Output.Rows == 2 && result.Output.Columns == 3,
                "The crop did not use the selected dimensions.");
            RequireApproximately(result.Output.OriginX, 10.5, 0.0,
                "The crop did not advance the source-frame X origin.");
            RequireApproximately(result.Output.OriginY, 20.25, 0.0,
                "The crop did not advance the source-frame Y origin.");
            Require(result.Output.PlanarUnit == "mm"
                && result.Output.HeightUnit == "raw-height"
                && result.Output.FrameId == "fixture-top"
                && result.Output.SourceId == "source.crop",
                "The crop did not preserve declared metadata.");
            double[] expected = { 6.0, double.NaN, 8.0, 10.0, 11.0, 12.0 };
            double[] actual = result.Output.CopyValues();
            Require(actual.Length == expected.Length, "The crop value count is incorrect.");
            for (int index = 0; index < expected.Length; index++)
            {
                Require(double.IsNaN(expected[index])
                        ? double.IsNaN(actual[index])
                        : actual[index] == expected[index],
                    "The crop did not preserve exact row-major values.");
            }
        }

        private static void TestHeightMapCropGuards()
        {
            HeightMap3D source = new HeightMap3D(
                2, 2, 0.0, 0.0, 1.0, 1.0,
                new[] { 1.0, 2.0, 3.0, 4.0 },
                "grid-index", "raw-height", "fixture", "source");
            HeightMapCropTool tool = new HeightMapCropTool();
            HeightMapCropResult invalid = tool.Execute(source, new HeightMapRoi(1, 1, 2, 2));
            Require(!invalid.Success && invalid.Output == null,
                "An out-of-grid crop must return a controlled failure without output.");

            CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            bool canceled = false;
            try
            {
                tool.Execute(source, HeightMapRoi.Full(source), cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                canceled = true;
            }
            finally
            {
                cancellation.Dispose();
            }
            Require(canceled, "A canceled crop must propagate OperationCanceledException.");
        }

        private static void TestHeightMapDomainMask()
        {
            HeightMap3D source = new HeightMap3D(
                2,
                3,
                10.0,
                20.0,
                0.5,
                0.25,
                new[] { 1.0, double.NaN, 3.0, 4.0, 5.0, 6.0 },
                "mm",
                "raw-height",
                "fixture-top",
                "source.domain");
            double[] sourceBefore = source.CopyValues();
            HeightGridMask mask = new HeightGridMask(
                2,
                3,
                new[] { true, false, true, false, false, true });

            HeightMapDomainMaskResult result = new HeightMapDomainMaskTool().Execute(source, mask);

            Require(result.Success && result.Output != null,
                "A valid domain mask must produce a typed output.");
            Require(result.ForegroundCellCount == 3
                && result.PreservedValidSampleCount == 3
                && result.PreservedMissingSampleCount == 0
                && result.ReducedToMissingCellCount == 2,
                "The domain mask did not report the exact foreground and reduced-missing counts.");
            Require(result.Output.Rows == source.Rows
                && result.Output.Columns == source.Columns
                && result.Output.OriginX == source.OriginX
                && result.Output.OriginY == source.OriginY
                && result.Output.ColumnPitch == source.ColumnPitch
                && result.Output.RowPitch == source.RowPitch
                && result.Output.PlanarUnit == source.PlanarUnit
                && result.Output.HeightUnit == source.HeightUnit
                && result.Output.FrameId == source.FrameId
                && result.Output.SourceId == source.SourceId,
                "The domain mask did not preserve same-grid geometry and metadata.");
            double[] expected = { 1.0, double.NaN, 3.0, double.NaN, double.NaN, 6.0 };
            double[] actual = result.Output.CopyValues();
            for (int index = 0; index < expected.Length; index++)
            {
                Require(double.IsNaN(expected[index])
                        ? double.IsNaN(actual[index])
                        : actual[index] == expected[index],
                    "The domain mask did not preserve foreground values and reduce background cells to NaN.");
            }

            double[] unchanged = source.CopyValues();
            for (int index = 0; index < sourceBefore.Length; index++)
            {
                Require(double.IsNaN(sourceBefore[index])
                        ? double.IsNaN(unchanged[index])
                        : sourceBefore[index] == unchanged[index],
                    "The domain mask must not mutate source values.");
            }
        }

        private static void TestHeightMapDomainMaskGuards()
        {
            HeightMap3D source = new HeightMap3D(
                2,
                2,
                0.0,
                0.0,
                1.0,
                1.0,
                new[] { 1.0, 2.0, 3.0, 4.0 },
                "grid-index",
                "raw-height",
                "fixture",
                "source");
            HeightMapDomainMaskTool tool = new HeightMapDomainMaskTool();

            HeightMapDomainMaskResult wrongDimensions = tool.Execute(
                source,
                new HeightGridMask(1, 2, new[] { true, true }));
            HeightMapDomainMaskResult wrongCount = tool.Execute(
                source,
                new HeightGridMask(2, 2, new[] { true, false }));
            HeightMapDomainMaskResult empty = tool.Execute(
                source,
                new HeightGridMask(2, 2, new[] { false, false, false, false }));
            Require(!wrongDimensions.Success && wrongDimensions.Output == null
                && !wrongCount.Success && wrongCount.Output == null
                && !empty.Success && empty.Output == null,
                "Invalid, mismatched, and empty domain masks must fail closed without output.");

            CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            bool canceled = false;
            try
            {
                tool.Execute(
                    source,
                    new HeightGridMask(2, 2, new[] { true, false, false, true }),
                    cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                canceled = true;
            }
            finally
            {
                cancellation.Dispose();
            }

            Require(canceled, "A canceled domain-mask operation must propagate OperationCanceledException.");
        }

        private static void TestHeightMapThresholdBackgroundRemoval()
        {
            HeightMap3D source = new HeightMap3D(
                3,
                4,
                10.0,
                20.0,
                0.5,
                0.25,
                new[]
                {
                    double.NaN, 1.0, 3.0, 5.0,
                    2.0, 4.0, 6.0, double.NaN,
                    -1.0, 0.0, 7.0, 8.0
                },
                "mm",
                "raw-height",
                "fixture-top",
                "source.threshold");
            double[] sourceBefore = source.CopyValues();
            HeightMapThresholdBackgroundRemovalTool tool =
                new HeightMapThresholdBackgroundRemovalTool();

            HeightMapThresholdBackgroundRemovalResult above = tool.Execute(
                source,
                new HeightMapThresholdBackgroundRemovalOptions
                {
                    Threshold = 3.0,
                    Mode = HeightThresholdBackgroundRemovalMode.KeepAtOrAboveThreshold
                });
            Require(above.Success && above.Output != null,
                "A valid at-or-above threshold must produce a typed output.");
            Require(above.InputValidSampleCount == 10
                && above.InputMissingSampleCount == 2
                && above.RetainedValidSampleCount == 6
                && above.RemovedBackgroundSampleCount == 4
                && above.OutputMissingSampleCount == 6,
                "The at-or-above threshold did not report exact input, retained, removed, and missing counts.");
            Require(above.Output.Rows == source.Rows
                && above.Output.Columns == source.Columns
                && above.Output.OriginX == source.OriginX
                && above.Output.OriginY == source.OriginY
                && above.Output.ColumnPitch == source.ColumnPitch
                && above.Output.RowPitch == source.RowPitch
                && above.Output.PlanarUnit == source.PlanarUnit
                && above.Output.HeightUnit == source.HeightUnit
                && above.Output.FrameId == source.FrameId
                && above.Output.SourceId == source.SourceId,
                "Threshold removal did not preserve same-grid geometry and metadata.");
            double[] expectedAbove =
            {
                double.NaN, double.NaN, 3.0, 5.0,
                double.NaN, 4.0, 6.0, double.NaN,
                double.NaN, double.NaN, 7.0, 8.0
            };
            AssertHeightValues(above.Output, expectedAbove,
                "At-or-above threshold removal did not preserve inclusive foreground values.");

            HeightMapThresholdBackgroundRemovalResult below = tool.Execute(
                source,
                new HeightMapThresholdBackgroundRemovalOptions
                {
                    Threshold = 3.0,
                    Mode = HeightThresholdBackgroundRemovalMode.KeepAtOrBelowThreshold
                });
            Require(below.Success && below.Output != null
                && below.RetainedValidSampleCount == 5
                && below.RemovedBackgroundSampleCount == 5,
                "The at-or-below threshold did not preserve its inclusive boundary and exact counts.");
            double[] expectedBelow =
            {
                double.NaN, 1.0, 3.0, double.NaN,
                2.0, double.NaN, double.NaN, double.NaN,
                -1.0, 0.0, double.NaN, double.NaN
            };
            AssertHeightValues(below.Output, expectedBelow,
                "At-or-below threshold removal did not preserve inclusive foreground values.");

            double[] unchanged = source.CopyValues();
            AssertHeightValues(unchanged, sourceBefore,
                "Threshold removal must not mutate source values.");
        }

        private static void TestHeightMapThresholdBackgroundRemovalGuards()
        {
            HeightMap3D source = new HeightMap3D(
                1,
                2,
                0.0,
                0.0,
                1.0,
                1.0,
                new[] { 1.0, 2.0 },
                "grid-index",
                "raw-height",
                "fixture",
                "source");
            HeightMapThresholdBackgroundRemovalTool tool =
                new HeightMapThresholdBackgroundRemovalTool();
            HeightMapThresholdBackgroundRemovalResult nonFiniteThreshold = tool.Execute(
                source,
                new HeightMapThresholdBackgroundRemovalOptions
                {
                    Threshold = double.NaN
                });
            HeightMapThresholdBackgroundRemovalResult invalidMode = tool.Execute(
                source,
                new HeightMapThresholdBackgroundRemovalOptions
                {
                    Threshold = 1.0,
                    Mode = (HeightThresholdBackgroundRemovalMode)99
                });
            HeightMap3D emptySource = new HeightMap3D(
                1,
                2,
                0.0,
                0.0,
                1.0,
                1.0,
                new[] { double.NaN, double.NaN },
                "grid-index",
                "raw-height",
                "fixture",
                "empty");
            HeightMapThresholdBackgroundRemovalResult noValidSamples = tool.Execute(
                emptySource,
                new HeightMapThresholdBackgroundRemovalOptions { Threshold = 1.0 });
            Require(!nonFiniteThreshold.Success && nonFiniteThreshold.Output == null
                && !invalidMode.Success && invalidMode.Output == null
                && !noValidSamples.Success && noValidSamples.Output == null,
                "Non-finite threshold, invalid mode, and all-missing source must fail closed without output.");

            CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            bool canceled = false;
            try
            {
                tool.Execute(
                    source,
                    new HeightMapThresholdBackgroundRemovalOptions { Threshold = 1.0 },
                    cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                canceled = true;
            }
            finally
            {
                cancellation.Dispose();
            }

            Require(canceled,
                "A canceled threshold background-removal operation must propagate OperationCanceledException.");
        }

        private static void AssertHeightValues(
            HeightMap3D map,
            IReadOnlyList<double> expected,
            string message)
        {
            AssertHeightValues(map.CopyValues(), expected, message);
        }

        private static void AssertHeightValues(
            IReadOnlyList<double> actual,
            IReadOnlyList<double> expected,
            string message)
        {
            Require(actual.Count == expected.Count, message);
            for (int index = 0; index < expected.Count; index++)
            {
                Require(double.IsNaN(expected[index])
                        ? double.IsNaN(actual[index])
                        : actual[index] == expected[index],
                    message);
            }
        }

        private static void TestHeightMapArrayFactory()
        {
            double[,] values =
            {
                { 1.0, 2.0, 3.0 },
                { 4.0, double.NaN, 6.0 }
            };
            HeightMap3D map = HeightMap3D.FromArray(
                values,
                originX: 10.0,
                originY: 20.0,
                columnPitch: 0.5,
                rowPitch: 0.25,
                planarUnit: "mm",
                heightUnit: "um",
                frameId: "fixture-top",
                sourceId: "array-source");
            values[0, 0] = 99.0;

            Require(map.Rows == 2 && map.Columns == 3, "The array factory did not preserve the rectangular dimensions.");
            RequireApproximately(map.GetHeight(0, 0), 1.0, 0.0, "The array factory did not copy its input.");
            RequireApproximately(map.GetHeight(1, 0), 4.0, 0.0, "The array factory did not preserve row-major values.");
            Require(double.IsNaN(map.GetHeight(1, 1)), "The array factory did not preserve a missing sample.");
            RequireApproximately(map.GetX(2), 11.0, 0.0, "The array factory produced an unexpected X coordinate.");
            RequireApproximately(map.GetY(1), 20.25, 0.0, "The array factory produced an unexpected Y coordinate.");
            Require(map.PlanarUnit == "mm" && map.HeightUnit == "um", "The array factory did not preserve separate units.");
            Require(map.FrameId == "fixture-top" && map.SourceId == "array-source", "The array factory did not preserve source identity.");
        }

        private static void TestHeightMapRejectsInvalidValues()
        {
            bool infinityRejected = false;
            try
            {
                _ = new HeightMap3D(1, 1, 0.0, 0.0, 1.0, 1.0, new[] { double.PositiveInfinity });
            }
            catch (ArgumentException)
            {
                infinityRejected = true;
            }

            bool extentRejected = false;
            try
            {
                _ = new HeightMap3D(1, 2, double.MaxValue, 0.0, double.MaxValue, 1.0, new[] { 0.0, 0.0 });
            }
            catch (ArgumentOutOfRangeException)
            {
                extentRejected = true;
            }

            Require(infinityRejected, "Height-map infinity must be rejected at construction.");
            Require(extentRejected, "A non-finite height-map coordinate extent must be rejected at construction.");
        }

        private static void TestThicknessPass()
        {
            HeightMap3D map = new HeightMap3D(
                2,
                3,
                0.0,
                0.0,
                1.0,
                1.0,
                new[] { 1.0, 1.1, 1.2, 1.3, double.NaN, 1.4 },
                "mm",
                "mm",
                "sensor-top",
                "sample-thickness");
            ThicknessInspectionTool tool = new ThicknessInspectionTool(new ThicknessInspectionOptions
            {
                MinimumThickness = 1.0,
                MaximumThickness = 1.5,
                MinimumValidSamples = 5,
                MinimumValidCoverageRatio = 0.8,
                InputRequirements = new HeightMapInputRequirements("mm", "mm", "sensor-top")
            });

            ThreeDInspectionResult result = tool.Execute(map);

            Require(result.Success, "Thickness pass must succeed.");
            Require(result.HasMeasurement, "Thickness pass must contain a measurement.");
            Require(result.MeasurementOutcome == ThreeDMeasurementOutcome.Passed, "Thickness pass must expose the passed measurement outcome.");
            Require(result.Unit == "mm" && result.PlanarUnit == "mm" && result.HeightUnit == "mm", "Declared map units were not preserved.");
            Require(result.FrameId == "sensor-top" && result.SourceId == "sample-thickness", "Declared map identity was not preserved.");
            Require(result.CoordinateConvention == "GridXGridYScalarHeight", "The result did not preserve the coordinate convention.");
            Require(result.TotalSampleCount == 6 && result.ValidSampleCount == 5 && result.MissingSampleCount == 1, "Unexpected typed thickness sample quality.");
            RequireApproximately(result.ValidCoverageRatio, 5.0 / 6.0, 1e-12, "Unexpected thickness coverage ratio.");
            RequireApproximately(result.Metrics[ThreeDInspectionMetricNames.Quality.ValidSampleCount], 5.0, 0.0, "Unexpected thickness valid sample count.");
            RequireApproximately(result.Metrics[ThreeDInspectionMetricNames.Thickness.Range], 0.4, 1e-12, "Unexpected thickness range.");
            Require(result.TryGetMetric(ThreeDInspectionMetricNames.Thickness.Mean, out double mean, out string meanUnit), "The typed thickness metric name was not found.");
            RequireApproximately(mean, 1.2, 1e-12, "Unexpected thickness mean.");
            Require(meanUnit == "mm" && result.MetricUnits[ThreeDInspectionMetricNames.Quality.ValidCoverageRatio] == "ratio", "Thickness metric units are incomplete.");
        }

        private static void TestThicknessUnitContractMismatch()
        {
            HeightMap3D map = new HeightMap3D(1, 2, 0.0, 0.0, 1.0, 1.0, new[] { 1000.0, 1100.0 }, "mm", "um", "fixture", "unit-mismatch");
            ThreeDInspectionResult result = new ThicknessInspectionTool(new ThicknessInspectionOptions
            {
                MinimumThickness = 0.0,
                MaximumThickness = 2.0,
                InputRequirements = new HeightMapInputRequirements("mm", "mm", "fixture")
            }).Execute(map);

            Require(!result.HasMeasurement && result.ErrorCode == ThreeDInspectionErrorCode.InputContractMismatch, "A height-unit mismatch must fail before measurement.");
            Require(result.MeasurementOutcome == ThreeDMeasurementOutcome.NotMeasured, "A rejected input must expose the not-measured outcome.");
            Require(result.ResultStatus == ThreeDInspectionResultStatus.InvalidInput, "A unit mismatch must be an invalid input result.");
            Require(result.PlanarUnit == "mm" && result.HeightUnit == "um", "The rejected input units were not retained for diagnostics.");
        }

        private static void TestThicknessFrameContractMismatch()
        {
            ThreeDInspectionResult result = new ThicknessInspectionTool(new ThicknessInspectionOptions
            {
                MinimumThickness = 0.0,
                MaximumThickness = 2.0,
                InputRequirements = new HeightMapInputRequirements("mm", "mm", "fixture-top")
            }).Execute(CreateThicknessMap());

            Require(!result.HasMeasurement && result.ErrorCode == ThreeDInspectionErrorCode.InputContractMismatch, "A frame mismatch must fail before measurement.");
            Require(result.FrameId == "sensor-top", "The rejected source frame was not retained for diagnostics.");
        }

        private static void TestThicknessInsufficientCoverage()
        {
            HeightMap3D map = new HeightMap3D(1, 4, 0.0, 0.0, 1.0, 1.0, new[] { 1.0, double.NaN, 1.1, double.NaN }, "mm", "fixture", "coverage");
            ThreeDInspectionResult result = new ThicknessInspectionTool(new ThicknessInspectionOptions
            {
                MinimumThickness = 0.0,
                MaximumThickness = 2.0,
                MinimumValidSamples = 1,
                MinimumValidCoverageRatio = 0.75
            }).Execute(map);

            Require(!result.HasMeasurement && result.ErrorCode == ThreeDInspectionErrorCode.InsufficientValidCoverage, "Coverage below the configured minimum must fail without a measurement.");
            Require(result.TotalSampleCount == 4 && result.ValidSampleCount == 2 && result.MissingSampleCount == 2, "Coverage failure must retain sample-quality evidence.");
            RequireApproximately(result.ValidCoverageRatio, 0.5, 0.0, "Unexpected failed coverage ratio.");
            Require(result.MetricUnits["ValidCoverageRatio"] == "ratio", "Coverage failure must retain the ratio unit.");
        }

        private static void TestThicknessToleranceFailure()
        {
            ThicknessInspectionTool tool = new ThicknessInspectionTool(new ThicknessInspectionOptions
            {
                MinimumThickness = 1.0,
                MaximumThickness = 1.25
            });

            ThreeDInspectionResult result = tool.Execute(CreateThicknessMap());

            Require(!result.Success, "Out-of-tolerance thickness must fail.");
            Require(result.HasMeasurement, "Out-of-tolerance thickness must retain the measurement.");
            Require(result.MeasurementOutcome == ThreeDMeasurementOutcome.OutOfTolerance, "An out-of-tolerance result must remain a completed measurement.");
            Require(result.ResultStatus == ThreeDInspectionResultStatus.Failed, "Out-of-tolerance thickness must be a failed measurement, not an input error.");
            RequireApproximately(result.Metrics[ThreeDInspectionMetricNames.Thickness.AboveUpperLimitCount], 2.0, 0.0, "Unexpected thickness upper-limit count.");
        }

        private static void TestThicknessInvalidRoi()
        {
            ThicknessInspectionTool tool = new ThicknessInspectionTool(new ThicknessInspectionOptions
            {
                MinimumThickness = 0.0,
                MaximumThickness = 10.0,
                Roi = new HeightMapRoi(2, 0, 1, 1)
            });

            ThreeDInspectionResult result = tool.Execute(CreateThicknessMap());

            Require(result.ResultStatus == ThreeDInspectionResultStatus.InvalidRoi, "Invalid thickness ROI must be rejected.");
            Require(result.MeasurementOutcome == ThreeDMeasurementOutcome.NotMeasured, "An invalid ROI must expose the not-measured outcome.");
        }

        private static void TestThicknessInsufficientSamples()
        {
            HeightMap3D map = new HeightMap3D(
                1,
                3,
                0.0,
                0.0,
                1.0,
                1.0,
                new[] { 1.0, double.NaN, double.NaN });
            ThicknessInspectionTool tool = new ThicknessInspectionTool(new ThicknessInspectionOptions
            {
                MinimumThickness = 0.0,
                MaximumThickness = 2.0,
                MinimumValidSamples = 2
            });

            ThreeDInspectionResult result = tool.Execute(map);

            Require(result.ResultStatus == ThreeDInspectionResultStatus.InsufficientData, "Insufficient thickness samples must be rejected.");
        }

        private static void TestWarpageAnalyticPlane()
        {
            HeightMap3D map = CreatePlaneMap(3, 3, 0.5, -0.25, 2.0);
            WarpageInspectionTool tool = new WarpageInspectionTool(new WarpageInspectionOptions
            {
                MaximumPeakToValley = 1e-10,
                MaximumRms = 1e-10,
                MinimumValidSamples = 9
            });

            ThreeDInspectionResult result = tool.Execute(map);

            Require(result.Success, "An analytic plane must pass warpage inspection.");
            Require(result.PlaneFit != null, "Warpage must expose the fitted plane.");
            RequireApproximately(result.PlaneFit.SlopeX, 0.5, 1e-12, "Unexpected warpage X slope.");
            RequireApproximately(result.PlaneFit.SlopeY, -0.25, 1e-12, "Unexpected warpage Y slope.");
            RequireApproximately(result.PlaneFit.Intercept, 2.0, 1e-12, "Unexpected warpage intercept.");
            RequireApproximately(result.Metrics["PeakToValley"], 0.0, 1e-10, "Unexpected analytic-plane peak-to-valley.");
            Require(result.MetricUnits["PlaneSlopeX"] == "mm/mm" && result.MetricUnits["PeakToValley"] == "mm", "Warpage metric units are incomplete.");
        }

        private static void TestWarpageToleranceFailure()
        {
            HeightMap3D map = new HeightMap3D(
                3,
                3,
                0.0,
                0.0,
                1.0,
                1.0,
                new[]
                {
                    0.0, 0.0, 0.0,
                    0.0, 1.0, 0.0,
                    0.0, 0.0, 0.0
                });
            WarpageInspectionTool tool = new WarpageInspectionTool(new WarpageInspectionOptions
            {
                MaximumPeakToValley = 0.1,
                MaximumRms = 0.1
            });

            ThreeDInspectionResult result = tool.Execute(map);

            Require(!result.Success, "Non-planar data must fail the tight warpage limit.");
            Require(result.HasMeasurement, "Out-of-tolerance warpage must retain the measurement.");
            Require(result.Metrics["PeakToValley"] > 0.1, "Expected a measurable warpage peak-to-valley.");
        }

        private static void TestWarpageInsufficientSamples()
        {
            HeightMap3D map = new HeightMap3D(
                2,
                2,
                0.0,
                0.0,
                1.0,
                1.0,
                new[] { double.NaN, double.NaN, double.NaN, double.NaN });
            WarpageInspectionTool tool = new WarpageInspectionTool(new WarpageInspectionOptions
            {
                MaximumPeakToValley = 1.0
            });

            ThreeDInspectionResult result = tool.Execute(map);

            Require(result.ResultStatus == ThreeDInspectionResultStatus.InsufficientData, "Warpage must reject empty finite data.");
        }

        private static void TestWarpageDegenerateGeometry()
        {
            HeightMap3D map = new HeightMap3D(
                1,
                3,
                0.0,
                0.0,
                1.0,
                1.0,
                new[] { 0.0, 1.0, 2.0 });
            WarpageInspectionTool tool = new WarpageInspectionTool(new WarpageInspectionOptions
            {
                MaximumPeakToValley = 1.0
            });

            ThreeDInspectionResult result = tool.Execute(map);

            Require(result.ResultStatus == ThreeDInspectionResultStatus.DegenerateGeometry, "Collinear warpage data must be rejected.");
        }

        private static void TestWarpageInvalidParameter()
        {
            WarpageInspectionTool tool = new WarpageInspectionTool(new WarpageInspectionOptions
            {
                MaximumPeakToValley = -0.1
            });

            ThreeDInspectionResult result = tool.Execute(CreatePlaneMap(2, 2, 0.0, 0.0, 1.0));

            Require(result.ResultStatus == ThreeDInspectionResultStatus.InvalidParameter, "Negative warpage limit must be rejected.");
        }

        private static void TestTwoPointLine()
        {
            TwoPointLineResult result = new TwoPointLineTool().Execute(
                new TwoPointLineInput(new ThreeDPoint(1.0, 2.0, 3.0), new ThreeDPoint(4.0, 6.0, 3.0)));

            Require(result.Success, "Two-point line must succeed for distinct finite points.");
            RequireApproximately(result.SegmentLength, 5.0, 1e-12, "Unexpected two-point segment length.");
            RequireApproximately(result.Direction.X, 0.6, 1e-12, "Unexpected two-point X direction.");
            RequireApproximately(result.Direction.Y, 0.8, 1e-12, "Unexpected two-point Y direction.");
            Require(result.SegmentStart.X == 1.0 && result.SegmentEnd.X == 4.0, "Two-point authored order was not retained.");
        }

        private static void TestTwoPointLineZeroLength()
        {
            ThreeDPoint point = new ThreeDPoint(1.0, 2.0, 3.0);
            TwoPointLineResult result = new TwoPointLineTool().Execute(new TwoPointLineInput(point, point));

            Require(!result.Success, "Two-point line must reject a zero-length segment.");
        }

        private static void TestDatumPlaneAnalyticSurface()
        {
            ThreeDInspectionResult result = new DatumPlaneRawHeightDeviationInspectionTool(
                new DatumPlaneRawHeightDeviationInspectionOptions
                {
                    PlaneNormalX = -2.0,
                    PlaneNormalY = 1.0,
                    PlaneNormalZ = -3.0,
                    PlaneOffset = -5.0,
                    MaximumPeakToValleyRawHeight = 0.000001
                }).Execute(CreatePlaneMap(3, 3, 2.0, 3.0, 5.0));

            Require(result.Success && result.HasMeasurement, "Analytic datum-plane surface must pass.");
            RequireApproximately(result.Metrics["PeakToValleyRawHeight"], 0.0, 1e-12, "Unexpected datum-plane P2V.");
            RequireApproximately(result.Metrics["RmsRawHeightResidual"], 0.0, 1e-12, "Unexpected datum-plane RMS.");
            RequireApproximately(result.Metrics["PlaneNormalY"], 1.0 / Math.Sqrt(14.0), 1e-12, "Datum-plane normal must be normalized.");
        }

        private static void TestDatumPlaneToleranceFailure()
        {
            HeightMap3D source = CreatePlaneMap(3, 3, 2.0, 3.0, 5.0);
            double[] values = source.CopyValues();
            values[values.Length - 1] += 0.1;
            source = new HeightMap3D(3, 3, 0.0, 0.0, 1.0, 1.0, values, "raw-height", "frame", "datum-failure");
            ThreeDInspectionResult result = new DatumPlaneRawHeightDeviationInspectionTool(
                new DatumPlaneRawHeightDeviationInspectionOptions
                {
                    PlaneNormalX = -2.0,
                    PlaneNormalY = 1.0,
                    PlaneNormalZ = -3.0,
                    PlaneOffset = -5.0,
                    MaximumPeakToValleyRawHeight = 0.001
                }).Execute(source);

            Require(!result.Success && result.HasMeasurement && result.ResultStatus == ThreeDInspectionResultStatus.Failed, "Out-of-limit datum-plane result must retain measurement evidence.");
            Require(result.Metrics["PeakToValleyRawHeight"] > 0.001, "Datum-plane failure must expose the P2V evidence.");
        }

        private static void TestDatumPlaneNearVertical()
        {
            ThreeDInspectionResult result = new DatumPlaneRawHeightDeviationInspectionTool(
                new DatumPlaneRawHeightDeviationInspectionOptions
                {
                    PlaneNormalX = 1.0,
                    PlaneNormalY = 0.01,
                    PlaneNormalZ = 0.0,
                    PlaneOffset = 0.0,
                    MaximumPeakToValleyRawHeight = 1.0
                }).Execute(CreatePlaneMap(2, 2, 0.0, 0.0, 1.0));

            Require(!result.HasMeasurement && result.ErrorCode == ThreeDInspectionErrorCode.DegenerateGeometry, "Near-vertical plane must be rejected before raw-height residual evaluation.");
        }

        private static void TestDatumPlaneMissingSamples()
        {
            HeightMap3D source = new HeightMap3D(2, 2, 0.0, 0.0, 1.0, 1.0, new[] { 5.0, double.NaN, 5.0, 6.0 }, "raw-height", "frame", "datum-missing");
            ThreeDInspectionResult result = new DatumPlaneRawHeightDeviationInspectionTool(
                new DatumPlaneRawHeightDeviationInspectionOptions
                {
                    PlaneNormalX = -1.0,
                    PlaneNormalY = 1.0,
                    PlaneNormalZ = 0.0,
                    PlaneOffset = -5.0,
                    MaximumPeakToValleyRawHeight = 0.000001,
                    MinimumValidSamples = 3
                }).Execute(source);

            Require(result.Success && result.HasMeasurement, "Three finite datum-plane samples must remain measurable.");
            RequireApproximately(result.Metrics["ValidSampleCount"], 3.0, 1e-12, "Unexpected datum-plane valid count.");
            RequireApproximately(result.Metrics["MissingSampleCount"], 1.0, 1e-12, "Unexpected datum-plane missing count.");
            Require(result.TotalSampleCount == 4 && result.ValidSampleCount == 3 && result.MissingSampleCount == 1, "Unexpected typed datum-plane sample quality.");
            RequireApproximately(result.ValidCoverageRatio, 0.75, 0.0, "Unexpected datum-plane coverage ratio.");
            Require(result.MetricUnits["PeakToValleyRawHeight"] == "raw-height", "Datum-plane metric unit was not preserved.");
        }

        private static void TestDatumPlaneMixedUnits()
        {
            HeightMap3D source = new HeightMap3D(
                2,
                2,
                0.0,
                0.0,
                1.0,
                1.0,
                new[] { 1.0, 1.0, 1.0, 1.0 },
                "mm",
                "um",
                "fixture",
                "datum-mixed-units");
            ThreeDInspectionResult result = new DatumPlaneRawHeightDeviationInspectionTool(
                new DatumPlaneRawHeightDeviationInspectionOptions
                {
                    PlaneNormalX = 0.0,
                    PlaneNormalY = 1.0,
                    PlaneNormalZ = 0.0,
                    PlaneOffset = -1.0,
                    MaximumPeakToValleyRawHeight = 0.1
                }).Execute(source);

            Require(!result.HasMeasurement && result.ErrorCode == ThreeDInspectionErrorCode.InputContractMismatch, "Datum-plane inspection must reject mixed coordinate units without conversion.");
        }

        private static void TestThreePointPlane()
        {
            ThreePointPlaneResult result = new ThreePointPlaneTool().Execute(
                new ThreePointPlaneInput(
                    new ThreeDPoint(1.0, 2.0, 3.0),
                    new ThreeDPoint(4.0, 2.0, 3.0),
                    new ThreeDPoint(1.0, 6.0, 3.0)));

            Require(result.Success, "Three-point plane must succeed for a non-collinear ordered triple.");
            RequireApproximately(result.Normal.X, 0.0, 1e-12, "Unexpected three-point plane normal X.");
            RequireApproximately(result.Normal.Y, 0.0, 1e-12, "Unexpected three-point plane normal Y.");
            RequireApproximately(result.Normal.Z, 1.0, 1e-12, "Unexpected three-point plane normal Z.");
            RequireApproximately(result.PlaneOffset, -3.0, 1e-12, "Unexpected three-point plane offset.");
            Require(result.SupportFirst.X == 1.0 && result.SupportSecond.X == 4.0 && result.SupportThird.Y == 6.0, "Three-point support order was not retained.");
        }

        private static void TestThreePointPlaneOrder()
        {
            ThreePointPlaneResult result = new ThreePointPlaneTool().Execute(
                new ThreePointPlaneInput(
                    new ThreeDPoint(1.0, 2.0, 3.0),
                    new ThreeDPoint(1.0, 6.0, 3.0),
                    new ThreeDPoint(4.0, 2.0, 3.0)));

            Require(result.Success, "Reordered non-collinear three-point plane must remain valid.");
            RequireApproximately(result.Normal.Z, -1.0, 1e-12, "Reordered support must reverse the oriented normal.");
            RequireApproximately(result.PlaneOffset, 3.0, 1e-12, "Reordered support must reverse the oriented plane offset.");
        }

        private static void TestThreePointPlaneDegenerate()
        {
            ThreePointPlaneResult collinear = new ThreePointPlaneTool().Execute(
                new ThreePointPlaneInput(
                    new ThreeDPoint(0.0, 0.0, 0.0),
                    new ThreeDPoint(1.0, 0.0, 0.0),
                    new ThreeDPoint(2.0, 0.0, 0.0)));
            ThreePointPlaneResult nearCollinear = new ThreePointPlaneTool().Execute(
                new ThreePointPlaneInput(
                    new ThreeDPoint(0.0, 0.0, 0.0),
                    new ThreeDPoint(1.0, 0.0, 0.0),
                    new ThreeDPoint(2.0, 1e-13, 0.0)));

            Require(!collinear.Success && !nearCollinear.Success, "Collinear and near-collinear support must be rejected.");
        }

        private static void TestLineIntersection()
        {
            LineIntersectionResult result = new LineIntersectionTool().Execute(
                CreateLine(new ThreeDPoint(0.0, 0.0, 0.0), new ThreeDPoint(1.0, 0.0, 0.0), new ThreeDPoint(-2.0, 0.0, 0.0), new ThreeDPoint(2.0, 0.0, 0.0)),
                CreateLine(new ThreeDPoint(0.0, 0.0, 0.0), new ThreeDPoint(0.0, 1.0, 0.0), new ThreeDPoint(0.0, -2.0, 0.0), new ThreeDPoint(0.0, 2.0, 0.0)),
                new LineIntersectionOptions
                {
                    MaximumClosestApproachDistance = 0.001,
                    MinimumAcuteAngleDegrees = 45.0,
                    MaximumSupportExtension = 0.0
                });

            Require(result.Success, "Perpendicular full-XYZ lines must intersect.");
            RequireApproximately(result.ClosestApproachDistance, 0.0, 1e-12, "Unexpected line-intersection gap.");
            RequireApproximately(result.AcuteAngleDegrees, 90.0, 1e-12, "Unexpected line-intersection acute angle.");
            RequireApproximately(result.CornerAnchor.X, 0.0, 1e-12, "Unexpected line-intersection corner X.");
        }

        private static void TestLineIntersectionParallel()
        {
            LineIntersectionResult result = new LineIntersectionTool().Execute(
                CreateLine(new ThreeDPoint(0.0, 0.0, 0.0), new ThreeDPoint(1.0, 0.0, 0.0), new ThreeDPoint(-2.0, 0.0, 0.0), new ThreeDPoint(2.0, 0.0, 0.0)),
                CreateLine(new ThreeDPoint(0.0, 1.0, 0.0), new ThreeDPoint(1.0, 0.0, 0.0), new ThreeDPoint(-2.0, 1.0, 0.0), new ThreeDPoint(2.0, 1.0, 0.0)),
                new LineIntersectionOptions
                {
                    MaximumClosestApproachDistance = 10.0,
                    MinimumAcuteAngleDegrees = 1.0,
                    MaximumSupportExtension = 1.0
                });

            Require(!result.Success, "Parallel full-XYZ lines must be rejected.");
        }

        private static ThreeDLineGeometry CreateLine(ThreeDPoint anchor, ThreeDPoint direction, ThreeDPoint start, ThreeDPoint end)
        {
            return new ThreeDLineGeometry(anchor, direction, start, end);
        }

        private static void TestFullXyzAffineSolve()
        {
            FullXyzAffineSolveResult result = new FullXyzAffineSolveTool().Execute(
                CreateAffinePairs(),
                new FullXyzAffineSolveOptions { MaximumConditionEstimate = 1000.0, ArithmeticResidualWarning = 1e-10 });

            Require(result.Success, "Full XYZ affine solve must recover four independent pairs.");
            RequireApproximately(result.Matrix.M11, 2.0, 1e-12, "Unexpected affine M11.");
            RequireApproximately(result.Matrix.M12, 0.5, 1e-12, "Unexpected affine M12.");
            RequireApproximately(result.Matrix.M13, -0.25, 1e-12, "Unexpected affine M13.");
            RequireApproximately(result.Matrix.M14, 10.0, 1e-12, "Unexpected affine M14.");
            RequireApproximately(result.ArithmeticMaximumResidual, 0.0, 1e-10, "Exact affine residual must be zero.");
        }

        private static void TestFullXyzAffineCondition()
        {
            FullXyzAffineSolveResult result = new FullXyzAffineSolveTool().Execute(
                CreateAffinePairs(),
                new FullXyzAffineSolveOptions { MaximumConditionEstimate = 0.5, ArithmeticResidualWarning = 0.0 });

            Require(!result.Success, "Full XYZ affine solve must reject an exceeded taught condition limit.");
        }

        private static void TestFullXyzAffineApply()
        {
            FullXyzAffineMatrix matrix = new FullXyzAffineMatrix(
                2.0, 0.5, -0.25, 10.0,
                -1.0, 3.0, 0.75, 20.0,
                0.25, -0.5, 4.0, 30.0);
            AffinePointCloudApplyResult result = new AffinePointCloudApplyTool().Execute(
                new[]
                {
                    new AffinePointCloudInputPoint(2, 3, 7.0, 3.0, 7.0, 2.0),
                    new AffinePointCloudInputPoint(5, 11, -2.0, 11.0, -2.0, 5.0)
                },
                matrix);

            Require(result.Success && result.Points.Count == 2, "Full XYZ affine apply must transform every supplied finite point.");
            Require(result.Points[0].Row == 2 && result.Points[0].Column == 3 && result.Points[0].RawHeight == 7.0, "Full XYZ affine apply must preserve the source locator and raw scalar.");
            RequireApproximately(result.Points[0].TransformedX, 19.0, 1e-12, "Unexpected transformed X.");
            RequireApproximately(result.Points[0].TransformedY, 39.5, 1e-12, "Unexpected transformed Y.");
            RequireApproximately(result.Points[0].TransformedZ, 35.25, 1e-12, "Unexpected transformed Z.");
        }

        private static void TestFullXyzAffineApplyDuplicateLocator()
        {
            AffinePointCloudApplyResult result = new AffinePointCloudApplyTool().Execute(
                new[]
                {
                    new AffinePointCloudInputPoint(0, 0, 1.0, 0.0, 1.0, 0.0),
                    new AffinePointCloudInputPoint(0, 0, 2.0, 0.0, 2.0, 0.0)
                },
                new FullXyzAffineMatrix(
                    1.0, 0.0, 0.0, 0.0,
                    0.0, 1.0, 0.0, 0.0,
                    0.0, 0.0, 1.0, 0.0));

            Require(!result.Success, "Full XYZ affine apply must reject duplicate source locators.");
        }

        private static void TestReferenceGridProjectionAndHoles()
        {
            ReferenceGridRegridResult result = new ReferenceGridRegridTool().Execute(
                new[]
                {
                    new ReferenceGridInputPoint(2, 4, 0.10, 0.10, 10.0),
                    new ReferenceGridInputPoint(2, 5, 1.10, 0.10, 20.0),
                    new ReferenceGridInputPoint(3, 4, 0.10, 1.10, 30.0)
                },
                CreateReferenceGridProfile(2, 2, 0.70));

            Require(result.Success && result.Cells.Count == 4, "Reference-grid re-sampling must emit every authored row-major cell.");
            RequireApproximately(result.Cells[0].Height, 10.0, 1e-12, "Unexpected first projected height.");
            RequireApproximately(result.Cells[1].Height, 20.0, 1e-12, "Unexpected second projected height.");
            RequireApproximately(result.Cells[2].Height, 30.0, 1e-12, "Unexpected third projected height.");
            Require(!double.IsNaN(result.Cells[0].PlanarDistanceSquared), "Reference-grid populated cells must retain winner planar-distance evidence.");
            Require(double.IsNaN(result.Cells[3].Height) && result.Cells[3].SourceRow == -1 && double.IsNaN(result.Cells[3].PlanarDistanceSquared), "Reference-grid holes must remain missing without fill.");
            RequireApproximately(result.CoverageRatio, 0.75, 1e-12, "Unexpected reference-grid coverage.");
            Require(result.MeetsMinimumCoverage, "Coverage must meet the authored Publish minimum.");
        }

        private static void TestReferenceGridCollisionTieBreak()
        {
            ReferenceGridRegridResult result = new ReferenceGridRegridTool().Execute(
                new[]
                {
                    new ReferenceGridInputPoint(9, 9, 0.75, 0.50, 90.0),
                    new ReferenceGridInputPoint(3, 8, 0.25, 0.50, 30.0),
                    new ReferenceGridInputPoint(3, 7, 0.25, 0.50, 20.0)
                },
                CreateReferenceGridProfile(1, 1, 1.0));

            Require(result.Success && result.CollisionCount == 2 && result.PopulatedCellCount == 1, "Reference-grid collisions must be counted without adding cells.");
            Require(result.Cells[0].SourceRow == 3 && result.Cells[0].SourceColumn == 7, "Equal planar-distance collisions must choose lower source row then column.");
            RequireApproximately(result.Cells[0].Height, 20.0, 1e-12, "Collision winner height was not retained.");
        }

        private static void TestReferenceGridOutOfBounds()
        {
            ReferenceGridRegridResult result = new ReferenceGridRegridTool().Execute(
                new[] { new ReferenceGridInputPoint(0, 0, 1.0, 0.0, 2.0) },
                CreateReferenceGridProfile(1, 1, 0.0));

            Require(!result.Success && result.Message.IndexOf("half-open", StringComparison.OrdinalIgnoreCase) >= 0, "Reference-grid upper U boundary must be rejected rather than assigned outside the grid.");
        }

        private static void TestReferenceGridInvalidAxes()
        {
            ReferenceGridProfile invalid = new ReferenceGridProfile(
                "frame.fixture-reference", "fixture-unit", "fixture reference", "R1",
                0.0, 0.0, 0.0,
                1.0, 0.0, 0.0,
                1.0, 0.0, 0.0,
                0.0, 0.0, 1.0,
                1.0, 1.0, 1, 1, 0.0);
            ReferenceGridRegridResult result = new ReferenceGridRegridTool().Execute(
                new[] { new ReferenceGridInputPoint(0, 0, 0.0, 0.0, 0.0) }, invalid);

            Require(!result.Success && result.Message.IndexOf("orthonormal", StringComparison.OrdinalIgnoreCase) >= 0, "Reference-grid non-orthonormal axes must be rejected.");
        }

        private static void TestDeterministicMedianFilterSpike()
        {
            DeterministicMedianFilterResult result = new DeterministicMedianFilterTool().Execute(
                3,
                3,
                new[] { 1.0, 1.0, 1.0, 1.0, 100.0, 1.0, 1.0, 1.0, 1.0 },
                new DeterministicMedianFilterOptions { KernelSize = 3 });

            Require(result.Success && result.Values.All(value => value == 1.0) && result.ChangedCount == 1,
                "Median filter must remove one isolated center spike.");
        }

        private static void TestDeterministicMedianFilterMissingAndBorder()
        {
            DeterministicMedianFilterResult missing = new DeterministicMedianFilterTool().Execute(
                3,
                1,
                new[] { 1.0, double.NaN, 5.0 },
                new DeterministicMedianFilterOptions { KernelSize = 3 });
            DeterministicMedianFilterResult border = new DeterministicMedianFilterTool().Execute(
                2,
                2,
                new[] { 1.0, 2.0, 3.0, 4.0 },
                new DeterministicMedianFilterOptions { KernelSize = 3 });

            Require(missing.Success && missing.Values[0] == 1.0 && double.IsNaN(missing.Values[1]) && missing.Values[2] == 5.0,
                "Median filter must preserve the source missing mask.");
            Require(border.Success && border.Values.All(value => value == 2.5),
                "Median filter borders must use available neighbors only.");
        }

        private static void TestDeterministicLocalMedianOutlierFilter()
        {
            double[] values =
            {
                1.0, 1.0, 1.0,
                1.0, 100.0, 1.0,
                1.0, 1.0, 1.0
            };
            DeterministicLocalMedianOutlierFilterResult result =
                new DeterministicLocalMedianOutlierFilterTool().Execute(
                    3,
                    3,
                    values,
                    new DeterministicLocalMedianOutlierFilterOptions
                    {
                        WindowSize = 3,
                        MaximumAbsoluteDeviation = 20.0,
                        MinimumValidNeighbors = 3
                    });
            double[] thresholdValues =
            {
                1.0, 1.0, 1.0,
                1.0, 21.0, 1.0,
                1.0, 1.0, 1.0
            };
            DeterministicLocalMedianOutlierFilterResult threshold =
                new DeterministicLocalMedianOutlierFilterTool().Execute(
                    3,
                    3,
                    thresholdValues,
                    new DeterministicLocalMedianOutlierFilterOptions
                    {
                        WindowSize = 3,
                        MaximumAbsoluteDeviation = 20.0,
                        MinimumValidNeighbors = 3
                    });

            Require(result.Success
                && result.OutlierIndices.Count == 1
                && result.OutlierIndices[0] == 4
                && double.IsNaN(result.Values[4]),
                "The local-median filter must remove only the isolated center spike.");
            Require(threshold.Success
                && threshold.OutlierIndices.Count == 0
                && threshold.Values[4] == 21.0,
                "Deviation exactly equal to the threshold must be retained.");
        }

        private static void TestLevelSurfaceDetrend()
        {
            const int rows = 4;
            const int columns = 4;
            double[] values = new double[rows * columns];
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    values[(row * columns) + column] =
                        10.0 + (2.0 * column) - (0.5 * row);
                }
            }

            LevelSurfaceResult result = new LevelSurfaceTool().Execute(
                rows,
                columns,
                values,
                new[]
                {
                    new LevelSurfaceRegion(0, 0, 4, 3),
                    new LevelSurfaceRegion(0, 2, 4, 2)
                },
                new LevelSurfaceOptions { MinimumValidSampleCount = 12 });

            Require(result.Success
                && result.ReferenceSampleCount == 16
                && result.RegionEvidence.Count == 2
                && result.RegionEvidence[0].ValidSampleCount == 12
                && result.RegionEvidence[1].ValidSampleCount == 8,
                "Level Surface must de-duplicate overlapping reference cells while retaining per-region counts.");
            RequireApproximately(result.FittedSlopeX, 2.0, 1e-12,
                "Unexpected Level Surface input X slope.");
            RequireApproximately(result.FittedSlopeZ, -0.5, 1e-12,
                "Unexpected Level Surface input Z slope.");
            RequireApproximately(result.OutputReferenceSlopeX, 0.0, 1e-12,
                "Level Surface must remove the reference X slope.");
            RequireApproximately(result.OutputReferenceSlopeZ, 0.0, 1e-12,
                "Level Surface must remove the reference Z slope.");
            Require(result.Values.All(value => Math.Abs(value - 12.25) < 1e-12),
                "Level Surface must detrend every finite cell to the reference mean.");
        }

        private static void TestLevelSurfaceInsufficientSupport()
        {
            LevelSurfaceResult result = new LevelSurfaceTool().Execute(
                2,
                2,
                new[] { 1.0, double.NaN, 2.0, 3.0 },
                new[] { new LevelSurfaceRegion(0, 0, 2, 2) },
                new LevelSurfaceOptions { MinimumValidSampleCount = 4 });

            Require(!result.Success
                && result.Values.Count == 0
                && result.Message.IndexOf(
                    "unique finite reference samples",
                    StringComparison.OrdinalIgnoreCase) >= 0,
                "Level Surface must fail closed when unique finite support is insufficient.");
        }

        private static void TestLevelFrameBasis()
        {
            LevelFrameResult result = new LevelFrameTool().Execute(
                new LevelFramePlane(0.0, 0.0, 12.5));

            Require(result.Success && result.SourceToFrameValues.Count == 12,
                "Level Frame must return one complete source-to-frame 3x4 matrix.");
            RequireApproximately(result.Origin.X, 0.0, 0.0, "Level Frame origin X must use the grid origin.");
            RequireApproximately(result.Origin.Y, 12.5, 0.0, "Level Frame origin must lie on the fitted plane.");
            RequireApproximately(result.Origin.Z, 0.0, 0.0, "Level Frame origin Z must use the grid origin.");
            RequireApproximately(result.UAxis.X, 1.0, 1e-12, "Level Frame U must deterministically project +X.");
            RequireApproximately(result.UAxis.Y, 0.0, 1e-12, "Level Frame U must lie on the fitted plane.");
            RequireApproximately(result.UAxis.Z, 0.0, 1e-12, "Level Frame U must lie on the fitted plane.");
            RequireApproximately(result.VAxis.Z, -1.0, 1e-12, "Level Frame V must preserve a right-handed U/V/H convention.");
            RequireApproximately(result.HAxis.Y, 1.0, 1e-12, "Level Frame H must point toward positive Y.");
            RequireApproximately(result.LinearDeterminant, 1.0, 1e-12, "Level Frame basis must have determinant +1.");
        }

        private static void TestLevelFramePlaneMapping()
        {
            const double slopeX = 0.5;
            const double slopeZ = -0.25;
            const double intercept = 40.0;
            LevelFrameResult result = new LevelFrameTool().Execute(
                new LevelFramePlane(slopeX, slopeZ, intercept));

            Require(result.Success, "A finite fitted plane must produce a Level Frame.");
            double[] points = { 0.0, 40.0, 0.0, 3.0, 41.0, 2.0, -4.0, 36.5, 6.0 };
            for (int index = 0; index < points.Length; index += 3)
            {
                double x = points[index];
                double y = points[index + 1];
                double z = points[index + 2];
                double h = (result.HAxis.X * x)
                    + (result.HAxis.Y * y)
                    + (result.HAxis.Z * z)
                    - ((result.HAxis.X * result.Origin.X)
                        + (result.HAxis.Y * result.Origin.Y)
                        + (result.HAxis.Z * result.Origin.Z));
                RequireApproximately(h, 0.0, 1e-12,
                    "A point on the fitted plane must map to zero Level Frame H.");
            }
        }

        private static void TestLevelFrameGuards()
        {
            LevelFrameResult invalid = new LevelFrameTool().Execute(
                new LevelFramePlane(double.NaN, 0.0, 1.0));
            Require(!invalid.Success
                && invalid.Message.IndexOf("finite", StringComparison.OrdinalIgnoreCase) >= 0,
                "Level Frame must fail closed for non-finite plane parameters.");

            CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            bool canceled = false;
            try
            {
                new LevelFrameTool().Execute(
                    new LevelFramePlane(0.1, -0.2, 3.0),
                    cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                canceled = true;
            }
            finally
            {
                cancellation.Dispose();
            }

            Require(canceled, "Level Frame must honor cancellation before constructing output.");
        }

    }
}

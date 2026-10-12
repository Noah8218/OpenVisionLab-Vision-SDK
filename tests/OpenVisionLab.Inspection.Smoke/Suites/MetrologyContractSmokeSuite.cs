using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Tool;
using OpenVisionLab.Vision3D.Geometry;
using OpenVisionLab.Vision3D.Inspection;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class MetrologyContractSmokeSuite
    {
        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase("Metrology contracts preserve independent anisotropic affine positions and recovery", TestAffinePositions);
            yield return new SmokeCase("Metrology contracts keep unit declarations separate from scalar conversion", TestDeclaredUnits);
            yield return new SmokeCase("Metrology contracts distinguish adjacent tolerance boundaries and insufficient data", TestToleranceAndQuality);
            yield return new SmokeCase("Metrology contracts preserve independent tilted datum height meaning and recovery", TestDatumHeight);
            yield return new SmokeCase("Metrology contracts preserve finite residual cancellation and tiny-normal fallback", TestResidualArithmeticBoundaries);
            yield return new SmokeCase("Metrology contracts expose authored bias across repeated synthetic conditions", TestRepeatedConditions);
        }

        private static void TestAffinePositions()
        {
            AffineTransformToolProperty property = new AffineTransformToolProperty
            {
                SourcePoint2X = 100, SourcePoint3Y = 80,
                DestinationPoint1X = 8, DestinationPoint1Y = 35,
                DestinationPoint2X = 58, DestinationPoint2Y = 10,
                DestinationPoint3X = 18, DestinationPoint3Y = 95,
                OutputWidth = 128, OutputHeight = 112, Interpolation = InterpolationFlags.Nearest
            };
            using (Mat source = new Mat(89, 113, MatType.CV_8UC1, Scalar.All(0)))
            using (AffineTransformTool tool = new AffineTransformTool())
            {
                source.Set(40, 48, (byte)244);
                tool.SetProperty(property);
                using (VisionToolResult result = tool.Execute(source))
                {
                    Require(result.Success, result.Message);
                    foreach (Point2d point in new[] { new Point2d(0.5, 0.25), new Point2d(48.75, 17.5), new Point2d(91.125, 79.875) })
                    {
                        double x = result.Metrics["AffineM11"] * point.X + result.Metrics["AffineM12"] * point.Y + result.Metrics["AffineM13"];
                        double y = result.Metrics["AffineM21"] * point.X + result.Metrics["AffineM22"] * point.Y + result.Metrics["AffineM23"];
                        RequireApproximately(x, 0.5 * point.X + 0.125 * point.Y + 8, 1e-12, "Independent affine X.");
                        RequireApproximately(y, -0.25 * point.X + 0.75 * point.Y + 35, 1e-12, "Independent affine Y.");
                    }
                    Require(result.ResultImage.At<byte>(53, 37) == 244, "The independent integer marker must land at (37,53).");
                    property.SourcePoint3Y = 0;
                    using (VisionToolResult invalid = tool.Execute(source))
                    {
                        Require(!invalid.Success && invalid.ErrorCode == VisionToolErrorCode.AffineDegenerateSource, "A collapsed datum must fail.");
                    }
                    property.SourcePoint3Y = 80;
                    using (VisionToolResult recovery = tool.Execute(source))
                    {
                        Require(recovery.Success && recovery.ResultImage.At<byte>(53, 37) == 244, "Affine recovery lost the marker.");
                    }
                    Require(result.ResultImage.At<byte>(53, 37) == 244, "A captured result must survive failure and recovery.");
                }
            }
        }

        private static void TestDeclaredUnits()
        {
            ThicknessInspectionOptions options = new ThicknessInspectionOptions
            {
                MinimumThickness = 0.5, MaximumThickness = 1.5,
                InputRequirements = new HeightMapInputRequirements("mm", "mm", "synthetic-fixture")
            };
            ThicknessInspectionTool tool = new ThicknessInspectionTool(options);
            HeightMap3D millimeters = new HeightMap3D(1, 1, 0, 0, 1, 1, new[] { 1.0 }, "mm", "synthetic-fixture", "synthetic-mm");
            HeightMap3D micrometers = new HeightMap3D(1, 1, 0, 0, 1, 1, new[] { 1000.0 }, "um", "synthetic-fixture", "synthetic-um");
            ThreeDInspectionResult captured = tool.Execute(millimeters);
            Require(captured.MeasurementOutcome == ThreeDMeasurementOutcome.Passed, captured.Message);
            Require(tool.Execute(micrometers).ErrorCode == ThreeDInspectionErrorCode.InputContractMismatch, "Units must be checked before measuring.");
            options.InputRequirements = new HeightMapInputRequirements("um", "um", "synthetic-fixture");
            ThreeDInspectionResult declared = tool.Execute(micrometers);
            Require(declared.MeasurementOutcome == ThreeDMeasurementOutcome.OutOfTolerance, "Changing a unit declaration must not rescale values or limits.");
            Require(declared.TryGetMetric(ThreeDInspectionMetricNames.Thickness.Mean, out double mean, out string unit) && unit == "um" && mean == 1000, "Scalar and unit must stay together.");
            options.MinimumThickness = 500;
            options.MaximumThickness = 1500;
            Require(tool.Execute(micrometers).MeasurementOutcome == ThreeDMeasurementOutcome.Passed, "Explicitly rescaled test limits must pass.");
            options.InputRequirements = new HeightMapInputRequirements("mm", "mm", "other-fixture");
            ThreeDInspectionResult wrongFrame = tool.Execute(millimeters);
            Require(wrongFrame.MeasurementOutcome == ThreeDMeasurementOutcome.NotMeasured && wrongFrame.ErrorCode == ThreeDInspectionErrorCode.InputContractMismatch, "A changed frame must not produce a measurement.");
            options.InputRequirements = new HeightMapInputRequirements("mm", "mm", "synthetic-fixture");
            options.MinimumThickness = 0.5;
            options.MaximumThickness = 1.5;
            Require(tool.Execute(millimeters).MeasurementOutcome == ThreeDMeasurementOutcome.Passed, "The same Tool must recover.");
            Require(captured.Metrics[ThreeDInspectionMetricNames.Thickness.Mean] == 1 && captured.HeightUnit == "mm", "Captured unit/value evidence must remain unchanged.");
        }

        private static void TestToleranceAndQuality()
        {
            ThicknessInspectionOptions options = new ThicknessInspectionOptions { MinimumThickness = 1, MaximumThickness = 2 };
            ThicknessInspectionTool tool = new ThicknessInspectionTool(options);
            double[] values = { Math.BitDecrement(1), 1, Math.BitIncrement(1), Math.BitDecrement(2), 2, Math.BitIncrement(2) };
            for (int index = 0; index < values.Length; index++)
            {
                HeightMap3D source = new HeightMap3D(1, 1, 0, 0, 1, 1, new[] { values[index] }, "mm", "synthetic-fixture", "boundary-" + index);
                ThreeDInspectionResult result = tool.Execute(source);
                ThreeDMeasurementOutcome expected = index == 0 || index == 5 ? ThreeDMeasurementOutcome.OutOfTolerance : ThreeDMeasurementOutcome.Passed;
                Require(result.MeasurementOutcome == expected && result.HasMeasurement && result.ErrorCode == ThreeDInspectionErrorCode.None, "Adjacent inclusive limit changed at index " + index);
                RequireApproximately(result.Metrics[ThreeDInspectionMetricNames.Thickness.Mean], values[index], 0, "Boundary value must remain available.");
                Console.WriteLine("EVIDENCE | metrology-boundary | index=" + index + " | value=" + values[index].ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " | outcome=" + result.MeasurementOutcome);
            }
            options.MinimumValidSamples = 2;
            options.MinimumValidCoverageRatio = 0.75;
            HeightMap3D sufficient = new HeightMap3D(2, 2, 0, 0, 1, 1, new[] { 1.0, 2.0, 1.5, double.NaN }, "mm", "synthetic-fixture", "coverage-boundary");
            Require(tool.Execute(sufficient).MeasurementOutcome == ThreeDMeasurementOutcome.Passed, "Exactly 75 percent valid coverage must pass.");
            ThreeDInspectionResult lowCoverage = tool.Execute(new HeightMap3D(2, 2, 0, 0, 1, 1, new[] { 1.0, 2.0, double.NaN, double.NaN }));
            Require(lowCoverage.MeasurementOutcome == ThreeDMeasurementOutcome.NotMeasured && lowCoverage.ErrorCode == ThreeDInspectionErrorCode.InsufficientValidCoverage && lowCoverage.ValidCoverageRatio == 0.5, "Insufficient coverage is not an out-of-tolerance measurement.");
            ThreeDInspectionResult missing = tool.Execute(new HeightMap3D(2, 2, 0, 0, 1, 1, new[] { 1.0, double.NaN, double.NaN, double.NaN }));
            Require(missing.MeasurementOutcome == ThreeDMeasurementOutcome.NotMeasured && missing.ErrorCode == ThreeDInspectionErrorCode.InsufficientValidSamples, "Too few points must remain not measured.");
            options.MaximumThickness = double.NaN;
            Require(tool.Execute(sufficient).ErrorCode == ThreeDInspectionErrorCode.InvalidParameter, "A non-finite limit must fail before measuring.");
            options.MaximumThickness = 2;
            Require(tool.Execute(sufficient).MeasurementOutcome == ThreeDMeasurementOutcome.Passed, "Valid measurement must recover after all rejection paths.");
        }

        private static void TestDatumHeight()
        {
            double[] heights = new double[9];
            for (int row = 0; row < 3; row++)
            {
                for (int column = 0; column < 3; column++)
                {
                    double x = 50 + column * 0.5;
                    double y = -20 + row * 0.25;
                    heights[row * 3 + column] = 0.5 * x - 0.25 * y + 10 + column * 0.25;
                }
            }
            HeightMap3D source = new HeightMap3D(3, 3, 50, -20, 0.5, 0.25, heights, "mm", "synthetic-fixture", "tilted-datum");
            DatumPlaneRawHeightDeviationInspectionOptions options = new DatumPlaneRawHeightDeviationInspectionOptions
            {
                PlaneNormalX = -0.5, PlaneNormalY = 1, PlaneNormalZ = 0.25, PlaneOffset = -10,
                MaximumPeakToValleyRawHeight = 0.5, InputRequirements = new HeightMapInputRequirements("mm", "mm", "synthetic-fixture")
            };
            DatumPlaneRawHeightDeviationInspectionTool tool = new DatumPlaneRawHeightDeviationInspectionTool(options);
            ThreeDInspectionResult result = tool.Execute(source);
            Console.WriteLine("EVIDENCE | metrology-datum | expectedSpan=0.5 | measuredSpan=" + result.Metrics["PeakToValleyRawHeight"].ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " | outcome=" + result.MeasurementOutcome);
            Require(result.MeasurementOutcome == ThreeDMeasurementOutcome.Passed, result.Message);
            RequireApproximately(result.Metrics["PeakToValleyRawHeight"], 0.5, 1e-12, "Raw-height span must preserve the authored vertical residual.");
            RequireApproximately(result.Metrics["RmsRawHeightResidual"], Math.Sqrt(5.0 / 48), 1e-12, "Raw-height RMS must not become normal distance.");
            Require(result.MetricUnits["RmsRawHeightResidual"] == "mm", "Residual unit must follow the scalar height declaration.");
            options.MaximumPeakToValleyRawHeight = 0.25;
            Require(tool.Execute(source).MeasurementOutcome == ThreeDMeasurementOutcome.OutOfTolerance, "Valid datum residuals outside a limit must remain measured.");
            options.PlaneNormalY = 0;
            ThreeDInspectionResult vertical = tool.Execute(source);
            Require(vertical.MeasurementOutcome == ThreeDMeasurementOutcome.NotMeasured && vertical.ErrorCode == ThreeDInspectionErrorCode.DegenerateGeometry, "A vertical datum has no raw-height function.");
            options.PlaneNormalY = 1;
            options.MaximumPeakToValleyRawHeight = 0.5;
            Require(tool.Execute(source).MeasurementOutcome == ThreeDMeasurementOutcome.Passed, "Datum measurement must recover.");
            RequireApproximately(result.Metrics["PeakToValleyRawHeight"], 0.5, 1e-12, "Captured datum evidence changed.");
        }

        private static void TestResidualArithmeticBoundaries()
        {
            double normalComponent = Math.Sqrt(3.0 / 8);
            Require(DatumPlaneRawHeightDeviationInspectionTool.TryCalculateRawHeightResidual(normalComponent, 0.5, -normalComponent, 0, double.MaxValue, double.MaxValue, 0, out double canceled) && canceled == 0,
                "The slope expression must retain finite opposite-term cancellation through the existing fallback.");
            Require(DatumPlaneRawHeightDeviationInspectionTool.TryCalculateRawHeightResidual(1, double.Epsilon, 0, 0, 0, 0, 7.5, out double tinyNormal) && tinyNormal == 7.5,
                "A zero-coordinate height remains representable even when a slope ratio overflows.");
            Require(!DatumPlaneRawHeightDeviationInspectionTool.TryCalculateRawHeightResidual(1, double.Epsilon, 0, 0, 1, 0, 0, out _), "An unrepresentable height must remain rejected.");
            Require(!DatumPlaneRawHeightDeviationInspectionTool.TryCalculateRawHeightResidual(0, 1, 0, 0, double.NaN, 0, 0, out _), "A non-finite coordinate must remain rejected.");
            Require(!DatumPlaneRawHeightDeviationInspectionTool.TryCalculateRawHeightResidual(1, 0, 0, 0, 0, 0, 0, out _), "A vertical plane must remain rejected.");
            Require(DatumPlaneRawHeightDeviationInspectionTool.TryCalculateRawHeightResidual(0.6, 0.8, 0, -1, 0, 0, 1.25, out double recovery) && recovery == 0, "The residual equation must recover after rejected inputs.");
        }

        private static void TestRepeatedConditions()
        {
            ThicknessInspectionTool tool = new ThicknessInspectionTool(new ThicknessInspectionOptions { MinimumThickness = 0.5, MaximumThickness = 1.5 });
            foreach (double bias in new[] { -0.125, 0.0, 0.125 })
            {
                double[] values = new double[9];
                for (int index = 0; index < values.Length; index++) values[index] = 1 + bias + (index % 3 - 1) * 0.03125;
                HeightMap3D source = new HeightMap3D(3, 3, 0, 0, 0.5, 0.25, values, "mm", "synthetic-fixture", "synthetic-condition");
                for (int repeat = 0; repeat < 3; repeat++)
                {
                    ThreeDInspectionResult result = tool.Execute(source);
                    Require(result.MeasurementOutcome == ThreeDMeasurementOutcome.Passed, result.Message);
                    RequireApproximately(result.Metrics[ThreeDInspectionMetricNames.Thickness.Mean] - 1, bias, 1e-12, "Repeatability must not hide authored bias against independent truth 1.");
                    RequireApproximately(result.Metrics[ThreeDInspectionMetricNames.Thickness.Range], 0.0625, 1e-12, "Known sample spread must stay explicit.");
                    Console.WriteLine("EVIDENCE | metrology-synthetic-bias | authored=" + bias.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " | repeat=" + repeat + " | measured=" + result.Metrics[ThreeDInspectionMetricNames.Thickness.Mean].ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                }
            }
        }
    }
}

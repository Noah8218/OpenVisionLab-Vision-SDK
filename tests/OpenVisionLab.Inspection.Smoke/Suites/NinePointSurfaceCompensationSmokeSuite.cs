using OpenVisionLab.Vision3D.FeatureExtraction;
using OpenVisionLab.Vision3D.Geometry;
using OpenVisionLab.Vision3D.Inspection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class NinePointSurfaceCompensationSmokeSuite
    {
        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase("Nine-point compensation removes tilted quadratic warp and preserves local height measurement", TestLocalHeight);
            yield return new SmokeCase("Nine-point compensation reproduces the full tensor model on uneven supported references", TestTensorModel);
            yield return new SmokeCase("Nine-point compensation preserves large origins separate units grid and representable local heights", TestCoordinatesAndUnits);
            yield return new SmokeCase("Nine-point compensation preserves missing cells rejects each missing reference and recovers", TestMissingSamples);
            yield return new SmokeCase("Nine-point compensation rejects duplicate skewed incomplete and unrepresentable reference grids", TestReferenceGrid);
            yield return new SmokeCase("Nine-point compensation requires explicit finite target grid frame and distinct output source", TestParameters);
            yield return new SmokeCase("Nine-point compensation fails without partial output on numeric overflow and recovers", TestNumericRange);
            yield return new SmokeCase("Nine-point compensation honors cancellation and snapshots evidence across retries and parallel calls", TestCancellationAndSnapshots);
            yield return new SmokeCase("Nine-point compensation distinguishes plane leveling and contaminated reference limitations", TestModelBoundary);
        }

        private static HeightMap3D Map(int rows, int columns, Func<int, int, double> height,
            double originX = 100.0, double originY = -20.0, double columnPitch = 0.25, double rowPitch = 0.5)
        {
            double[] values = Enumerable.Range(0, rows * columns).Select(index => height(index / columns, index % columns)).ToArray();
            return new HeightMap3D(rows, columns, originX, originY, columnPitch, rowPitch, values, "mm", "um", "synthetic-board", "synthetic-input");
        }

        private static NinePointSurfaceCompensationOptions Options(int rows, int columns, double target = 0.0)
        {
            return new NinePointSurfaceCompensationOptions
            {
                ReferenceRows = new[] { 0, (rows - 1) / 2, rows - 1 },
                ReferenceColumns = new[] { 0, (columns - 1) / 2, columns - 1 },
                TargetHeight = target,
                OutputFrameId = "synthetic-compensated-frame",
                OutputSourceId = "synthetic-compensated-source"
            };
        }

        private static double Background(int row, int column, int rows, int columns)
        {
            double u = 2.0 * column / (columns - 1) - 1.0;
            double v = 2.0 * row / (rows - 1) - 1.0;
            return 40.0 + 2.0 * u - 3.0 * v + 4.0 * u * u + 1.5 * u * v - 2.0 * v * v;
        }

        private static double LocalHeight(int row, int column)
        {
            if (row >= 18 && row <= 22 && column >= 12 && column <= 17) return 5.25;
            if (row >= 45 && row <= 48 && column >= 60 && column <= 64) return -1.75;
            return 0.0;
        }

        private static void TestLocalHeight()
        {
            HeightMap3D input = Map(61, 81, (row, column) => Background(row, column, 61, 81) + LocalHeight(row, column));
            double[] retainedInput = input.CopyValues();
            NinePointSurfaceCompensationResult result = new NinePointSurfaceCompensationTool().Execute(input, Options(61, 81));
            Require(result.Success && result.ControlPoints.Count == 9 && result.BackgroundHeights.Count == 61 * 81, "Nine real samples and a complete background are required.");
            for (int row = 0; row < 61; row++)
            {
                for (int column = 0; column < 81; column++)
                {
                    RequireApproximately(result.BackgroundHeights[row * 81 + column], Background(row, column, 61, 81), 1e-11, "Independent curved background changed.");
                    RequireApproximately(result.Output.GetHeight(row, column), LocalHeight(row, column), 1e-11, "Compensation erased a local protrusion/dimple or retained substrate warp.");
                }
            }

            RequireApproximately(result.ReferencePeakToValley, 6.0, 1e-11, "Nine-point plane-residual PV must follow the independent analytic quadratic.");
            RequireApproximately(result.ReferenceRms, 7.0 / 3.0, 1e-11, "Nine-point plane-residual RMS must follow the independent analytic quadratic.");
            RequireApproximately(result.PlaneSlopeX, 0.2, 1e-12, "Plane X slope must use um/mm.");
            RequireApproximately(result.PlaneSlopeY, -0.2, 1e-12, "Plane Y slope must use um/mm.");
            RequireApproximately(result.PlaneHeightAtOrigin, 127.0 / 3.0, 1e-11, "Plane height must be evaluated at the input XY origin.");
            for (int index = 0; index < 9; index++)
            {
                NinePointSurfaceControlEvidence point = result.ControlPoints[index];
                Require(point.Row == index / 3 * 30 && point.Column == index % 3 * 40, "Measured reference positions/order changed.");
                RequireApproximately(point.Height, input.GetHeight(point.Row, point.Column), 0.0, "Control height must come from the input cell.");
                RequireApproximately(point.Height - point.ReferencePlaneHeight, point.PlaneResidual, 1e-11, "Raw height and plane deviation were conflated.");
                RequireApproximately(point.CompensatedHeight, 0.0, 1e-11, "Control correction did not reach the authored target.");
            }

            ThreeDInspectionResult measured = new ThicknessInspectionTool(new ThicknessInspectionOptions
            {
                Roi = new HeightMapRoi(18, 12, 5, 6), MinimumThickness = 5.25 - 1e-10, MaximumThickness = 5.25 + 1e-10
            }).Execute(result.Output);
            Require(measured.Success, "The existing explicit height-measurement tool must accept the compensated local feature.");
            RequireApproximately(measured.Metrics[ThreeDInspectionMetricNames.Thickness.Mean], 5.25, 1e-11, "Post-compensation local height changed.");
            Require(measured.HeightUnit == "um" && input.CopyValues().SequenceEqual(retainedInput), "Measurement units or original source changed.");

            // C3D reserves zero for missing data. A nonzero compensation datum can retain substrate cells on that persistence path.
            NinePointSurfaceCompensationResult offset = new NinePointSurfaceCompensationTool().Execute(input, Options(61, 81, 10.0));
            ThreeDInspectionResult localDeviation = new DatumPlaneRawHeightDeviationInspectionTool(new DatumPlaneRawHeightDeviationInspectionOptions
            {
                Roi = new HeightMapRoi(18, 12, 5, 6), PlaneNormalY = 1.0, PlaneOffset = -10.0, MaximumPeakToValleyRawHeight = 1e-10
            }).Execute(offset.Output);
            Require(offset.Success && offset.Output.CopyValues().All(value => (float)value != 0.0f)
                && !localDeviation.HasMeasurement && localDeviation.ErrorCode == ThreeDInspectionErrorCode.InputContractMismatch,
                "The nonzero target must preserve cells without bypassing the Euclidean plane tool's matching-unit contract.");
            HeightFieldPlaneFitSample[] reference = offset.ControlPoints.Select(point => new HeightFieldPlaneFitSample(
                new ThreeDPoint(point.X, point.CompensatedHeight, point.Y), point.CompensatedHeight)).ToArray();
            HeightFieldPlaneFitSample[] measurement = (from row in Enumerable.Range(18, 5) from column in Enumerable.Range(12, 6)
                let height = offset.Output.GetHeight(row, column)
                select new HeightFieldPlaneFitSample(new ThreeDPoint(offset.Output.GetX(column), height, offset.Output.GetY(row)), height)).ToArray();
            DualSurfaceThicknessInspectionResult localHeight = new DualSurfaceThicknessInspectionTool().Execute(reference, measurement,
                5.25 - 1e-10, 5.25 + 1e-10, 30);
            Require(localHeight.Decision == DualSurfaceThicknessDecision.Pass, "Existing raw-height separation must accept the compensated substrate and feature samples.");
            RequireApproximately(localHeight.Mean, 5.25, 1e-11, "The compensation target must not be included in local height above the substrate.");
            RequireApproximately(localHeight.Minimum, 5.25, 1e-11, "Minimum local height changed after nonzero-target compensation.");
            RequireApproximately(localHeight.Maximum, 5.25, 1e-11, "Maximum local height changed after nonzero-target compensation.");
        }

        private static void TestTensorModel()
        {
            const int rows = 37;
            const int columns = 53;
            Func<int, int, double> polynomial = (row, column) =>
            {
                double x = (double)column / (columns - 1);
                double y = (double)row / (rows - 1);
                return -7.0 + 2.0 * x - 3.0 * y + 4.0 * x * x + 5.0 * x * y - 6.0 * y * y
                    + 7.0 * x * x * y - 8.0 * x * y * y + 9.0 * x * x * y * y;
            };
            NinePointSurfaceCompensationOptions options = Options(rows, columns, 10.0);
            options.ReferenceRows = new[] { 0, 12, 36 };
            options.ReferenceColumns = new[] { 0, 37, 52 };
            NinePointSurfaceCompensationResult result = new NinePointSurfaceCompensationTool().Execute(Map(rows, columns, polynomial), options);
            Require(result.Success, "Supported uneven references must reproduce all nine independent tensor terms.");
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    RequireApproximately(result.BackgroundHeights[row * columns + column], polynomial(row, column), 1e-11, "Tensor model lost an independent cross term.");
                    RequireApproximately(result.Output.GetHeight(row, column), 10.0, 1e-11, "Authored target height changed.");
                }
            }
        }

        private static void TestCoordinatesAndUnits()
        {
            const double commonHeight = 1e12;
            HeightMap3D input = Map(65, 65, (row, column) => commonHeight + Background(row, column, 65, 65)
                + (row == 17 && column == 13 ? 5.25 : 0.0), 1e12, -1e12, 0.25, 0.5);
            NinePointSurfaceCompensationResult result = new NinePointSurfaceCompensationTool().Execute(input, Options(65, 65));
            double roundingBound = 16.0 * (Math.BitIncrement(commonHeight) - commonHeight);
            Require(result.Success && result.Output.Rows == input.Rows && result.Output.Columns == input.Columns,
                "Large origin must not crop or resample the output grid.");
            RequireApproximately(result.Output.GetHeight(17, 13), 5.25, roundingBound, "A representable local height was lost to a common-height cancellation.");
            Require(result.Output.OriginX == input.OriginX && result.Output.OriginY == input.OriginY
                && result.Output.ColumnPitch == input.ColumnPitch && result.Output.RowPitch == input.RowPitch
                && result.Output.PlanarUnit == "mm" && result.Output.HeightUnit == "um"
                && result.Output.FrameId != input.FrameId && result.Output.SourceId != input.SourceId, "XY/units/derived identities changed.");
            foreach (NinePointSurfaceControlEvidence point in result.ControlPoints)
            {
                Require(point.X == input.GetX(point.Column) && point.Y == input.GetY(point.Row), "Actual measured XY was replaced with display coordinates.");
            }
        }

        private static void TestMissingSamples()
        {
            NinePointSurfaceCompensationTool tool = new NinePointSurfaceCompensationTool();
            HeightMap3D valid = Map(9, 9, (row, column) => Background(row, column, 9, 9));
            HeightMap3D missing = Map(9, 9, (row, column) => row == 2 && column == 3 ? double.NaN : Background(row, column, 9, 9));
            NinePointSurfaceCompensationResult retained = tool.Execute(missing, Options(9, 9));
            Require(retained.Success && double.IsNaN(retained.Output.GetHeight(2, 3)) && double.IsFinite(retained.BackgroundHeights[2 * 9 + 3]),
                "A missing non-reference input must stay missing while the background remains a separate model.");
            foreach (int row in new[] { 0, 4, 8 })
            {
                foreach (int column in new[] { 0, 4, 8 })
                {
                    double[] values = valid.CopyValues();
                    values[row * 9 + column] = double.NaN;
                    HeightMap3D bad = new HeightMap3D(9, 9, 0, 0, 1, 1, values, "mm", "um", valid.FrameId, valid.SourceId);
                    AssertFailure(tool.Execute(bad, Options(9, 9)), NinePointSurfaceCompensationFailureReason.MissingReferenceSample);
                }
            }

            Require(tool.Execute(valid, Options(9, 9)).Success && retained.ControlPoints.Count == 9
                && double.IsNaN(retained.Output.GetHeight(2, 3)), "Missing-reference failure damaged recovery or retained evidence.");
        }

        private static void TestReferenceGrid()
        {
            NinePointSurfaceCompensationTool tool = new NinePointSurfaceCompensationTool();
            HeightMap3D input = Map(21, 21, (row, column) => row + column);
            int[][] invalid = { null, new[] { 0, 20 }, new[] { 0, 5, 10, 20 }, new[] { 0, 0, 20 }, new[] { 20, 10, 0 },
                new[] { 1, 10, 20 }, new[] { 0, 10, 19 }, new[] { 0, 1, 20 }, new[] { 0, 19, 20 }, new[] { 0, -1, 20 } };
            foreach (int[] indices in invalid)
            {
                NinePointSurfaceCompensationOptions badRows = Options(21, 21);
                badRows.ReferenceRows = indices;
                AssertFailure(tool.Execute(input, badRows), NinePointSurfaceCompensationFailureReason.InvalidReferenceGrid);
                NinePointSurfaceCompensationOptions badColumns = Options(21, 21);
                badColumns.ReferenceColumns = indices;
                AssertFailure(tool.Execute(input, badColumns), NinePointSurfaceCompensationFailureReason.InvalidReferenceGrid);
            }

            AssertFailure(tool.Execute(Map(2, 21, (row, column) => 0), Options(2, 21)), NinePointSurfaceCompensationFailureReason.InvalidReferenceGrid);
            AssertFailure(tool.Execute(Map(21, 2, (row, column) => 0), Options(21, 2)), NinePointSurfaceCompensationFailureReason.InvalidReferenceGrid);
            AssertFailure(tool.Execute(Map(21, 21, (row, column) => 0, 1e30, 0, 1, 1), Options(21, 21)), NinePointSurfaceCompensationFailureReason.InvalidReferenceGrid);
            foreach (int middle in new[] { 5, 15 })
            {
                NinePointSurfaceCompensationOptions boundary = Options(21, 21);
                boundary.ReferenceRows = new[] { 0, middle, 20 };
                boundary.ReferenceColumns = new[] { 0, middle, 20 };
                Require(tool.Execute(input, boundary).Success, "Inclusive 25/75 percent reference boundaries changed.");
            }
        }

        private static void TestParameters()
        {
            NinePointSurfaceCompensationTool tool = new NinePointSurfaceCompensationTool();
            HeightMap3D input = Map(5, 5, (row, column) => 0);
            AssertFailure(tool.Execute(null, Options(5, 5)), NinePointSurfaceCompensationFailureReason.InvalidInput);
            AssertFailure(tool.Execute(input, null), NinePointSurfaceCompensationFailureReason.InvalidInput);
            foreach (double target in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                AssertFailure(tool.Execute(input, Options(5, 5, target)), NinePointSurfaceCompensationFailureReason.InvalidInput);
            }

            foreach (string id in new[] { null, " " })
            {
                NinePointSurfaceCompensationOptions options = Options(5, 5);
                options.OutputFrameId = id;
                AssertFailure(tool.Execute(input, options), NinePointSurfaceCompensationFailureReason.InvalidInput);
            }
            NinePointSurfaceCompensationOptions sameGrid = Options(5, 5);
            sameGrid.OutputFrameId = input.FrameId;
            NinePointSurfaceCompensationResult sameGridResult = tool.Execute(input, sameGrid);
            Require(sameGridResult.Success && sameGridResult.Output.FrameId == input.FrameId
                && sameGridResult.Output.SourceId != input.SourceId, "A new scalar field may retain its unchanged explicit XY grid frame.");
            foreach (string id in new[] { null, " ", input.SourceId })
            {
                NinePointSurfaceCompensationOptions options = Options(5, 5);
                options.OutputSourceId = id;
                AssertFailure(tool.Execute(input, options), NinePointSurfaceCompensationFailureReason.InvalidInput);
            }
            Require(tool.Execute(input, Options(5, 5)).Success, "Invalid parameters must not poison the next explicit call.");
        }

        private static void TestNumericRange()
        {
            NinePointSurfaceCompensationTool tool = new NinePointSurfaceCompensationTool();
            HeightMap3D extreme = Map(5, 5, (row, column) => row == 0 && column == 0 ? -double.MaxValue : double.MaxValue);
            AssertFailure(tool.Execute(extreme, Options(5, 5)), NinePointSurfaceCompensationFailureReason.NumericalFailure);
            HeightMap3D invalidPlane = Map(5, 5, (row, column) => row == 0 && column == 0 ? 1e200 : 0);
            AssertFailure(tool.Execute(invalidPlane, Options(5, 5)), NinePointSurfaceCompensationFailureReason.NumericalFailure);
            HeightMap3D invalidOutput = Map(5, 5, (row, column) => row == 1 && column == 1 ? double.MaxValue : 0);
            AssertFailure(tool.Execute(invalidOutput, Options(5, 5, double.MaxValue)), NinePointSurfaceCompensationFailureReason.NumericalFailure);
            NinePointSurfaceCompensationResult largeConstant = tool.Execute(Map(5, 5, (row, column) => 1.7e308), Options(5, 5));
            Require(largeConstant.Success && largeConstant.Output.CopyValues().All(value => value == 0.0),
                "A finite common height must not overflow a centered zero-background difference.");
            Require(tool.Execute(Map(5, 5, (row, column) => row + column), Options(5, 5)).Success, "Numeric failure must not retain partial state.");
        }

        private static void TestCancellationAndSnapshots()
        {
            NinePointSurfaceCompensationTool tool = new NinePointSurfaceCompensationTool();
            HeightMap3D input = Map(9, 9, (row, column) => Background(row, column, 9, 9));
            NinePointSurfaceCompensationOptions options = Options(9, 9);
            using (CancellationTokenSource cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                RequireThrows<OperationCanceledException>(() => tool.Execute(input, options, cancelled.Token));
            }

            NinePointSurfaceCompensationResult retained = tool.Execute(input, options);
            double[] retainedOutput = retained.Output.CopyValues();
            options.ReferenceRows = new[] { 0, 1, 8 };
            AssertFailure(tool.Execute(input, options), NinePointSurfaceCompensationFailureReason.InvalidReferenceGrid);
            Require(retained.ControlPoints.Count == 9 && retained.Output.CopyValues().SequenceEqual(retainedOutput)
                && ReferenceEquals(retained.Input, input), "Options/retry changed an already completed immutable result.");
            double[] copy = retained.Output.CopyValues();
            copy[0] = 12345;
            Require(retained.Output.GetHeight(0, 0) != 12345, "Output map leaked its owned values.");
            RequireThrows<NotSupportedException>(() => ((IList<double>)retained.BackgroundHeights)[0] = 12345);
            RequireThrows<NotSupportedException>(() => ((IList<NinePointSurfaceControlEvidence>)retained.ControlPoints).Clear());
            Parallel.For(0, 4, index =>
            {
                NinePointSurfaceCompensationResult current = tool.Execute(input, Options(9, 9, index));
                Require(current.Success && current.Output.CopyValues().All(value => Math.Abs(value - index) < 1e-11),
                    "Independent calls on one stateless tool shared mutable compensation state.");
            });
        }

        private static void TestModelBoundary()
        {
            HeightMap3D bare = Map(17, 17, (row, column) => Background(row, column, 17, 17));
            LevelSurfaceRegion[] references = (from row in new[] { 0, 8, 16 } from column in new[] { 0, 8, 16 }
                select new LevelSurfaceRegion(row, column, 1, 1)).ToArray();
            LevelSurfaceResult planeOnly = new LevelSurfaceTool().Execute(17, 17, bare.CopyValues(), references,
                new LevelSurfaceOptions { MinimumValidSampleCount = 9 });
            Require(planeOnly.Success, "Existing explicit plane-leveling call must remain usable.");
            double[] referenceResidual = (from row in new[] { 0, 8, 16 } from column in new[] { 0, 8, 16 }
                select planeOnly.Values[row * 17 + column]).ToArray();
            RequireApproximately(referenceResidual.Max() - referenceResidual.Min(), 6.0, 1e-11,
                "Plane leveling must not be described as removing curved substrate warp.");
            NinePointSurfaceCompensationTool tool = new NinePointSurfaceCompensationTool();
            NinePointSurfaceCompensationResult corrected = tool.Execute(bare, Options(17, 17));
            Require(corrected.Success && corrected.Output.CopyValues().All(value => Math.Abs(value) < 1e-11), "Curved compensation did not flatten independent bare-substrate cells.");
            HeightMap3D contaminated = Map(17, 17, (row, column) => Background(row, column, 17, 17) + (row == 8 && column == 8 ? 5.0 : 0.0));
            NinePointSurfaceCompensationResult influenced = tool.Execute(contaminated, Options(17, 17));
            Require(influenced.Success && Math.Abs(influenced.Output.GetHeight(8, 8)) < 1e-11
                && Math.Abs(influenced.BackgroundHeights[8 * 17 + 8] - corrected.BackgroundHeights[8 * 17 + 8] - 5.0) < 1e-11,
                "Nine interpolation controls cannot independently distinguish a component on a control from substrate warp.");
        }

        private static void AssertFailure(NinePointSurfaceCompensationResult result, NinePointSurfaceCompensationFailureReason expected)
        {
            Require(!result.Success && result.FailureReason == expected && result.Output == null
                && result.BackgroundHeights.Count == 0 && result.ControlPoints.Count == 0,
                "Failure must report its reason without usable partial maps or measurement evidence: " + result.FailureReason);
        }
    }
}

using OpenVisionLab.Vision3D.FeatureExtraction;
using OpenVisionLab.Vision3D.Inspection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class DatumPlaneFitSmokeSuite
    {
        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase("Datum consensus recovers isolated and clustered reference contamination", TestContamination);
            yield return new SmokeCase("Datum consensus rejects insufficient ambiguous and degenerate reference support", TestSupport);
            yield return new SmokeCase("Datum consensus retains the inclusive height residual boundary", TestInclusiveBoundary);
            yield return new SmokeCase("Datum consensus preserves large-origin uneven reference geometry", TestLargeOrigin);
            yield return new SmokeCase("Datum consensus reuses one plane and preserves flatness volume and thickness defects", TestMeasurements);
            yield return new SmokeCase("Datum consensus leveling shares selection mean statistics and grid evidence", TestLeveling);
            yield return new SmokeCase("Datum consensus snapshots evidence and recovers after invalid numerical and canceled inputs", TestSnapshotAndRecovery);
            yield return new SmokeCase("Datum consensus exposes its bounded hypothesis sampling limit", TestBoundedPool);
        }

        private static DatumPlaneFitOptions Options(double threshold = 0.1, double ratio = 0.75, int count = 3)
        {
            return new DatumPlaneFitOptions { MaximumAbsoluteHeightResidual = threshold, MinimumInlierRatio = ratio, MinimumInlierCount = count };
        }

        private static HeightFieldPlaneFitSample[] Grid(int rows, int columns, Func<int, int, double> height)
        {
            return Enumerable.Range(0, rows * columns).Select(index =>
            {
                int row = index / columns;
                int column = index % columns;
                double value = height(row, column);
                return new HeightFieldPlaneFitSample(new ThreeDPoint(column, value, row), value);
            }).ToArray();
        }

        private static void TestContamination()
        {
            DeterministicDatumPlaneFitTool tool = new DeterministicDatumPlaneFitTool();
            HeightFieldPlaneFitSample[] isolated = Grid(3, 3, (row, column) => row == 1 && column == 1 ? 9.0 : 0.0);
            DatumPlaneFitResult first = tool.Execute(isolated, Options());
            Require(first.Success && first.InlierSampleCount == 8 && first.InputSampleCount == 9
                && first.RejectedSampleIndices.Single() == 4 && first.Plane.SampleCount == 8, "Isolated contamination must be excluded only from the datum.");
            RequireApproximately(first.Plane.Intercept, 0.0, 1e-12, "The independent horizontal datum changed.");
            RequireApproximately(first.HeightResiduals[4], 9.0, 1e-12, "Excluded reference height evidence was lost.");
            RequireApproximately(new LeastSquaresHeightFieldPlaneFitTool().Execute(isolated).Intercept, 1.0, 1e-12, "The all-input legacy least-squares contract changed.");
            HeightFieldPlaneFitSample[] independentRaw = isolated.Select(sample => new HeightFieldPlaneFitSample(
                sample.Position, 100.0 + 2.0 * sample.Position.X - sample.Position.Z)).ToArray();
            DatumPlaneFitResult rawResult = tool.Execute(independentRaw, Options());
            Require(rawResult.Success && rawResult.RejectedSampleIndices.Single() == 4, "RawHeight must not replace the geometric Y selection axis.");
            RequireApproximately(rawResult.MeanInlierRawHeight, 101.0, 1e-12, "Independent RawHeight mean was conflated with geometric Y.");
            RequireApproximately(rawResult.Plane.EvaluateY(1, 1), 0.0, 1e-12, "Independent RawHeight changed the geometric datum.");

            int[] contaminated = { 6, 7, 11 };
            HeightFieldPlaneFitSample[] cluster = Grid(5, 5, (row, column) => 10.0 + 0.5 * column - 0.25 * row
                + (contaminated.Contains(row * 5 + column) ? 100.0 : 0.0));
            DatumPlaneFitResult fitted = tool.Execute(cluster, Options(0.1, 0.8, 20));
            Require(fitted.Success && fitted.InlierSampleCount == 22 && fitted.RejectedSampleIndices.SequenceEqual(contaminated), "Clustered contamination must preserve exact reference membership.");
            RequireApproximately(fitted.Plane.SlopeX, 0.5, 1e-12, "Tilt X changed.");
            RequireApproximately(fitted.Plane.SlopeZ, -0.25, 1e-12, "Tilt Z changed.");
            RequireApproximately(fitted.MeanInlierRawHeight, 231.5 / 22.0, 1e-12, "Mean must use the independent admitted datum heights.");
            Require(fitted.HypothesisSampleIndices.Count == 12 && fitted.EvaluatedHypothesisCount == 220
                && fitted.RefitIterationCount <= DeterministicDatumPlaneFitTool.MaximumRefitIterations, "Finite hypothesis/refit bounds changed.");
            DatumPlaneFitResult repeated = tool.Execute(cluster, Options(0.1, 0.8, 20));
            Require(repeated.Success && repeated.InlierSampleIndices.SequenceEqual(fitted.InlierSampleIndices)
                && repeated.HeightResiduals.SequenceEqual(fitted.HeightResiduals), "Stable input must produce identical membership and evidence.");
        }

        private static void TestSupport()
        {
            DeterministicDatumPlaneFitTool tool = new DeterministicDatumPlaneFitTool();
            DatumPlaneFitResult insufficient = tool.Execute(Grid(4, 3, (row, column) => row < 2 ? 0.0 : 10.0), Options());
            Require(!insufficient.Success && insufficient.Plane == null && insufficient.FailureReason == DatumPlaneFitFailureReason.InsufficientSupport,
                "Balanced parallel references must not invent a majority datum.");
            DatumPlaneFitResult ambiguous = tool.Execute(Grid(3, 3, (row, column) => Math.Abs(column - 1.0)), Options(0.01, 0.6, 6));
            Require(!ambiguous.Success && ambiguous.FailureReason == DatumPlaneFitFailureReason.AmbiguousSupport
                && ambiguous.InlierSamples.Count == 0 && ambiguous.HeightResiduals.Count == 0, "Distinct maximum-support memberships must fail closed without a usable plane.");
            HeightFieldPlaneFitSample[] line = Enumerable.Range(0, 5).Select(index => new HeightFieldPlaneFitSample(new ThreeDPoint(index, 2 * index, index), 2 * index)).ToArray();
            DatumPlaneFitResult degenerate = tool.Execute(line, Options());
            Require(!degenerate.Success && degenerate.FailureReason == DatumPlaneFitFailureReason.DegenerateGeometry, "A reference spanning one horizontal axis must be rejected.");
            Require(!tool.Execute(Grid(3, 3, (row, column) => 0.0), Options(0.1, 0.75, 10)).Success, "An unavailable authored inlier count must not be lowered.");
            Require(tool.Execute(Grid(3, 3, (row, column) => 0.0), Options()).Success, "Support rejection must not poison the next datum.");
        }

        private static void TestInclusiveBoundary()
        {
            int[] positive = { 0, 4, 20, 24 };
            int[] negative = { 1, 3, 21, 23 };
            HeightFieldPlaneFitSample[] samples = Grid(5, 5, (row, column) => positive.Contains(row * 5 + column) ? 0.25
                : negative.Contains(row * 5 + column) ? -0.25 : 0.0);
            DatumPlaneFitResult result = new DeterministicDatumPlaneFitTool().Execute(samples, Options(0.25, 1.0, 25));
            Require(result.Success && result.InlierSampleCount == 25 && result.RejectedSampleIndices.Count == 0, "Residual equal to the configured threshold must be retained.");
            RequireApproximately(result.Plane.Intercept, 0.0, 1e-12, "Balanced authored boundary heights must retain a zero datum.");
            RequireApproximately(result.InlierHeightResidualRms, Math.Sqrt(0.5 / 25.0), 1e-12, "Height RMS must include all eight boundary samples.");
            DatumPlaneFitResult broadThreshold = new DeterministicDatumPlaneFitTool().Execute(samples, Options(1e200, 1.0, 25));
            Require(broadThreshold.Success, "A broad finite threshold must retain the same authored balanced samples.");
            RequireApproximately(broadThreshold.InlierHeightResidualRms, Math.Sqrt(0.5 / 25.0), 1e-12,
                "RMS must remain independent of the admission threshold magnitude.");
        }

        private static void TestLargeOrigin()
        {
            double[] horizontalX = { 0.0, 0.5, 2.0, 7.0, 16.0 };
            double[] horizontalZ = { 0.0, 0.25, 1.0, 5.0, 11.0 };
            foreach (double origin in new[] { 0.0, 1e9, 1e12 })
            {
                HeightFieldPlaneFitSample[] samples = Enumerable.Range(0, 25).Select(index =>
                {
                    double x = horizontalX[index % 5];
                    double z = horizontalZ[index / 5];
                    double height = origin + 3.0 + 0.5 * x - 0.25 * z + (index == 12 ? 16.0 : 0.0);
                    return new HeightFieldPlaneFitSample(new ThreeDPoint(origin + x, height, -origin + z), height);
                }).ToArray();
                foreach (HeightFieldPlaneFitSample[] ordered in new[] { samples, samples.Reverse().ToArray() })
                {
                    DatumPlaneFitResult result = new DeterministicDatumPlaneFitTool().Execute(ordered, Options(0.1, 0.9, 24));
                    Require(result.Success && result.RejectedSampleIndices.Single() == 12, "Large uneven coordinates changed datum membership.");
                    RequireApproximately(result.Plane.SlopeX, 0.5, 1e-12, "Large-origin X slope changed.");
                    RequireApproximately(result.Plane.SlopeZ, -0.25, 1e-12, "Large-origin Z slope changed.");
                    RequireApproximately(result.HeightResiduals[12], 16.0, 1e-10, "Centered Y residual lost the independently authored spike.");
                    RequireApproximately(result.InlierHeightResidualRms, 0.0, 1e-10, "Large-origin input must not create a false height RMS.");
                }
            }
        }

        private static void TestMeasurements()
        {
            DatumPlaneFitResult datum = new DeterministicDatumPlaneFitTool().Execute(Grid(3, 3, (row, column) => row == 1 && column == 1 ? 9.0 : 0.0), Options());
            double[] heights = { 2.0, -1.0, 4.0 };
            HeightFieldPlaneFitSample[] measured = heights.Select((height, index) => new HeightFieldPlaneFitSample(new ThreeDPoint(index, height, index % 2), height)).ToArray();
            PlaneFlatnessInspectionResult flatness = PlaneFlatnessInspectionTool.ExecuteWithDatum(datum, measured, 3.0);
            Require(!flatness.Passed && flatness.MeasurementSampleCount == 3 && ReferenceEquals(flatness.ReferencePlane, datum.Plane)
                && ReferenceEquals(flatness.DatumPlaneFitEvidence, datum), "Flatness must measure every defect with the actual selected plane.");
            RequireApproximately(flatness.Flatness, 5.0, 1e-12, "Independent orthogonal peak-to-valley changed.");
            RequireApproximately(flatness.RootMeanSquareDistance, Math.Sqrt(7.0), 1e-12, "Independent defect RMS changed.");
            VolumeInspectionOptions volumeOptions = new VolumeInspectionOptions { SampleArea = 0.5, ExpectedNetVolume = 2.5, Tolerance = 0.0 };
            VolumeInspectionResult volume = VolumeInspectionTool.ExecuteWithDatum(datum, measured, volumeOptions);
            Require(volume.Passed && volume.MeasurementSampleCount == 3 && ReferenceEquals(volume.ReferencePlane, datum.Plane), "Volume must reuse the datum and retain all signed defects.");
            RequireApproximately(volume.AboveVolume, 3.0, 1e-12, "Above volume changed.");
            RequireApproximately(volume.BelowVolume, 0.5, 1e-12, "Below volume changed.");
            DualSurfaceThicknessInspectionResult thickness = DualSurfaceThicknessInspectionTool.ExecuteWithDatum(datum, measured, 0.0, 3.0, 3);
            Require(thickness.Success && thickness.Decision == DualSurfaceThicknessDecision.Fail && thickness.BelowLowerLimitCount == 1
                && thickness.AboveUpperLimitCount == 1 && ReferenceEquals(thickness.ReferencePlane, datum.Plane), "Thickness must preserve both sides of the authored limit.");
            RequireApproximately(thickness.Mean, 5.0 / 3.0, 1e-12, "Independent H-axis thickness mean changed.");
            RequireApproximately(thickness.ReferenceFitHeightRootMeanSquare, 0.0, 1e-12, "Reference RMS must use the admitted datum samples.");
            DualSurfaceThicknessInspectionResult[] invalidMeasurements =
            {
                DualSurfaceThicknessInspectionTool.ExecuteWithDatum(datum, null, 0.0, 3.0, 3),
                DualSurfaceThicknessInspectionTool.ExecuteWithDatum(datum, new HeightFieldPlaneFitSample[] { null }, 0.0, 3.0, 1),
                DualSurfaceThicknessInspectionTool.ExecuteWithDatum(datum, measured, double.NaN, 3.0, 3)
            };
            foreach (DualSurfaceThicknessInspectionResult invalid in invalidMeasurements)
                Require(!invalid.Success && invalid.ReferencePlane == null && ReferenceEquals(invalid.DatumPlaneFitEvidence, datum),
                    "Measurement validation failure must retain the completed datum evidence without reporting a measurement plane.");
            Require(DualSurfaceThicknessInspectionTool.ExecuteWithDatum(datum, measured, -1.0, 4.0, 3).Decision == DualSurfaceThicknessDecision.Pass,
                "A valid measurement must recover after measurement validation failure.");
            DatumPlaneFitResult tilted = new DeterministicDatumPlaneFitTool().Execute(Grid(3, 3,
                (row, column) => 10.0 + 0.5 * column - 0.25 * row + (row == 1 && column == 1 ? 9.0 : 0.0)), Options());
            HeightFieldPlaneFitSample[] tiltedMeasured = heights.Select((height, index) =>
            {
                double raw = 10.0 + 0.5 * index - 0.25 * (index % 2) + height;
                return new HeightFieldPlaneFitSample(new ThreeDPoint(index, raw, index % 2), raw);
            }).ToArray();
            RequireApproximately(PlaneFlatnessInspectionTool.ExecuteWithDatum(tilted, tiltedMeasured, 5.0).Flatness, 5.0 / Math.Sqrt(1.3125), 1e-12,
                "Tilted flatness must retain the orthogonal distance definition.");
            RequireApproximately(VolumeInspectionTool.ExecuteWithDatum(tilted, tiltedMeasured, volumeOptions).NetVolume, 2.5, 1e-12,
                "Tilted volume must use the vertical residual rather than orthogonal distance.");
            RequireApproximately(DualSurfaceThicknessInspectionTool.ExecuteWithDatum(tilted, tiltedMeasured, -1.0, 4.0, 3).Mean, 5.0 / 3.0, 1e-12,
                "Tilted thickness must retain its independent H-axis definition.");

            DatumPlaneFitResult rejected = new DeterministicDatumPlaneFitTool().Execute(Grid(3, 3, (row, column) => Math.Abs(column - 1.0)), Options(0.01, 0.6, 6));
            RequireThrows<InvalidOperationException>(() => PlaneFlatnessInspectionTool.ExecuteWithDatum(rejected, measured, 3.0));
            RequireThrows<InvalidOperationException>(() => VolumeInspectionTool.ExecuteWithDatum(rejected, measured, volumeOptions));
            DualSurfaceThicknessInspectionResult failed = DualSurfaceThicknessInspectionTool.ExecuteWithDatum(rejected, measured, 0.0, 3.0, 3);
            Require(!failed.Success && failed.ReferencePlane == null && ReferenceEquals(failed.DatumPlaneFitEvidence, rejected), "Rejected datum is not an out-of-tolerance measurement.");
            Require(PlaneFlatnessInspectionTool.ExecuteWithDatum(datum, measured, 5.0).Passed && VolumeInspectionTool.ExecuteWithDatum(datum, measured, volumeOptions).Passed
                && DualSurfaceThicknessInspectionTool.ExecuteWithDatum(datum, measured, -1.0, 4.0, 3).Decision == DualSurfaceThicknessDecision.Pass, "Valid measurements must recover after datum rejection.");
        }

        private static void TestLeveling()
        {
            double[] values = Enumerable.Range(0, 25).Select(index => 10.0 + 0.5 * (index % 5) - 0.25 * (index / 5)).ToArray();
            values[6] += 100.0;
            values[7] += 100.0;
            values[24] += 5.0;
            values[18] = double.NaN;
            double[] before = (double[])values.Clone();
            LevelSurfaceRegion[] regions = { new LevelSurfaceRegion(0, 0, 3, 3), new LevelSurfaceRegion(1, 1, 2, 2) };
            LevelSurfaceOptions options = new LevelSurfaceOptions { MinimumValidSampleCount = 3, DatumPlaneFitOptions = Options() };
            LevelSurfaceTool tool = new LevelSurfaceTool();
            LevelSurfaceResult result = tool.Execute(5, 5, values, regions, options);
            Require(result.Success && result.ReferenceSampleCount == 7 && result.DatumPlaneFitEvidence.InputSampleCount == 9
                && result.RegionEvidence[0].ValidSampleCount == 9 && result.RegionEvidence[1].ValidSampleCount == 4, "Overlap and finite input counts must remain distinct from admitted datum count.");
            int[] rejectedGrid = result.DatumPlaneFitEvidence.RejectedSampleIndices.Select(index => result.ReferenceSampleIndices[index]).ToArray();
            int[] expectedRejected = { 6, 7 };
            Require(rejectedGrid.SequenceEqual(expectedRejected), "Datum evidence must map back to exact source grid cells.");
            RequireApproximately(result.TargetHeight, 71.25 / 7.0, 1e-12, "Leveling target must exclude contaminated reference heights.");
            RequireApproximately(result.ReferenceResidualRms, 0.0, 1e-12, "Leveling RMS must use the same admitted membership.");
            RequireApproximately(result.ReferenceResidualPeakToValley, 0.0, 1e-12, "Leveling range must use the same admitted membership.");
            RequireApproximately(result.OutputReferenceSlopeX, 0.0, 1e-12, "Output reference X slope changed.");
            RequireApproximately(result.OutputReferenceSlopeZ, 0.0, 1e-12, "Output reference Z slope changed.");
            RequireApproximately(result.Values[24] - result.TargetHeight, 5.0, 1e-12, "The measurement protrusion must survive leveling.");
            RequireApproximately(result.Values[6] - result.TargetHeight, 100.0, 1e-12, "Excluded reference height must remain in the output grid.");
            Require(double.IsNaN(result.Values[18]) && values.SequenceEqual(before), "Leveling must preserve input and missing grid identity.");
            options.MinimumValidSampleCount = 8;
            LevelSurfaceResult insufficient = tool.Execute(5, 5, values, regions, options);
            Require(!insufficient.Success && insufficient.Values.Count == 0 && insufficient.DatumPlaneFitEvidence.Success, "Leveling's own minimum support must not be lowered after selection.");
            options.MinimumValidSampleCount = 3;
            options.DatumPlaneFitOptions.MinimumInlierRatio = 1.0;
            LevelSurfaceResult rejected = tool.Execute(5, 5, values, regions, options);
            Require(!rejected.Success && rejected.DatumPlaneFitEvidence.FailureReason == DatumPlaneFitFailureReason.InsufficientSupport, "Leveling must preserve the typed datum failure.");
            options.DatumPlaneFitOptions.MinimumInlierRatio = 0.75;
            RequireThrows<OperationCanceledException>(() => tool.Execute(5, 5, values, regions, options, new CancellationToken(true)));
            Require(tool.Execute(5, 5, values, regions, options).Success, "The same leveling Tool must recover after rejection and cancellation.");
            options.DatumPlaneFitOptions = null;
            LevelSurfaceResult legacy = tool.Execute(5, 5, values, regions, options);
            Require(legacy.Success && legacy.ReferenceSampleCount == 9 && legacy.DatumPlaneFitEvidence == null
                && legacy.ReferenceSampleIndices.Count == 0, "Missing opt-in must retain the existing all-reference path.");
        }

        private static void TestSnapshotAndRecovery()
        {
            HeightFieldPlaneFitSample[] samples = Grid(3, 3, (row, column) => 0.0);
            DatumPlaneFitOptions options = Options();
            DeterministicDatumPlaneFitTool tool = new DeterministicDatumPlaneFitTool();
            DatumPlaneFitResult result = tool.Execute(samples, options);
            samples[0] = new HeightFieldPlaneFitSample(new ThreeDPoint(0, 100, 0), 100);
            options.MaximumAbsoluteHeightResidual = 0.5;
            options.MinimumInlierCount = 7;
            options.MinimumInlierRatio = 0.9;
            Require(result.InlierSamples[0].RawHeight == 0.0 && result.MaximumAbsoluteHeightResidual == 0.1
                && result.MinimumInlierCount == 3 && result.MinimumInlierRatio == 0.75, "Input replacement or option changes must not rewrite a completed snapshot.");
            RequireThrows<NotSupportedException>(() => ((IList<int>)result.InlierSampleIndices)[0] = 99);
            RequireThrows<NotSupportedException>(() => ((IList<double>)result.HeightResiduals)[0] = 99.0);
            RequireThrows<ArgumentNullException>(() => tool.Execute(null, Options()));
            RequireThrows<ArgumentNullException>(() => tool.Execute(samples, null));
            RequireThrows<ArgumentException>(() => tool.Execute(samples, Options(double.NaN)));
            RequireThrows<ArgumentException>(() => tool.Execute(samples, Options(0.1, 0.5)));
            RequireThrows<ArgumentException>(() => tool.Execute(samples, Options(0.1, double.PositiveInfinity)));
            HeightFieldPlaneFitSample[] bad = (HeightFieldPlaneFitSample[])samples.Clone();
            bad[1] = new HeightFieldPlaneFitSample(new ThreeDPoint(1, double.NaN, 0), 0.0);
            RequireThrows<ArgumentException>(() => tool.Execute(bad, Options()));
            bad[1] = new HeightFieldPlaneFitSample(new ThreeDPoint(1, 0, 0), double.PositiveInfinity);
            RequireThrows<ArgumentException>(() => tool.Execute(bad, Options()));
            HeightFieldPlaneFitSample[] huge =
            {
                new HeightFieldPlaneFitSample(new ThreeDPoint(0, 0, 0), 0),
                new HeightFieldPlaneFitSample(new ThreeDPoint(1e200, 0, 0), 0),
                new HeightFieldPlaneFitSample(new ThreeDPoint(0, 0, 1e200), 0)
            };
            Require(tool.Execute(huge, Options()).FailureReason == DatumPlaneFitFailureReason.NumericalFailure, "Finite arithmetic overflow must not produce a usable datum.");
            RequireThrows<OperationCanceledException>(() => tool.Execute(samples, Options(), new CancellationToken(true)));
            HeightFieldPlaneFitSample[] activeSamples = Grid(5, 5, (row, column) => 0.0);
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                CancelingSamples duringClassification = new CancelingSamples(activeSamples, cancellation, activeSamples.Length + 5);
                RequireThrows<OperationCanceledException>(() => tool.Execute(duringClassification, Options(), cancellation.Token));
                Require(duringClassification.ReadCount > activeSamples.Length, "Active cancellation must occur after input validation begins hypothesis work.");
            }
            Require(tool.Execute(Grid(3, 3, (row, column) => 0.0), Options()).Success, "The datum Tool must recover after numeric and canceled inputs.");
        }

        private static void TestBoundedPool()
        {
            int[] pool = Enumerable.Range(0, 12).Select(index => index * 24 / 11).ToArray();
            HeightFieldPlaneFitSample[] samples = Enumerable.Range(0, 25).Select(index =>
                new HeightFieldPlaneFitSample(new ThreeDPoint(index, 0.0, pool.Contains(index) ? 0.0 : 1.0 + index % 3), 0.0)).ToArray();
            RequireApproximately(new LeastSquaresHeightFieldPlaneFitTool().Execute(samples).RootMeanSquareDistance, 0.0, 1e-12, "The independent full reference is a valid zero plane.");
            DatumPlaneFitResult result = new DeterministicDatumPlaneFitTool().Execute(samples, Options());
            Require(!result.Success && result.FailureReason == DatumPlaneFitFailureReason.DegenerateGeometry
                && result.HypothesisSampleIndices.SequenceEqual(pool) && result.EvaluatedHypothesisCount == 220,
                "A degenerate finite pool must expose its limit without claiming exhaustive recovery.");
        }

        private sealed class CancelingSamples : IReadOnlyList<HeightFieldPlaneFitSample>
        {
            private readonly HeightFieldPlaneFitSample[] samples;
            private readonly CancellationTokenSource cancellation;
            private readonly int cancelAtRead;

            internal CancelingSamples(HeightFieldPlaneFitSample[] samples, CancellationTokenSource cancellation, int cancelAtRead)
            {
                this.samples = samples;
                this.cancellation = cancellation;
                this.cancelAtRead = cancelAtRead;
            }

            public int Count => samples.Length;
            internal int ReadCount { get; private set; }
            public HeightFieldPlaneFitSample this[int index]
            {
                get
                {
                    if (++ReadCount == cancelAtRead) cancellation.Cancel();
                    return samples[index];
                }
            }

            public IEnumerator<HeightFieldPlaneFitSample> GetEnumerator() => ((IEnumerable<HeightFieldPlaneFitSample>)samples).GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}

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
    internal static class ThreeDStatisticsAndEvidenceSmokeSuite
    {
        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase("Height-grid summary preserves missing policy and distribution", TestHeightGridSummary);
            yield return new SmokeCase("Height distribution preserves finite statistics and tie order", TestHeightDistributionStatistics);
            yield return new SmokeCase("Grid diagnostics preserve implicit row-major evidence", TestImplicitGridDiagnostics);
            yield return new SmokeCase("Grid diagnostics preserve exact malformed explicit evidence", TestMalformedExplicitGridDiagnostics);
            yield return new SmokeCase("Connected regions preserve deterministic four- and eight-neighbor labeling", TestConnectedRegions);
            yield return new SmokeCase("Connected regions fail closed on invalid masks", TestConnectedRegionsInvalidInput);
            yield return new SmokeCase("Connected region metrics preserve area, center, orientation, and bounds", TestConnectedRegionMetrics);
            yield return new SmokeCase("Connected region metrics fail closed on invalid geometry", TestConnectedRegionMetricsInvalidInput);
            yield return new SmokeCase("Connected region presence preserves explicit coverage and height decisions", TestConnectedRegionPresence);
            yield return new SmokeCase("Connected region presence fails closed on invalid inputs", TestConnectedRegionPresenceInvalidInput);
            yield return new SmokeCase("Connected region fill height preserves reference-surface residuals and per-region gates", TestConnectedRegionFillHeight);
            yield return new SmokeCase("Connected region fill height fails closed on invalid inputs", TestConnectedRegionFillHeightInvalidInput);
            yield return new SmokeCase("Height-map region statistics preserve row-major aggregation", TestHeightMapRegionStatistics);
            yield return new SmokeCase("Completeness Grid preserves reference-relative cell decisions", TestCompletenessGridInspection);
            yield return new SmokeCase("Mask-aware Completeness excludes unselected cells", TestMaskAwareCompletenessGridInspection);
            yield return new SmokeCase("Mask-aware Completeness fails closed on invalid masks", TestMaskAwareCompletenessGridInspectionInvalidInput);
            yield return new SmokeCase("Reference-grid reconstruction preserves declared and reference-axis coordinates", TestReferenceGridPointReconstruction);
            yield return new SmokeCase("Dual-surface thickness preserves analytic separation statistics", TestDualSurfaceThicknessInspection);
            yield return new SmokeCase("Dual-surface thickness preserves independent lower and upper failures", TestDualSurfaceThicknessInspectionFailure);
            yield return new SmokeCase("Dual-surface thickness rejects degenerate reference geometry", TestDualSurfaceThicknessInspectionDegenerateReference);
            yield return new SmokeCase("Height deviation preserves peak-side selection and pass decision", TestHeightDeviationInspection);
            yield return new SmokeCase("Height deviation preserves tolerance failure", TestHeightDeviationInspectionFailure);
            yield return new SmokeCase("Height deviation rejects invalid summary evidence", TestHeightDeviationInspectionInvalidInput);
            yield return new SmokeCase("Declared mesh normal quality accepts dense aligned normals", TestDeclaredMeshNormalQualityValid);
            yield return new SmokeCase("Declared mesh normal quality rejects reversed normals", TestDeclaredMeshNormalQualityReversed);
            yield return new SmokeCase("Declared mesh normal quality rejects partial and invalid topology", TestDeclaredMeshNormalQualityPartialAndInvalidTopology);
            yield return new SmokeCase("Landmark correspondence validation accepts independent tetrahedra", TestLandmarkCorrespondenceValidation);
            yield return new SmokeCase("Landmark correspondence validation rejects a coplanar source", TestLandmarkCorrespondenceValidationCoplanar);
            yield return new SmokeCase("Landmark correspondence validation rejects the taught volume boundary", TestLandmarkCorrespondenceValidationBoundary);
            yield return new SmokeCase("Repeatability statistics preserve sample standard deviation and range", TestRepeatabilityStatistics);
            yield return new SmokeCase("Repeatability statistics preserve a zero-spread series", TestRepeatabilityStatisticsZeroSpread);
            yield return new SmokeCase("Repeatability statistics reject insufficient and non-finite input", TestRepeatabilityStatisticsInvalidInput);
            yield return new SmokeCase("Labeled evidence statistics preserve role groups and population spread", TestLabeledEvidenceStatistics);
            yield return new SmokeCase("Labeled evidence statistics preserve empty roles", TestLabeledEvidenceStatisticsEmptyRoles);
            yield return new SmokeCase("Labeled evidence statistics reject invalid input", TestLabeledEvidenceStatisticsInvalidInput);
            yield return new SmokeCase("Threshold candidate analysis selects deterministic minimum, maximum, and range", TestThresholdCandidateAnalysis);
            yield return new SmokeCase("Threshold candidate analysis rejects invalid development evidence", TestThresholdCandidateAnalysisInvalidInput);
        }

        private static void TestHeightGridSummary()
        {
            HeightGridSummaryResult result = new HeightGridSummaryTool().Execute(
                new[] { 0.0f, 1.0f, 2.0f, float.NaN, 3.0f, 4.0f },
                new HeightGridSummaryOptions
                {
                    ZeroIsMissing = true,
                    DistributionBinCount = 2
                });

            Require(result.Success, result.Message);
            Require(result.SampleCount == 6
                    && result.ValidSampleCount == 4
                    && result.MissingSampleCount == 2
                    && result.ZeroSampleCount == 1
                    && result.NonFiniteSampleCount == 1,
                "Height-grid missing policy evidence changed.");
            RequireApproximately(result.Minimum, 1.0, 0.0, "Unexpected height-grid minimum.");
            RequireApproximately(result.Maximum, 4.0, 0.0, "Unexpected height-grid maximum.");
            RequireApproximately(result.Mean, 2.5, 0.0, "Unexpected height-grid mean.");
            Require(result.Bins.Count == 2
                    && result.Bins[0] == 2
                    && result.Bins[1] == 2
                    && result.PeakBinIndex == 0,
                "Height-grid distribution or exact-tie order changed.");
            RequireApproximately(result.PeakLowerBound, 1.0, 0.0, "Unexpected peak lower bound.");
            RequireApproximately(result.PeakUpperBound, 2.5, 0.0, "Unexpected peak upper bound.");
        }

        private static void TestHeightDistributionStatistics()
        {
            HeightDistributionStatisticsResult result =
                new HeightDistributionStatisticsTool().Execute(
                    new[] { 1.0, 2.0, double.NaN, 3.0, 4.0 },
                    new HeightDistributionStatisticsOptions
                    {
                        BinCount = 2,
                        ExpectedValidSampleCount = 4
                    });

            Require(result.Success, result.Message);
            Require(result.ValidSampleCount == 4
                    && result.MissingSampleCount == 1
                    && result.PeakBinIndex == 0
                    && result.Bins[0] == 2
                    && result.Bins[1] == 2,
                "Height-distribution finite counts, bins, or tie order changed.");
            RequireApproximately(result.Minimum, 1.0, 0.0, "Unexpected distribution minimum.");
            RequireApproximately(result.Maximum, 4.0, 0.0, "Unexpected distribution maximum.");
            RequireApproximately(result.Mean, 2.5, 0.0, "Unexpected distribution mean.");
        }

        private static void TestImplicitGridDiagnostics()
        {
            GridDiagnosticsResult result = new GridDiagnosticsTool().Execute(2, 2);

            Require(result.State == GridDiagnosticState.Pass
                    && result.DeclaredCellCount == 4
                    && result.ObservedSampleCount == 4
                    && result.UniqueLocatorCount == 4,
                "Implicit grid diagnostic counts changed.");
            Require(result.Checks.Count == 4
                    && result.Checks[0].Code == GridDiagnosticCode.Topology
                    && result.Checks[1].Code == GridDiagnosticCode.LocatorMonotonicity
                    && result.Checks[2].Code == GridDiagnosticCode.DuplicateLocator
                    && result.Checks[3].Code == GridDiagnosticCode.CoordinateFiniteness,
                "Implicit grid diagnostic order changed.");
            Require(result.Checks.All(check =>
                    check.State == GridDiagnosticState.Pass
                    && check.AffectedCount == 0
                    && !check.FirstSampleOrdinal.HasValue),
                "Implicit grid diagnostics must remain exact pass evidence.");
        }

        private static void TestMalformedExplicitGridDiagnostics()
        {
            GridDiagnosticsResult result = new GridDiagnosticsTool().Execute(
                2,
                2,
                new[]
                {
                    new GridCoordinateSample(0, 0, 0.0, 0.0, 1.0),
                    new GridCoordinateSample(1, 0, 0.0, 1.0, 2.0),
                    new GridCoordinateSample(0, 1, 1.0, double.NaN, double.PositiveInfinity),
                    new GridCoordinateSample(0, 1, 1.0, 0.0, 4.0)
                });

            Require(result.State == GridDiagnosticState.Error
                    && result.DeclaredCellCount == 4
                    && result.ObservedSampleCount == 4
                    && result.UniqueLocatorCount == 3,
                "Malformed explicit grid diagnostic counts changed.");
            Require(result.Checks[0].State == GridDiagnosticState.Error
                    && result.Checks[0].AffectedCount == 1
                    && result.Checks[0].FirstSampleOrdinal == 3
                    && result.Checks[0].FirstRow == 0
                    && result.Checks[0].FirstColumn == 1
                    && result.Checks[0].FirstComponent == "Locator"
                    && result.Checks[0].Message == "Grid topology has 1 mismatch(es).",
                "Explicit topology evidence changed.");
            Require(result.Checks[1].State == GridDiagnosticState.Error
                    && result.Checks[1].AffectedCount == 1
                    && result.Checks[1].FirstSampleOrdinal == 2
                    && result.Checks[1].FirstRow == 0
                    && result.Checks[1].FirstColumn == 1
                    && result.Checks[1].FirstComponent == "Locator"
                    && result.Checks[1].Message == "Grid has 1 descending locator transition(s).",
                "Explicit locator-order evidence changed.");
            Require(result.Checks[2].State == GridDiagnosticState.Error
                    && result.Checks[2].AffectedCount == 1
                    && result.Checks[2].FirstSampleOrdinal == 3
                    && result.Checks[2].FirstRow == 0
                    && result.Checks[2].FirstColumn == 1
                    && result.Checks[2].FirstComponent == "Locator"
                    && result.Checks[2].Message == "Grid has 1 duplicate locator occurrence(s).",
                "Explicit duplicate-locator evidence changed.");
            Require(result.Checks[3].State == GridDiagnosticState.Error
                    && result.Checks[3].AffectedCount == 2
                    && result.Checks[3].FirstSampleOrdinal == 2
                    && result.Checks[3].FirstRow == 0
                    && result.Checks[3].FirstColumn == 1
                    && result.Checks[3].FirstComponent == "Y"
                    && result.Checks[3].Message == "Grid has 2 non-finite coordinate component(s).",
                "Explicit coordinate-finiteness evidence changed.");
        }

        private static void TestConnectedRegions()
        {
            bool[] mask =
            {
                true, true, false, false, false,
                false, true, false, false, true,
                false, false, false, true, false,
                false, false, false, true, true,
                false, false, false, false, false
            };

            ConnectedRegionTool tool = new ConnectedRegionTool();
            ConnectedRegionResult four = tool.Execute(
                new HeightGridMask(5, 5, mask),
                new ConnectedRegionOptions
                {
                    Connectivity = ConnectedRegionConnectivity.Four
                });
            ConnectedRegionResult eight = tool.Execute(
                new HeightGridMask(5, 5, mask),
                new ConnectedRegionOptions
                {
                    Connectivity = ConnectedRegionConnectivity.Eight
                });

            Require(four.Success
                    && four.ForegroundCellCount == 7
                    && four.RegionCount == 3
                    && four.Regions.Select(region => region.CellCount)
                        .SequenceEqual(new[] { 3, 1, 3 }),
                "Four-neighbor connected-region counts changed.");
            Require(four.Regions[0].SeedRow == 0
                    && four.Regions[0].SeedColumn == 0
                    && four.Regions[0].MinimumRow == 0
                    && four.Regions[0].MinimumColumn == 0
                    && four.Regions[0].MaximumRow == 1
                    && four.Regions[0].MaximumColumn == 1
                    && four.Regions[0].Cells.Select(cell => cell.Row + "," + cell.Column)
                        .SequenceEqual(new[] { "0,0", "0,1", "1,1" }),
                "Four-neighbor region identity or row-major cell order changed.");
            Require(eight.Success
                    && eight.ForegroundCellCount == 7
                    && eight.RegionCount == 2
                    && eight.Regions.Select(region => region.CellCount)
                        .SequenceEqual(new[] { 3, 4 }),
                "Eight-neighbor connected-region counts changed.");
            Require(eight.Regions[1].Cells.Select(cell => cell.Row + "," + cell.Column)
                .SequenceEqual(new[] { "1,4", "2,3", "3,3", "3,4" }),
                "Eight-neighbor diagonal connectivity changed.");
        }

        private static void TestConnectedRegionsInvalidInput()
        {
            ConnectedRegionTool tool = new ConnectedRegionTool();
            ConnectedRegionResult mismatchedMask = tool.Execute(
                new HeightGridMask(2, 2, new[] { true, false, true }));
            ConnectedRegionResult invalidConnectivity = tool.Execute(
                new HeightGridMask(1, 1, new[] { true }),
                new ConnectedRegionOptions
                {
                    Connectivity = (ConnectedRegionConnectivity)99
                });
            CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            bool cancellationPropagated = false;
            try
            {
                tool.Execute(
                    new HeightGridMask(1, 1, new[] { true }),
                    cancellationToken: cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                cancellationPropagated = true;
            }
            finally
            {
                cancellation.Dispose();
            }

            Require(!mismatchedMask.Success
                    && mismatchedMask.Regions.Count == 0
                    && mismatchedMask.ForegroundCellCount == 0,
                "A mask/value dimension mismatch must fail closed.");
            Require(!invalidConnectivity.Success
                    && invalidConnectivity.Regions.Count == 0,
                "Unsupported connected-region connectivity must fail closed.");
            Require(cancellationPropagated,
                "Connected-region cancellation must propagate without a partial result.");
        }

        private static void TestConnectedRegionMetrics()
        {
            ConnectedRegionResult labeled = new ConnectedRegionTool().Execute(
                new HeightGridMask(
                    4,
                    6,
                    new[]
                    {
                        true, true, true, false, false, false,
                        false, false, false, false, false, false,
                        false, false, false, false, true, false,
                        false, false, false, false, false, true
                    }),
                new ConnectedRegionOptions
                {
                    Connectivity = ConnectedRegionConnectivity.Eight
                });
            ConnectedRegionMetricsResult result = new ConnectedRegionMetricsTool().Execute(
                labeled,
                new ConnectedRegionMetricsOptions
                {
                    OriginX = 10.0,
                    OriginY = 20.0,
                    ColumnPitch = 2.0,
                    RowPitch = 3.0
                });

            ConnectedRegionMetric horizontal = result.Regions[0];
            ConnectedRegionMetric diagonal = result.Regions[1];
            Require(result.Success
                    && result.RegionCount == 2
                    && result.TotalArea == 30.0,
                "Connected-region metric aggregate counts or area changed.");
            Require(horizontal.CellCount == 3
                    && horizontal.Area == 18.0
                    && horizontal.CenterX == 12.0
                    && horizontal.CenterY == 20.0
                    && horizontal.HasOrientation
                    && horizontal.OrientationDegrees == 0.0
                    && horizontal.Bounding.MinimumX == 9.0
                    && horizontal.Bounding.MinimumY == 18.5
                    && horizontal.Bounding.MaximumX == 15.0
                    && horizontal.Bounding.MaximumY == 21.5
                    && horizontal.Bounding.Width == 6.0
                    && horizontal.Bounding.Height == 3.0,
                "Horizontal connected-region metrics or bounds changed.");
            Require(diagonal.CellCount == 2
                    && diagonal.Area == 12.0
                    && diagonal.CenterX == 19.0
                    && diagonal.CenterY == 27.5
                    && diagonal.HasOrientation
                    && Math.Abs(diagonal.OrientationDegrees
                        - (Math.Atan2(3.0, 2.0) * 180.0 / Math.PI)) < 1e-12
                    && diagonal.Bounding.MinimumX == 17.0
                    && diagonal.Bounding.MinimumY == 24.5
                    && diagonal.Bounding.MaximumX == 21.0
                    && diagonal.Bounding.MaximumY == 30.5,
                "Diagonal connected-region metrics, orientation, or bounds changed.");

            ConnectedRegionResult isotropic = new ConnectedRegionTool().Execute(
                new HeightGridMask(2, 2, new[] { true, true, true, true }));
            ConnectedRegionMetricsResult isotropicMetrics = new ConnectedRegionMetricsTool().Execute(isotropic);
            Require(isotropicMetrics.Success
                    && isotropicMetrics.Regions.Count == 1
                    && !isotropicMetrics.Regions[0].HasOrientation
                    && double.IsNaN(isotropicMetrics.Regions[0].OrientationDegrees),
                "An isotropic region must not fabricate an orientation.");
        }

        private static void TestConnectedRegionMetricsInvalidInput()
        {
            ConnectedRegionMetricsTool tool = new ConnectedRegionMetricsTool();
            ConnectedRegionResult failedLabeling = new ConnectedRegionTool().Execute(
                new HeightGridMask(2, 2, new[] { true, false, true }));
            ConnectedRegionMetricsResult failedInput = tool.Execute(failedLabeling);
            ConnectedRegionResult validLabeling = new ConnectedRegionTool().Execute(
                new HeightGridMask(1, 1, new[] { true }));
            ConnectedRegionMetricsResult invalidGeometry = tool.Execute(
                validLabeling,
                new ConnectedRegionMetricsOptions
                {
                    ColumnPitch = 0.0
                });
            CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            bool cancellationPropagated = false;
            try
            {
                tool.Execute(validLabeling, cancellationToken: cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                cancellationPropagated = true;
            }
            finally
            {
                cancellation.Dispose();
            }

            Require(!failedInput.Success && failedInput.Regions.Count == 0,
                "Metrics must fail closed when labeling failed.");
            Require(!invalidGeometry.Success && invalidGeometry.Regions.Count == 0,
                "Non-positive metric pitches must fail closed.");
            Require(cancellationPropagated,
                "Connected-region metric cancellation must propagate without a partial result.");
        }

        private static void TestConnectedRegionPresence()
        {
            ConnectedRegionResult labeled = new ConnectedRegionTool().Execute(
                new HeightGridMask(
                    3,
                    4,
                    new[]
                    {
                        true, true, false, false,
                        false, false, false, true,
                        false, false, false, false
                    }));
            ConnectedRegionPresenceResult result =
                new ConnectedRegionPresenceTool().Execute(
                    labeled,
                    3,
                    4,
                    new[]
                    {
                        5.0, 5.0, double.NaN, double.NaN,
                        double.NaN, double.NaN, double.NaN, 1.0,
                        double.NaN, double.NaN, double.NaN, double.NaN
                    },
                    new ConnectedRegionPresenceOptions
                    {
                        MinimumFiniteCoverageRatio = 1.0,
                        MinimumMeanHeight = 4.0,
                        MaximumMeanHeight = 6.0
                    });
            ConnectedRegionPresenceFeature present = result.Regions[0];
            ConnectedRegionPresenceFeature missing = result.Regions[1];

            Require(result.Success
                    && result.RegionCount == 2
                    && result.PresentRegionCount == 1
                    && result.MissingRegionCount == 1
                    && result.AggregateDecision
                        == ConnectedRegionPresenceDecision.Present,
                "Connected-region presence aggregate decisions changed.");
            Require(present.TotalCellCount == 2
                    && present.FiniteCellCount == 2
                    && present.MissingCellCount == 0
                    && present.FiniteCoverageRatio == 1.0
                    && present.MeanHeight == 5.0
                    && present.CoverageDisposition
                        == ConnectedRegionPresenceCoverageDisposition.Accepted
                    && present.HeightDisposition
                        == ConnectedRegionPresenceHeightDisposition.Accepted
                    && present.Decision == ConnectedRegionPresenceDecision.Present,
                "Present connected-region coverage or height evidence changed.");
            Require(missing.TotalCellCount == 1
                    && missing.FiniteCellCount == 1
                    && missing.FiniteCoverageRatio == 1.0
                    && missing.MeanHeight == 1.0
                    && missing.HeightDisposition
                        == ConnectedRegionPresenceHeightDisposition.BelowMinimum
                    && missing.Decision == ConnectedRegionPresenceDecision.Missing,
                "Missing connected-region height evidence changed.");

            ConnectedRegionResult noRegions = new ConnectedRegionTool().Execute(
                new HeightGridMask(1, 2, new[] { false, false }));
            ConnectedRegionPresenceResult noRegionResult =
                new ConnectedRegionPresenceTool().Execute(
                    noRegions,
                    1,
                    2,
                    new[] { double.NaN, double.NaN });
            Require(noRegionResult.Success
                    && noRegionResult.RegionCount == 0
                    && noRegionResult.AggregateDecision
                        == ConnectedRegionPresenceDecision.Missing,
                "An empty connected-region set must remain explicit missing evidence.");
        }

        private static void TestConnectedRegionPresenceInvalidInput()
        {
            ConnectedRegionPresenceTool tool = new ConnectedRegionPresenceTool();
            ConnectedRegionResult failedLabeling = new ConnectedRegionTool().Execute(
                new HeightGridMask(2, 2, new[] { true, false, true }));
            ConnectedRegionPresenceResult failedInput = tool.Execute(
                failedLabeling,
                2,
                2,
                new[] { 1.0, 1.0, 1.0, 1.0 });
            ConnectedRegionResult validLabeling = new ConnectedRegionTool().Execute(
                new HeightGridMask(1, 1, new[] { true }));
            ConnectedRegionPresenceResult invalidCoverage = tool.Execute(
                validLabeling,
                1,
                1,
                new[] { 1.0 },
                new ConnectedRegionPresenceOptions
                {
                    MinimumFiniteCoverageRatio = 1.1
                });
            ConnectedRegionPresenceResult invalidRange = tool.Execute(
                validLabeling,
                1,
                1,
                new[] { 1.0 },
                new ConnectedRegionPresenceOptions
                {
                    MinimumMeanHeight = 2.0,
                    MaximumMeanHeight = 1.0
                });
            CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            bool cancellationPropagated = false;
            try
            {
                tool.Execute(
                    validLabeling,
                    1,
                    1,
                    new[] { 1.0 },
                    cancellationToken: cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                cancellationPropagated = true;
            }
            finally
            {
                cancellation.Dispose();
            }

            Require(!failedInput.Success && failedInput.Regions.Count == 0,
                "Presence must fail closed when labeling failed.");
            Require(!invalidCoverage.Success && invalidCoverage.Regions.Count == 0,
                "Coverage thresholds outside [0, 1] must fail closed.");
            Require(!invalidRange.Success && invalidRange.Regions.Count == 0,
                "Reversed height thresholds must fail closed.");
            Require(cancellationPropagated,
                "Connected-region presence cancellation must propagate without a partial result.");
        }

        private static void TestConnectedRegionFillHeight()
        {
            ConnectedRegionResult labeled = new ConnectedRegionTool().Execute(
                new HeightGridMask(
                    5,
                    5,
                    new[]
                    {
                        true, true, false, true, true,
                        true, false, false, false, false,
                        false, false, false, false, false,
                        false, false, false, false, false,
                        false, false, false, false, false
                    }));
            ConnectedRegionFillHeightResult result =
                new ConnectedRegionFillHeightTool().Execute(
                    labeled,
                    5,
                    5,
                    new[]
                    {
                        12.0, 12.5, double.NaN, 10.5, 11.0,
                        11.75, double.NaN, double.NaN, double.NaN, double.NaN,
                        double.NaN, double.NaN, double.NaN, double.NaN, double.NaN,
                        double.NaN, double.NaN, double.NaN, double.NaN, double.NaN,
                        double.NaN, double.NaN, double.NaN, double.NaN, double.NaN
                    },
                    new ConnectedRegionFillHeightOptions
                    {
                        ReferenceSurface = new ConnectedRegionFillHeightReferenceSurface
                        {
                            SlopeX = 0.5,
                            SlopeZ = -0.25,
                            Intercept = 10.0
                        },
                        MinimumFiniteCoverageRatio = 1.0,
                        MinimumMeanFillHeight = 1.5,
                        MaximumMeanFillHeight = 2.5
                    });
            ConnectedRegionFillHeightFeature accepted = result.Regions[0];
            ConnectedRegionFillHeightFeature rejected = result.Regions[1];

            Require(result.Success
                    && result.RegionCount == 2
                    && result.AcceptedRegionCount == 1
                    && result.RejectedRegionCount == 1,
                "Connected-region fill-height region decisions changed.");
            Require(accepted.TotalCellCount == 3
                    && accepted.FiniteCellCount == 3
                    && accepted.FiniteCoverageRatio == 1.0
                    && accepted.MeanFillHeight == 2.0
                    && accepted.MinimumFillHeight == 2.0
                    && accepted.MaximumFillHeight == 2.0
                    && accepted.CoverageDisposition
                        == ConnectedRegionFillHeightCoverageDisposition.Accepted
                    && accepted.FillHeightDisposition
                        == ConnectedRegionFillHeightDisposition.Accepted
                    && accepted.Decision == ConnectedRegionFillHeightDecision.Accepted,
                "Accepted connected-region fill-height evidence changed.");
            Require(rejected.TotalCellCount == 2
                    && rejected.FiniteCellCount == 2
                    && rejected.FiniteCoverageRatio == 1.0
                    && rejected.MeanFillHeight == -1.0
                    && rejected.MinimumFillHeight == -1.0
                    && rejected.MaximumFillHeight == -1.0
                    && rejected.FillHeightDisposition
                        == ConnectedRegionFillHeightDisposition.BelowMinimum
                    && rejected.Decision == ConnectedRegionFillHeightDecision.Rejected,
                "Rejected connected-region fill-height evidence changed.");
        }

        private static void TestConnectedRegionFillHeightInvalidInput()
        {
            ConnectedRegionFillHeightTool tool = new ConnectedRegionFillHeightTool();
            ConnectedRegionResult failedLabeling = new ConnectedRegionTool().Execute(
                new HeightGridMask(2, 2, new[] { true, false, true }));
            ConnectedRegionFillHeightResult failedInput = tool.Execute(
                failedLabeling,
                2,
                2,
                new[] { 1.0, 1.0, 1.0, 1.0 },
                new ConnectedRegionFillHeightOptions
                {
                    ReferenceSurface = new ConnectedRegionFillHeightReferenceSurface()
                });
            ConnectedRegionResult validLabeling = new ConnectedRegionTool().Execute(
                new HeightGridMask(1, 1, new[] { true }));
            ConnectedRegionFillHeightResult invalidSurface = tool.Execute(
                validLabeling,
                1,
                1,
                new[] { 1.0 },
                new ConnectedRegionFillHeightOptions
                {
                    ReferenceSurface = new ConnectedRegionFillHeightReferenceSurface
                    {
                        SlopeX = double.NaN
                    }
                });
            ConnectedRegionFillHeightResult invalidRange = tool.Execute(
                validLabeling,
                1,
                1,
                new[] { 1.0 },
                new ConnectedRegionFillHeightOptions
                {
                    ReferenceSurface = new ConnectedRegionFillHeightReferenceSurface(),
                    MinimumMeanFillHeight = 2.0,
                    MaximumMeanFillHeight = 1.0
                });
            CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            bool cancellationPropagated = false;
            try
            {
                tool.Execute(
                    validLabeling,
                    1,
                    1,
                    new[] { 1.0 },
                    new ConnectedRegionFillHeightOptions
                    {
                        ReferenceSurface = new ConnectedRegionFillHeightReferenceSurface()
                    },
                    cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                cancellationPropagated = true;
            }
            finally
            {
                cancellation.Dispose();
            }

            Require(!failedInput.Success && failedInput.Regions.Count == 0,
                "Fill height must fail closed when labeling failed.");
            Require(!invalidSurface.Success && invalidSurface.Regions.Count == 0,
                "Non-finite reference-surface coefficients must fail closed.");
            Require(!invalidRange.Success && invalidRange.Regions.Count == 0,
                "Reversed fill-height thresholds must fail closed.");
            Require(cancellationPropagated,
                "Connected-region fill-height cancellation must propagate without a partial result.");
        }

        private static void TestHeightMapRegionStatistics()
        {
            HeightMapRegionStatisticsResult result =
                new HeightMapRegionStatisticsTool().Execute(
                    3,
                    3,
                    new[]
                    {
                        1.0, 2.0, double.NaN,
                        4.0, 5.0, 6.0,
                        7.0, 8.0, 9.0
                    },
                    new HeightGridRegion(1, 0, 2, 2));

            Require(result.Success, result.Message);
            Require(result.TotalCellCount == 4
                    && result.FiniteCellCount == 4
                    && result.MissingCellCount == 0,
                "Height-map region counts changed.");
            RequireApproximately(result.Sum, 24.0, 0.0, "Unexpected region sum.");
            RequireApproximately(result.Mean, 6.0, 0.0, "Unexpected region mean.");
            RequireApproximately(result.FiniteCoverageRatio, 1.0, 0.0, "Unexpected region coverage.");
        }

        private static void TestCompletenessGridInspection()
        {
            CompletenessGridInspectionResult result =
                new CompletenessGridInspectionTool().Execute(
                    4,
                    4,
                    new[]
                    {
                        10.0, 10.0, 10.0, 10.0,
                        10.0, 10.0, 10.0, 10.0,
                        11.0, 11.0, double.NaN, double.NaN,
                        10.0, 10.0, 10.0, 10.0
                    },
                    new HeightGridRegion(0, 0, 1, 2),
                    new HeightGridRegion(2, 0, 1, 4),
                    new CompletenessGridProfile
                    {
                        Rows = 1,
                        Columns = 2,
                        XPitchColumns = 2,
                        ZPitchRows = 1,
                        CellWidthColumns = 2,
                        CellHeightRows = 1
                    },
                    new CompletenessPresencePolicy
                    {
                        MinimumFiniteCoverageRatio = 0.5,
                        MinimumReferenceRelativeMeanHeight = 0.0,
                        MaximumReferenceRelativeMeanHeight = 2.0
                    });

            Require(result.Success, result.Message);
            Require(result.ReferenceFiniteCellCount == 2
                    && result.ReferenceMeanHeight == 10.0
                    && result.Cells.Count == 2
                    && result.PassedCellCount == 1
                    && result.FailedCellCount == 1
                    && result.AggregateDecision == CompletenessCellDecision.Fail,
                "Completeness-grid aggregate evidence changed.");
            Require(result.Cells[0].Decision == CompletenessCellDecision.Pass
                    && result.Cells[0].ReferenceRelativeMeanHeight == 1.0
                    && result.Cells[1].Decision == CompletenessCellDecision.Fail
                    && result.Cells[1].HeightDisposition == CompletenessHeightDisposition.Missing,
                "Completeness-grid cell evidence changed.");
        }

        private static void TestMaskAwareCompletenessGridInspection()
        {
            CompletenessGridInspectionResult result =
                new CompletenessGridInspectionTool().ExecuteMaskAware(
                    3,
                    3,
                    new[]
                    {
                        10.0, 100.0, 10.0,
                        12.0, 100.0, 12.0,
                        double.NaN, 12.0, 100.0
                    },
                    new HeightGridRegion(0, 0, 1, 1),
                    new HeightGridRegion(1, 0, 2, 3),
                    new HeightGridMask(
                        3,
                        3,
                        new[]
                        {
                            false, false, false,
                            true, false, true,
                            true, true, false
                        }),
                    new CompletenessGridProfile
                    {
                        Rows = 1,
                        Columns = 1,
                        XPitchColumns = 3,
                        ZPitchRows = 2,
                        CellWidthColumns = 3,
                        CellHeightRows = 2
                    },
                    new CompletenessPresencePolicy
                    {
                        MinimumFiniteCoverageRatio = 0.75,
                        MinimumReferenceRelativeMeanHeight = 0.0,
                        MaximumReferenceRelativeMeanHeight = 2.0
                    });

            Require(result.Success, result.Message);
            Require(result.Cells.Count == 1
                    && result.Cells[0].TotalCellCount == 4
                    && result.Cells[0].FiniteCellCount == 3
                    && result.Cells[0].MissingCellCount == 1
                    && result.Cells[0].FiniteCoverageRatio == 0.75
                    && result.Cells[0].MeanHeight == 12.0
                    && result.Cells[0].ReferenceRelativeMeanHeight == 2.0
                    && result.Cells[0].Decision == CompletenessCellDecision.Pass,
                "Mask-aware Completeness must evaluate only selected cells and preserve selected missing samples.");
        }

        private static void TestMaskAwareCompletenessGridInspectionInvalidInput()
        {
            CompletenessGridInspectionTool tool = new CompletenessGridInspectionTool();
            CompletenessGridProfile profile = new CompletenessGridProfile
            {
                Rows = 1,
                Columns = 1,
                XPitchColumns = 2,
                ZPitchRows = 2,
                CellWidthColumns = 2,
                CellHeightRows = 2
            };
            CompletenessPresencePolicy policy = new CompletenessPresencePolicy
            {
                MinimumFiniteCoverageRatio = 0.0,
                MinimumReferenceRelativeMeanHeight = -1.0,
                MaximumReferenceRelativeMeanHeight = 1.0
            };
            CompletenessGridInspectionResult empty = tool.ExecuteMaskAware(
                2,
                2,
                new[] { 10.0, 10.0, 11.0, 11.0 },
                new HeightGridRegion(0, 0, 1, 1),
                new HeightGridRegion(0, 0, 2, 2),
                new HeightGridMask(2, 2, new[] { false, false, false, false }),
                profile,
                policy);
            CompletenessGridInspectionResult mismatched = tool.ExecuteMaskAware(
                2,
                2,
                new[] { 10.0, 10.0, 11.0, 11.0 },
                new HeightGridRegion(0, 0, 1, 1),
                new HeightGridRegion(0, 0, 2, 2),
                new HeightGridMask(1, 2, new[] { true, true }),
                profile,
                policy);

            Require(!empty.Success
                    && empty.Cells.Count == 0
                    && !mismatched.Success
                    && mismatched.Cells.Count == 0,
                "Empty and dimension-mismatched Completeness masks must fail closed.");
        }

        private static void TestReferenceGridPointReconstruction()
        {
            ReferenceGridDefinition definition = new ReferenceGridDefinition
            {
                Origin = new ReferenceGridVector(10.0, -4.0, 2.0),
                UAxis = new ReferenceGridVector(1.0, 0.0, 0.0),
                VAxis = new ReferenceGridVector(0.0, 0.0, 1.0),
                HAxis = new ReferenceGridVector(0.0, 1.0, 0.0),
                PitchU = 2.0,
                PitchV = 3.0
            };
            ReferenceGridPointReconstructionResult result =
                new ReferenceGridPointReconstructionTool().Execute(
                    2,
                    2,
                    new[] { 1.0, 2.0, 3.0, double.NaN },
                    new HeightGridRegion(0, 0, 2, 2),
                    definition,
                    new ReferenceGridPointReconstructionOptions
                    {
                        CoordinateMode = ReferenceGridCoordinateMode.DeclaredFrame
                    });

            Require(result.Success, result.Message);
            Require(result.Samples.Count == 3, "Reference-grid missing-cell handling changed.");
            ReferenceGridPointSample first = result.Samples[0];
            RequireApproximately(first.U, 1.0, 0.0, "Unexpected first U coordinate.");
            RequireApproximately(first.V, 1.5, 0.0, "Unexpected first V coordinate.");
            RequireApproximately(first.X, 11.0, 0.0, "Unexpected first X coordinate.");
            RequireApproximately(first.Y, -3.0, 0.0, "Unexpected first Y coordinate.");
            RequireApproximately(first.Z, 3.5, 0.0, "Unexpected first Z coordinate.");
            Require(result.Samples[2].Row == 1 && result.Samples[2].Column == 0,
                "Reference-grid row-major order changed.");
        }

        private static void TestDualSurfaceThicknessInspection()
        {
            HeightFieldPlaneFitSample[] reference = CreateThicknessPlaneSamples(10.0, 10.0, 10.0, 10.0);
            HeightFieldPlaneFitSample[] measurement = CreateThicknessPlaneSamples(15.0, 15.0, 15.0, 15.0);
            DualSurfaceThicknessInspectionResult result =
                new DualSurfaceThicknessInspectionTool().Execute(reference, measurement, 4.0, 6.0, 4);

            Require(result.Success && result.Decision == DualSurfaceThicknessDecision.Pass,
                "Analytic dual-surface thickness must pass.");
            RequireApproximately(result.Mean, 5.0, 0.0, "Unexpected thickness mean.");
            RequireApproximately(result.Minimum, 5.0, 0.0, "Unexpected thickness minimum.");
            RequireApproximately(result.Maximum, 5.0, 0.0, "Unexpected thickness maximum.");
            RequireApproximately(result.Range, 0.0, 0.0, "Unexpected thickness range.");
            RequireApproximately(result.RootMeanSquareSpread, 0.0, 0.0, "Unexpected thickness RMS spread.");
            RequireApproximately(result.ReferenceFitHeightRootMeanSquare, 0.0, 0.0, "Unexpected reference H RMS.");
            Require(result.ReferenceSampleCount == 4 && result.MeasurementSampleCount == 4,
                "Dual-surface sample counts changed.");
        }

        private static void TestDualSurfaceThicknessInspectionFailure()
        {
            HeightFieldPlaneFitSample[] reference = CreateThicknessPlaneSamples(10.0, 10.0, 10.0, 10.0);
            HeightFieldPlaneFitSample[] measurement = CreateThicknessPlaneSamples(13.0, 15.0, 15.0, 17.0);
            DualSurfaceThicknessInspectionResult result =
                new DualSurfaceThicknessInspectionTool().Execute(reference, measurement, 4.0, 6.0, 4);

            Require(result.Success && result.Decision == DualSurfaceThicknessDecision.Fail,
                "Out-of-limit dual-surface thickness must fail.");
            Require(result.BelowLowerLimitCount == 1 && result.AboveUpperLimitCount == 1,
                "Dual-surface limit counts changed.");
            RequireApproximately(result.Mean, 5.0, 0.0, "Unexpected failed thickness mean.");
            RequireApproximately(result.RootMeanSquareSpread, Math.Sqrt(2.0), 1e-12,
                "Unexpected failed thickness RMS spread.");
        }

        private static void TestDualSurfaceThicknessInspectionDegenerateReference()
        {
            HeightFieldPlaneFitSample[] reference =
            {
                new HeightFieldPlaneFitSample(new ThreeDPoint(0.0, 10.0, 0.0), 10.0),
                new HeightFieldPlaneFitSample(new ThreeDPoint(1.0, 10.0, 0.0), 10.0),
                new HeightFieldPlaneFitSample(new ThreeDPoint(2.0, 10.0, 0.0), 10.0)
            };
            DualSurfaceThicknessInspectionResult result =
                new DualSurfaceThicknessInspectionTool().Execute(
                    reference,
                    CreateThicknessPlaneSamples(15.0, 15.0, 15.0, 15.0),
                    4.0,
                    6.0,
                    1);

            Require(!result.Success && result.Decision == DualSurfaceThicknessDecision.Error,
                "Degenerate thickness reference must fail closed.");
            Require(result.Message.StartsWith("Reference surface fit failed:", StringComparison.Ordinal),
                "Degenerate thickness reference message changed.");
        }

        private static void TestHeightDeviationInspection()
        {
            HeightDeviationInspectionResult result =
                new HeightDeviationInspectionTool().Execute(8.0, 13.0, 10.0, 12, 3.0);

            Require(result.Success && result.Decision == HeightDeviationDecision.Pass,
                "Height deviation at tolerance must pass.");
            RequireApproximately(result.LowDeviation, 2.0, 0.0, "Unexpected low deviation.");
            RequireApproximately(result.HighDeviation, 3.0, 0.0, "Unexpected high deviation.");
            RequireApproximately(result.PeakDeviation, 3.0, 0.0, "Unexpected peak deviation.");
        }

        private static void TestHeightDeviationInspectionFailure()
        {
            HeightDeviationInspectionResult result =
                new HeightDeviationInspectionTool().Execute(8.0, 13.0, 10.0, 12, 2.5);

            Require(result.Success && result.Decision == HeightDeviationDecision.Fail,
                "Height deviation above tolerance must fail.");
            RequireApproximately(result.PeakDeviation, 3.0, 0.0, "Unexpected failed peak deviation.");
        }

        private static void TestHeightDeviationInspectionInvalidInput()
        {
            HeightDeviationInspectionResult result =
                new HeightDeviationInspectionTool().Execute(double.NaN, 13.0, 10.0, 12, 2.5);

            Require(!result.Success && result.Decision == HeightDeviationDecision.Error,
                "Invalid height summary must fail closed.");
            Require(double.IsNaN(result.PeakDeviation), "Invalid height summary must not expose a peak value.");
        }

        private static void TestDeclaredMeshNormalQualityValid()
        {
            ThreeDPoint[] points = CreateNormalQualitySquare();
            DeclaredMeshNormalQualityResult result =
                new DeclaredMeshNormalQualityTool().Execute(
                    points,
                    new[] { 0, 1, 2, 0, 2, 3 },
                    new[]
                    {
                        new ThreeDPoint(0.0, 0.0, 1.0),
                        new ThreeDPoint(0.0, 0.0, 1.0),
                        new ThreeDPoint(0.0, 0.0, 1.0),
                        new ThreeDPoint(0.0, 0.0, 1.0)
                    },
                    null,
                    1e-3,
                    0.5);

            Require(result.State == DeclaredMeshNormalQualityState.Valid,
                "Dense aligned normals must be valid.");
            Require(result.ComparableCornerCount == 6 && result.ConsistentCornerCount == 6,
                "Every referenced normal corner must be comparable and aligned.");
            RequireApproximately(result.MinimumAlignment, 1.0, 0.0,
                "Unexpected aligned-normal minimum cosine.");
        }

        private static void TestDeclaredMeshNormalQualityReversed()
        {
            DeclaredMeshNormalQualityResult result =
                new DeclaredMeshNormalQualityTool().Execute(
                    CreateNormalQualitySquare(),
                    new[] { 0, 1, 2, 0, 2, 3 },
                    new[]
                    {
                        new ThreeDPoint(0.0, 0.0, -1.0),
                        new ThreeDPoint(0.0, 0.0, -1.0),
                        new ThreeDPoint(0.0, 0.0, -1.0),
                        new ThreeDPoint(0.0, 0.0, -1.0)
                    },
                    null,
                    1e-3,
                    0.5);

            Require(result.State == DeclaredMeshNormalQualityState.Invalid,
                "Reversed normals must fail closed.");
            Require(result.ReversedCornerCount == 6 && result.ConsistentCornerCount == 0,
                "Reversed-normal evidence changed.");
        }

        private static void TestDeclaredMeshNormalQualityPartialAndInvalidTopology()
        {
            ThreeDPoint[] points = CreateNormalQualitySquare();
            DeclaredMeshNormalQualityTool tool = new DeclaredMeshNormalQualityTool();
            DeclaredMeshNormalQualityResult partial = tool.Execute(
                points,
                new[] { 0, 1, 2, 0, 2, 3 },
                new[]
                {
                    new ThreeDPoint(0.0, 0.0, 1.0),
                    new ThreeDPoint(0.0, 0.0, 1.0),
                    new ThreeDPoint(0.0, 0.0, 1.0)
                },
                null,
                1e-3,
                0.5);
            DeclaredMeshNormalQualityResult invalid = tool.Execute(
                points,
                new[] { 0, 4, 5 },
                new[]
                {
                    new ThreeDPoint(0.0, 0.0, 1.0),
                    new ThreeDPoint(0.0, 0.0, 1.0),
                    new ThreeDPoint(0.0, 0.0, 1.0),
                    new ThreeDPoint(0.0, 0.0, 1.0)
                },
                null,
                1e-3,
                0.5);

            Require(partial.State == DeclaredMeshNormalQualityState.Invalid
                && partial.NormalCount == 3,
                "Partial declared normals must fail closed.");
            Require(invalid.State == DeclaredMeshNormalQualityState.Invalid
                && invalid.InvalidIndexCount == 2
                && invalid.ComparableCornerCount == 0,
                "Invalid topology evidence changed.");
        }

        private static void TestLandmarkCorrespondenceValidation()
        {
            ThreeDPoint[] tetrahedron = CreateIndependentTetrahedron();
            LandmarkCorrespondenceValidationResult result =
                new LandmarkCorrespondenceValidationTool().Execute(
                    tetrahedron,
                    tetrahedron,
                    0.1);

            Require(result.Success && result.SourceRank == 4 && result.ReferenceRank == 4,
                "Independent landmark tetrahedra must pass.");
            RequireApproximately(
                result.SourceNormalizedTetrahedronVolume,
                1.0 / Math.Pow(Math.Sqrt(2.0), 3.0),
                1e-15,
                "Unexpected normalized landmark volume.");
        }

        private static void TestLandmarkCorrespondenceValidationCoplanar()
        {
            LandmarkCorrespondenceValidationResult result =
                new LandmarkCorrespondenceValidationTool().Execute(
                    new[]
                    {
                        new ThreeDPoint(0.0, 0.0, 0.0),
                        new ThreeDPoint(1.0, 0.0, 0.0),
                        new ThreeDPoint(0.0, 1.0, 0.0),
                        new ThreeDPoint(1.0, 1.0, 0.0)
                    },
                    CreateIndependentTetrahedron(),
                    0.1);

            Require(!result.Success && result.SourceRank == 3,
                "Coplanar source landmarks must fail closed.");
            Require(result.Message.StartsWith(
                "Source landmark tetrahedron is not affine-independent",
                StringComparison.Ordinal),
                "Coplanar source failure message changed.");
        }

        private static void TestLandmarkCorrespondenceValidationBoundary()
        {
            ThreeDPoint[] tetrahedron = CreateIndependentTetrahedron();
            LandmarkCorrespondenceValidationTool tool =
                new LandmarkCorrespondenceValidationTool();
            LandmarkCorrespondenceValidationResult baseline =
                tool.Execute(tetrahedron, tetrahedron, 0.1);
            LandmarkCorrespondenceValidationResult boundary =
                tool.Execute(
                    tetrahedron,
                    tetrahedron,
                    baseline.SourceNormalizedTetrahedronVolume);

            Require(!boundary.Success,
                "A normalized volume equal to the taught minimum must fail closed.");
        }

        private static void TestRepeatabilityStatistics()
        {
            RepeatabilityStatisticsResult result =
                new RepeatabilityStatisticsTool().Execute(new[] { 10.0, 12.0, 14.0, 16.0 });

            Require(result.Success && result.Count == 4,
                "A four-run repeatability series must be accepted.");
            RequireApproximately(result.Mean, 13.0, 0.0,
                "Unexpected repeatability mean.");
            RequireApproximately(result.Minimum, 10.0, 0.0,
                "Unexpected repeatability minimum.");
            RequireApproximately(result.Maximum, 16.0, 0.0,
                "Unexpected repeatability maximum.");
            RequireApproximately(result.SampleStandardDeviation, 2.581988897471611, 0.0,
                "Unexpected repeatability sample standard deviation.");
            RequireApproximately(result.SixSigmaSpread, 15.491933384829668, 0.0,
                "Unexpected repeatability six-sigma spread.");
            RequireApproximately(result.Range, 6.0, 0.0,
                "Unexpected repeatability range.");
        }

        private static void TestRepeatabilityStatisticsZeroSpread()
        {
            RepeatabilityStatisticsResult result =
                new RepeatabilityStatisticsTool().Execute(new[] { 4.25, 4.25, 4.25 });

            Require(result.Success && result.Count == 3,
                "A finite zero-spread series must be accepted.");
            RequireApproximately(result.Mean, 4.25, 0.0,
                "Unexpected zero-spread mean.");
            RequireApproximately(result.SampleStandardDeviation, 0.0, 0.0,
                "Unexpected zero-spread sample standard deviation.");
            RequireApproximately(result.SixSigmaSpread, 0.0, 0.0,
                "Unexpected zero-spread six-sigma value.");
            RequireApproximately(result.Range, 0.0, 0.0,
                "Unexpected zero-spread range.");
        }

        private static void TestRepeatabilityStatisticsInvalidInput()
        {
            RepeatabilityStatisticsTool tool = new RepeatabilityStatisticsTool();
            RepeatabilityStatisticsResult insufficient = tool.Execute(new[] { 1.0 });
            RepeatabilityStatisticsResult nonFinite = tool.Execute(new[] { 1.0, double.NaN });
            RepeatabilityStatisticsResult invalidPolicy = tool.Execute(
                new[] { 1.0, 2.0 },
                (RepeatabilityNegativeVariancePolicy)99);

            Require(!insufficient.Success && insufficient.Count == 1,
                "A single repeatability value must fail closed.");
            Require(!nonFinite.Success && nonFinite.Count == 2,
                "A non-finite repeatability value must fail closed.");
            Require(!invalidPolicy.Success && invalidPolicy.Count == 2,
                "An unsupported negative-variance policy must fail closed.");
            Require(double.IsNaN(nonFinite.Mean)
                && double.IsNaN(nonFinite.SampleStandardDeviation)
                && double.IsNaN(nonFinite.Range),
                "Invalid repeatability input must not expose partial statistics.");
        }

        private static void TestLabeledEvidenceStatistics()
        {
            LabeledEvidenceStatisticsResult result =
                new LabeledEvidenceStatisticsTool().Execute(
                    new[]
                    {
                        new LabeledEvidenceStatisticsObservation("good-1", LabeledEvidenceRole.Good, 2.0),
                        new LabeledEvidenceStatisticsObservation("good-1", LabeledEvidenceRole.Good, 4.0),
                        new LabeledEvidenceStatisticsObservation("bad-1", LabeledEvidenceRole.Bad, -10.0),
                        new LabeledEvidenceStatisticsObservation("bad-2", LabeledEvidenceRole.Bad, 20.0)
                    });

            LabeledEvidenceRoleStatistics good = result.RoleStatistics
                .Single(item => item.Role == LabeledEvidenceRole.Good);
            LabeledEvidenceRoleStatistics bad = result.RoleStatistics
                .Single(item => item.Role == LabeledEvidenceRole.Bad);
            LabeledEvidenceRoleStatistics heldOut = result.RoleStatistics
                .Single(item => item.Role == LabeledEvidenceRole.HeldOut);
            Require(result.Success && result.RoleStatistics.Count == 3,
                "Every supported evidence role must be reported.");
            Require(good.SampleCount == 1 && good.ValueCount == 2,
                "Opaque Good sample identity counting changed.");
            RequireApproximately(good.Mean.Value, 3.0, 0.0,
                "Unexpected Good mean.");
            RequireApproximately(good.PopulationStandardDeviation.Value, 1.0, 0.0,
                "Unexpected Good population standard deviation.");
            Require(bad.SampleCount == 2 && bad.ValueCount == 2,
                "Opaque Bad sample identity counting changed.");
            RequireApproximately(bad.Minimum.Value, -10.0, 0.0,
                "Unexpected Bad minimum.");
            RequireApproximately(bad.Maximum.Value, 20.0, 0.0,
                "Unexpected Bad maximum.");
            RequireApproximately(bad.PopulationStandardDeviation.Value, 15.0, 0.0,
                "Unexpected Bad population standard deviation.");
            Require(heldOut.SampleCount == 0 && heldOut.ValueCount == 0
                && !heldOut.Mean.HasValue
                && !heldOut.PopulationStandardDeviation.HasValue,
                "An empty Held-out role must remain explicit without fabricated statistics.");
        }

        private static void TestLabeledEvidenceStatisticsEmptyRoles()
        {
            LabeledEvidenceStatisticsResult result =
                new LabeledEvidenceStatisticsTool().Execute(
                    new[]
                    {
                        new LabeledEvidenceStatisticsObservation("held-1", LabeledEvidenceRole.HeldOut, 7.5)
                    });

            LabeledEvidenceRoleStatistics heldOut = result.RoleStatistics
                .Single(item => item.Role == LabeledEvidenceRole.HeldOut);
            Require(result.Success && heldOut.SampleCount == 1 && heldOut.ValueCount == 1,
                "A finite Held-out-only series must be accepted.");
            RequireApproximately(heldOut.Mean.Value, 7.5, 0.0,
                "Unexpected Held-out mean.");
            RequireApproximately(heldOut.PopulationStandardDeviation.Value, 0.0, 0.0,
                "A single Held-out observation must have zero population spread.");
        }

        private static void TestLabeledEvidenceStatisticsInvalidInput()
        {
            LabeledEvidenceStatisticsTool tool = new LabeledEvidenceStatisticsTool();
            LabeledEvidenceStatisticsResult nonFinite = tool.Execute(
                new[]
                {
                    new LabeledEvidenceStatisticsObservation("good-1", LabeledEvidenceRole.Good, double.NaN)
                });
            LabeledEvidenceStatisticsResult invalidRole = tool.Execute(
                new[]
                {
                    new LabeledEvidenceStatisticsObservation("good-1", (LabeledEvidenceRole)99, 1.0)
                });

            Require(!nonFinite.Success && nonFinite.RoleStatistics.Count == 0,
                "Non-finite labeled evidence must fail closed.");
            Require(!invalidRole.Success && invalidRole.RoleStatistics.Count == 0,
                "An unsupported labeled evidence role must fail closed.");
        }

        private static void TestThresholdCandidateAnalysis()
        {
            ThresholdCandidateAnalysisResult result =
                new ThresholdCandidateAnalysisTool().Execute(
                    new[]
                    {
                        new ThresholdCandidateObservation(0, ThresholdObservationClass.Accepted, 2.0),
                        new ThresholdCandidateObservation(1, ThresholdObservationClass.Accepted, 4.0),
                        new ThresholdCandidateObservation(2, ThresholdObservationClass.Rejected, -10.0),
                        new ThresholdCandidateObservation(3, ThresholdObservationClass.Rejected, 20.0)
                    });

            ThresholdCandidateAnalysisCandidate minimum = result.Candidates
                .Single(item => item.LimitKind == ThresholdCandidateLimitKind.Minimum);
            ThresholdCandidateAnalysisCandidate maximum = result.Candidates
                .Single(item => item.LimitKind == ThresholdCandidateLimitKind.Maximum);
            ThresholdCandidateAnalysisCandidate range = result.Candidates
                .Single(item => item.LimitKind == ThresholdCandidateLimitKind.Range);
            Require(result.Success && result.Candidates.Count == 3,
                "Exactly one candidate per supported threshold kind is required.");
            RequireApproximately(minimum.Minimum.Value, 2.0, 0.0,
                "Unexpected deterministic minimum candidate.");
            Require(minimum.ErrorCount == 1 && minimum.RejectedAcceptedCount == 1,
                "Minimum candidate decision counts changed.");
            RequireApproximately(maximum.Maximum.Value, 4.0, 0.0,
                "Unexpected deterministic maximum candidate.");
            Require(maximum.ErrorCount == 1 && maximum.RejectedAcceptedCount == 1,
                "Maximum candidate decision counts changed.");
            RequireApproximately(range.Minimum.Value, 2.0, 0.0,
                "Unexpected deterministic range minimum.");
            RequireApproximately(range.Maximum.Value, 4.0, 0.0,
                "Unexpected deterministic range maximum.");
            Require(range.ErrorCount == 0
                && range.AcceptedAcceptedCount == 2
                && range.RejectedRejectedCount == 2,
                "Range candidate classification changed.");
            Require(range.Decisions.Select(item => item.ObservationIndex)
                .SequenceEqual(new[] { 0, 1, 2, 3 }),
                "Threshold decision order must follow the supplied observation order.");
        }

        private static void TestThresholdCandidateAnalysisInvalidInput()
        {
            ThresholdCandidateAnalysisTool tool = new ThresholdCandidateAnalysisTool();
            ThresholdCandidateAnalysisResult oneClass = tool.Execute(
                new[]
                {
                    new ThresholdCandidateObservation(0, ThresholdObservationClass.Accepted, 1.0)
                });
            ThresholdCandidateAnalysisResult duplicateIndex = tool.Execute(
                new[]
                {
                    new ThresholdCandidateObservation(0, ThresholdObservationClass.Accepted, 1.0),
                    new ThresholdCandidateObservation(0, ThresholdObservationClass.Rejected, 2.0)
                });
            ThresholdCandidateAnalysisResult nonFinite = tool.Execute(
                new[]
                {
                    new ThresholdCandidateObservation(0, ThresholdObservationClass.Accepted, 1.0),
                    new ThresholdCandidateObservation(1, ThresholdObservationClass.Rejected, double.PositiveInfinity)
                });

            Require(!oneClass.Success && oneClass.Candidates.Count == 0,
                "Single-class threshold evidence must fail closed.");
            Require(!duplicateIndex.Success && duplicateIndex.Candidates.Count == 0,
                "Duplicate threshold observation indices must fail closed.");
            Require(!nonFinite.Success && nonFinite.Candidates.Count == 0,
                "Non-finite threshold evidence must fail closed.");
        }

        private static ThreeDPoint[] CreateNormalQualitySquare()
        {
            return new[]
            {
                new ThreeDPoint(0.0, 0.0, 0.0),
                new ThreeDPoint(1.0, 0.0, 0.0),
                new ThreeDPoint(1.0, 1.0, 0.0),
                new ThreeDPoint(0.0, 1.0, 0.0)
            };
        }

        private static ThreeDPoint[] CreateIndependentTetrahedron()
        {
            return new[]
            {
                new ThreeDPoint(0.0, 0.0, 0.0),
                new ThreeDPoint(1.0, 0.0, 0.0),
                new ThreeDPoint(0.0, 1.0, 0.0),
                new ThreeDPoint(0.0, 0.0, 1.0)
            };
        }

        private static HeightFieldPlaneFitSample[] CreateThicknessPlaneSamples(
            double first,
            double second,
            double third,
            double fourth)
        {
            return new[]
            {
                new HeightFieldPlaneFitSample(new ThreeDPoint(0.0, 10.0, 0.0), first),
                new HeightFieldPlaneFitSample(new ThreeDPoint(1.0, 10.0, 0.0), second),
                new HeightFieldPlaneFitSample(new ThreeDPoint(0.0, 10.0, 1.0), third),
                new HeightFieldPlaneFitSample(new ThreeDPoint(1.0, 10.0, 1.0), fourth)
            };
        }

    }
}

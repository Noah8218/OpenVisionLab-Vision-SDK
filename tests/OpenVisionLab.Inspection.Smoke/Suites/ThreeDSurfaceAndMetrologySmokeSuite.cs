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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;
using static OpenVisionLab.Inspection.Smoke.SmokeFixtures;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class ThreeDSurfaceAndMetrologySmokeSuite
    {
        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase("Height-difference edge retains strongest pair and exact-tie order", TestDeterministicHeightDifferenceEdge);
            yield return new SmokeCase("Height-difference edge skips missing pairs and requires support", TestDeterministicHeightDifferenceEdgeMissingAndSupport);
            yield return new SmokeCase("Height-difference edge exposes bounded top-two candidates per scanline", TestDeterministicHeightDifferenceEdgeCandidateEvidence);
            yield return new SmokeCase("Height-difference edge candidate evidence covers both axes and polarity modes", TestDeterministicHeightDifferenceEdgeCandidateEvidenceAxesAndPolarity);
            yield return new SmokeCase("Height-difference edge cancellation and candidate contract guards fail closed", TestDeterministicHeightDifferenceEdgeCandidateEvidenceCancellationAndGuards);
            yield return new SmokeCase("Height-difference edge keeps large finite magnitude means representable", TestHeightDifferenceEdgeLargeMagnitudeMean);
            yield return new SmokeCase("Height-difference edge rejects inconsistent candidate and scanline evidence", TestHeightDifferenceEdgeEvidenceConsistency);
            yield return new SmokeCase("Height-difference edge snapshots diagnostics and preserves legacy construction", TestHeightDifferenceEdgeDiagnosticSnapshot);
            yield return new SmokeCase("Deterministic line fit preserves full-XYZ inliers and direction", TestDeterministicLineFit);
            yield return new SmokeCase("Deterministic line fit rejects insufficient support", TestDeterministicLineFitSupportFailure);
            yield return new SmokeCase("Rigid surface pose search recovers known yaw and translation", TestDeterministicRigidSurfacePoseSearch);
            yield return new SmokeCase("Surface coverage preserves one-way unique occlusion evidence", TestDeterministicSurfaceCoverageOcclusion);
            yield return new SmokeCase("Rigid surface pose search fails closed on bounded domains", TestDeterministicRigidSurfacePoseSearchBounds);
            yield return new SmokeCase("Multiple surface match returns stable disjoint two-object results", TestDeterministicMultipleSurfaceMatch);
            yield return new SmokeCase("Multiple surface match fails closed on invalid contracts and expanded candidate budget", TestDeterministicMultipleSurfaceMatchBudget);
            yield return new SmokeCase("Surface matching challenge recovers one object among background points", TestSurfaceMatchingChallengeBackground);
            yield return new SmokeCase("Surface matching challenge preserves partial noisy coverage and rejection", TestSurfaceMatchingChallengePartialNoise);
            yield return new SmokeCase("Surface matching challenge preserves original disjoint evidence across instances", TestSurfaceMatchingChallengeDisjoint);
            yield return new SmokeCase("Surface matching challenge keeps symmetric results deterministic", TestSurfaceMatchingChallengeSymmetry);
            yield return new SmokeCase("Surface matching challenge recovers after failure budget rejection and cancellation", TestSurfaceMatchingChallengeRecovery);
            yield return new SmokeCase("Pose symmetry equivalence uses model-space post-multiplication", TestRigidPoseSymmetryEquivalencePostMultiply);
            yield return new SmokeCase("Pose symmetry equivalence preserves direct comparison for none", TestRigidPoseSymmetryEquivalenceNone);
            yield return new SmokeCase("Pose symmetry equivalence supports X and Y cyclic groups", TestRigidPoseSymmetryEquivalenceAxes);
            yield return new SmokeCase("Pose symmetry equivalence preserves thresholds and tie order", TestRigidPoseSymmetryEquivalenceThresholds);
            yield return new SmokeCase("Pose symmetry equivalence rejects invalid contracts", TestRigidPoseSymmetryEquivalenceInvalid);
            yield return new SmokeCase("Model surface selection preserves source order when disabled", TestModelSurfaceSelectionDisabled);
            yield return new SmokeCase("Model surface selection applies explicit exclusions", TestModelSurfaceSelectionExplicit);
            yield return new SmokeCase("Model surface selection removes exact geometric duplicates", TestModelSurfaceSelectionExactDuplicate);
            yield return new SmokeCase("Model surface selection canonicalizes authored order", TestModelSurfaceSelectionDeterministic);
            yield return new SmokeCase("Model surface selection rejects invalid contracts", TestModelSurfaceSelectionInvalid);
            yield return new SmokeCase("Triangle-mesh distance preserves closest feature and robust sign evidence", TestTriangleMeshDistance);
            yield return new SmokeCase("Nominal/actual mesh comparison preserves streaming statistics and sampling", TestNominalActualMeshComparison);
            yield return new SmokeCase("Rigid-transform diagnostics preserve plausibility measures", TestRigidTransformDiagnostics);
            yield return new SmokeCase("Rigid point-pair alignment recovers a known proper pose", TestRigidPointPairAlignment);
            yield return new SmokeCase("Rigid point-pair alignment rejects degenerate and mismatched triangles", TestRigidPointPairAlignmentInvalid);
            yield return new SmokeCase("Constrained best-fit rigid alignment recovers a noisy known pose", TestConstrainedBestFitRigidAlignment);
            yield return new SmokeCase("Constrained best-fit rigid alignment enforces bounded geometry and cancellation gates", TestConstrainedBestFitRigidAlignmentInvalid);
            yield return new SmokeCase("Surface-model preparation preserves even triangle samples", TestDeterministicSurfaceModelPreparation);
            yield return new SmokeCase("Model key-point extraction preserves deterministic spatial coverage", TestDeterministicModelKeyPointExtraction);
            yield return new SmokeCase("Model key-point extraction is independent of input order", TestDeterministicModelKeyPointExtractionOrder);
            yield return new SmokeCase("Model key-point extraction honors minimum separation", TestDeterministicModelKeyPointExtractionSeparation);
            yield return new SmokeCase("Model key-point extraction rejects invalid contracts", TestDeterministicModelKeyPointExtractionInvalid);
            yield return new SmokeCase("Acquisition direction classifies facing, away, and grazing normals", TestAcquisitionDirectionOrientation);
            yield return new SmokeCase("Acquisition direction preserves canonical order and grazing boundary", TestAcquisitionDirectionOrientationOrderAndBoundary);
            yield return new SmokeCase("Acquisition direction rejects invalid contracts", TestAcquisitionDirectionOrientationInvalid);
            yield return new SmokeCase("Prepared-scene preparation preserves even point samples", TestDeterministicPreparedScenePreparation);
            yield return new SmokeCase("Model surface-edge extraction preserves boundary topology", TestDeterministicModelSurfaceEdgeExtraction);
            yield return new SmokeCase("Organized scene surface-edge extraction anchors height steps", TestDeterministicOrganizedSceneSurfaceEdgeExtraction);
            yield return new SmokeCase("Surface-edge coverage reuses unique nearest matching", TestDeterministicSurfaceEdgeCoverage);
            yield return new SmokeCase("Surface-edge coverage accepts an empty scene as zero coverage", TestDeterministicSurfaceEdgeCoverageEmptyScene);
            yield return new SmokeCase("Numerical reliability preserves large-origin uneven planes and independent datum measurements", TestLargeOriginPlaneReliability);
            yield return new SmokeCase("Numerical reliability preserves double leveling and missing samples", TestDoubleLevelSurfaceReliability);
            yield return new SmokeCase("Numerical reliability preserves height coverage contract rejection and recovery", TestMissingHeightCoverageReliability);
            yield return new SmokeCase("Least-squares height-field plane fit preserves analytic coefficients", TestLeastSquaresHeightFieldPlaneFit);
            yield return new SmokeCase("Height-field plane fit preserves inclined translated distances and rejects overflow", TestPlaneFitNumericalBoundaries);
            yield return new SmokeCase("3D analytic qualification preserves plane coefficients across coordinate scales", TestAnalyticPlaneScale);
            yield return new SmokeCase("3D analytic qualification keeps geometric rejection scale independent", TestAnalyticPlaneRank);
            yield return new SmokeCase("3D analytic qualification distinguishes tilted flatness vertical volume and thickness", TestAnalyticDatumMeasurements);
            yield return new SmokeCase("3D analytic qualification recovers proper rigid poses across coordinate scales", TestAnalyticRigidScale);
            yield return new SmokeCase("3D analytic qualification reports outlier residuals and recovers after failure and cancellation", TestAnalyticRigidNoiseAndRecovery);
            yield return new SmokeCase("3D analytic qualification rejects gap flush arithmetic overflow and recovers", TestAnalyticGapOverflow);
            yield return new SmokeCase("Plane flatness measures independent reference and surface samples", TestPlaneFlatnessInspection);
            yield return new SmokeCase("Plane flatness rejects degenerate reference geometry", TestPlaneFlatnessDegenerateReference);
            yield return new SmokeCase("Point pair measures dimensions relative to the height axis", TestPointPairDimensions);
            yield return new SmokeCase("Point pair honors a rotated height axis", TestPointPairDimensionsRotatedAxis);
            yield return new SmokeCase("Point pair rejects coincident positions", TestPointPairDimensionsCoincident);
            yield return new SmokeCase("Point pair preserves scale and rejects unrepresentable measurements", TestPointPairNumericBoundaries);
            yield return new SmokeCase("Gap/flush measures signed separation and height difference", TestGapFlush);
            yield return new SmokeCase("Gap/flush preserves signed overlap", TestGapFlushOverlap);
            yield return new SmokeCase("Gap/flush rejects an empty region", TestGapFlushEmptyRegion);
            yield return new SmokeCase("Volume integrates signed height relative to a reference plane", TestVolume);
            yield return new SmokeCase("Volume preserves below-plane sign and tolerance failure", TestVolumeBelowPlane);
            yield return new SmokeCase("Volume rejects an empty measurement ROI", TestVolumeEmptyMeasurement);
            yield return new SmokeCase("Volume rejects finite inputs whose accumulation overflows", TestVolumeOverflow);
            yield return new SmokeCase("Volume distinguishes accumulation overflow and inclusive tolerance", TestVolumeNumericalBoundaries);
            yield return new SmokeCase("Cross-section measures axis width and scalar-height range", TestCrossSectionDimensions);
            yield return new SmokeCase("Cross-section reports independent width and height failures", TestCrossSectionDimensionsFailure);
            yield return new SmokeCase("Cross-section rejects non-finite samples", TestCrossSectionDimensionsInvalidSample);
            yield return new SmokeCase("Cross-section rejects unrepresentable spans and recovers", TestCrossSectionUnrepresentableDimensions);
        }

        private static void TestDeterministicHeightDifferenceEdge()
        {
            HeightDifferenceEdgeResult result = new DeterministicHeightDifferenceEdgeTool().Execute(
                3,
                4,
                new[]
                {
                    0.0, 5.0, 15.0, 25.0,
                    0.0, 7.0, 17.0, 27.0,
                    0.0, 9.0, 19.0, 29.0
                },
                new HeightDifferenceEdgeOptions
                {
                    Selection = new HeightDifferenceEdgeSelection(0, 0, 3, 4),
                    ComparisonAxis = HeightDifferenceEdgeComparisonAxis.AcrossColumns,
                    Polarity = HeightDifferenceEdgePolarity.Rising,
                    MinimumDelta = 10.0
                });

            Require(result.Success, "Height-difference edge must accept the analytic scanlines.");
            Require(result.Points.Count == 3 && result.Diagnostics.EligiblePairCount == 9 && result.Diagnostics.SkippedMissingPairCount == 0,
                "Height-difference edge must retain the expected scan diagnostics.");
            Require(result.Points.All(point => point.FirstColumn == 1 && point.SecondColumn == 2 && point.Magnitude == 10.0),
                "Exact strongest-pair ties must retain the first start index.");
        }

        private static void TestDeterministicHeightDifferenceEdgeMissingAndSupport()
        {
            HeightDifferenceEdgeResult missing = new DeterministicHeightDifferenceEdgeTool().Execute(
                3,
                3,
                new[]
                {
                    0.0, 10.0, 25.0,
                    0.0, double.NaN, 30.0,
                    0.0, 10.0, 25.0
                },
                new HeightDifferenceEdgeOptions
                {
                    Selection = new HeightDifferenceEdgeSelection(0, 0, 3, 3),
                    ComparisonAxis = HeightDifferenceEdgeComparisonAxis.AcrossColumns,
                    Polarity = HeightDifferenceEdgePolarity.Rising,
                    MinimumDelta = 10.0
                });
            HeightDifferenceEdgeResult insufficient = new DeterministicHeightDifferenceEdgeTool().Execute(
                2,
                2,
                new[] { 0.0, 10.0, 0.0, 1.0 },
                new HeightDifferenceEdgeOptions
                {
                    Selection = new HeightDifferenceEdgeSelection(0, 0, 2, 2),
                    ComparisonAxis = HeightDifferenceEdgeComparisonAxis.AcrossColumns,
                    Polarity = HeightDifferenceEdgePolarity.Rising,
                    MinimumDelta = 5.0
                });

            Require(missing.Success && missing.Points.Count == 2 && missing.Diagnostics.SkippedMissingPairCount == 2,
                "Missing edge cells must skip only their adjacent pairs without filling or bridging.");
            Require(missing.Diagnostics.Scanlines.Count == 3
                && missing.Diagnostics.Scanlines[1].SearchComplete
                && missing.Diagnostics.Scanlines[1].EligibleCandidateCount == 0
                && missing.Diagnostics.Scanlines[1].TopCandidate == null
                && missing.Diagnostics.Scanlines[1].SecondCandidate == null,
                "A complete scanline with only missing pairs must remain explicit no-candidate evidence.");
            Require(!insufficient.Success && insufficient.Message.Contains("at least two accepted", StringComparison.OrdinalIgnoreCase),
                "Height-difference edge must reject fewer than two accepted scanlines.");
        }

        private static void TestDeterministicHeightDifferenceEdgeCandidateEvidence()
        {
            double[] values =
            {
                0.0, 8.0, 0.0, 7.0, 0.0,
                0.0, 6.0, 12.0, 0.0, 6.0,
                0.0, 7.0, 0.0, 0.0, 8.0
            };
            HeightDifferenceEdgeResult result = new DeterministicHeightDifferenceEdgeTool().Execute(
                3,
                5,
                values,
                new HeightDifferenceEdgeOptions
                {
                    Selection = new HeightDifferenceEdgeSelection(0, 0, 3, 5),
                    ComparisonAxis = HeightDifferenceEdgeComparisonAxis.AcrossColumns,
                    Polarity = HeightDifferenceEdgePolarity.Rising,
                    MinimumDelta = 5.0
                });

            Require(result.Success && result.Points.Count == 3, "Top-two candidate extraction must preserve the accepted strongest points.");
            Require(result.Diagnostics.Scanlines.Count == 3, "Candidate evidence must cover every selected scanline.");
            HeightDifferenceEdgeResult repeat = new DeterministicHeightDifferenceEdgeTool().Execute(
                3,
                5,
                values,
                new HeightDifferenceEdgeOptions
                {
                    Selection = new HeightDifferenceEdgeSelection(0, 0, 3, 5),
                    ComparisonAxis = HeightDifferenceEdgeComparisonAxis.AcrossColumns,
                    Polarity = HeightDifferenceEdgePolarity.Rising,
                    MinimumDelta = 5.0
                });
            Require(repeat.Success && repeat.Diagnostics.Scanlines[1].TopCandidate.PairStartIndex == result.Diagnostics.Scanlines[1].TopCandidate.PairStartIndex
                && repeat.Diagnostics.Scanlines[1].SecondCandidate.PairStartIndex == result.Diagnostics.Scanlines[1].SecondCandidate.PairStartIndex
                && repeat.Diagnostics.Scanlines[1].ScoreDifference == result.Diagnostics.Scanlines[1].ScoreDifference,
                "Repeated direct execution must preserve candidate identity and score evidence.");

            HeightDifferenceEdgeScanlineDiagnostics first = result.Diagnostics.Scanlines[0];
            Require(first.SearchComplete && first.EligibleCandidateCount == 2
                && first.TopCandidate.PairStartIndex == 0
                && first.SecondCandidate.PairStartIndex == 2,
                "The first scanline must rank its two eligible pairs by magnitude and start index.");
            RequireApproximately(first.ScoreDifference, 1.0, 0.0, "The first scanline score difference changed.");
            Require(first.TopCandidate.FirstColumn == 0 && first.TopCandidate.SecondColumn == 1
                && first.SecondCandidate.FirstColumn == 2 && first.SecondCandidate.SecondColumn == 3,
                "Top-two candidates must retain both endpoint coordinates.");

            HeightDifferenceEdgeScanlineDiagnostics shared = result.Diagnostics.Scanlines[1];
            Require(shared.EligibleCandidateCount == 3
                && shared.TopCandidate.PairStartIndex == 0
                && shared.SecondCandidate.PairStartIndex == 1
                && shared.TopCandidate.SecondColumn == shared.SecondCandidate.FirstColumn,
                "Adjacent pairs sharing one sample must remain distinct candidates.");
            RequireApproximately(shared.ScoreDifference, 0.0, 0.0, "Exact-tied candidates must retain a zero score difference.");
            Require(result.Points[1].FirstColumn == 0 && result.Points[1].SecondColumn == 1,
                "The existing strongest-per-scanline winner and output order must remain unchanged.");

            HeightDifferenceEdgeScanlineDiagnostics last = result.Diagnostics.Scanlines[2];
            Require(last.TopCandidate.PairStartIndex == 3 && last.SecondCandidate.PairStartIndex == 0,
                "A later stronger pair must replace the winner while retaining the prior runner-up.");

            double[] offsetValues =
            {
                99.0, 99.0, 99.0, 99.0, 99.0, 99.0,
                99.0, 99.0, 0.0, 8.0, 0.0, 7.0,
                99.0, 99.0, 0.0, 6.0, 12.0, 0.0,
                99.0, 99.0, 99.0, 99.0, 99.0, 99.0
            };
            HeightDifferenceEdgeResult offset = new DeterministicHeightDifferenceEdgeTool().Execute(
                4,
                6,
                offsetValues,
                new HeightDifferenceEdgeOptions
                {
                    Selection = new HeightDifferenceEdgeSelection(1, 2, 2, 4),
                    ComparisonAxis = HeightDifferenceEdgeComparisonAxis.AcrossColumns,
                    Polarity = HeightDifferenceEdgePolarity.Rising,
                    MinimumDelta = 5.0
                });
            Require(offset.Success && offset.Diagnostics.Scanlines[0].ScanlineIndex == 1
                && offset.Diagnostics.Scanlines[1].ScanlineIndex == 2
                && offset.Diagnostics.Scanlines[0].TopCandidate.FirstColumn == 2
                && offset.Diagnostics.Scanlines[0].TopCandidate.SecondColumn == 3,
                "ROI-offset evidence must retain absolute scanline and endpoint identities inside the selected rectangle.");
        }

        private static void TestDeterministicHeightDifferenceEdgeCandidateEvidenceAxesAndPolarity()
        {
            double[] risingValues =
            {
                0.0, 10.0, 0.0, 0.0,
                7.0, 15.0, 0.0, 7.0,
                7.0, 20.0, 0.0, 7.0,
                7.0, 20.0, 0.0, 7.0
            };
            double[] fallingValues =
            {
                7.0, 20.0, 0.0, 7.0,
                0.0, 15.0, 0.0, 0.0,
                0.0, 10.0, 0.0, 0.0,
                0.0, 10.0, 0.0, 0.0
            };

            HeightDifferenceEdgeResult rising = ExecuteEdgeForPolarity(
                4,
                4,
                risingValues,
                HeightDifferenceEdgeComparisonAxis.AcrossRows,
                HeightDifferenceEdgePolarity.Rising);
            HeightDifferenceEdgeResult falling = ExecuteEdgeForPolarity(
                4,
                4,
                fallingValues,
                HeightDifferenceEdgeComparisonAxis.AcrossRows,
                HeightDifferenceEdgePolarity.Falling);
            HeightDifferenceEdgeResult absolute = ExecuteEdgeForPolarity(
                4,
                4,
                fallingValues,
                HeightDifferenceEdgeComparisonAxis.AcrossRows,
                HeightDifferenceEdgePolarity.Absolute);

            foreach (HeightDifferenceEdgeResult result in new[] { rising, falling, absolute })
            {
                Require(result.Success && result.Points.Count == 3 && result.Diagnostics.Scanlines.Count == 4,
                    "Both-axis candidate evidence must preserve accepted support and one record per selected scanline.");
                Require(result.Diagnostics.Scanlines[2].SearchComplete
                    && result.Diagnostics.Scanlines[2].EligibleCandidateCount == 0
                    && result.Diagnostics.Scanlines[2].TopCandidate == null,
                    "A no-candidate column must not be represented as unavailable evidence.");
            }

            Require(rising.Diagnostics.Scanlines[1].TopCandidate.SignedDelta > 0.0
                && rising.Diagnostics.Scanlines[1].SecondCandidate.SignedDelta > 0.0,
                "Rising polarity must retain only positive candidate deltas.");
            Require(rising.Diagnostics.Scanlines[0].SearchComplete
                && rising.Diagnostics.Scanlines[0].EligibleCandidateCount == 1
                && !rising.Diagnostics.Scanlines[0].HasNumericScoreDifference
                && double.IsNaN(rising.Diagnostics.Scanlines[0].ScoreDifference),
                "A complete single-candidate scanline must explicitly prove that no competitor was found without inventing a score difference.");
            Require(falling.Diagnostics.Scanlines[1].TopCandidate.SignedDelta < 0.0
                && falling.Diagnostics.Scanlines[1].SecondCandidate.SignedDelta < 0.0,
                "Falling polarity must retain only negative candidate deltas.");
            Require(absolute.Diagnostics.Scanlines[1].TopCandidate.SignedDelta < 0.0
                && absolute.Diagnostics.Scanlines[1].TopCandidate.Magnitude >= 5.0,
                "Absolute polarity must retain the signed candidate while ranking by magnitude.");
        }

        private static HeightDifferenceEdgeResult ExecuteEdgeForPolarity(
            int rowCount,
            int columnCount,
            IReadOnlyList<double> values,
            HeightDifferenceEdgeComparisonAxis axis,
            HeightDifferenceEdgePolarity polarity)
        {
            return new DeterministicHeightDifferenceEdgeTool().Execute(
                rowCount,
                columnCount,
                values,
                new HeightDifferenceEdgeOptions
                {
                    Selection = new HeightDifferenceEdgeSelection(0, 0, rowCount, columnCount),
                    ComparisonAxis = axis,
                    Polarity = polarity,
                    MinimumDelta = 5.0
                });
        }

        private static void TestDeterministicHeightDifferenceEdgeCandidateEvidenceCancellationAndGuards()
        {
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                double[] values =
                {
                    0.0, 10.0, 0.0,
                    0.0, 10.0, 0.0,
                    0.0, 10.0, 0.0
                };
                RequireThrows<OperationCanceledException>(() => new DeterministicHeightDifferenceEdgeTool().Execute(
                    3,
                    3,
                    values,
                    new HeightDifferenceEdgeOptions
                    {
                        Selection = new HeightDifferenceEdgeSelection(0, 0, 3, 3),
                        ComparisonAxis = HeightDifferenceEdgeComparisonAxis.AcrossColumns,
                        Polarity = HeightDifferenceEdgePolarity.Rising,
                        MinimumDelta = 5.0
                    },
                    cancellation.Token));
            }

            RequireThrows<ArgumentException>(() => _ = new HeightDifferenceEdgeCandidate(
                0,
                0,
                0,
                0,
                double.NaN,
                0,
                1,
                1.0,
                1.0,
                1.0));
            RequireThrows<ArgumentOutOfRangeException>(() => _ = new HeightDifferenceEdgeCandidate(
                0,
                0,
                -1,
                0,
                0.0,
                0,
                1,
                1.0,
                1.0,
                1.0));
            RequireThrows<ArgumentException>(() => _ = new HeightDifferenceEdgeCandidate(
                0,
                0,
                0,
                0,
                0.0,
                0,
                2,
                2.0,
                2.0,
                2.0));
            HeightDifferenceEdgeCandidate top = new HeightDifferenceEdgeCandidate(0, 0, 0, 0, 0.0, 0, 1, 5.0, 5.0, 5.0);
            HeightDifferenceEdgeCandidate second = new HeightDifferenceEdgeCandidate(0, 1, 0, 1, 5.0, 0, 2, 10.0, 5.0, 5.0);
            RequireThrows<ArgumentException>(() => _ = new HeightDifferenceEdgeScanlineDiagnostics(0, true, 1, top, second));
            RequireThrows<ArgumentException>(() => _ = new HeightDifferenceEdgeScanlineDiagnostics(0, true, 2, null, second));
            RequireThrows<ArgumentException>(() => _ = new HeightDifferenceEdgeScanlineDiagnostics(1, true, 1, top, null));
            HeightDifferenceEdgeScanlineDiagnostics duplicateScanline = new HeightDifferenceEdgeScanlineDiagnostics(0, true, 1, top, null);
            RequireThrows<ArgumentException>(() => _ = new HeightDifferenceEdgeDiagnostics(
                2,
                2,
                0,
                2,
                0,
                5.0,
                5.0,
                5.0,
                new[] { duplicateScanline, duplicateScanline }));

            HeightDifferenceEdgeDiagnostics legacy = new HeightDifferenceEdgeDiagnostics(
                2,
                2,
                0,
                2,
                0,
                5.0,
                5.0,
                5.0);
            Require(legacy.Scanlines.Count == 0,
                "Legacy diagnostics construction must remain explicitly unavailable rather than fabricating per-scanline evidence.");

            HeightDifferenceEdgeResult overflow = new DeterministicHeightDifferenceEdgeTool().Execute(
                2,
                2,
                new[] { double.MaxValue, -double.MaxValue, double.MaxValue, -double.MaxValue },
                new HeightDifferenceEdgeOptions
                {
                    Selection = new HeightDifferenceEdgeSelection(0, 0, 2, 2),
                    ComparisonAxis = HeightDifferenceEdgeComparisonAxis.AcrossColumns,
                    Polarity = HeightDifferenceEdgePolarity.Absolute,
                    MinimumDelta = 1.0
                });
            Require(!overflow.Success && overflow.Message.Contains("score must be finite", StringComparison.OrdinalIgnoreCase),
                "Non-finite adjacent score overflow must fail closed without publishing candidate evidence.");
        }

        private static void TestHeightDifferenceEdgeLargeMagnitudeMean()
        {
            double[] values = { 0.0, double.MaxValue, 0.0, double.MaxValue };
            HeightDifferenceEdgeResult result = ExecuteEdgeForPolarity(2, 2, values, HeightDifferenceEdgeComparisonAxis.AcrossColumns, HeightDifferenceEdgePolarity.Rising);
            Require(result.Success && result.Points.Count == 2, "Representable adjacent differences must remain accepted.");
            Require(result.Diagnostics.AcceptedMagnitudeMinimum == double.MaxValue
                && result.Diagnostics.AcceptedMagnitudeMaximum == double.MaxValue
                && result.Diagnostics.AcceptedMagnitudeMean == double.MaxValue,
                "The mean of equal finite magnitudes must equal that magnitude even when their sum overflows.");
        }

        private static void TestHeightDifferenceEdgeEvidenceConsistency()
        {
            RequireThrows<ArgumentException>(() => _ = new HeightDifferenceEdgeCandidate(0, 0, 0, 0, 1.0, 0, 1, 6.0, 4.0, 4.0));
            RequireThrows<ArgumentException>(() => _ = new HeightDifferenceEdgeCandidate(1, 0, 0, 0, 1.0, 0, 1, 6.0, 5.0, 5.0));
            HeightDifferenceEdgeCandidate first = new HeightDifferenceEdgeCandidate(0, 0, 0, 0, 1.0, 0, 1, 6.0, 5.0, 5.0);
            HeightDifferenceEdgeCandidate second = new HeightDifferenceEdgeCandidate(1, 0, 1, 0, 1.0, 1, 1, 6.0, 5.0, 5.0);
            RequireThrows<ArgumentException>(() => _ = new HeightDifferenceEdgeScanlineDiagnostics(0, true, 2, first, null));
            HeightDifferenceEdgeScanlineDiagnostics[] scanlines =
            {
                new HeightDifferenceEdgeScanlineDiagnostics(0, true, 1, first, null),
                new HeightDifferenceEdgeScanlineDiagnostics(1, true, 1, second, null)
            };
            RequireThrows<ArgumentException>(() => _ = new HeightDifferenceEdgeDiagnostics(2, 2, 0, 1, 1, 5.0, 5.0, 5.0, scanlines));
            RequireThrows<ArgumentException>(() => _ = new HeightDifferenceEdgeDiagnostics(2, 1, 0, 2, 0, 5.0, 5.0, 5.0, scanlines));
        }

        private static void TestHeightDifferenceEdgeDiagnosticSnapshot()
        {
            HeightDifferenceEdgeCandidate candidate = new HeightDifferenceEdgeCandidate(0, 0, 0, 0, 0.0, 0, 1, 5.0, 5.0, 5.0);
            HeightDifferenceEdgeScanlineDiagnostics complete = new HeightDifferenceEdgeScanlineDiagnostics(0, true, 1, candidate, null);
            HeightDifferenceEdgeScanlineDiagnostics replacement = new HeightDifferenceEdgeScanlineDiagnostics(0, true, 0, null, null);
            HeightDifferenceEdgeScanlineDiagnostics[] source = { complete };
            HeightDifferenceEdgeDiagnostics diagnostics = new HeightDifferenceEdgeDiagnostics(1, 1, 0, 1, 0, 5.0, 5.0, 5.0, source);
            source[0] = replacement;
            Require(ReferenceEquals(diagnostics.Scanlines[0], complete), "Changing the caller's array must not rewrite the retained snapshot.");
            IList<HeightDifferenceEdgeScanlineDiagnostics> list = diagnostics.Scanlines as IList<HeightDifferenceEdgeScanlineDiagnostics>;
            Require(list == null || list.IsReadOnly, "Published scanline evidence must be read-only.");
            if (list != null)
            {
                RequireThrows<NotSupportedException>(() => list[0] = replacement);
            }
            Require(ReferenceEquals(diagnostics.Scanlines[0], complete), "A consumer must not rewrite the published scanline evidence.");

            HeightDifferenceEdgeDiagnostics legacy = new HeightDifferenceEdgeDiagnostics(-1, -1, -1, 0, 0, double.NaN, double.NaN, double.NaN);
            Require(legacy.ScanlineCount == -1 && double.IsNaN(legacy.AcceptedMagnitudeMean) && legacy.Scanlines.Count == 0,
                "The existing eight-argument constructor must keep its unvalidated data-carrier behavior and omit new evidence.");
        }

        private static void TestDeterministicLineFit()
        {
            List<DeterministicLineFitPoint> points = new List<DeterministicLineFitPoint>();
            for (int index = 0; index < 8; index++)
            {
                points.Add(new DeterministicLineFitPoint(index, new ThreeDPoint(2.0 + (0.5 * index), -3.0 + (0.25 * index), index)));
            }
            points.Add(new DeterministicLineFitPoint(8, new ThreeDPoint(20.0, -30.0, 8.0)));
            points.Add(new DeterministicLineFitPoint(9, new ThreeDPoint(-10.0, 25.0, 9.0)));

            DeterministicLineFitOptions options = new DeterministicLineFitOptions
            {
                InputHash = new string('A', 64),
                MaximumOrthogonalResidual = 0.05,
                MinimumInlierCount = 6,
                MinimumInlierRatio = 0.6,
                MinimumInlierScanlineSpan = 5,
                PositiveScanlineAxis = DeterministicLineFitPositiveAxis.Z
            };
            DeterministicLineFitResult first = new DeterministicLineFitTool().Execute(points, options);
            DeterministicLineFitResult second = new DeterministicLineFitTool().Execute(points, options);
            double norm = Math.Sqrt((0.5 * 0.5) + (0.25 * 0.25) + 1.0);

            Require(first.Success && second.Success, "Deterministic line fit must accept the analytic full-XYZ inlier set.");
            Require(first.Diagnostics.InlierCount == 8 && first.Diagnostics.OutlierCount == 2, "Deterministic line fit must retain the expected inlier membership.");
            RequireApproximately(first.Geometry.Direction.X, 0.5 / norm, 1e-9, "Unexpected deterministic line direction X.");
            RequireApproximately(first.Geometry.Direction.Y, 0.25 / norm, 1e-9, "Unexpected deterministic line direction Y.");
            RequireApproximately(first.Geometry.Direction.Z, 1.0 / norm, 1e-9, "Unexpected deterministic line direction Z.");
            Require(first.PointDiagnostics.Count == second.PointDiagnostics.Count
                && first.PointDiagnostics.Where(point => point.IsInlier).Count() == second.PointDiagnostics.Where(point => point.IsInlier).Count(),
                "Repeated deterministic line fits must retain identical membership counts.");
        }

        private static void TestDeterministicLineFitSupportFailure()
        {
            DeterministicLineFitResult result = new DeterministicLineFitTool().Execute(
                new[]
                {
                    new DeterministicLineFitPoint(0, new ThreeDPoint(0.0, 0.0, 0.0)),
                    new DeterministicLineFitPoint(1, new ThreeDPoint(0.0, 0.0, 1.0)),
                    new DeterministicLineFitPoint(2, new ThreeDPoint(0.0, 0.0, 2.0)),
                    new DeterministicLineFitPoint(3, new ThreeDPoint(50.0, 50.0, 3.0)),
                    new DeterministicLineFitPoint(4, new ThreeDPoint(-50.0, -50.0, 4.0))
                },
                new DeterministicLineFitOptions
                {
                    InputHash = new string('B', 64),
                    MaximumOrthogonalResidual = 0.01,
                    MinimumInlierCount = 3,
                    MinimumInlierRatio = 0.8,
                    MinimumInlierScanlineSpan = 2,
                    PositiveScanlineAxis = DeterministicLineFitPositiveAxis.Z
                });

            Require(!result.Success && result.Message.Contains("support", StringComparison.OrdinalIgnoreCase), "Deterministic line fit must reject insufficient taught support.");
        }

        private static void TestDeterministicRigidSurfacePoseSearch()
        {
            IReadOnlyList<SurfaceMatchSample> model = CreateSurfaceMatchModel();
            RigidSurfacePose knownPose = CreateKnownSurfacePose();
            IReadOnlyList<SurfaceMatchSample> scene = model
                .Select(sample => new SurfaceMatchSample(
                    sample.Order,
                    knownPose.Transform(sample.Position)))
                .ToArray();
            DeterministicRigidSurfacePoseSearchTool tool =
                new DeterministicRigidSurfacePoseSearchTool();
            DeterministicRigidSurfacePoseSearchResult first =
                tool.Execute(model, scene, CreateSurfaceSearchOptions());
            DeterministicRigidSurfacePoseSearchResult second =
                tool.Execute(model, scene, CreateSurfaceSearchOptions());

            Require(first.Success && first.Matched && first.Pose != null,
                "Known surface pose must produce one matched rigid result.");
            Require(first.EvaluatedCandidateCount == 7
                && first.Coverage.MatchedModelSampleCount == 5
                && first.Coverage.Matches.Count == 5,
                "Known surface pose must preserve the bounded candidate count and full coverage.");
            RequireApproximately(first.Pose.M11, Math.Sqrt(3.0) / 2.0, 1e-12,
                "Unexpected known-pose rotation M11.");
            RequireApproximately(first.Pose.M12, -0.5, 1e-12,
                "Unexpected known-pose rotation M12.");
            RequireApproximately(first.Pose.M21, 0.5, 1e-12,
                "Unexpected known-pose rotation M21.");
            RequireApproximately(first.Pose.M22, Math.Sqrt(3.0) / 2.0, 1e-12,
                "Unexpected known-pose rotation M22.");
            RequireApproximately(first.Pose.TranslationX, 10.0, 1e-12,
                "Unexpected known-pose translation X.");
            RequireApproximately(first.Pose.TranslationY, -4.0, 1e-12,
                "Unexpected known-pose translation Y.");
            RequireApproximately(first.Pose.TranslationZ, 2.0, 1e-12,
                "Unexpected known-pose translation Z.");
            Require(second.Success
                && second.Matched
                && second.Pose != null
                && first.Pose.M11 == second.Pose.M11
                && first.Pose.TranslationX == second.Pose.TranslationX
                && first.Coverage.InlierRmse == second.Coverage.InlierRmse,
                "Repeated rigid surface pose search must be deterministic.");
        }

        private static void TestDeterministicSurfaceCoverageOcclusion()
        {
            IReadOnlyList<SurfaceMatchSample> model = CreateSurfaceMatchModel();
            RigidSurfacePose knownPose = CreateKnownSurfacePose();
            IReadOnlyList<SurfaceMatchSample> scene = model
                .Take(4)
                .Select(sample => new SurfaceMatchSample(
                    sample.Order,
                    knownPose.Transform(sample.Position)))
                .ToArray();
            DeterministicSurfaceCoverageResult result =
                new DeterministicSurfaceCoverageTool().Execute(
                    model,
                    scene,
                    knownPose,
                    1e-6);

            Require(result.Success
                && result.MatchedModelSampleCount == 4
                && result.UnmatchedModelSampleCount == 1
                && result.Matches.Count == 4,
                "One removed scene sample must retain four unique matches.");
            RequireApproximately(result.CoverageRatio, 0.8, 1e-15,
                "Occluded surface coverage must be four fifths.");
            Require(result.HasInlierRmse && result.InlierRmse <= 1e-12,
                "Exact retained scene samples must have near-zero RMSE.");
            Require(result.Matches.Select(match => match.SceneSampleOrder).Distinct().Count()
                == result.Matches.Count,
                "A scene sample must never be claimed more than once.");

            DeterministicSurfaceCoverageResult largeFiniteDistance =
                new DeterministicSurfaceCoverageTool().Execute(
                    new[]
                    {
                        new SurfaceMatchSample(
                            0,
                            new ThreeDPoint(0.0, 0.0, 0.0))
                    },
                    new[]
                    {
                        new SurfaceMatchSample(
                            0,
                            new ThreeDPoint(1e200, 0.0, 0.0))
                    },
                    new RigidSurfacePose(
                        1.0, 0.0, 0.0,
                        0.0, 1.0, 0.0,
                        0.0, 0.0, 1.0,
                        0.0, 0.0, 0.0),
                    1.1e200);

            Require(largeFiniteDistance.Success
                && largeFiniteDistance.MatchedModelSampleCount == 1
                && largeFiniteDistance.HasInlierRmse,
                "A representable finite distance inside a large finite limit must remain matched.");
            RequireApproximately(
                largeFiniteDistance.InlierRmse / 1e200,
                1.0,
                1e-15,
                "Large finite surface coverage must retain a finite RMSE.");
        }

        private static void TestDeterministicRigidSurfacePoseSearchBounds()
        {
            IReadOnlyList<SurfaceMatchSample> model = CreateSurfaceMatchModel();
            RigidSurfacePose knownPose = CreateKnownSurfacePose();
            IReadOnlyList<SurfaceMatchSample> scene = model
                .Select(sample => new SurfaceMatchSample(
                    sample.Order,
                    knownPose.Transform(sample.Position)))
                .ToArray();
            DeterministicRigidSurfacePoseSearchOptions bounded =
                CreateSurfaceSearchOptions();
            bounded.MinimumTranslationX = -1.0;
            bounded.MaximumTranslationX = 1.0;
            bounded.MinimumTranslationY = -1.0;
            bounded.MaximumTranslationY = 1.0;
            bounded.MinimumTranslationZ = -1.0;
            bounded.MaximumTranslationZ = 1.0;
            DeterministicRigidSurfacePoseSearchResult noMatch =
                new DeterministicRigidSurfacePoseSearchTool().Execute(
                    model,
                    scene,
                    bounded);

            DeterministicRigidSurfacePoseSearchOptions insufficientBudget =
                CreateSurfaceSearchOptions();
            insufficientBudget.MaximumCandidateCount = 6;
            DeterministicRigidSurfacePoseSearchResult rejected =
                new DeterministicRigidSurfacePoseSearchTool().Execute(
                    model,
                    scene,
                    insufficientBudget);

            Require(noMatch.Success
                && !noMatch.Matched
                && noMatch.Pose == null
                && noMatch.EvaluatedCandidateCount == 7
                && noMatch.RejectionReason.Contains(
                    "bounds",
                    StringComparison.OrdinalIgnoreCase),
                "Translation bounds must produce a controlled no-match result.");
            Require(!rejected.Success
                && rejected.Message.Contains(
                    "exceeds",
                    StringComparison.OrdinalIgnoreCase),
                "A declared candidate budget must fail closed before search.");
        }

        private static void TestDeterministicMultipleSurfaceMatch()
        {
            IReadOnlyList<SurfaceMatchSample> model = CreateSurfaceMatchModel();
            RigidSurfacePose firstPose = CreateKnownSurfacePose();
            RigidSurfacePose secondPose = new RigidSurfacePose(
                firstPose.M11,
                firstPose.M12,
                firstPose.M13,
                firstPose.M21,
                firstPose.M22,
                firstPose.M23,
                firstPose.M31,
                firstPose.M32,
                firstPose.M33,
                -12.0,
                7.0,
                1.0);
            IReadOnlyList<SurfaceMatchSample> scene = model
                .Select(sample => firstPose.Transform(sample.Position))
                .Concat(model.Select(sample => secondPose.Transform(sample.Position)))
                .Select((point, order) => new SurfaceMatchSample(order, point))
                .ToArray();
            DeterministicMultipleSurfaceMatchOptions options =
                CreateMultipleSurfaceSearchOptions();
            DeterministicMultipleSurfaceMatchTool tool =
                new DeterministicMultipleSurfaceMatchTool();
            DeterministicMultipleSurfaceMatchResult first =
                tool.Execute(model, scene, options);
            DeterministicMultipleSurfaceMatchResult repeated =
                tool.Execute(model, scene, options);

            Require(first.Success
                && first.Matches.Count == 2
                && first.Matches[0].Order == 0
                && first.Matches[1].Order == 1,
                "The two-object fixture must return two ordered results.");
            RequireApproximately(first.Matches[0].Pose.TranslationX, 10.0, 1e-12,
                "Unexpected first multiple-match translation X.");
            RequireApproximately(first.Matches[1].Pose.TranslationX, -12.0, 1e-12,
                "Unexpected second multiple-match translation X.");
            Require(first.Matches.All(match =>
                    match.Coverage.MatchedModelSampleCount == 5
                    && match.Coverage.HasInlierRmse
                    && match.Coverage.InlierRmse <= 1e-12),
                "Each multiple-match result must retain full exact coverage.");
            Require(first.Matches
                    .SelectMany(match => match.Coverage.Matches)
                    .Select(match => match.SceneSampleOrder)
                    .Distinct()
                    .Count() == 10,
                "Multiple-match results must not share scene samples.");
            Require(repeated.Success
                && repeated.Matches.Count == 2
                && first.EvaluatedCandidateCount == repeated.EvaluatedCandidateCount
                && first.Matches[0].Pose.TranslationX
                    == repeated.Matches[0].Pose.TranslationX
                && first.Matches[1].Pose.TranslationX
                    == repeated.Matches[1].Pose.TranslationX,
                "Repeated multiple-match search must preserve order and poses.");
        }

        private static void TestDeterministicMultipleSurfaceMatchBudget()
        {
            IReadOnlyList<SurfaceMatchSample> model = CreateSurfaceMatchModel();
            RigidSurfacePose knownPose = CreateKnownSurfacePose();
            IReadOnlyList<SurfaceMatchSample> scene = model
                .Select(sample => new SurfaceMatchSample(
                    sample.Order,
                    knownPose.Transform(sample.Position)))
                .ToArray();
            DeterministicMultipleSurfaceMatchOptions options =
                CreateMultipleSurfaceSearchOptions();
            options.MaximumExpandedCandidateCount = 1;
            DeterministicMultipleSurfaceMatchResult result =
                new DeterministicMultipleSurfaceMatchTool().Execute(
                    model,
                    scene,
                    options);
            DeterministicMultipleSurfaceMatchOptions invalidBounds =
                CreateMultipleSurfaceSearchOptions();
            invalidBounds.PoseSearchOptions.MinimumTranslationX = double.NaN;
            DeterministicMultipleSurfaceMatchResult invalidResult =
                new DeterministicMultipleSurfaceMatchTool().Execute(
                    model,
                    scene,
                    invalidBounds);

            Require(!result.Success
                && result.Message.Contains(
                    "expanded candidate count",
                    StringComparison.OrdinalIgnoreCase),
                "Multiple-match search must reject an insufficient expanded candidate budget before execution.");
            Require(!invalidResult.Success
                && invalidResult.Message.Contains(
                    "finite and ordered",
                    StringComparison.OrdinalIgnoreCase),
                "Multiple-match search must fail closed on invalid nested pose bounds.");
        }

        private static void TestSurfaceMatchingChallengeBackground()
        {
            SurfaceMatchSample[] model = CreateSurfaceChallengeModel();
            SurfaceMatchSample[] scene = CreateSurfaceChallengeScene(model, 6, 0.0, false);
            DeterministicMultipleSurfaceMatchOptions options = CreateSurfaceChallengeOptions(6);
            DeterministicRigidSurfacePoseSearchResult centroid = new DeterministicRigidSurfacePoseSearchTool().Execute(model, scene, options.PoseSearchOptions);
            Require(centroid.Success && !centroid.Matched && centroid.Pose == null,
                "The centroid-only path must retain its documented background limitation.");
            DeterministicMultipleSurfaceMatchResult result = new DeterministicMultipleSurfaceMatchTool().Execute(model, scene, options);
            Require(result.Success && result.Matches.Count == 1, "Anchor search must recover the authored object among background points.");
            RequireSurfaceChallengePose(result.Matches[0].Pose, 10.0, -4.0, 2.0);
            Require(result.EvaluatedCandidateCount == 3 * model.Length * scene.Length,
                "Challenge search must enumerate the declared rotation/model/scene anchor domain.");
            Require(result.Matches[0].Coverage.MatchedModelSampleCount == 6 && result.Matches[0].Coverage.InlierRmse < 1e-12,
                "All six independently authored points must match with near-zero residual.");
            Require(result.Matches[0].Coverage.Matches.All(match => match.SceneSampleOrder == 1 + 2 * match.ModelSampleOrder),
                "Background points must not replace the authored scene evidence.");
        }

        private static void TestSurfaceMatchingChallengePartialNoise()
        {
            SurfaceMatchSample[] model = CreateSurfaceChallengeModel();
            SurfaceMatchSample[] scene = CreateSurfaceChallengeScene(model, 4, 0.04, false);
            DeterministicMultipleSurfaceMatchOptions options = CreateSurfaceChallengeOptions(4);
            options.PoseSearchOptions.MaximumCorrespondenceDistance = 0.05;
            DeterministicMultipleSurfaceMatchTool tool = new DeterministicMultipleSurfaceMatchTool();
            DeterministicMultipleSurfaceMatchResult result = tool.Execute(model, scene, options);
            Require(result.Success && result.Matches.Count == 1, "Partial noisy evidence must satisfy the declared four-point support.");
            RequireSurfaceChallengePose(result.Matches[0].Pose, 10.0, -4.0, 2.0);
            Require(result.Matches[0].Coverage.MatchedModelSampleCount == 4 && result.Matches[0].Coverage.UnmatchedModelSampleCount == 2,
                "Two occluded model points must remain unmatched.");
            RequireApproximately(result.Matches[0].Coverage.CoverageRatio, 4.0 / 6.0, 1e-15, "Unexpected partial coverage.");
            RequireApproximately(result.Matches[0].Coverage.InlierRmse, 0.04 / Math.Sqrt(4.0), 1e-12,
                "The single authored perturbation must produce its independent RMS residual.");
            options.PoseSearchOptions.MaximumCorrespondenceDistance = 0.03;
            DeterministicMultipleSurfaceMatchResult rejected = tool.Execute(model, scene, options);
            Require(rejected.Success && rejected.Matches.Count == 0 && rejected.StopReason.Contains("below the required 4", StringComparison.Ordinal),
                "A rejected noisy point must not produce a pose below the declared support.");
            options.PoseSearchOptions.MaximumCorrespondenceDistance = 0.05;
            Require(SurfaceChallengeFingerprint(tool.Execute(model, scene, options)) == SurfaceChallengeFingerprint(result),
                "A support rejection must not leave stale claims on the next execution.");
        }

        private static void TestSurfaceMatchingChallengeDisjoint()
        {
            SurfaceMatchSample[] model = CreateSurfaceChallengeModel();
            SurfaceMatchSample[] scene = CreateSurfaceChallengeScene(model, 6, 0.0, true);
            DeterministicMultipleSurfaceMatchOptions options = CreateSurfaceChallengeOptions(6);
            options.MaximumMatchCount = 3;
            DeterministicMultipleSurfaceMatchTool tool = new DeterministicMultipleSurfaceMatchTool();
            DeterministicMultipleSurfaceMatchResult result = tool.Execute(model, scene, options);
            Require(result.Success && result.Matches.Count == 2 && result.StopReason.Length > 0,
                "Exactly two objects must be returned before the remaining background fails support.");
            int[] evidenceOrders = result.Matches.SelectMany(instance => instance.Coverage.Matches).Select(match => match.SceneSampleOrder).ToArray();
            Require(evidenceOrders.Length == 12 && evidenceOrders.Distinct().Count() == 12,
                "Different instances must never share a scene sample.");
            foreach (DeterministicSurfaceMatchInstance instance in result.Matches)
            {
                bool firstObject = Math.Abs(instance.Pose.TranslationX - 10.0) < 1e-12;
                RequireSurfaceChallengePose(instance.Pose, firstObject ? 10.0 : -12.0, firstObject ? -4.0 : 7.0, firstObject ? 2.0 : 1.0);
                int offset = firstObject ? 1 : 13;
                Require(instance.Coverage.SceneSampleCount == scene.Length && instance.Coverage.MatchedModelSampleCount == 6,
                    "Coverage must preserve the full source scene count for every instance.");
                Require(instance.Coverage.Matches.All(match => match.SceneSampleOrder == offset + 2 * match.ModelSampleOrder),
                    "Compacted available samples must map back to original scene orders.");
            }
            Require(SurfaceChallengeFingerprint(result) == SurfaceChallengeFingerprint(tool.Execute(model, scene, options)),
                "Repeated multi-instance execution must preserve all result fields.");
        }

        private static void TestSurfaceMatchingChallengeSymmetry()
        {
            SurfaceMatchSample[] model = new[] { new ThreeDPoint(-1, -1, 0), new ThreeDPoint(1, -1, 0), new ThreeDPoint(1, 1, 0), new ThreeDPoint(-1, 1, 0) }
                .Select((point, order) => new SurfaceMatchSample(order, point)).ToArray();
            SurfaceMatchSample[] scene = CreateSurfaceChallengeScene(model, 4, 0.0, false);
            DeterministicMultipleSurfaceMatchOptions options = CreateSurfaceChallengeOptions(4);
            DeterministicMultipleSurfaceMatchTool tool = new DeterministicMultipleSurfaceMatchTool();
            DeterministicMultipleSurfaceMatchResult result = tool.Execute(model, scene, options);
            Require(result.Success && result.Matches.Count == 1 && result.Matches[0].Coverage.MatchedModelSampleCount == 4,
                "Symmetry-equivalent grid poses must retain full support.");
            RequireApproximately(result.Matches[0].Pose.TranslationX, 10.0, 1e-12, "Unexpected symmetric-object X translation.");
            RequireApproximately(result.Matches[0].Pose.TranslationY, -4.0, 1e-12, "Unexpected symmetric-object Y translation.");
            RequireApproximately(result.Matches[0].Pose.TranslationZ, 2.0, 1e-12, "Unexpected symmetric-object Z translation.");
            Require(result.Matches[0].Coverage.InlierRmse < 1e-12 && SurfaceChallengeFingerprint(result) == SurfaceChallengeFingerprint(tool.Execute(model, scene, options)),
                "A symmetric shape must retain deterministic equivalent results without claiming a unique orientation.");
        }

        private static void TestSurfaceMatchingChallengeRecovery()
        {
            SurfaceMatchSample[] model = CreateSurfaceChallengeModel();
            SurfaceMatchSample[] scene = CreateSurfaceChallengeScene(model, 6, 0.0, false);
            DeterministicMultipleSurfaceMatchOptions options = CreateSurfaceChallengeOptions(6);
            DeterministicMultipleSurfaceMatchTool tool = new DeterministicMultipleSurfaceMatchTool();
            SurfaceMatchSample[] invalidOrder = (SurfaceMatchSample[])scene.Clone();
            invalidOrder[0] = new SurfaceMatchSample(1, scene[0].Position);
            SurfaceMatchSample[] invalidPosition = (SurfaceMatchSample[])scene.Clone();
            invalidPosition[0] = new SurfaceMatchSample(0, new ThreeDPoint(double.NaN, 0, 0));
            foreach (SurfaceMatchSample[] invalid in new[] { invalidOrder, invalidPosition })
            {
                DeterministicMultipleSurfaceMatchResult failed = tool.Execute(model, invalid, options);
                Require(!failed.Success && failed.Matches.Count == 0 && failed.EvaluatedCandidateCount == 0,
                    "Invalid scene contracts must fail before publishing candidate evidence.");
            }
            int exactBudget = 3 * model.Length * scene.Length;
            options.MaximumExpandedCandidateCount = exactBudget - 1;
            Require(!tool.Execute(model, scene, options).Success, "One candidate below the required budget must fail closed.");
            options.MaximumExpandedCandidateCount = exactBudget;
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            OperationCanceledException exception = RequireThrows<OperationCanceledException>(() => tool.Execute(model, scene, options, cancellation.Token));
            Require(exception.CancellationToken == cancellation.Token, "Cancellation must retain the original token.");
            DeterministicMultipleSurfaceMatchResult recovered = tool.Execute(model, scene, options);
            Require(recovered.Success && recovered.Matches.Count == 1 && recovered.EvaluatedCandidateCount == exactBudget,
                "The same tool must recover with the exact inclusive budget after failure and cancellation.");
            RequireSurfaceChallengePose(recovered.Matches[0].Pose, 10.0, -4.0, 2.0);
        }

        private static SurfaceMatchSample[] CreateSurfaceChallengeModel()
        {
            return new[] { new ThreeDPoint(0, 0, 0), new ThreeDPoint(2, 0, 0), new ThreeDPoint(0, 3, 0), new ThreeDPoint(0, 0, 5), new ThreeDPoint(2, 3, 5), new ThreeDPoint(7, 1, 2) }
                .Select((point, order) => new SurfaceMatchSample(order, point)).ToArray();
        }

        private static SurfaceMatchSample[] CreateSurfaceChallengeScene(SurfaceMatchSample[] model, int visibleCount, double noise, bool twoObjects)
        {
            List<ThreeDPoint> points = new List<ThreeDPoint> { new ThreeDPoint(100, 100, 100) };
            for (int instance = 0; instance < (twoObjects ? 2 : 1); instance++)
            {
                for (int index = 0; index < visibleCount; index++)
                {
                    ThreeDPoint point = model[index].Position;
                    // The fixture's truth is authored directly, independently of RigidSurfacePose.Transform.
                    points.Add(new ThreeDPoint(-point.Y + (instance == 0 ? 10.0 : -12.0) + (index == 3 ? noise : 0.0), point.X + (instance == 0 ? -4.0 : 7.0), point.Z + (instance == 0 ? 2.0 : 1.0)));
                    points.Add(new ThreeDPoint(-100 - index, 70 + instance, 90 + index));
                }
            }
            return points.Select((point, order) => new SurfaceMatchSample(order, point)).ToArray();
        }

        private static DeterministicMultipleSurfaceMatchOptions CreateSurfaceChallengeOptions(int minimumMatchedCount)
        {
            DeterministicMultipleSurfaceMatchOptions options = CreateMultipleSurfaceSearchOptions();
            options.PoseSearchOptions.MinimumRotationZDegrees = -90.0;
            options.PoseSearchOptions.MaximumRotationZDegrees = 90.0;
            options.PoseSearchOptions.RotationStepZDegrees = 90.0;
            options.PoseSearchOptions.MinimumMatchedSampleCount = minimumMatchedCount;
            options.MaximumMatchCount = 1;
            options.MaximumExpandedCandidateCount = 10000;
            return options;
        }

        private static void RequireSurfaceChallengePose(RigidSurfacePose pose, double x, double y, double z)
        {
            double[] actual = { pose.M11, pose.M12, pose.M13, pose.M21, pose.M22, pose.M23, pose.M31, pose.M32, pose.M33, pose.TranslationX, pose.TranslationY, pose.TranslationZ };
            double[] expected = { 0, -1, 0, 1, 0, 0, 0, 0, 1, x, y, z };
            for (int index = 0; index < expected.Length; index++)
                RequireApproximately(actual[index], expected[index], 1e-12, "Unexpected independent surface challenge pose component " + index + ".");
        }

        private static string SurfaceChallengeFingerprint(DeterministicMultipleSurfaceMatchResult result)
        {
            return System.Text.Json.JsonSerializer.Serialize(result);
        }

        private static void TestRigidPoseSymmetryEquivalencePostMultiply()
        {
            double cosine = Math.Sqrt(3.0) / 2.0;
            RigidSurfacePose reference = new RigidSurfacePose(
                1.0, 0.0, 0.0,
                0.0, cosine, -0.5,
                0.0, 0.5, cosine,
                10.0, -4.0, 2.0);
            RigidSurfacePose candidate = new RigidSurfacePose(
                0.0, -1.0, 0.0,
                cosine, 0.0, -0.5,
                0.5, 0.0, cosine,
                10.0, -4.0, 2.0);
            RigidPoseSymmetryEquivalenceResult result =
                new RigidPoseSymmetryEquivalenceTool().Execute(
                    reference,
                    candidate,
                    CreateSymmetryOptions(
                        RigidPoseSymmetryKind.DiscreteRotation,
                        RigidPoseSymmetryAxis.Z,
                        4,
                        1e-9,
                        1e-6));

            Require(result.Success
                && result.Equivalent
                && result.SymmetryOperationIndex == 1,
                "A candidate equal to reference * Rz(90) must be symmetry-equivalent.");
            RequireApproximately(
                result.SymmetryOperationAngleDegrees,
                90.0,
                1e-12,
                "Unexpected winning symmetry angle.");
            Require(result.TranslationDifference <= 1e-12
                && result.RotationDifferenceDegrees <= 1e-6,
                "Exact symmetry-equivalent poses must have near-zero residuals.");
        }

        private static void TestRigidPoseSymmetryEquivalenceNone()
        {
            RigidSurfacePose reference = CreateRotationPose(
                RigidPoseSymmetryAxis.Z,
                0.0,
                1.0,
                2.0,
                3.0);
            RigidSurfacePose rotated = CreateRotationPose(
                RigidPoseSymmetryAxis.Z,
                90.0,
                1.0,
                2.0,
                3.0);
            RigidPoseSymmetryEquivalenceOptions options =
                CreateSymmetryOptions(
                    RigidPoseSymmetryKind.None,
                    RigidPoseSymmetryAxis.None,
                    1,
                    1e-9,
                    1e-6);
            RigidPoseSymmetryEquivalenceTool tool =
                new RigidPoseSymmetryEquivalenceTool();
            RigidPoseSymmetryEquivalenceResult identical =
                tool.Execute(reference, reference, options);
            RigidPoseSymmetryEquivalenceResult different =
                tool.Execute(reference, rotated, options);

            Require(identical.Success
                && identical.Equivalent
                && identical.SymmetryOperationIndex == 0,
                "None symmetry must accept identical poses through operation zero.");
            Require(different.Success
                && !different.Equivalent
                && different.SymmetryOperationIndex == 0
                && different.RotationDifferenceDegrees > 89.999,
                "None symmetry must preserve direct rotation comparison.");
        }

        private static void TestRigidPoseSymmetryEquivalenceAxes()
        {
            RigidSurfacePose identity = CreateRotationPose(
                RigidPoseSymmetryAxis.Z,
                0.0,
                0.0,
                0.0,
                0.0);
            RigidPoseSymmetryEquivalenceTool tool =
                new RigidPoseSymmetryEquivalenceTool();
            RigidPoseSymmetryEquivalenceResult x = tool.Execute(
                identity,
                CreateRotationPose(
                    RigidPoseSymmetryAxis.X,
                    180.0,
                    0.0,
                    0.0,
                    0.0),
                CreateSymmetryOptions(
                    RigidPoseSymmetryKind.DiscreteRotation,
                    RigidPoseSymmetryAxis.X,
                    2,
                    1e-9,
                    1e-6));
            RigidPoseSymmetryEquivalenceResult y = tool.Execute(
                identity,
                CreateRotationPose(
                    RigidPoseSymmetryAxis.Y,
                    120.0,
                    0.0,
                    0.0,
                    0.0),
                CreateSymmetryOptions(
                    RigidPoseSymmetryKind.DiscreteRotation,
                    RigidPoseSymmetryAxis.Y,
                    3,
                    1e-9,
                    1e-6));

            Require(x.Success && x.Equivalent
                && x.SymmetryOperationIndex == 1,
                "Order-two X symmetry must recognize 180 degrees.");
            Require(y.Success && y.Equivalent
                && y.SymmetryOperationIndex == 1,
                "Order-three Y symmetry must recognize 120 degrees.");
        }

        private static void TestRigidPoseSymmetryEquivalenceThresholds()
        {
            RigidSurfacePose identity = CreateRotationPose(
                RigidPoseSymmetryAxis.Z,
                0.0,
                0.0,
                0.0,
                0.0);
            RigidSurfacePose near = CreateRotationPose(
                RigidPoseSymmetryAxis.Z,
                90.2,
                0.05,
                0.0,
                0.0);
            RigidPoseSymmetryEquivalenceTool tool =
                new RigidPoseSymmetryEquivalenceTool();
            RigidPoseSymmetryEquivalenceResult accepted = tool.Execute(
                identity,
                near,
                CreateSymmetryOptions(
                    RigidPoseSymmetryKind.DiscreteRotation,
                    RigidPoseSymmetryAxis.Z,
                    4,
                    0.05,
                    0.2));
            RigidPoseSymmetryEquivalenceResult rejected = tool.Execute(
                identity,
                near,
                CreateSymmetryOptions(
                    RigidPoseSymmetryKind.DiscreteRotation,
                    RigidPoseSymmetryAxis.Z,
                    4,
                    0.049,
                    0.19));
            RigidPoseSymmetryEquivalenceResult tie = tool.Execute(
                identity,
                CreateRotationPose(
                    RigidPoseSymmetryAxis.Z,
                    90.0,
                    0.0,
                    0.0,
                    0.0),
                CreateSymmetryOptions(
                    RigidPoseSymmetryKind.DiscreteRotation,
                    RigidPoseSymmetryAxis.Z,
                    2,
                    0.0,
                    90.0));

            Require(accepted.Success
                && accepted.Equivalent
                && accepted.SymmetryOperationIndex == 1,
                "Residuals on the authored inclusive limits must pass.");
            RequireApproximately(
                accepted.TranslationDifference,
                0.05,
                1e-12,
                "Unexpected translation residual.");
            RequireApproximately(
                accepted.RotationDifferenceDegrees,
                0.2,
                1e-9,
                "Unexpected rotation residual.");
            Require(rejected.Success && !rejected.Equivalent,
                "Residuals outside either authored limit must fail.");
            Require(tie.Success
                && tie.Equivalent
                && tie.SymmetryOperationIndex == 0,
                "An exact discrete tie must choose the lowest operation index.");
        }

        private static void TestRigidPoseSymmetryEquivalenceInvalid()
        {
            RigidSurfacePose identity = CreateRotationPose(
                RigidPoseSymmetryAxis.Z,
                0.0,
                0.0,
                0.0,
                0.0);
            RigidPoseSymmetryEquivalenceTool tool =
                new RigidPoseSymmetryEquivalenceTool();
            RigidPoseSymmetryEquivalenceResult invalidSymmetry = tool.Execute(
                identity,
                identity,
                CreateSymmetryOptions(
                    RigidPoseSymmetryKind.None,
                    RigidPoseSymmetryAxis.Z,
                    1,
                    0.0,
                    0.0));
            RigidSurfacePose nonRigid = new RigidSurfacePose(
                2.0, 0.0, 0.0,
                0.0, 1.0, 0.0,
                0.0, 0.0, 1.0,
                0.0, 0.0, 0.0);
            RigidPoseSymmetryEquivalenceResult invalidPose = tool.Execute(
                identity,
                nonRigid,
                CreateSymmetryOptions(
                    RigidPoseSymmetryKind.None,
                    RigidPoseSymmetryAxis.None,
                    1,
                    0.0,
                    0.0));

            Require(!invalidSymmetry.Success
                && invalidSymmetry.Message.Contains(
                    "None symmetry",
                    StringComparison.OrdinalIgnoreCase),
                "A malformed none declaration must fail closed.");
            Require(!invalidPose.Success
                && invalidPose.Message.Contains(
                    "rigid",
                    StringComparison.OrdinalIgnoreCase),
                "A non-rigid candidate pose must fail closed.");
        }

        private static void TestModelSurfaceSelectionDisabled()
        {
            ThreeDPoint[] points;
            SurfaceModelTriangleInput[] triangles;
            CreateModelSurfaceSelectionFixture(out points, out triangles);
            DeterministicModelSurfaceSelectionResult result =
                new DeterministicModelSurfaceSelectionTool().Execute(
                    points,
                    triangles,
                    new DeterministicModelSurfaceSelectionOptions());

            Require(result.Success
                && result.RetainedSourceTriangleIndices.SequenceEqual(
                    new[] { 0, 1, 2, 3 })
                && result.RemovedSurfaces.Count == 0,
                "Disabled cleanup must preserve every source triangle in source order.");
        }

        private static void TestModelSurfaceSelectionExplicit()
        {
            ThreeDPoint[] points;
            SurfaceModelTriangleInput[] triangles;
            CreateModelSurfaceSelectionFixture(out points, out triangles);
            DeterministicModelSurfaceSelectionResult result =
                new DeterministicModelSurfaceSelectionTool().Execute(
                    points,
                    triangles,
                    new DeterministicModelSurfaceSelectionOptions
                    {
                        ExplicitInternalSourceTriangleIndices =
                            new[] { 1 },
                        ExplicitUnobservableSourceTriangleIndices =
                            new[] { 2 }
                    });

            Require(result.Success
                && result.RetainedSourceTriangleIndices.SequenceEqual(
                    new[] { 0, 3 })
                && result.RemovedSurfaces.Count == 2
                && result.RemovedSurfaces[0].SourceTriangleIndex == 1
                && result.RemovedSurfaces[0].Reason
                    == ModelSurfaceRemovalReason.ExplicitInternal
                && result.RemovedSurfaces[1].SourceTriangleIndex == 2
                && result.RemovedSurfaces[1].Reason
                    == ModelSurfaceRemovalReason.ExplicitUnobservable,
                "Explicit internal and unobservable exclusions must retain typed evidence.");
        }

        private static void TestModelSurfaceSelectionExactDuplicate()
        {
            ThreeDPoint[] points;
            SurfaceModelTriangleInput[] triangles;
            CreateModelSurfaceSelectionFixture(out points, out triangles);
            DeterministicModelSurfaceSelectionResult result =
                new DeterministicModelSurfaceSelectionTool().Execute(
                    points,
                    triangles,
                    new DeterministicModelSurfaceSelectionOptions
                    {
                        RemoveExactDuplicateTriangles = true
                    });

            RemovedModelSurface duplicate = result.RemovedSurfaces.Single();
            Require(result.Success
                && result.RetainedSourceTriangleIndices.SequenceEqual(
                    new[] { 0, 1, 2 })
                && duplicate.SourceTriangleIndex == 3
                && duplicate.Reason
                    == ModelSurfaceRemovalReason.ExactDuplicate
                && duplicate.DuplicateOfSourceTriangleIndex == 0,
                "Exact-coordinate duplicates must retain the lowest source-triangle index.");
        }

        private static void TestModelSurfaceSelectionDeterministic()
        {
            ThreeDPoint[] points;
            SurfaceModelTriangleInput[] triangles;
            CreateModelSurfaceSelectionFixture(out points, out triangles);
            DeterministicModelSurfaceSelectionTool tool =
                new DeterministicModelSurfaceSelectionTool();
            DeterministicModelSurfaceSelectionResult first = tool.Execute(
                points,
                triangles,
                new DeterministicModelSurfaceSelectionOptions
                {
                    ExplicitInternalSourceTriangleIndices =
                        new[] { 2, 1 },
                    RemoveExactDuplicateTriangles = true
                });
            DeterministicModelSurfaceSelectionResult second = tool.Execute(
                points,
                triangles,
                new DeterministicModelSurfaceSelectionOptions
                {
                    ExplicitInternalSourceTriangleIndices =
                        new[] { 1, 2 },
                    RemoveExactDuplicateTriangles = true
                });

            Require(first.Success
                && second.Success
                && first.ExplicitInternalSourceTriangleIndices.SequenceEqual(
                    new[] { 1, 2 })
                && first.RetainedSourceTriangleIndices.SequenceEqual(
                    second.RetainedSourceTriangleIndices)
                && first.RemovedSurfaces.Select(item => item.SourceTriangleIndex)
                    .SequenceEqual(second.RemovedSurfaces.Select(
                        item => item.SourceTriangleIndex)),
                "Authored exclusion order must not change canonical selection evidence.");
        }

        private static void TestModelSurfaceSelectionInvalid()
        {
            ThreeDPoint[] points;
            SurfaceModelTriangleInput[] triangles;
            CreateModelSurfaceSelectionFixture(out points, out triangles);
            DeterministicModelSurfaceSelectionTool tool =
                new DeterministicModelSurfaceSelectionTool();
            DeterministicModelSurfaceSelectionResult overlap = tool.Execute(
                points,
                triangles,
                new DeterministicModelSurfaceSelectionOptions
                {
                    ExplicitInternalSourceTriangleIndices = new[] { 1 },
                    ExplicitUnobservableSourceTriangleIndices = new[] { 1 }
                });
            DeterministicModelSurfaceSelectionResult outside = tool.Execute(
                points,
                triangles,
                new DeterministicModelSurfaceSelectionOptions
                {
                    ExplicitInternalSourceTriangleIndices = new[] { 4 }
                });
            DeterministicModelSurfaceSelectionResult empty = tool.Execute(
                points,
                triangles,
                new DeterministicModelSurfaceSelectionOptions
                {
                    ExplicitInternalSourceTriangleIndices =
                        new[] { 0, 1, 2, 3 }
                });

            Require(!overlap.Success
                && overlap.Message.Contains(
                    "both explicitly",
                    StringComparison.OrdinalIgnoreCase),
                "Overlapping authored roles must fail closed.");
            Require(!outside.Success
                && outside.Message.Contains(
                    "must exist",
                    StringComparison.OrdinalIgnoreCase),
                "Out-of-range authored exclusions must fail closed.");
            Require(!empty.Success
                && empty.Message.Contains(
                    "retain at least one",
                    StringComparison.OrdinalIgnoreCase),
                "A selection that removes every surface must fail closed.");
        }

        private static void CreateModelSurfaceSelectionFixture(
            out ThreeDPoint[] points,
            out SurfaceModelTriangleInput[] triangles)
        {
            points = new[]
            {
                new ThreeDPoint(0.0, 0.0, 0.0),
                new ThreeDPoint(1.0, 0.0, 0.0),
                new ThreeDPoint(0.0, 1.0, 0.0),
                new ThreeDPoint(0.0, 0.0, -1.0),
                new ThreeDPoint(1.0, 0.0, -1.0),
                new ThreeDPoint(0.0, 1.0, -1.0),
                new ThreeDPoint(2.0, 0.0, 0.0),
                new ThreeDPoint(3.0, 0.0, 0.0),
                new ThreeDPoint(2.0, 1.0, 0.0),
                new ThreeDPoint(0.0, 0.0, 0.0),
                new ThreeDPoint(1.0, 0.0, 0.0),
                new ThreeDPoint(0.0, 1.0, 0.0)
            };
            triangles = new[]
            {
                new SurfaceModelTriangleInput(0, 1, 2),
                new SurfaceModelTriangleInput(3, 4, 5),
                new SurfaceModelTriangleInput(6, 7, 8),
                new SurfaceModelTriangleInput(9, 10, 11)
            };
        }

        private static void TestTriangleMeshDistance()
        {
            TriangleMeshDistanceTool tool = new TriangleMeshDistanceTool(
                new[]
                {
                    new MeshTriangle(
                        7,
                        new ThreeDPoint(0.0, 0.0, 0.0),
                        new ThreeDPoint(2.0, 0.0, 0.0),
                        new ThreeDPoint(0.0, 2.0, 0.0))
                });
            PointMeshDistance face = tool.Execute(
                new ThreeDPoint(0.5, 0.5, 1.0));
            PointMeshDistance boundary = tool.Execute(
                new ThreeDPoint(1.0, -1.0, 1.0));
            PointMeshDistance recovered = tool.ExecuteRobustSign(
                new ThreeDPoint(1.0, -1.0, 1.0),
                boundary.UnsignedDistance);

            Require(tool.TriangleCount == 1
                && face.SourceTriangleIndex == 7
                && face.ClosestFeature == MeshClosestFeature.FaceInterior
                && face.SignResolved
                && face.SignedDistance.HasValue,
                "Face-interior distance must retain direct signed evidence.");
            RequireApproximately(face.UnsignedDistance, 1.0, 1e-12,
                "Unexpected face-interior unsigned distance.");
            RequireApproximately(face.SignedDistance.Value, 1.0, 1e-12,
                "Unexpected face-interior signed distance.");
            Require(boundary.ClosestFeature == MeshClosestFeature.Edge
                && !boundary.SignResolved
                && !boundary.SignedDistance.HasValue,
                "Boundary distance must not guess a direct sign.");
            Require(recovered.SignResolved
                && recovered.SignedDistance.HasValue,
                "Robust boundary-sign execution must return explicit evidence.");
            RequireApproximately(
                recovered.SignedDistance.Value,
                Math.Sqrt(2.0),
                1e-12,
                "Unexpected robust boundary sign distance.");

            const double translatedOrigin = 1e8;
            TriangleMeshDistanceTool translatedTool =
                new TriangleMeshDistanceTool(
                    new[]
                    {
                        new MeshTriangle(
                            8,
                            new ThreeDPoint(
                                translatedOrigin,
                                translatedOrigin,
                                0.0),
                            new ThreeDPoint(
                                translatedOrigin + 1.0,
                                translatedOrigin,
                                0.0),
                            new ThreeDPoint(
                                translatedOrigin,
                                translatedOrigin + 1.0,
                                0.0))
                    });
            PointMeshDistance translatedFace = translatedTool.Execute(
                new ThreeDPoint(
                    translatedOrigin + 0.25,
                    translatedOrigin + 0.25,
                    1.0));

            Require(translatedFace.ClosestFeature
                    == MeshClosestFeature.FaceInterior
                && translatedFace.SignResolved,
                "A valid translated double-precision triangle must not collapse during indexing.");
            RequireApproximately(
                translatedFace.UnsignedDistance,
                1.0,
                1e-12,
                "Translated mesh distance must preserve double precision.");
        }

        private static void TestNominalActualMeshComparison()
        {
            NominalActualMeshComparisonResult result =
                new NominalActualMeshComparisonTool().Execute(
                    new[]
                    {
                        new MeshTriangle(
                            3,
                            new ThreeDPoint(0.0, 0.0, 0.0),
                            new ThreeDPoint(2.0, 0.0, 0.0),
                            new ThreeDPoint(0.0, 2.0, 0.0))
                    },
                    new[]
                    {
                        new ThreeDPoint(0.5, 0.5, 1.0),
                        new ThreeDPoint(0.5, 0.5, -2.0)
                    },
                    new NominalActualMeshComparisonOptions(
                        2,
                        -1.5,
                        1.5,
                        2));

            Require(result.Success
                && result.ProcessedPointCount == 2
                && result.BelowToleranceCount == 1
                && result.WithinToleranceCount == 1
                && result.AboveToleranceCount == 0
                && result.DirectSignResolvedCount == 2
                && result.RobustSignRecoveredCount == 0
                && result.DisplayStride == 1
                && result.DisplaySamples.Count == 2,
                "Nominal/actual comparison must retain deterministic counts and display sampling.");
            RequireApproximately(result.UnsignedStatistics.Mean, 1.5, 1e-12,
                "Unexpected unsigned-deviation mean.");
            RequireApproximately(result.SignedStatistics.Mean, -0.5, 1e-12,
                "Unexpected signed-deviation mean.");
            Require(result.DisplaySamples[0].SourceTriangleIndex == 3
                && result.DisplaySamples[0].PointIndex == 0,
                "Display evidence must retain source triangle and query order.");
        }

        private static void TestRigidTransformDiagnostics()
        {
            RigidTransformDiagnosticsTool tool =
                new RigidTransformDiagnosticsTool();
            RigidTransformDiagnosticsResult result = tool.Execute(
                new[]
                {
                    0.0, -1.0, 0.0, 3.0,
                    1.0, 0.0, 0.0, 4.0,
                    0.0, 0.0, 1.0, 0.0,
                    0.0, 0.0, 0.0, 1.0
                });
            RigidTransformDiagnosticsResult rejected = tool.Execute(
                new[]
                {
                    double.NaN, 0.0, 0.0, 0.0,
                    0.0, 1.0, 0.0, 0.0,
                    0.0, 0.0, 1.0, 0.0,
                    0.0, 0.0, 0.0, 1.0
                });

            Require(result.Success,
                "Finite rigid input must produce transform diagnostics.");
            RequireApproximately(result.HomogeneousRowMaximumError, 0.0, 0.0,
                "Unexpected homogeneous-row error.");
            RequireApproximately(result.RotationOrthogonalityMaximumError, 0.0, 0.0,
                "Unexpected rotation orthogonality error.");
            RequireApproximately(result.RotationDeterminant, 1.0, 0.0,
                "Unexpected rotation determinant.");
            RequireApproximately(result.RotationDeterminantUnitError, 0.0, 0.0,
                "Unexpected determinant-unit error.");
            RequireApproximately(result.TranslationMagnitude, 5.0, 1e-12,
                "Unexpected translation magnitude.");
            RequireApproximately(result.RotationAngleDegrees, 90.0, 1e-12,
                "Unexpected rotation angle.");
            Require(!rejected.Success
                && rejected.Message.Contains("16 finite", StringComparison.Ordinal),
                "Non-finite transform input must fail closed.");
        }

        private static void TestRigidPointPairAlignment()
        {
            var correspondences = new[]
            {
                new RigidPointPairCorrespondence(
                    new ThreeDPoint(0.0, 0.0, 0.0),
                    new ThreeDPoint(10.0, -4.0, 2.0)),
                new RigidPointPairCorrespondence(
                    new ThreeDPoint(1.0, 0.0, 0.0),
                    new ThreeDPoint(10.0, -3.0, 2.0)),
                new RigidPointPairCorrespondence(
                    new ThreeDPoint(0.0, 1.0, 0.0),
                    new ThreeDPoint(9.0, -4.0, 2.0))
            };
            var options = new RigidPointPairAlignmentOptions
            {
                MaximumPairLengthError = 1e-12,
                MinimumNormalizedCrossMagnitude = 1e-12
            };
            var first = new RigidPointPairAlignmentTool().Execute(correspondences, options);
            var second = new RigidPointPairAlignmentTool().Execute(correspondences, options);
            var pose = first.Pose;

            Require(first.Success && second.Success && pose != null, "Known rigid point-pair fixture must produce a pose.");
            RequireApproximately(pose.M11, 0.0, 1e-12, "Unexpected rigid point-pair M11.");
            RequireApproximately(pose.M12, -1.0, 1e-12, "Unexpected rigid point-pair M12.");
            RequireApproximately(pose.M21, 1.0, 1e-12, "Unexpected rigid point-pair M21.");
            RequireApproximately(pose.M22, 0.0, 1e-12, "Unexpected rigid point-pair M22.");
            RequireApproximately(pose.M33, 1.0, 1e-12, "Unexpected rigid point-pair M33.");
            RequireApproximately(pose.TranslationX, 10.0, 1e-12, "Unexpected rigid point-pair translation X.");
            RequireApproximately(pose.TranslationY, -4.0, 1e-12, "Unexpected rigid point-pair translation Y.");
            RequireApproximately(pose.TranslationZ, 2.0, 1e-12, "Unexpected rigid point-pair translation Z.");
            Require(first.Residuals.Count == 3 && first.MaximumResidual <= 1e-12,
                "Rigid point-pair fixture must preserve three residual records at machine precision.");
            Require(second.Success
                && second.Pose != null
                && second.Pose.M12 == pose.M12
                && second.Pose.TranslationX == pose.TranslationX
                && second.MaximumResidual == first.MaximumResidual,
                "Repeated rigid point-pair execution must be deterministic.");
        }

        private static void TestRigidPointPairAlignmentInvalid()
        {
            var collinear = new[]
            {
                new RigidPointPairCorrespondence(new ThreeDPoint(0.0, 0.0, 0.0), new ThreeDPoint(0.0, 0.0, 0.0)),
                new RigidPointPairCorrespondence(new ThreeDPoint(1.0, 0.0, 0.0), new ThreeDPoint(1.0, 0.0, 0.0)),
                new RigidPointPairCorrespondence(new ThreeDPoint(2.0, 0.0, 0.0), new ThreeDPoint(2.0, 0.0, 0.0))
            };
            var mismatch = new[]
            {
                new RigidPointPairCorrespondence(new ThreeDPoint(0.0, 0.0, 0.0), new ThreeDPoint(0.0, 0.0, 0.0)),
                new RigidPointPairCorrespondence(new ThreeDPoint(1.0, 0.0, 0.0), new ThreeDPoint(2.0, 0.0, 0.0)),
                new RigidPointPairCorrespondence(new ThreeDPoint(0.0, 1.0, 0.0), new ThreeDPoint(0.0, 1.0, 0.0))
            };
            var rejectedCollinear = new RigidPointPairAlignmentTool().Execute(
                collinear,
                new RigidPointPairAlignmentOptions { MaximumPairLengthError = 1e-12 });
            var rejectedMismatch = new RigidPointPairAlignmentTool().Execute(
                mismatch,
                new RigidPointPairAlignmentOptions { MaximumPairLengthError = 1e-12 });
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var canceled = false;
            try
            {
                _ = new RigidPointPairAlignmentTool().Execute(
                    mismatch,
                    new RigidPointPairAlignmentOptions { MaximumPairLengthError = 10.0 },
                    cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                canceled = true;
            }

            Require(!rejectedCollinear.Success && rejectedCollinear.Message.Contains("collinear", StringComparison.OrdinalIgnoreCase),
                "Collinear rigid point pairs must fail closed.");
            Require(!rejectedMismatch.Success && rejectedMismatch.Message.Contains("lengths differ", StringComparison.OrdinalIgnoreCase),
                "Distance-inconsistent rigid point pairs must fail closed.");
            Require(canceled, "Rigid point-pair alignment must honor cancellation before evaluation.");
        }

        private static void TestConstrainedBestFitRigidAlignment()
        {
            var exact = new[]
            {
                new ConstrainedBestFitRigidCorrespondence(new ThreeDPoint(0.0, 0.0, 0.0), new ThreeDPoint(10.0, -4.0, 2.0)),
                new ConstrainedBestFitRigidCorrespondence(new ThreeDPoint(1.0, 0.0, 0.0), new ThreeDPoint(10.0, -3.0, 2.0)),
                new ConstrainedBestFitRigidCorrespondence(new ThreeDPoint(0.0, 2.0, 0.0), new ThreeDPoint(8.0, -4.0, 2.0)),
                new ConstrainedBestFitRigidCorrespondence(new ThreeDPoint(0.0, 0.0, 3.0), new ThreeDPoint(10.0, -4.0, 5.0)),
                new ConstrainedBestFitRigidCorrespondence(new ThreeDPoint(2.0, 1.0, 1.0), new ThreeDPoint(9.0, -2.0, 3.0)),
                new ConstrainedBestFitRigidCorrespondence(new ThreeDPoint(-1.0, 1.0, 2.0), new ThreeDPoint(9.0, -5.0, 4.0))
            };
            var options = new ConstrainedBestFitRigidAlignmentOptions
            {
                MaximumCorrespondenceCount = 64,
                MinimumNormalizedLineSpread = 1e-12,
                ArithmeticResidualWarning = 1e-6
            };
            var first = new ConstrainedBestFitRigidAlignmentTool().Execute(exact, options);
            var second = new ConstrainedBestFitRigidAlignmentTool().Execute(exact, options);
            var pose = first.Pose;

            Require(first.Success && second.Success && pose != null, "Exact best-fit fixture must produce a pose.");
            Require(first.PairCount == 6 && first.UsedAllCorrespondences && first.Residuals.Count == 6,
                "Best-fit fixture must preserve every ordered correspondence and residual.");
            RequireApproximately(pose.M11, 0.0, 1e-12, "Unexpected best-fit M11.");
            RequireApproximately(pose.M12, -1.0, 1e-12, "Unexpected best-fit M12.");
            RequireApproximately(pose.M21, 1.0, 1e-12, "Unexpected best-fit M21.");
            RequireApproximately(pose.M22, 0.0, 1e-12, "Unexpected best-fit M22.");
            RequireApproximately(pose.M33, 1.0, 1e-12, "Unexpected best-fit M33.");
            RequireApproximately(pose.TranslationX, 10.0, 1e-12, "Unexpected best-fit translation X.");
            RequireApproximately(pose.TranslationY, -4.0, 1e-12, "Unexpected best-fit translation Y.");
            RequireApproximately(pose.TranslationZ, 2.0, 1e-12, "Unexpected best-fit translation Z.");
            Require(first.RmsResidual <= 1e-12 && first.MaximumResidual <= 1e-12,
                "Exact best-fit fixture must have machine-precision residuals.");
            Require(second.Pose.M12 == pose.M12
                && second.Pose.TranslationX == pose.TranslationX
                && second.RmsResidual == first.RmsResidual,
                "Repeated best-fit execution must be deterministic.");

            var noisy = exact.Select((pair, index) => index == 4
                ? new ConstrainedBestFitRigidCorrespondence(
                    pair.Source,
                    new ThreeDPoint(pair.Reference.X + 0.02, pair.Reference.Y - 0.01, pair.Reference.Z + 0.03))
                : pair).ToArray();
            var noisyResult = new ConstrainedBestFitRigidAlignmentTool().Execute(
                noisy,
                new ConstrainedBestFitRigidAlignmentOptions
                {
                    MaximumCorrespondenceCount = 64,
                    MinimumNormalizedLineSpread = 1e-12,
                    ArithmeticResidualWarning = 0.001
                });
            Require(noisyResult.Success
                && noisyResult.ArithmeticResidualWarningExceeded
                && noisyResult.RmsResidual > 0.0
                && noisyResult.MaximumResidual > noisyResult.RmsResidual,
                "Noisy best-fit fixture must preserve fit diagnostics and warning state.");
            RequireApproximately(noisyResult.Pose.M12, -1.0, 0.02, "Noisy best-fit rotation drifted beyond the bounded fixture tolerance.");
        }

        private static void TestConstrainedBestFitRigidAlignmentInvalid()
        {
            var collinear = new[]
            {
                new ConstrainedBestFitRigidCorrespondence(new ThreeDPoint(0.0, 0.0, 0.0), new ThreeDPoint(1.0, 1.0, 1.0)),
                new ConstrainedBestFitRigidCorrespondence(new ThreeDPoint(1.0, 0.0, 0.0), new ThreeDPoint(2.0, 1.0, 1.0)),
                new ConstrainedBestFitRigidCorrespondence(new ThreeDPoint(2.0, 0.0, 0.0), new ThreeDPoint(3.0, 1.0, 1.0)),
                new ConstrainedBestFitRigidCorrespondence(new ThreeDPoint(3.0, 0.0, 0.0), new ThreeDPoint(4.0, 1.0, 1.0))
            };
            var valid = new[]
            {
                new ConstrainedBestFitRigidCorrespondence(new ThreeDPoint(0.0, 0.0, 0.0), new ThreeDPoint(1.0, 1.0, 1.0)),
                new ConstrainedBestFitRigidCorrespondence(new ThreeDPoint(1.0, 0.0, 0.0), new ThreeDPoint(2.0, 1.0, 1.0)),
                new ConstrainedBestFitRigidCorrespondence(new ThreeDPoint(0.0, 1.0, 0.0), new ThreeDPoint(1.0, 2.0, 1.0)),
                new ConstrainedBestFitRigidCorrespondence(new ThreeDPoint(0.0, 0.0, 1.0), new ThreeDPoint(1.0, 1.0, 2.0)),
                new ConstrainedBestFitRigidCorrespondence(new ThreeDPoint(1.0, 1.0, 1.0), new ThreeDPoint(2.0, 2.0, 2.0))
            };
            var tool = new ConstrainedBestFitRigidAlignmentTool();
            var rejectedCollinear = tool.Execute(collinear, new ConstrainedBestFitRigidAlignmentOptions());
            var rejectedCount = tool.Execute(valid.Take(3).ToArray(), new ConstrainedBestFitRigidAlignmentOptions());
            var rejectedCap = tool.Execute(valid, new ConstrainedBestFitRigidAlignmentOptions { MaximumCorrespondenceCount = 4 });
            var duplicate = valid.Select((pair, index) => index == 1
                ? new ConstrainedBestFitRigidCorrespondence(pair.Source, valid[0].Reference)
                : pair).ToArray();
            var rejectedDuplicate = tool.Execute(duplicate, new ConstrainedBestFitRigidAlignmentOptions());
            var nonFinite = valid.Select((pair, index) => index == 2
                ? new ConstrainedBestFitRigidCorrespondence(new ThreeDPoint(double.NaN, 0.0, 0.0), pair.Reference)
                : pair).ToArray();
            var rejectedNonFinite = tool.Execute(nonFinite, new ConstrainedBestFitRigidAlignmentOptions());
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var canceled = false;
            try
            {
                _ = tool.Execute(valid, new ConstrainedBestFitRigidAlignmentOptions(), cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                canceled = true;
            }

            Require(!rejectedCollinear.Success
                && rejectedCollinear.Message.Contains("collinear", StringComparison.OrdinalIgnoreCase),
                "Collinear best-fit correspondences must fail closed.");
            Require(!rejectedCount.Success && rejectedCount.Message.Contains("four", StringComparison.OrdinalIgnoreCase),
                "Best-fit correspondence count below four must fail closed.");
            Require(!rejectedCap.Success && rejectedCap.Message.Contains("maximum", StringComparison.OrdinalIgnoreCase),
                "Best-fit correspondence count above the authored cap must fail closed.");
            Require(!rejectedDuplicate.Success && rejectedDuplicate.Message.Contains("unique", StringComparison.OrdinalIgnoreCase),
                "Duplicate best-fit coordinates must fail closed.");
            Require(!rejectedNonFinite.Success && rejectedNonFinite.Message.Contains("finite", StringComparison.OrdinalIgnoreCase),
                "Non-finite best-fit coordinates must fail closed.");
            Require(canceled, "Constrained best-fit rigid alignment must honor cancellation before evaluation.");
        }

        private static void TestDeterministicSurfaceModelPreparation()
        {
            ThreeDPoint[] points =
            {
                new ThreeDPoint(0.0, 0.0, 0.0),
                new ThreeDPoint(2.0, 0.0, 0.0),
                new ThreeDPoint(2.0, 2.0, 0.0),
                new ThreeDPoint(0.0, 2.0, 0.0)
            };
            SurfaceModelTriangleInput[] triangles =
            {
                new SurfaceModelTriangleInput(0, 1, 2),
                new SurfaceModelTriangleInput(0, 2, 3)
            };
            ThreeDPoint[] normals = points
                .Select(_ => new ThreeDPoint(0.0, 0.0, 1.0))
                .ToArray();
            DeterministicSurfaceModelPreparationResult result =
                new DeterministicSurfaceModelPreparationTool().Execute(
                    points,
                    triangles,
                    normals,
                    new DeterministicSurfaceModelPreparationOptions
                    {
                        MaximumSampleCount = 1
                    });

            Require(result.Success && result.Samples.Count == 1,
                "Surface-model preparation must return one controlled sample.");
            PreparedSurfaceModelSample sample = result.Samples[0];
            Require(sample.Order == 0 && sample.SourceTriangleIndex == 1,
                "Even triangle selection must preserve the established index schedule.");
            RequireApproximately(sample.Position.X, 2.0 / 3.0, 0.0,
                "Unexpected selected triangle centroid X.");
            RequireApproximately(sample.Position.Y, 4.0 / 3.0, 0.0,
                "Unexpected selected triangle centroid Y.");
            RequireApproximately(sample.Normal.Z, 1.0, 0.0,
                "Declared normal averaging must retain the source orientation.");
        }

        private static void TestDeterministicPreparedScenePreparation()
        {
            ThreeDPoint[] points = Enumerable.Range(0, 5)
                .Select(index => new ThreeDPoint(index, 0.0, index * 0.5))
                .ToArray();
            DeterministicPreparedScenePreparationResult result =
                new DeterministicPreparedScenePreparationTool().Execute(
                    points,
                    new DeterministicPreparedScenePreparationOptions
                    {
                        MaximumSampleCount = 2
                    });

            Require(result.Success && result.Samples.Count == 2,
                "Prepared-scene preparation must return the requested sample count.");
            Require(result.Samples[0].SourcePointIndex == 1
                && result.Samples[1].SourcePointIndex == 3,
                "Even point selection must preserve stable source locators.");
            Require(result.Samples[0].Position == points[1]
                && result.Samples[1].Position == points[3],
                "Prepared-scene samples must preserve the selected source objects.");
        }

        private static void TestDeterministicModelSurfaceEdgeExtraction()
        {
            ThreeDPoint[] points =
            {
                new ThreeDPoint(0.0, 0.0, 0.0),
                new ThreeDPoint(2.0, 0.0, 0.0),
                new ThreeDPoint(2.0, 2.0, 0.0),
                new ThreeDPoint(0.0, 2.0, 0.0)
            };
            SurfaceModelTriangleInput[] triangles =
            {
                new SurfaceModelTriangleInput(0, 1, 2),
                new SurfaceModelTriangleInput(0, 2, 3)
            };
            DeterministicModelSurfaceEdgeExtractionResult result =
                new DeterministicModelSurfaceEdgeExtractionTool().Execute(
                    points,
                    triangles,
                    new DeterministicModelSurfaceEdgeExtractionOptions
                    {
                        MinimumEdgeLength = 0.1,
                        MinimumCreaseAngleDegrees = 1.0,
                        IncludeBoundaryEdges = true
                    });

            Require(result.Success && result.Edges.Count == 4,
                "A flat triangulated square must expose four boundary edges only.");
            Require(result.Edges.All(edge =>
                    edge.Kind == ExtractedModelSurfaceEdgeKind.Boundary),
                "The flat internal diagonal must not be classified as a crease.");
            Require(result.Edges[0].FirstPointIndex == 0
                && result.Edges[0].SecondPointIndex == 1
                && result.Edges[1].FirstPointIndex == 0
                && result.Edges[1].SecondPointIndex == 3,
                "Model edge ordering must use sorted undirected point locators.");
        }

        private static void TestDeterministicOrganizedSceneSurfaceEdgeExtraction()
        {
            ThreeDPoint[] points =
            {
                new ThreeDPoint(0.0, 0.0, 0.0),
                new ThreeDPoint(1.0, 0.0, 2.0),
                new ThreeDPoint(2.0, 0.0, 0.0),
                new ThreeDPoint(0.0, 1.0, 0.0),
                new ThreeDPoint(1.0, 1.0, 2.0),
                new ThreeDPoint(2.0, 1.0, 0.0)
            };
            DeterministicOrganizedSceneSurfaceEdgeExtractionResult result =
                new DeterministicOrganizedSceneSurfaceEdgeExtractionTool()
                    .Execute(
                        points,
                        new DeterministicOrganizedSceneSurfaceEdgeExtractionOptions
                        {
                            Width = 3,
                            Height = 2,
                            MinimumAbsoluteHeightStep = 2.0,
                            IncludeColumnNeighbors = true,
                            IncludeRowNeighbors = false
                        });

            Require(result.Success && result.Edges.Count == 4,
                "Inclusive height-step extraction must retain four column edges.");
            Require(result.Edges[0].AnchorPointIndex == 1
                && result.Edges[1].AnchorPointIndex == 1
                && result.Edges[2].AnchorPointIndex == 4
                && result.Edges[3].AnchorPointIndex == 4,
                "Every organized height step must anchor at its higher endpoint.");
            Require(result.Edges.All(edge =>
                    edge.Axis == ExtractedSceneSurfaceEdgeAxis.AcrossColumns
                    && edge.AbsoluteHeightStep == 2.0),
                "Scene edge axis and threshold evidence were not preserved.");
        }

        private static void TestDeterministicSurfaceEdgeCoverage()
        {
            SurfaceEdgeAnchorSample[] model =
            {
                new SurfaceEdgeAnchorSample(
                    0, new ThreeDPoint(0.0, 0.0, 0.0)),
                new SurfaceEdgeAnchorSample(
                    1, new ThreeDPoint(2.0, 0.0, 0.0))
            };
            SurfaceEdgeAnchorSample[] scene =
            {
                new SurfaceEdgeAnchorSample(
                    0, new ThreeDPoint(0.1, 0.0, 0.0)),
                new SurfaceEdgeAnchorSample(
                    1, new ThreeDPoint(2.1, 0.0, 0.0))
            };
            RigidSurfacePose identity = new RigidSurfacePose(
                1.0, 0.0, 0.0,
                0.0, 1.0, 0.0,
                0.0, 0.0, 1.0,
                0.0, 0.0, 0.0);
            DeterministicSurfaceEdgeCoverageResult result =
                new DeterministicSurfaceEdgeCoverageTool().Execute(
                    model,
                    scene,
                    identity,
                    0.2);

            Require(result.Success
                && result.MatchedModelEdgeCount == 2
                && result.UnmatchedModelEdgeCount == 0
                && result.Matches.Count == 2,
                "Surface-edge coverage must retain two unique nearest matches.");
            RequireApproximately(result.CoverageRatio, 1.0, 0.0,
                "Surface-edge coverage ratio must remain decision-free and exact.");
            RequireApproximately(result.InlierRmse, 0.1, 1e-12,
                "Unexpected surface-edge coverage RMSE.");
        }

        private static void TestDeterministicSurfaceEdgeCoverageEmptyScene()
        {
            SurfaceEdgeAnchorSample[] model =
            {
                new SurfaceEdgeAnchorSample(
                    0, new ThreeDPoint(0.0, 0.0, 0.0))
            };
            RigidSurfacePose identity = new RigidSurfacePose(
                1.0, 0.0, 0.0,
                0.0, 1.0, 0.0,
                0.0, 0.0, 1.0,
                0.0, 0.0, 0.0);
            DeterministicSurfaceEdgeCoverageResult result =
                new DeterministicSurfaceEdgeCoverageTool().Execute(
                    model,
                    Array.Empty<SurfaceEdgeAnchorSample>(),
                    identity,
                    0.2);

            Require(result.Success
                && result.ModelEdgeCount == 1
                && result.SceneEdgeCount == 0
                && result.MatchedModelEdgeCount == 0
                && result.UnmatchedModelEdgeCount == 1
                && result.CoverageRatio == 0.0
                && !result.HasInlierRmse
                && result.Matches.Count == 0,
                "An empty scene-edge set must remain valid zero-coverage evidence.");
        }

        private static IReadOnlyList<SurfaceMatchSample> CreateSurfaceMatchModel()
        {
            return new[]
            {
                new SurfaceMatchSample(0, new ThreeDPoint(0.0, 0.0, 0.0)),
                new SurfaceMatchSample(1, new ThreeDPoint(2.0, 0.0, 0.0)),
                new SurfaceMatchSample(2, new ThreeDPoint(0.0, 3.0, 0.0)),
                new SurfaceMatchSample(3, new ThreeDPoint(4.0, 1.0, 0.0)),
                new SurfaceMatchSample(4, new ThreeDPoint(1.0, 5.0, 0.0))
            };
        }

        private static RigidSurfacePose CreateKnownSurfacePose()
        {
            double cosine = Math.Sqrt(3.0) / 2.0;
            return new RigidSurfacePose(
                cosine,
                -0.5,
                0.0,
                0.5,
                cosine,
                0.0,
                0.0,
                0.0,
                1.0,
                10.0,
                -4.0,
                2.0);
        }

        private static void TestDeterministicModelKeyPointExtraction()
        {
            ModelKeyPointInput[] samples = CreateModelKeyPointFixture();
            DeterministicModelKeyPointExtractionResult result =
                new DeterministicModelKeyPointExtractionTool().Execute(
                    samples,
                    new DeterministicModelKeyPointExtractionOptions
                    {
                        MaximumKeyPointCount = 3,
                        MinimumSeparation = 0.0
                    });

            Require(result.Success, result.Message);
            Require(
                result.KeyPoints.Select(point => point.SourceSampleOrder)
                    .SequenceEqual(new[] { 0, 3, 2 }),
                "Model key-point farthest-point order changed.");
            RequireApproximately(
                result.KeyPoints[1].NearestSelectedDistance,
                5.0,
                0.0,
                "Unexpected second key-point separation.");
            RequireApproximately(
                result.KeyPoints[2].NearestSelectedDistance,
                2.0,
                0.0,
                "Unexpected third key-point separation.");
        }

        private static void TestDeterministicModelKeyPointExtractionOrder()
        {
            ModelKeyPointInput[] samples = CreateModelKeyPointFixture();
            DeterministicModelKeyPointExtractionTool tool =
                new DeterministicModelKeyPointExtractionTool();
            DeterministicModelKeyPointExtractionOptions options =
                new DeterministicModelKeyPointExtractionOptions
                {
                    MaximumKeyPointCount = 4,
                    MinimumSeparation = 0.0
                };
            DeterministicModelKeyPointExtractionResult first =
                tool.Execute(samples, options);
            DeterministicModelKeyPointExtractionResult second =
                tool.Execute(samples.Reverse().ToArray(), options);

            Require(first.Success && second.Success,
                first.Message + second.Message);
            Require(
                first.KeyPoints.Select(point => point.SourceSampleOrder)
                    .SequenceEqual(second.KeyPoints.Select(
                        point => point.SourceSampleOrder)),
                "Input order changed model key-point identities.");
        }

        private static void TestDeterministicModelKeyPointExtractionSeparation()
        {
            DeterministicModelKeyPointExtractionResult result =
                new DeterministicModelKeyPointExtractionTool().Execute(
                    CreateModelKeyPointFixture(),
                    new DeterministicModelKeyPointExtractionOptions
                    {
                        MaximumKeyPointCount = 4,
                        MinimumSeparation = 2.0
                    });

            Require(result.Success, result.Message);
            Require(
                result.KeyPoints.Select(point => point.SourceSampleOrder)
                    .SequenceEqual(new[] { 0, 3 }),
                "Minimum separation must exclude points on the boundary.");
        }

        private static void TestDeterministicModelKeyPointExtractionInvalid()
        {
            DeterministicModelKeyPointExtractionTool tool =
                new DeterministicModelKeyPointExtractionTool();
            ModelKeyPointInput[] duplicateOrders =
            {
                new ModelKeyPointInput(
                    0,
                    new ThreeDPoint(0.0, 0.0, 0.0),
                    new ThreeDPoint(0.0, 0.0, 1.0)),
                new ModelKeyPointInput(
                    0,
                    new ThreeDPoint(1.0, 0.0, 0.0),
                    new ThreeDPoint(0.0, 0.0, 1.0))
            };
            DeterministicModelKeyPointExtractionResult duplicate =
                tool.Execute(
                    duplicateOrders,
                    new DeterministicModelKeyPointExtractionOptions
                    {
                        MaximumKeyPointCount = 1
                    });
            DeterministicModelKeyPointExtractionResult invalidNormal =
                tool.Execute(
                    new[]
                    {
                        new ModelKeyPointInput(
                            0,
                            new ThreeDPoint(0.0, 0.0, 0.0),
                            new ThreeDPoint(0.0, 0.0, 2.0))
                    },
                    new DeterministicModelKeyPointExtractionOptions
                    {
                        MaximumKeyPointCount = 1
                    });

            Require(!duplicate.Success,
                "Duplicate source sample orders must fail closed.");
            Require(!invalidNormal.Success,
                "Non-unit source sample normals must fail closed.");
        }

        private static ModelKeyPointInput[] CreateModelKeyPointFixture()
        {
            ThreeDPoint normal = new ThreeDPoint(0.0, 0.0, 1.0);
            return new[]
            {
                new ModelKeyPointInput(
                    2,
                    new ThreeDPoint(0.0, 2.0, 0.0),
                    normal),
                new ModelKeyPointInput(
                    0,
                    new ThreeDPoint(0.0, 0.0, 0.0),
                    normal),
                new ModelKeyPointInput(
                    3,
                    new ThreeDPoint(5.0, 0.0, 0.0),
                    normal),
                new ModelKeyPointInput(
                    1,
                    new ThreeDPoint(1.0, 0.0, 0.0),
                    normal)
            };
        }

        private static void TestAcquisitionDirectionOrientation()
        {
            AcquisitionDirectionOrientationResult result =
                new AcquisitionDirectionOrientationTool().Execute(
                    new ThreeDPoint(0.0, 0.0, -2.0),
                    new[]
                    {
                        new AcquisitionDirectionNormalInput(
                            0,
                            new ThreeDPoint(0.0, 0.0, 3.0)),
                        new AcquisitionDirectionNormalInput(
                            1,
                            new ThreeDPoint(0.0, 0.0, -4.0)),
                        new AcquisitionDirectionNormalInput(
                            2,
                            new ThreeDPoint(5.0, 0.0, 0.0))
                    },
                    new AcquisitionDirectionOrientationOptions
                    {
                        GrazingAbsoluteCosineMaximum = 0.05
                    });

            Require(result.Success, result.Message);
            RequireApproximately(
                result.NormalizedSensorToSceneDirection.Z,
                -1.0,
                0.0,
                "Sensor-to-scene direction was not normalized.");
            Require(
                result.Items.Select(item => item.Orientation).SequenceEqual(
                    new[]
                    {
                        AcquisitionDirectionOrientation.SensorFacing,
                        AcquisitionDirectionOrientation.AwayFromSensor,
                        AcquisitionDirectionOrientation.Grazing
                    }),
                "Acquisition direction orientation changed.");
            RequireApproximately(result.Items[0].AlignmentCosine, -1.0, 0.0,
                "Unexpected sensor-facing alignment.");
            RequireApproximately(result.Items[1].AlignmentCosine, 1.0, 0.0,
                "Unexpected away-from-sensor alignment.");
            RequireApproximately(result.Items[2].AlignmentCosine, 0.0, 0.0,
                "Unexpected grazing alignment.");
        }

        private static void TestAcquisitionDirectionOrientationOrderAndBoundary()
        {
            double z = Math.Sqrt(1.0 - 0.05 * 0.05);
            AcquisitionDirectionOrientationResult result =
                new AcquisitionDirectionOrientationTool().Execute(
                    new ThreeDPoint(1.0, 0.0, 0.0),
                    new[]
                    {
                        new AcquisitionDirectionNormalInput(
                            4,
                            new ThreeDPoint(-1.0, 0.0, 0.0)),
                        new AcquisitionDirectionNormalInput(
                            2,
                            new ThreeDPoint(0.05, 0.0, z))
                    },
                    new AcquisitionDirectionOrientationOptions
                    {
                        GrazingAbsoluteCosineMaximum = 0.05
                    });

            Require(result.Success, result.Message);
            Require(
                result.Items.Select(item => item.SourceOrder)
                    .SequenceEqual(new[] { 2, 4 }),
                "Acquisition direction output order must be canonical.");
            Require(
                result.Items[0].Orientation
                    == AcquisitionDirectionOrientation.Grazing,
                "The inclusive grazing boundary changed.");
        }

        private static void TestAcquisitionDirectionOrientationInvalid()
        {
            AcquisitionDirectionOrientationTool tool =
                new AcquisitionDirectionOrientationTool();
            AcquisitionDirectionOrientationOptions options =
                new AcquisitionDirectionOrientationOptions
                {
                    GrazingAbsoluteCosineMaximum = 0.05
                };
            AcquisitionDirectionOrientationResult zeroDirection = tool.Execute(
                new ThreeDPoint(0.0, 0.0, 0.0),
                new[]
                {
                    new AcquisitionDirectionNormalInput(
                        0,
                        new ThreeDPoint(0.0, 0.0, 1.0))
                },
                options);
            AcquisitionDirectionOrientationResult duplicateOrders = tool.Execute(
                new ThreeDPoint(0.0, 0.0, -1.0),
                new[]
                {
                    new AcquisitionDirectionNormalInput(
                        0,
                        new ThreeDPoint(0.0, 0.0, 1.0)),
                    new AcquisitionDirectionNormalInput(
                        0,
                        new ThreeDPoint(0.0, 1.0, 0.0))
                },
                options);
            AcquisitionDirectionOrientationResult nonFiniteNormal = tool.Execute(
                new ThreeDPoint(0.0, 0.0, -1.0),
                new[]
                {
                    new AcquisitionDirectionNormalInput(
                        0,
                        new ThreeDPoint(double.NaN, 0.0, 1.0))
                },
                options);
            AcquisitionDirectionOrientationResult invalidThreshold = tool.Execute(
                new ThreeDPoint(0.0, 0.0, -1.0),
                new[]
                {
                    new AcquisitionDirectionNormalInput(
                        0,
                        new ThreeDPoint(0.0, 0.0, 1.0))
                },
                new AcquisitionDirectionOrientationOptions
                {
                    GrazingAbsoluteCosineMaximum = 1.0
                });

            Require(!zeroDirection.Success,
                "A zero acquisition direction must fail closed.");
            Require(!duplicateOrders.Success,
                "Duplicate normal source orders must fail closed.");
            Require(!nonFiniteNormal.Success,
                "A non-finite normal must fail closed.");
            Require(!invalidThreshold.Success,
                "An invalid grazing threshold must fail closed.");
        }

        private static RigidPoseSymmetryEquivalenceOptions
            CreateSymmetryOptions(
                RigidPoseSymmetryKind kind,
                RigidPoseSymmetryAxis axis,
                int order,
                double maximumTranslationDifference,
                double maximumRotationDifferenceDegrees)
        {
            return new RigidPoseSymmetryEquivalenceOptions
            {
                Symmetry = new RigidPoseSymmetry(kind, axis, order),
                MaximumTranslationDifference = maximumTranslationDifference,
                MaximumRotationDifferenceDegrees =
                    maximumRotationDifferenceDegrees,
                RigidTransformTolerance = 1e-9
            };
        }

        private static RigidSurfacePose CreateRotationPose(
            RigidPoseSymmetryAxis axis,
            double angleDegrees,
            double translationX,
            double translationY,
            double translationZ)
        {
            double radians = angleDegrees * Math.PI / 180.0;
            double cosine = Math.Cos(radians);
            double sine = Math.Sin(radians);
            switch (axis)
            {
                case RigidPoseSymmetryAxis.X:
                    return new RigidSurfacePose(
                        1.0, 0.0, 0.0,
                        0.0, cosine, -sine,
                        0.0, sine, cosine,
                        translationX, translationY, translationZ);
                case RigidPoseSymmetryAxis.Y:
                    return new RigidSurfacePose(
                        cosine, 0.0, sine,
                        0.0, 1.0, 0.0,
                        -sine, 0.0, cosine,
                        translationX, translationY, translationZ);
                default:
                    return new RigidSurfacePose(
                        cosine, -sine, 0.0,
                        sine, cosine, 0.0,
                        0.0, 0.0, 1.0,
                        translationX, translationY, translationZ);
            }
        }

        private static DeterministicRigidSurfacePoseSearchOptions
            CreateSurfaceSearchOptions()
        {
            return new DeterministicRigidSurfacePoseSearchOptions
            {
                MinimumRotationXDegrees = 0.0,
                MaximumRotationXDegrees = 0.0,
                RotationStepXDegrees = 1.0,
                MinimumRotationYDegrees = 0.0,
                MaximumRotationYDegrees = 0.0,
                RotationStepYDegrees = 1.0,
                MinimumRotationZDegrees = -45.0,
                MaximumRotationZDegrees = 45.0,
                RotationStepZDegrees = 15.0,
                MinimumTranslationX = 8.0,
                MaximumTranslationX = 12.0,
                MinimumTranslationY = -6.0,
                MaximumTranslationY = -2.0,
                MinimumTranslationZ = 1.0,
                MaximumTranslationZ = 3.0,
                MaximumCorrespondenceDistance = 1e-6,
                MinimumMatchedSampleCount = 3,
                MaximumCandidateCount = 100
            };
        }

        private static DeterministicMultipleSurfaceMatchOptions
            CreateMultipleSurfaceSearchOptions()
        {
            DeterministicRigidSurfacePoseSearchOptions pose =
                CreateSurfaceSearchOptions();
            pose.MinimumRotationZDegrees = 30.0;
            pose.MaximumRotationZDegrees = 30.0;
            pose.MinimumTranslationX = -15.0;
            pose.MaximumTranslationX = 15.0;
            pose.MinimumTranslationY = -8.0;
            pose.MaximumTranslationY = 9.0;
            pose.MinimumTranslationZ = 0.0;
            pose.MaximumTranslationZ = 3.0;
            return new DeterministicMultipleSurfaceMatchOptions
            {
                PoseSearchOptions = pose,
                MaximumMatchCount = 2,
                MaximumExpandedCandidateCount = 1000
            };
        }

        private static void TestLargeOriginPlaneReliability()
        {
            LeastSquaresHeightFieldPlaneFitTool tool = new LeastSquaresHeightFieldPlaneFitTool();
            foreach (double origin in new[] { 0.0, 1e9, 1e12 })
            {
                foreach (bool uneven in new[] { false, true })
                {
                    (int X, int Z)[] locators = uneven
                        ? new[] { (-2, -2), (-1, 3), (0, 0), (0, 1), (2, 0), (9, -4) }
                        : Enumerable.Range(-2, 5).SelectMany(x => Enumerable.Range(-2, 5).Select(z => (x, z))).ToArray();
                    HeightFieldPlaneFitSample[] samples = locators.Select(point => new HeightFieldPlaneFitSample(
                        new ThreeDPoint(origin + 0.25 * point.X, origin + 0.125 * point.X - 0.125 * point.Z, -origin + 0.5 * point.Z),
                        origin + 0.5 * point.X - 0.25 * point.Z)).ToArray();
                    foreach (HeightFieldPlaneFitSample[] ordered in new[] { samples, samples.Reverse().ToArray() })
                    {
                        LeastSquaresHeightFieldPlaneFitResult fit = tool.Execute(ordered);
                        RequireApproximately(fit.SlopeX, 0.5, 1e-12, "Large-origin plane X slope changed.");
                        RequireApproximately(fit.SlopeZ, -0.25, 1e-12, "Large-origin plane Z slope changed.");
                        RequireApproximately(fit.RootMeanSquareDistance, 0.0, 1e-12, "An exactly represented plane must not acquire an origin-dependent residual.");
                        double coordinateTolerance = Math.Max(1e-12, Math.BitIncrement(origin) - origin);
                        foreach (HeightFieldPlaneFitSample sample in samples)
                            RequireApproximately(fit.EvaluateY(sample.Position.X, sample.Position.Z), sample.Position.Y, coordinateTolerance, "Centered evaluation exceeded the input coordinate resolution.");
                        RequireApproximately(fit.TargetRawHeightDelta, 0.0, coordinateTolerance, "Independent raw plane lost its large-origin fit.");
                        HeightFieldPlaneFitSample[] measured = new[] { -0.25, 0.0, 0.5, 1.0 }.Select((height, index) => new HeightFieldPlaneFitSample(
                            new ThreeDPoint(origin + 3 + index * 0.25, origin + 0.5 * (3 + index * 0.25) - 1.0 + height, -origin + 4),
                            origin + 0.5 * (3 + index * 0.25) - 1.0 + height)).ToArray();
                        PlaneFlatnessInspectionResult flatness = new PlaneFlatnessInspectionTool().Execute(ordered, measured, 2.0);
                        RequireApproximately(flatness.Flatness, 1.25 / Math.Sqrt(1.3125), 1e-12, "Large-origin orthogonal flatness changed.");
                        RequireApproximately(flatness.RootMeanSquareDistance, 0.5, 1e-12, "Independent large-origin datum RMS changed.");
                        VolumeInspectionResult volume = new VolumeInspectionTool().Execute(ordered, measured,
                            new VolumeInspectionOptions { SampleArea = 0.25, ExpectedNetVolume = 0.3125, Tolerance = 1e-12 });
                        Require(volume.Passed, "Centered plane evaluation must preserve signed vertical volume.");
                        RequireApproximately(volume.AboveVolume, 0.375, 1e-12, "Unexpected large-origin above volume.");
                        RequireApproximately(volume.BelowVolume, 0.0625, 1e-12, "Unexpected large-origin below volume.");
                    }
                }
            }
            LeastSquaresHeightFieldPlaneFitResult legacy = new LeastSquaresHeightFieldPlaneFitResult(0.5, -0.25, 3.0,
                new ThreeDPoint(0, 1, 0), 0, 3, 0, null, new ThreeDPoint(100, 200, 300), 0, 0, 0);
            RequireApproximately(legacy.EvaluateY(2, 4), 3.0, 0.0, "Public result construction must retain coefficient-based legacy evaluation.");
            RequireThrows<ArgumentException>(() => tool.Execute(new HeightFieldPlaneFitSample[] { null, null, null }));
            RequireThrows<ArgumentException>(() => tool.Execute(Enumerable.Range(0, 3).Select(x => new HeightFieldPlaneFitSample(new ThreeDPoint(x, 0, x), 0)).ToArray()));
            RequireApproximately(tool.Execute(CreateAnalyticPlaneSamples(0.5, -0.25, 2.0, new double[9])).SlopeX, 0.5, 1e-12,
                "Plane fitting must recover after rejected null or degenerate input.");
        }

        private static void TestDoubleLevelSurfaceReliability()
        {
            double[] values = Enumerable.Range(0, 25).Select(index => 1e9 + 0.5 * (index % 5) - 0.25 * (index / 5)).ToArray();
            values[12] = double.NaN;
            LevelSurfaceTool tool = new LevelSurfaceTool();
            LevelSurfaceOptions options = new LevelSurfaceOptions { MinimumValidSampleCount = 24 };
            LevelSurfaceRegion[] regions = { new LevelSurfaceRegion(0, 0, 5, 5) };
            LevelSurfaceResult result = tool.Execute(5, 5, values, regions, options);
            Require(result.Success && result.ReferenceSampleCount == 24 && result.RegionEvidence[0].ValidSampleCount == 24,
                "Leveling must retain exactly the finite reference samples.");
            RequireApproximately(result.FittedSlopeX, 0.5, 1e-12, "Double height slope X was narrowed.");
            RequireApproximately(result.FittedSlopeZ, -0.25, 1e-12, "Double height slope Z was narrowed.");
            Require(double.IsNaN(result.Values[12]) && result.Values.Where(double.IsFinite).All(value => Math.Abs(value - (1e9 + 0.5)) <= 1e-7),
                "The known plane must level to its reference mean while preserving the missing cell.");
            RequireApproximately(result.OutputReferenceSlopeX, 0.0, 1e-12, "Leveled reference still has X slope.");
            RequireApproximately(result.OutputReferenceSlopeZ, 0.0, 1e-12, "Leveled reference still has Z slope.");
            options.MinimumValidSampleCount = 25;
            Require(!tool.Execute(5, 5, values, regions, options).Success, "Missing reference support must fail closed.");
            options.MinimumValidSampleCount = 24;
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            RequireThrows<OperationCanceledException>(() => tool.Execute(5, 5, values, regions, options, cancellation.Token));
            Require(tool.Execute(5, 5, values, regions, options).Success, "Leveling must recover after support rejection and cancellation.");
        }

        private static void TestMissingHeightCoverageReliability()
        {
            double[] heights = { 1.0, 1.0, double.NaN, 1.0, 1.0, 1.0, double.NaN, 1.0, 1.0 };
            HeightMap3D map = new HeightMap3D(3, 3, 1e9, -1e9, 0.25, 0.5, heights, "mm", "mm", "datum", "missing-fixture");
            ThicknessInspectionOptions options = new ThicknessInspectionOptions
            {
                MinimumThickness = 0.9, MaximumThickness = 1.1, MinimumValidSamples = 3,
                MinimumValidCoverageRatio = 7.0 / 9.0, InputRequirements = new HeightMapInputRequirements("mm", "mm", "datum")
            };
            ThicknessInspectionTool tool = new ThicknessInspectionTool(options);
            ThreeDInspectionResult accepted = tool.Execute(map);
            Require(accepted.Success && accepted.HasMeasurement && accepted.ValidSampleCount == 7 && accepted.MissingSampleCount == 2,
                "Exact coverage boundary must accept finite heights and preserve missing counts.");
            options.MinimumValidCoverageRatio = 7.0 / 9.0 + 1e-8;
            ThreeDInspectionResult insufficient = tool.Execute(map);
            Require(!insufficient.Success && !insufficient.HasMeasurement && insufficient.ErrorCode == ThreeDInspectionErrorCode.InsufficientValidCoverage,
                "Insufficient coverage must not publish a measurement.");
            Require(insufficient.ValidSampleCount == 7 && insufficient.MissingSampleCount == 2,
                "Coverage failure must retain its actual sample evidence.");
            options.MinimumValidCoverageRatio = 7.0 / 9.0;
            HeightMap3D wrongUnits = new HeightMap3D(3, 3, 0, 0, 1, 1, heights, "mm", "raw", "datum", "wrong-unit");
            Require(tool.Execute(wrongUnits).ErrorCode == ThreeDInspectionErrorCode.InputContractMismatch,
                "Declared unit mismatch must fail before physical measurement.");
            Require(!tool.Execute(new HeightMap3D(3, 3, 0, 0, 1, 1, Enumerable.Repeat(double.NaN, 9).ToArray(), "mm", "mm", "datum", "empty")).HasMeasurement,
                "All-missing input must retain NotMeasured.");
            Require(tool.Execute(map).Success, "The same tool must recover after unit and missing-data failures.");
        }

        private static void TestLeastSquaresHeightFieldPlaneFit()
        {
            HeightFieldPlaneFitSample[] samples = CreateAnalyticPlaneSamples(0.5, -0.25, 2.0, new double[9]);
            LeastSquaresHeightFieldPlaneFitResult result = new LeastSquaresHeightFieldPlaneFitTool().Execute(samples);

            RequireApproximately(result.SlopeX, 0.5, 1e-12, "Unexpected height-field plane X slope.");
            RequireApproximately(result.SlopeZ, -0.25, 1e-12, "Unexpected height-field plane Z slope.");
            RequireApproximately(result.Intercept, 2.0, 1e-12, "Unexpected height-field plane intercept.");
            RequireApproximately(result.RootMeanSquareDistance, 0.0, 1e-7, "Analytic plane fit RMS must be zero within float-compatible distance precision.");
        }

        private static void TestPlaneFitNumericalBoundaries()
        {
            const double originX = 1000000.0, originY = -2000000.0, originZ = 3000000.0;
            double normalLength = Math.Sqrt(1.3125);
            List<HeightFieldPlaneFitSample> samples = new List<HeightFieldPlaneFitSample>();
            for (int x = -1; x <= 1; x++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    double residual = 0.1 * x * z;
                    samples.Add(new HeightFieldPlaneFitSample(
                        new ThreeDPoint(originX + x, originY + 2.0 + (0.5 * x) - (0.25 * z) + (normalLength * residual), originZ + z),
                        100.0 + (2.0 * x) + (3.0 * z) + (5.0 * x * z)));
                }
            }
            LeastSquaresHeightFieldPlaneFitTool tool = new LeastSquaresHeightFieldPlaneFitTool();
            LeastSquaresHeightFieldPlaneFitResult result = tool.Execute(samples);
            RequireApproximately(result.SlopeX, 0.5, 1e-9, "Translated inclined plane lost its X slope.");
            RequireApproximately(result.SlopeZ, -0.25, 1e-9, "Translated inclined plane lost its Z slope.");
            RequireApproximately(result.EvaluateY(originX, originZ), originY + 2.0, 1e-8, "Translated plane evaluation lost its center.");
            RequireApproximately(result.Normal.X, -0.5 / normalLength, 1e-9, "Plane normal X changed.");
            RequireApproximately(result.Normal.Y, 1.0 / normalLength, 1e-9, "Plane normal Y changed.");
            RequireApproximately(result.Normal.Z, 0.25 / normalLength, 1e-9, "Plane normal Z changed.");
            RequireApproximately(result.RootMeanSquareDistance, 1.0 / 15.0, 1e-8, "Orthogonal RMS changed after translation on all axes.");
            RequireApproximately(result.TargetAbsoluteDistance, 0.1, 1e-8, "Largest residual magnitude changed.");
            RequireApproximately(result.TargetProjection.Y, result.EvaluateY(result.TargetProjection.X, result.TargetProjection.Z),
                1e-8, "Projected target does not lie on the fitted plane.");
            RequireApproximately(Math.Abs(result.TargetRawHeightDelta), 5.0, 1e-8, "Raw-height fit must remain independent of display coordinates.");

            RequireThrows<ArgumentNullException>(() => tool.Execute(null));
            RequireThrows<ArgumentException>(() => tool.Execute(Array.Empty<HeightFieldPlaneFitSample>()));
            foreach (double magnitude in new[] { 1e200, 1e100 })
            {
                RequireThrows<InvalidOperationException>(() => tool.Execute(new[]
                {
                    new HeightFieldPlaneFitSample(new ThreeDPoint(-magnitude, 0, -magnitude), 0),
                    new HeightFieldPlaneFitSample(new ThreeDPoint(magnitude, 0, -magnitude), 0),
                    new HeightFieldPlaneFitSample(new ThreeDPoint(0, 0, magnitude), 0)
                }));
            }
            LeastSquaresHeightFieldPlaneFitResult largeConstant = tool.Execute(new[]
            {
                new HeightFieldPlaneFitSample(new ThreeDPoint(0, 1e308, 0), 0),
                new HeightFieldPlaneFitSample(new ThreeDPoint(1, 1e308, 0), 0),
                new HeightFieldPlaneFitSample(new ThreeDPoint(0, 1e308, 1), 0)
            });
            RequireApproximately(largeConstant.EvaluateY(0.5, 0.5), 1e308, 0.0,
                "A representable constant plane must not overflow a global mean.");
            RequireApproximately(largeConstant.RootMeanSquareDistance, 0.0, 0.0,
                "A constant plane must retain zero residual.");
            RequireThrows<InvalidOperationException>(() => tool.Execute(new[]
            {
                new HeightFieldPlaneFitSample(new ThreeDPoint(0, -1e308, 0), 0),
                new HeightFieldPlaneFitSample(new ThreeDPoint(1, 1e308, 0), 0),
                new HeightFieldPlaneFitSample(new ThreeDPoint(0, 1e308, 1), 0)
            }));
        }

        private static void TestPlaneFlatnessInspection()
        {
            HeightFieldPlaneFitSample[] reference = CreateAnalyticPlaneSamples(0.5, -0.25, 2.0, new double[9]);
            HeightFieldPlaneFitSample[] measurement = CreateAnalyticPlaneSamples(
                0.5,
                -0.25,
                2.0,
                new[] { -0.4, 0.0, 0.6, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 });
            PlaneFlatnessInspectionResult result = new PlaneFlatnessInspectionTool().Execute(reference, measurement, 1.1);

            Require(result.Passed && result.ReferenceSampleCount == 9 && result.MeasurementSampleCount == 9,
                "Independent plane-flatness sample counts or pass state are incorrect.");
            RequireApproximately(result.Flatness, 1.0, 1e-6, "Unexpected orthogonal flatness.");
            Require(result.MinimumSignedDistance < 0.0 && result.MaximumSignedDistance > 0.0,
                "Plane-flatness extrema must preserve signed sides of the reference plane.");

            HeightFieldPlaneFitSample[] translatedReference =
            {
                new HeightFieldPlaneFitSample(new ThreeDPoint(0.0, 1000000.0, 0.0), 1000000.0),
                new HeightFieldPlaneFitSample(new ThreeDPoint(1.0, 1000000.0, 0.0), 1000000.0),
                new HeightFieldPlaneFitSample(new ThreeDPoint(0.0, 1000000.0, 1.0), 1000000.0)
            };
            HeightFieldPlaneFitSample[] translatedMeasurement =
            {
                new HeightFieldPlaneFitSample(new ThreeDPoint(0.0, 1000000.0, 0.0), 1000000.0),
                new HeightFieldPlaneFitSample(new ThreeDPoint(1.0, 1000000.02, 0.0), 1000000.02),
                new HeightFieldPlaneFitSample(new ThreeDPoint(0.0, 1000000.0, 1.0), 1000000.0)
            };
            PlaneFlatnessInspectionResult translated = new PlaneFlatnessInspectionTool().Execute(
                translatedReference,
                translatedMeasurement,
                0.005);
            Require(!translated.Passed,
                "Plane flatness must not turn a translated 0.02 deviation into a pass.");
            RequireApproximately(translated.Flatness, 0.02, 1e-9,
                "Plane flatness must preserve the deviation after a large Y translation.");
        }

        private static void TestPlaneFlatnessDegenerateReference()
        {
            HeightFieldPlaneFitSample[] reference =
            {
                new HeightFieldPlaneFitSample(new ThreeDPoint(0.0, 0.0, 0.0), 0.0),
                new HeightFieldPlaneFitSample(new ThreeDPoint(1.0, 1.0, 0.0), 1.0),
                new HeightFieldPlaneFitSample(new ThreeDPoint(2.0, 2.0, 0.0), 2.0)
            };
            HeightFieldPlaneFitSample[] measurement = CreateAnalyticPlaneSamples(0.0, 0.0, 0.0, new double[9]);

            try
            {
                new PlaneFlatnessInspectionTool().Execute(reference, measurement, 1.0);
                throw new InvalidOperationException("Degenerate reference geometry must be rejected.");
            }
            catch (ArgumentException exception)
            {
                Require(exception.Message.Contains("span two horizontal axes", StringComparison.OrdinalIgnoreCase),
                    "Degenerate reference rejection must retain the plane-fit contract.");
            }
        }

        private static void TestPointPairDimensions()
        {
            PointPairDimensionsInspectionResult result = new PointPairDimensionsInspectionTool().Execute(
                new ThreeDPoint(1.0, 2.0, 3.0),
                new ThreeDPoint(4.0, 6.0, 7.0),
                new ThreeDPoint(0.0, 1.0, 0.0),
                12.0,
                16.0,
                PointPairOptions(Math.Sqrt(41.0), 5.0, Math.Atan2(4.0, 5.0) * 180.0 / Math.PI));

            Require(result.Passed, "Analytic point pair must pass exact tolerances.");
            RequireApproximately(result.Distance, Math.Sqrt(41.0), 1e-12, "Unexpected point-pair distance.");
            RequireApproximately(result.PlanarWidth, 5.0, 1e-12, "Unexpected point-pair planar width.");
            RequireApproximately(result.AxialHeightDelta, 4.0, 1e-12, "Unexpected point-pair axial height delta.");
            RequireApproximately(result.ScalarHeightDelta, 4.0, 1e-12, "Unexpected point-pair scalar height delta.");
        }

        private static void TestPointPairDimensionsRotatedAxis()
        {
            PointPairDimensionsInspectionResult result = new PointPairDimensionsInspectionTool().Execute(
                new ThreeDPoint(0.0, 0.0, 0.0),
                new ThreeDPoint(3.0, 4.0, 12.0),
                new ThreeDPoint(0.0, 0.0, 2.0),
                2.0,
                14.0,
                PointPairOptions(13.0, 5.0, Math.Atan2(12.0, 5.0) * 180.0 / Math.PI));

            Require(result.Passed, "Rotated-axis point pair must pass exact tolerances.");
            RequireApproximately(result.NormalizedHeightAxis.Z, 1.0, 1e-12, "Height axis was not normalized.");
            RequireApproximately(result.PlanarWidth, 5.0, 1e-12, "Planar width must be orthogonal to the declared height axis.");
            RequireApproximately(result.AxialHeightDelta, 12.0, 1e-12, "Axial height must follow the declared height axis.");
        }

        private static void TestPointPairDimensionsCoincident()
        {
            try
            {
                new PointPairDimensionsInspectionTool().Execute(
                    new ThreeDPoint(1.0, 2.0, 3.0),
                    new ThreeDPoint(1.0, 2.0, 3.0),
                    new ThreeDPoint(0.0, 1.0, 0.0),
                    0.0,
                    0.0,
                    PointPairOptions(0.0, 0.0, 0.0));
                throw new InvalidOperationException("Coincident point-pair positions must be rejected.");
            }
            catch (ArgumentException exception)
            {
                Require(exception.Message.Contains("distinct", StringComparison.OrdinalIgnoreCase),
                    "Coincident point-pair rejection must explain the distinct-point contract.");
            }
        }

        private static void TestGapFlush()
        {
            GapFlushInspectionResult result = new GapFlushInspectionTool().Execute(
                0.0, 2.0, 3.0, 5.0,
                new GapFlushRegionStatistics(20, 100.0, 1.0),
                new GapFlushRegionStatistics(30, 104.0, 1.4),
                GapFlushOptions(1.0, 4.0));

            Require(result.Passed, "Analytic gap/flush must pass exact tolerances.");
            RequireApproximately(result.SignedGap, 1.0, 1e-12, "Unexpected signed gap.");
            RequireApproximately(result.SignedFlush, 4.0, 1e-12, "Unexpected signed flush.");
            RequireApproximately(result.SignedReferenceFlush, 0.4, 1e-12, "Unexpected reference-height flush.");
        }

        private static void TestGapFlushOverlap()
        {
            GapFlushInspectionResult result = new GapFlushInspectionTool().Execute(
                0.0, 2.0, 1.5, 3.5,
                new GapFlushRegionStatistics(2, 8.0, 8.0),
                new GapFlushRegionStatistics(2, 9.0, 9.0),
                GapFlushOptions(-0.5, 1.0));

            Require(result.Passed, "Authored overlap must retain its negative signed gap.");
            RequireApproximately(result.SignedGap, -0.5, 1e-12, "Overlap sign was lost.");
        }

        private static void TestGapFlushEmptyRegion()
        {
            try
            {
                new GapFlushInspectionTool().Execute(
                    0.0, 1.0, 2.0, 3.0,
                    new GapFlushRegionStatistics(0, 1.0, 1.0),
                    new GapFlushRegionStatistics(1, 2.0, 2.0),
                    GapFlushOptions(1.0, 1.0));
                throw new InvalidOperationException("Empty gap/flush input must be rejected.");
            }
            catch (ArgumentException exception)
            {
                Require(exception.Message.Contains("at least one sample", StringComparison.OrdinalIgnoreCase),
                    "Empty gap/flush rejection must name the sample requirement.");
            }
        }

        private static GapFlushInspectionOptions GapFlushOptions(double expectedGap, double expectedFlush) =>
            new GapFlushInspectionOptions
            {
                ExpectedGap = expectedGap,
                GapTolerance = 1e-9,
                ExpectedFlush = expectedFlush,
                FlushTolerance = 1e-9
            };

        private static void TestVolume()
        {
            HeightFieldPlaneFitSample[] reference = CreateAnalyticPlaneSamples(0.5, -0.25, 2.0, new double[9]);
            HeightFieldPlaneFitSample[] measurement = CreateAnalyticPlaneSamples(
                0.5, -0.25, 2.0, new[] { 1.0, 1.0, 1.0, 1.0, 0.0, -1.0, -1.0, -1.0, -1.0 });
            double normalLength = Math.Sqrt(1.3125);
            VolumeInspectionResult result = new VolumeInspectionTool().Execute(
                reference,
                measurement,
                VolumeOptions(0.5, 0.0, 1e-9));

            Require(result.Passed, "Balanced analytic volume must pass.");
            RequireApproximately(result.AboveVolume, 2.0 * normalLength, 1e-10, "Unexpected above-plane volume.");
            RequireApproximately(result.BelowVolume, 2.0 * normalLength, 1e-10, "Unexpected below-plane volume.");
            RequireApproximately(result.NetVolume, 0.0, 1e-10, "Balanced volume must have zero net value.");
        }

        private static void TestVolumeBelowPlane()
        {
            HeightFieldPlaneFitSample[] reference = CreateAnalyticPlaneSamples(0.0, 0.0, 3.0, new double[9]);
            HeightFieldPlaneFitSample[] measurement = CreateAnalyticPlaneSamples(0.0, 0.0, 3.0, new[] { -2.0, -2.0, -2.0, -2.0, -2.0, -2.0, -2.0, -2.0, -2.0 });
            VolumeInspectionResult result = new VolumeInspectionTool().Execute(
                reference,
                measurement,
                VolumeOptions(0.25, 0.0, 1.0));

            Require(!result.Passed, "Out-of-tolerance below-plane volume must fail.");
            RequireApproximately(result.AboveVolume, 0.0, 1e-12, "Below-plane data must not add above volume.");
            RequireApproximately(result.BelowVolume, 4.5, 1e-12, "Unexpected below-plane volume.");
            RequireApproximately(result.NetVolume, -4.5, 1e-12, "Below-plane net volume must remain negative.");
        }

        private static void TestVolumeEmptyMeasurement()
        {
            try
            {
                new VolumeInspectionTool().Execute(
                    CreateAnalyticPlaneSamples(0.0, 0.0, 0.0, new double[9]),
                    Array.Empty<HeightFieldPlaneFitSample>(),
                    VolumeOptions(1.0, 0.0, 0.0));
                throw new InvalidOperationException("Empty volume measurement input must be rejected.");
            }
            catch (ArgumentException exception)
            {
                Require(exception.Message.Contains("at least one sample", StringComparison.OrdinalIgnoreCase),
                    "Empty volume rejection must name the sample requirement.");
            }
        }

        private static void TestVolumeOverflow()
        {
            HeightFieldPlaneFitSample[] reference = CreateAnalyticPlaneSamples(0.0, 0.0, 0.0, new double[9]);
            try
            {
                new VolumeInspectionTool().Execute(
                    reference,
                    new[] { new HeightFieldPlaneFitSample(new ThreeDPoint(0.0, 1e308, 0.0), 1e308) },
                    VolumeOptions(2.0, 0.0, 1.0));
                throw new InvalidOperationException("Volume accumulation overflow must be rejected.");
            }
            catch (InvalidOperationException exception)
            {
                Require(exception.Message.Contains("non-finite", StringComparison.OrdinalIgnoreCase),
                    "Volume overflow rejection must identify the non-finite calculation.");
            }
        }

        private static void TestVolumeNumericalBoundaries()
        {
            HeightFieldPlaneFitSample[] reference = CreateAnalyticPlaneSamples(0.0, 0.0, 0.0, new double[9]);
            VolumeInspectionTool tool = new VolumeInspectionTool();
            foreach (double height in new[] { 1e308, -1e308 })
            {
                HeightFieldPlaneFitSample sample = new HeightFieldPlaneFitSample(new ThreeDPoint(0, height, 0), height);
                InvalidOperationException overflow = RequireThrows<InvalidOperationException>(
                    () => tool.Execute(reference, new[] { sample, sample }, VolumeOptions(1.0, 0.0, 0.0)));
                Require(overflow.Message.Contains("accumulation", StringComparison.Ordinal), "Finite sample volumes must reject overflowing accumulation.");
                InvalidOperationException difference = RequireThrows<InvalidOperationException>(
                    () => tool.Execute(reference, new[] { sample }, VolumeOptions(1.0, -height, 0.0)));
                Require(difference.Message.Contains("acceptance difference", StringComparison.Ordinal), "Acceptance subtraction overflow must not become a tolerance failure.");
            }
            HeightFieldPlaneFitSample[] measurement = { new HeightFieldPlaneFitSample(new ThreeDPoint(0, 2, 0), 2) };
            Require(tool.Execute(reference, measurement, VolumeOptions(1.0, 1.0, 1.0)).Passed, "Exact volume tolerance boundary must pass.");
            VolumeInspectionResult outside = tool.Execute(reference, measurement, VolumeOptions(1.0, 1.0, 0.999));
            Require(!outside.Passed && outside.NetVolume == 2.0, "Valid out-of-tolerance volume must retain the measurement without throwing.");
        }

        private static VolumeInspectionOptions VolumeOptions(double sampleArea, double expectedNetVolume, double tolerance) =>
            new VolumeInspectionOptions
            {
                SampleArea = sampleArea,
                ExpectedNetVolume = expectedNetVolume,
                Tolerance = tolerance
            };

        private static void TestCrossSectionDimensions()
        {
            CrossSectionDimensionsInspectionResult result = new CrossSectionDimensionsInspectionTool().Execute(
                new[]
                {
                    new CrossSectionDimensionsSample(2, -1.5, 10.0),
                    new CrossSectionDimensionsSample(3, 0.5, 15.0),
                    new CrossSectionDimensionsSample(4, 3.5, 5.0)
                },
                CrossSectionOptions(5.0, 10.0));

            Require(result.Passed, "Analytic cross-section must pass exact acceptance.");
            RequireApproximately(result.Width, 5.0, 1e-12, "Unexpected cross-section width.");
            RequireApproximately(result.HeightRange, 10.0, 1e-12, "Unexpected cross-section height range.");
            RequireApproximately(result.HeightMinimum, 5.0, 1e-12, "Unexpected cross-section minimum height.");
            RequireApproximately(result.HeightMaximum, 15.0, 1e-12, "Unexpected cross-section maximum height.");
        }

        private static void TestCrossSectionDimensionsFailure()
        {
            CrossSectionDimensionsInspectionResult result = new CrossSectionDimensionsInspectionTool().Execute(
                new[]
                {
                    new CrossSectionDimensionsSample(0, 0.0, 2.0),
                    new CrossSectionDimensionsSample(1, 4.0, 8.0)
                },
                new CrossSectionDimensionsInspectionOptions
                {
                    ExpectedWidth = 3.0,
                    WidthTolerance = 0.1,
                    ExpectedHeightRange = 6.0,
                    HeightTolerance = 0.1
                });

            Require(!result.Passed && !result.WidthPassed && result.HeightPassed,
                "Cross-section acceptance must retain independent metric status.");
        }

        private static void TestCrossSectionDimensionsInvalidSample()
        {
            try
            {
                new CrossSectionDimensionsInspectionTool().Execute(
                    new[]
                    {
                        new CrossSectionDimensionsSample(0, 0.0, 1.0),
                        new CrossSectionDimensionsSample(1, double.NaN, 2.0)
                    },
                    CrossSectionOptions(1.0, 1.0));
                throw new InvalidOperationException("Non-finite cross-section samples must be rejected.");
            }
            catch (ArgumentException exception)
            {
                Require(exception.Message.Contains("finite", StringComparison.OrdinalIgnoreCase),
                    "Cross-section rejection must explain the finite sample contract.");
            }
        }


        private static void TestPointPairNumericBoundaries()
        {
            PointPairDimensionsInspectionTool tool = new PointPairDimensionsInspectionTool();
            ThreeDPoint first = new ThreeDPoint(0, 0, 0);
            ThreeDPoint second = new ThreeDPoint(0, 0, 5);
            foreach (double axisMagnitude in new[] { 1e-8, 1.0, 2.0, 1e200, double.MaxValue })
            {
                PointPairDimensionsInspectionResult result = tool.Execute(
                    first, second, new ThreeDPoint(0, 0, axisMagnitude), 0, 5, PointPairOptions(5, 0, 90));
                Require(result.Passed && result.NormalizedHeightAxis.Z == 1 && result.PlanarWidth == 0
                    && result.AxialHeightDelta == 5, "Axis magnitude must not change the analytic measurement.");
                Require(!tool.Execute(first, second, new ThreeDPoint(0, 0, axisMagnitude),
                    0, 5, PointPairOptions(5, 5, 0)).Passed, "The orthogonal-plane false-accept reproducer must fail.");
            }
            double unitComponent = 1 / Math.Sqrt(3);
            PointPairDimensionsInspectionResult wideAxis = tool.Execute(first, second,
                new ThreeDPoint(double.MaxValue, double.MaxValue, double.MaxValue), 0, 5,
                PointPairOptions(5, 5 * Math.Sqrt(2.0 / 3), Math.Asin(unitComponent) * 180 / Math.PI));
            Require(wideAxis.Passed, "A finite axis direction is valid even when its unnormalized norm is unrepresentable.");
            PointPairDimensionsInspectionResult large = tool.Execute(first, new ThreeDPoint(0, 0, 1e200),
                new ThreeDPoint(0, 0, 1), 0, 0, PointPairOptions(1e200, 0, 90));
            Require(large.Passed && large.Distance == 1e200, "A representable distance must survive intermediate square overflow.");
            RequireThrows<InvalidOperationException>(() => tool.Execute(first, new ThreeDPoint(3, 4, 0),
                new ThreeDPoint(0, 0, 1), -double.MaxValue, double.MaxValue, PointPairOptions(5, 5, 0)));
            RequireThrows<InvalidOperationException>(() => tool.Execute(new ThreeDPoint(-double.MaxValue, 0, 0),
                new ThreeDPoint(double.MaxValue, 0, 0), new ThreeDPoint(0, 0, 1), 0, 0, PointPairOptions(5, 5, 0)));
            Require(tool.Execute(first, second, new ThreeDPoint(0, 0, 2), 0, 5, PointPairOptions(5, 0, 90)).Passed,
                "Arithmetic failure must permit the next valid measurement.");
        }

        private static void TestCrossSectionUnrepresentableDimensions()
        {
            CrossSectionDimensionsInspectionTool tool = new CrossSectionDimensionsInspectionTool();
            RequireThrows<InvalidOperationException>(() => tool.Execute(new[]
            {
                new CrossSectionDimensionsSample(0, -double.MaxValue, 0),
                new CrossSectionDimensionsSample(1, double.MaxValue, 1)
            }, CrossSectionOptions(1, 1)));
            RequireThrows<InvalidOperationException>(() => tool.Execute(new[]
            {
                new CrossSectionDimensionsSample(0, 0, -double.MaxValue),
                new CrossSectionDimensionsSample(1, 1, double.MaxValue)
            }, CrossSectionOptions(1, 1)));
            CrossSectionDimensionsInspectionResult recovered = tool.Execute(new[]
            {
                new CrossSectionDimensionsSample(0, 0, 2),
                new CrossSectionDimensionsSample(1, 4, 8)
            }, CrossSectionOptions(4, 6));
            Require(recovered.Passed && recovered.Width == 4 && recovered.HeightRange == 6,
                "Section overflow rejection must preserve ordinary width/height results and valid recovery.");
        }

        private static CrossSectionDimensionsInspectionOptions CrossSectionOptions(double expectedWidth, double expectedHeightRange) =>
            new CrossSectionDimensionsInspectionOptions
            {
                ExpectedWidth = expectedWidth,
                WidthTolerance = 1e-9,
                ExpectedHeightRange = expectedHeightRange,
                HeightTolerance = 1e-9
            };

        private static PointPairDimensionsInspectionOptions PointPairOptions(
            double distance,
            double planarWidth,
            double elevationAngleDegrees) =>
            new PointPairDimensionsInspectionOptions
            {
                ExpectedDistance = distance,
                DistanceTolerance = 1e-10,
                ExpectedPlanarWidth = planarWidth,
                PlanarWidthTolerance = 1e-10,
                ExpectedElevationAngleDegrees = elevationAngleDegrees,
                ElevationAngleToleranceDegrees = 1e-10
            };

        private static void TestAnalyticPlaneScale()
        {
            double[] scales = { 1e-9, 1e-6, 1e-3, 1.0, 1e3, 1e6 };
            LeastSquaresHeightFieldPlaneFitTool tool = new LeastSquaresHeightFieldPlaneFitTool();
            double maximumError = 0.0;
            foreach (double scale in scales)
            {
                HeightFieldPlaneFitSample[] samples = CreateScaledQualificationPlane(scale);
                LeastSquaresHeightFieldPlaneFitResult result = tool.Execute(samples);
                LeastSquaresHeightFieldPlaneFitResult reversed = tool.Execute(samples.Reverse().ToArray());
                RequireApproximately(result.SlopeX, 0.5, 2e-12, "Unit conversion changed the plane X slope.");
                RequireApproximately(result.SlopeZ, -0.25, 2e-12, "Unit conversion changed the plane Z slope.");
                RequireApproximately(result.Intercept / scale, 2.0, 2e-12, "Plane intercept did not scale with coordinates.");
                RequireApproximately(result.EvaluateY(4.0 * scale, -2.0 * scale) / scale, 4.5, 2e-12,
                    "Plane evaluation must agree with the independently authored equation outside the reference grid.");
                RequireApproximately(result.RootMeanSquareDistance / scale, 0.0, 2e-12, "Exact-plane residual changed with units.");
                RequireApproximately(result.TargetRawHeightDelta / scale, 0.0, 2e-12, "Independent raw-height plane lost its exact fit.");
                RequireApproximately(reversed.SlopeX, 0.5, 2e-12, "Reordering samples changed the analytic X slope.");
                RequireApproximately(reversed.SlopeZ, -0.25, 2e-12, "Reordering samples changed the analytic Z slope.");
                maximumError = Math.Max(maximumError, Math.Max(Math.Abs(result.SlopeX - 0.5), Math.Abs(result.SlopeZ + 0.25)));
                maximumError = Math.Max(maximumError, Math.Abs(result.Intercept / scale - 2.0));
            }
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "EVIDENCE | plane-scale | scales={0} | max-normalized-coefficient-error={1:R}", scales.Length, maximumError));
        }

        private static void TestAnalyticPlaneRank()
        {
            double[] scales = { 1e-9, 1e-6, 1e-3, 1.0, 1e3, 1e6 };
            double[] perturbations = { 0.0, 1e-7 };
            LeastSquaresHeightFieldPlaneFitTool tool = new LeastSquaresHeightFieldPlaneFitTool();
            foreach (double scale in scales)
            {
                foreach (double perturbation in perturbations)
                {
                    HeightFieldPlaneFitSample[] samples = new HeightFieldPlaneFitSample[4];
                    for (int index = 0; index < samples.Length; index++)
                    {
                        double x = index < 2 ? -scale : scale;
                        double z = x + ((index % 2 == 0 ? -1.0 : 1.0) * perturbation * scale);
                        double y = (0.5 * x) - (0.25 * z) + (2.0 * scale);
                        samples[index] = new HeightFieldPlaneFitSample(new ThreeDPoint(x, y, z), y);
                    }
                    ArgumentException failure = RequireThrows<ArgumentException>(() => tool.Execute(samples));
                    Require(failure.ParamName == "samples" && failure.Message.Contains("span two horizontal axes", StringComparison.Ordinal),
                        "Rank rejection must retain the existing geometric input contract at every scale.");
                }
                RequireApproximately(tool.Execute(CreateScaledQualificationPlane(scale)).SlopeX, 0.5, 2e-12,
                    "A valid plane must recover after a degenerate input at the same coordinate scale.");
            }
        }

        private static void TestAnalyticDatumMeasurements()
        {
            double[] scales = { 1e-9, 1e-6, 1e-3, 1.0, 1e3, 1e6 };
            double[] verticalOffsets = { -0.25, 0.0, 0.5, 1.0, -0.5, 0.25 };
            double normalLength = Math.Sqrt(1.3125);
            double maximumFlatnessError = 0.0, maximumVolumeError = 0.0, maximumThicknessError = 0.0;
            PlaneFlatnessInspectionTool flatnessTool = new PlaneFlatnessInspectionTool();
            VolumeInspectionTool volumeTool = new VolumeInspectionTool();
            DualSurfaceThicknessInspectionTool thicknessTool = new DualSurfaceThicknessInspectionTool();
            foreach (double scale in scales)
            {
                HeightFieldPlaneFitSample[] reference = CreateScaledQualificationPlane(scale);
                HeightFieldPlaneFitSample[] measurement = new HeightFieldPlaneFitSample[verticalOffsets.Length];
                for (int index = 0; index < measurement.Length; index++)
                {
                    double x = (3.0 + (0.5 * index)) * scale, z = (-2.0 + (0.5 * index)) * scale;
                    double y = (0.5 * x) - (0.25 * z) + ((2.0 + verticalOffsets[index]) * scale);
                    measurement[index] = new HeightFieldPlaneFitSample(new ThreeDPoint(x, y, z), y);
                }
                PlaneFlatnessInspectionResult flatness = flatnessTool.Execute(reference, measurement, 1.51 * scale / normalLength);
                PlaneFlatnessInspectionResult outside = flatnessTool.Execute(reference, measurement, 1.49 * scale / normalLength);
                Require(flatness.Passed && !outside.Passed, "Tilted flatness must preserve valid measurements on both sides of the authored tolerance.");
                RequireApproximately(flatness.Flatness / scale, 1.5 / normalLength, 2e-12, "Flatness must use orthogonal distances.");
                RequireApproximately(flatness.MinimumSignedDistance / scale, -0.5 / normalLength, 2e-12, "Minimum signed plane distance changed.");
                RequireApproximately(flatness.MaximumSignedDistance / scale, 1.0 / normalLength, 2e-12, "Maximum signed plane distance changed.");
                RequireApproximately(flatness.RootMeanSquareDistance / scale, Math.Sqrt(1.625 / 6.0) / normalLength, 2e-12,
                    "Flatness RMS must agree with the closed-form signed residuals.");
                RequireApproximately(flatness.MinimumProjection.Y / scale,
                    ((0.5 * flatness.MinimumProjection.X) - (0.25 * flatness.MinimumProjection.Z)) / scale + 2.0, 2e-12,
                    "The extremum projection must lie on the independent datum equation.");
                double sampleArea = 0.125 * scale * scale, expectedVolume = sampleArea * scale;
                VolumeInspectionResult volume = volumeTool.Execute(reference, measurement, VolumeOptions(sampleArea, expectedVolume, 2e-12 * expectedVolume));
                Require(volume.Passed, "Analytic vertical volume must pass its numerical fixture tolerance.");
                RequireApproximately(volume.AboveVolume / expectedVolume, 1.75, 2e-12, "Above volume must integrate vertical residuals.");
                RequireApproximately(volume.BelowVolume / expectedVolume, 0.75, 2e-12, "Below volume must retain negative-side magnitude.");
                RequireApproximately(volume.NetVolume / expectedVolume, 1.0, 2e-12, "Volume must scale cubically with coordinates.");
                DualSurfaceThicknessInspectionResult thickness = thicknessTool.Execute(reference, measurement, -0.51 * scale, 1.01 * scale, 6);
                Require(thickness.Decision == DualSurfaceThicknessDecision.Pass && thickness.MeasurementSampleCount == 6,
                    "Independent thickness samples must all participate in the measurement.");
                RequireApproximately(thickness.Mean / scale, 1.0 / 6.0, 2e-12, "Thickness must use signed height-axis separation.");
                RequireApproximately(thickness.Range / scale, 1.5, 2e-12, "Thickness range must use vertical separation.");
                maximumFlatnessError = Math.Max(maximumFlatnessError, Math.Abs(flatness.Flatness / scale - (1.5 / normalLength)));
                maximumVolumeError = Math.Max(maximumVolumeError, Math.Abs(volume.NetVolume / expectedVolume - 1.0));
                maximumThicknessError = Math.Max(maximumThicknessError, Math.Abs(thickness.Mean / scale - (1.0 / 6.0)));
            }
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "EVIDENCE | tilted-datum | max-normalized-flatness-error={0:R} | max-relative-volume-error={1:R} | max-normalized-thickness-error={2:R}",
                maximumFlatnessError, maximumVolumeError, maximumThicknessError));
        }

        private static void TestAnalyticRigidScale()
        {
            double[] scales = { 1e-9, 1e-6, 1e-3, 1.0, 1e3, 1e6 };
            double[][] rotations =
            {
                new double[] { 1, 0, 0, 0, 0, -1, 0, 1, 0 },
                new double[] { 0, 0, 1, 0, 1, 0, -1, 0, 0 },
                new double[] { 0, -1, 0, 1, 0, 0, 0, 0, 1 },
                new double[] { 0, 0, 1, 1, 0, 0, 0, 1, 0 },
                new double[] { -7.0 / 9, 4.0 / 9, 4.0 / 9, 4.0 / 9, -1.0 / 9, 8.0 / 9, 4.0 / 9, 8.0 / 9, -1.0 / 9 }
            };
            ConstrainedBestFitRigidAlignmentTool tool = new ConstrainedBestFitRigidAlignmentTool();
            double maximumRotationError = 0.0, maximumTranslationError = 0.0, maximumNormalizedResidual = 0.0;
            foreach (double scale in scales)
            {
                ConstrainedBestFitRigidAlignmentOptions options = new ConstrainedBestFitRigidAlignmentOptions { ArithmeticResidualWarning = 2e-12 * scale };
                foreach (double[] rotation in rotations)
                {
                    ConstrainedBestFitRigidCorrespondence[] pairs = CreateRigidQualificationPairs(scale, rotation);
                    ConstrainedBestFitRigidAlignmentResult result = tool.Execute(pairs, options);
                    ConstrainedBestFitRigidAlignmentResult reversed = tool.Execute(pairs.Reverse().ToArray(), options);
                    Require(result.Success && result.Pose != null && result.UsedAllCorrespondences && reversed.Success,
                        "The analytic full-XYZ rigid fixture must produce an all-pair proper pose at every scale.");
                    IReadOnlyList<double> matrix = result.Pose.ToRowMajor4X4();
                    IReadOnlyList<double> reversedMatrix = reversed.Pose.ToRowMajor4X4();
                    for (int row = 0; row < 3; row++)
                    {
                        for (int column = 0; column < 3; column++)
                        {
                            double expected = rotation[(row * 3) + column], actual = matrix[(row * 4) + column];
                            RequireApproximately(actual, expected, 2e-12, "Recovered rotation disagrees with the independent known rigid matrix.");
                            RequireApproximately(reversedMatrix[(row * 4) + column], expected, 2e-12, "Reversed pairs changed the known rigid rotation.");
                            maximumRotationError = Math.Max(maximumRotationError, Math.Abs(actual - expected));
                        }
                    }
                    RequireApproximately(result.Pose.TranslationX / scale, 10.0, 2e-12, "Translation X did not scale with units.");
                    RequireApproximately(result.Pose.TranslationY / scale, -4.0, 2e-12, "Translation Y did not scale with units.");
                    RequireApproximately(result.Pose.TranslationZ / scale, 2.0, 2e-12, "Translation Z did not scale with units.");
                    Require(!result.ArithmeticResidualWarningExceeded, "Exact correspondences must not produce an arithmetic residual warning.");
                    RequireApproximately(result.MaximumResidual / scale, 0.0, 2e-12, "Exact rigid residuals did not remain zero after unit conversion.");
                    maximumTranslationError = Math.Max(maximumTranslationError, Math.Max(Math.Abs(result.Pose.TranslationX / scale - 10.0),
                        Math.Max(Math.Abs(result.Pose.TranslationY / scale + 4.0), Math.Abs(result.Pose.TranslationZ / scale - 2.0))));
                    maximumNormalizedResidual = Math.Max(maximumNormalizedResidual, result.MaximumResidual / scale);
                }
            }
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "EVIDENCE | rigid-scale | poses={0} | max-rotation-entry-error={1:R} | max-normalized-translation-error={2:R} | max-normalized-residual={3:R}",
                scales.Length * rotations.Length, maximumRotationError, maximumTranslationError, maximumNormalizedResidual));
        }

        private static void TestAnalyticRigidNoiseAndRecovery()
        {
            double[] rotation = { 0, -1, 0, 1, 0, 0, 0, 0, 1 };
            ConstrainedBestFitRigidCorrespondence[] clean = CreateRigidQualificationPairs(1.0, rotation);
            ConstrainedBestFitRigidCorrespondence[] noisy = (ConstrainedBestFitRigidCorrespondence[])clean.Clone();
            ThreeDPoint reference = clean[0].Reference;
            noisy[0] = new ConstrainedBestFitRigidCorrespondence(clean[0].Source, new ThreeDPoint(reference.X, reference.Y + 0.25, reference.Z));
            ConstrainedBestFitRigidAlignmentOptions options = new ConstrainedBestFitRigidAlignmentOptions { ArithmeticResidualWarning = 0.01 };
            ConstrainedBestFitRigidAlignmentTool tool = new ConstrainedBestFitRigidAlignmentTool();
            ConstrainedBestFitRigidAlignmentResult noisyResult = tool.Execute(noisy, options);
            Require(noisyResult.Success && noisyResult.UsedAllCorrespondences && noisyResult.ArithmeticResidualWarningExceeded,
                "All-pair alignment must report the authored outlier rather than silently excluding it.");
            RequireApproximately(noisyResult.Pose.M12, -1.0, 2e-12, "The symmetric radial outlier must preserve the known rotation.");
            RequireApproximately(noisyResult.Pose.TranslationY, -4.0 + (1.0 / 24.0), 2e-12, "All-pair translation must include the outlier centroid shift.");
            RequireApproximately(noisyResult.RmsResidual, Math.Sqrt(5.0) / 24.0, 2e-12, "Outlier RMS disagrees with independent centered residuals.");
            RequireApproximately(noisyResult.MaximumResidual, 5.0 / 24.0, 2e-12, "Outlier maximum residual disagrees with the closed form.");
            ConstrainedBestFitRigidAlignmentResult failure = tool.Execute(Array.Empty<ConstrainedBestFitRigidCorrespondence>(), options);
            Require(!failure.Success && failure.Pose == null && failure.Residuals.Count == 0, "A rejected call must not retain a prior pose or residuals.");
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            OperationCanceledException canceled = RequireThrows<OperationCanceledException>(() => tool.Execute(clean, options, cancellation.Token));
            Require(canceled.CancellationToken == cancellation.Token, "Cancellation must retain the caller's token.");
            ConstrainedBestFitRigidAlignmentResult recovered = tool.Execute(clean, options);
            Require(recovered.Success && !recovered.ArithmeticResidualWarningExceeded && recovered.UsedAllCorrespondences,
                "Clean alignment must recover after an outlier, rejection and cancellation on the same tool.");
            RequireApproximately(recovered.Pose.TranslationY, -4.0, 2e-12, "Recovery must not retain the outlier centroid shift.");
            RequireApproximately(recovered.RmsResidual, 0.0, 2e-12, "Recovery must clear earlier residuals.");
            Require(clean[0].Reference.Y == reference.Y, "Alignment must preserve caller-owned correspondences.");
        }

        private static void TestAnalyticGapOverflow()
        {
            GapFlushInspectionTool tool = new GapFlushInspectionTool();
            GapFlushInspectionOptions options = GapFlushOptions(0.5, -0.25);
            double maximum = double.MaxValue;
            for (int metric = 0; metric < 3; metric++)
            {
                GapFlushRegionStatistics first = new GapFlushRegionStatistics(2, metric == 1 ? -maximum : 1.0, metric == 2 ? -maximum : 1.0);
                GapFlushRegionStatistics second = new GapFlushRegionStatistics(2, metric == 1 ? maximum : 0.75, metric == 2 ? maximum : 0.75);
                InvalidOperationException failure = RequireThrows<InvalidOperationException>(() => tool.Execute(
                    metric == 0 ? -maximum : 0.0, metric == 0 ? -0.75 * maximum : 1.0,
                    metric == 0 ? 0.75 * maximum : 1.5, metric == 0 ? maximum : 2.5, first, second, options));
                Require(failure.Message.Contains("non-finite", StringComparison.Ordinal), "Gap/flush overflow must identify a calculation failure.");
                GapFlushInspectionResult recovered = tool.Execute(0.0, 1.0, 1.5, 2.5,
                    new GapFlushRegionStatistics(2, 1.0, 1.0), new GapFlushRegionStatistics(2, 0.75, 0.75), options);
                Require(recovered.Passed && recovered.FirstSampleCount == 2 && recovered.SecondSampleCount == 2,
                    "A valid gap/flush must recover after each overflowing metric.");
                RequireApproximately(recovered.SignedGap, 0.5, 0.0, "Recovered gap changed.");
                RequireApproximately(recovered.SignedFlush, -0.25, 0.0, "Recovered flush changed.");
                RequireApproximately(recovered.SignedReferenceFlush, -0.25, 0.0, "Recovered reference flush changed.");
            }
        }

        private static HeightFieldPlaneFitSample[] CreateScaledQualificationPlane(double scale)
        {
            double[] offsets = new double[9];
            HeightFieldPlaneFitSample[] samples = CreateAnalyticPlaneSamples(0.5, -0.25, 2.0, offsets);
            return samples.Select(sample => new HeightFieldPlaneFitSample(
                new ThreeDPoint(sample.Position.X * scale, sample.Position.Y * scale, sample.Position.Z * scale),
                ((-3.0 * sample.Position.X) + (2.0 * sample.Position.Z) + 5.0) * scale)).ToArray();
        }

        private static ConstrainedBestFitRigidCorrespondence[] CreateRigidQualificationPairs(double scale, IReadOnlyList<double> rotation)
        {
            ThreeDPoint[] source =
            {
                new ThreeDPoint(scale, 0, 0), new ThreeDPoint(-scale, 0, 0),
                new ThreeDPoint(0, 2.0 * scale, 0), new ThreeDPoint(0, -2.0 * scale, 0),
                new ThreeDPoint(0, 0, 3.0 * scale), new ThreeDPoint(0, 0, -3.0 * scale)
            };
            return source.Select(point => new ConstrainedBestFitRigidCorrespondence(point, new ThreeDPoint(
                (rotation[0] * point.X) + (rotation[1] * point.Y) + (rotation[2] * point.Z) + (10.0 * scale),
                (rotation[3] * point.X) + (rotation[4] * point.Y) + (rotation[5] * point.Z) - (4.0 * scale),
                (rotation[6] * point.X) + (rotation[7] * point.Y) + (rotation[8] * point.Z) + (2.0 * scale)))).ToArray();
        }

        private static HeightFieldPlaneFitSample[] CreateAnalyticPlaneSamples(
            double slopeX,
            double slopeZ,
            double intercept,
            IReadOnlyList<double> normalOffsets)
        {
            HeightFieldPlaneFitSample[] samples = new HeightFieldPlaneFitSample[9];
            double normalLength = Math.Sqrt((slopeX * slopeX) + 1.0 + (slopeZ * slopeZ));
            for (int z = 0; z < 3; z++)
            {
                for (int x = 0; x < 3; x++)
                {
                    int index = (z * 3) + x;
                    double y = (slopeX * x) + (slopeZ * z) + intercept + (normalOffsets[index] * normalLength);
                    samples[index] = new HeightFieldPlaneFitSample(new ThreeDPoint(x, y, z), y);
                }
            }

            return samples;
        }
    }
}

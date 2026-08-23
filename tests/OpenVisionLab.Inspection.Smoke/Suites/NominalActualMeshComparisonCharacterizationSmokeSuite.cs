using OpenVisionLab.Vision3D.FeatureExtraction;
using System;
using System.Collections.Generic;
using System.Threading;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class NominalActualMeshComparisonCharacterizationSmokeSuite
    {
        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase(
                "Nominal/actual mesh comparison characterizes robust recovery, inclusive tolerance, and statistics",
                TestRobustRecoveryToleranceAndStatistics);
            yield return new SmokeCase(
                "Nominal/actual mesh comparison characterizes display stride, cap, and disabled sampling",
                TestDisplaySampling);
            yield return new SmokeCase(
                "Nominal/actual mesh comparison characterizes stream and invalid-input controlled failures",
                TestControlledFailures);
            yield return new SmokeCase(
                "Nominal/actual mesh comparison characterizes progress cadence and cancellation propagation",
                TestProgressAndCancellation);
        }

        private static void TestRobustRecoveryToleranceAndStatistics()
        {
            ThreeDPoint[] points =
            {
                Point(0.5, 0.5, -2.0),
                Point(0.5, 0.5, -1.0),
                Point(1.0, 0.0, 1.0),
                Point(0.5, 0.5, 2.0)
            };
            NominalActualMeshComparisonResult result =
                new NominalActualMeshComparisonTool().Execute(
                    CreateMesh(),
                    points,
                    new NominalActualMeshComparisonOptions(
                        points.Length,
                        -1.0,
                        1.0,
                        points.Length));

            Require(result.Success
                    && result.ProcessedPointCount == 4
                    && result.BelowToleranceCount == 1
                    && result.WithinToleranceCount == 2
                    && result.AboveToleranceCount == 1
                    && result.DirectSignResolvedCount == 3
                    && result.RobustSignRecoveredCount == 1
                    && result.DisplayStride == 1
                    && result.DisplaySamples.Count == 4,
                "Robust recovery, inclusive tolerance, or comparison counts changed.");
            RequireStatistics(
                result.UnsignedStatistics,
                4,
                1.0,
                2.0,
                1.5,
                0.5,
                Math.Sqrt(2.5),
                "unsigned");
            RequireStatistics(
                result.SignedStatistics,
                4,
                -2.0,
                2.0,
                0.0,
                Math.Sqrt(2.5),
                Math.Sqrt(2.5),
                "signed");

            double[] signedDistances = { -2.0, -1.0, 1.0, 2.0 };
            for (int index = 0; index < result.DisplaySamples.Count; index++)
            {
                NominalActualMeshDeviationSample sample =
                    result.DisplaySamples[index];
                Require(sample.PointIndex == index
                        && sample.SourceTriangleIndex == 3
                        && sample.RobustSignRecovered == (index == 2),
                    "Display evidence lost query order, source identity, or robust-sign state.");
                RequirePoint(sample.Point, points[index],
                    "display query point " + index);
                RequireApproximately(
                    sample.SignedDistance,
                    signedDistances[index],
                    1e-12,
                    "Unexpected signed display deviation.");
            }

            RequirePoint(
                result.DisplaySamples[2].ClosestPoint,
                Point(1.0, 0.0, 0.0),
                "robust edge closest point");
            RequireApproximately(
                result.DisplaySamples[2].UnsignedDistance,
                1.0,
                1e-12,
                "Unexpected robust-edge unsigned deviation.");
        }

        private static void TestDisplaySampling()
        {
            ThreeDPoint[] points =
            {
                Point(0.5, 0.5, 1.0),
                Point(0.5, 0.5, 2.0),
                Point(0.5, 0.5, 3.0),
                Point(0.5, 0.5, 4.0),
                Point(0.5, 0.5, 5.0),
                Point(0.5, 0.5, 6.0),
                Point(0.5, 0.5, 7.0)
            };
            NominalActualMeshComparisonTool tool =
                new NominalActualMeshComparisonTool();
            NominalActualMeshComparisonResult sampled = tool.Execute(
                CreateMesh(),
                points,
                new NominalActualMeshComparisonOptions(
                    points.Length,
                    -10.0,
                    10.0,
                    3));

            Require(sampled.Success
                    && sampled.ProcessedPointCount == 7
                    && sampled.DisplayStride == 3
                    && sampled.DisplaySamples.Count == 3,
                "Display sampling stride or cap changed.");
            long[] expectedIndices = { 0, 3, 6 };
            for (int index = 0; index < expectedIndices.Length; index++)
            {
                NominalActualMeshDeviationSample sample =
                    sampled.DisplaySamples[index];
                Require(sample.PointIndex == expectedIndices[index]
                        && sample.SourceTriangleIndex == 3,
                    "Display sampling lost deterministic query order or source identity.");
                RequireApproximately(
                    sample.SignedDistance,
                    expectedIndices[index] + 1.0,
                    1e-12,
                    "Display sampling retained the wrong deviation.");
            }

            NominalActualMeshComparisonResult disabled = tool.Execute(
                CreateMesh(),
                points,
                new NominalActualMeshComparisonOptions(
                    points.Length,
                    -10.0,
                    10.0,
                    0));
            Require(disabled.Success
                    && disabled.ProcessedPointCount == 7
                    && disabled.DisplayStride == 0
                    && disabled.DisplaySamples.Count == 0,
                "Zero maximum display samples must disable retained display evidence.");
        }

        private static void TestControlledFailures()
        {
            MeshTriangle[] mesh = CreateMesh();
            ThreeDPoint point = Point(0.5, 0.5, 1.0);
            NominalActualMeshComparisonTool tool =
                new NominalActualMeshComparisonTool();

            RequireCanonicalFailure(
                tool.Execute(
                    mesh,
                    new[] { point },
                    new NominalActualMeshComparisonOptions(2, -1.0, 1.0, 1)),
                "not consumed completely");
            RequireCanonicalFailure(
                tool.Execute(
                    mesh,
                    new[] { point, point },
                    new NominalActualMeshComparisonOptions(1, -1.0, 1.0, 1)),
                "more points than declared");
            RequireCanonicalFailure(
                tool.Execute(
                    mesh,
                    new[] { Point(double.NaN, 0.0, 0.0) },
                    new NominalActualMeshComparisonOptions(1, -1.0, 1.0, 1)),
                "finite coordinates");
            RequireCanonicalFailure(
                tool.Execute(
                    null,
                    new[] { point },
                    new NominalActualMeshComparisonOptions(1, -1.0, 1.0, 1)),
                "nominalTriangles");
            RequireCanonicalFailure(
                tool.Execute(
                    mesh,
                    null,
                    new NominalActualMeshComparisonOptions(1, -1.0, 1.0, 1)),
                "queryPoints");
            RequireCanonicalFailure(
                tool.Execute(mesh, new[] { point }, null),
                "options");
            RequireCanonicalFailure(
                tool.Execute(
                    mesh,
                    new[] { point },
                    new NominalActualMeshComparisonOptions(0, -1.0, 1.0, 1)),
                "expected query point count must be positive");
            RequireCanonicalFailure(
                tool.Execute(
                    mesh,
                    new[] { point },
                    new NominalActualMeshComparisonOptions(1, 0.0, 1.0, 1)),
                "zero-centred");
            RequireCanonicalFailure(
                tool.Execute(
                    mesh,
                    new[] { point },
                    new NominalActualMeshComparisonOptions(1, -1.0, 1.0, -1)),
                "MaximumDisplaySamples");
        }

        private static void TestProgressAndCancellation()
        {
            const long expectedPointCount = 65537;
            RecordingProgress progress = new RecordingProgress();
            NominalActualMeshComparisonTool tool =
                new NominalActualMeshComparisonTool();
            NominalActualMeshComparisonResult result = tool.Execute(
                CreateMesh(),
                RepeatPoint(Point(0.5, 0.5, 1.0), expectedPointCount),
                new NominalActualMeshComparisonOptions(
                    expectedPointCount,
                    -2.0,
                    2.0,
                    0),
                progress);

            Require(result.Success
                    && result.ProcessedPointCount == expectedPointCount
                    && progress.Events.Count == 2,
                "Progress characterization did not complete with two deterministic milestones.");
            RequireProgress(progress.Events[0], 65536, expectedPointCount);
            RequireProgress(
                progress.Events[1],
                expectedPointCount,
                expectedPointCount);

            OperationCanceledException cancellation =
                CaptureException<OperationCanceledException>(
                    () => tool.Execute(
                        CreateMesh(),
                        new[] { Point(0.5, 0.5, 1.0) },
                        new NominalActualMeshComparisonOptions(
                            1,
                            -2.0,
                            2.0,
                            0),
                        cancellationToken: new CancellationToken(true)));
            Require(cancellation != null,
                "A pre-cancelled comparison must propagate cancellation.");
        }

        private static MeshTriangle[] CreateMesh()
        {
            return new[]
            {
                new MeshTriangle(
                    3,
                    Point(0.0, 0.0, 0.0),
                    Point(2.0, 0.0, 0.0),
                    Point(0.0, 2.0, 0.0))
            };
        }

        private static ThreeDPoint Point(double x, double y, double z)
        {
            return new ThreeDPoint(x, y, z);
        }

        private static IEnumerable<ThreeDPoint> RepeatPoint(
            ThreeDPoint point,
            long count)
        {
            for (long index = 0; index < count; index++)
            {
                yield return point;
            }
        }

        private static void RequireStatistics(
            MeshDeviationStatistics actual,
            long count,
            double minimum,
            double maximum,
            double mean,
            double standardDeviationPopulation,
            double rootMeanSquare,
            string label)
        {
            Require(actual != null && actual.Count == count,
                "Unexpected " + label + " statistics count.");
            RequireApproximately(actual.Minimum, minimum, 1e-12,
                "Unexpected " + label + " statistics minimum.");
            RequireApproximately(actual.Maximum, maximum, 1e-12,
                "Unexpected " + label + " statistics maximum.");
            RequireApproximately(actual.Mean, mean, 1e-12,
                "Unexpected " + label + " statistics mean.");
            RequireApproximately(
                actual.StandardDeviationPopulation,
                standardDeviationPopulation,
                1e-12,
                "Unexpected " + label + " population standard deviation.");
            RequireApproximately(actual.RootMeanSquare, rootMeanSquare, 1e-12,
                "Unexpected " + label + " root mean square.");
        }

        private static void RequireCanonicalFailure(
            NominalActualMeshComparisonResult result,
            string messageFragment)
        {
            Require(!result.Success
                    && !string.IsNullOrWhiteSpace(result.Message)
                    && result.Message.IndexOf(
                        messageFragment,
                        StringComparison.OrdinalIgnoreCase) >= 0
                    && result.ProcessedPointCount == 0
                    && result.UnsignedStatistics == null
                    && result.SignedStatistics == null
                    && result.BelowToleranceCount == 0
                    && result.WithinToleranceCount == 0
                    && result.AboveToleranceCount == 0
                    && result.DirectSignResolvedCount == 0
                    && result.RobustSignRecoveredCount == 0
                    && result.DisplayStride == 0
                    && result.DisplaySamples != null
                    && result.DisplaySamples.Count == 0
                    && result.IndexDuration == TimeSpan.Zero
                    && result.CalculationDuration == TimeSpan.Zero,
                "A controlled failure did not preserve its canonical result shape for '"
                + messageFragment
                + "'.");
        }

        private static void RequirePoint(
            ThreeDPoint actual,
            ThreeDPoint expected,
            string label)
        {
            RequireApproximately(actual.X, expected.X, 1e-12,
                "Unexpected " + label + " X.");
            RequireApproximately(actual.Y, expected.Y, 1e-12,
                "Unexpected " + label + " Y.");
            RequireApproximately(actual.Z, expected.Z, 1e-12,
                "Unexpected " + label + " Z.");
        }

        private static void RequireProgress(
            NominalActualMeshComparisonProgress actual,
            long processedPointCount,
            long totalPointCount)
        {
            Require(actual.ProcessedPointCount == processedPointCount
                    && actual.TotalPointCount == totalPointCount,
                "Progress counts changed at a characterized milestone.");
        }

        private static TException CaptureException<TException>(Action action)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException exception)
            {
                Require(exception.GetType() == typeof(TException),
                    "The comparison propagated an unexpected cancellation type.");
                return exception;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "The comparison propagated "
                    + exception.GetType().FullName
                    + " instead of "
                    + typeof(TException).FullName
                    + ".",
                    exception);
            }

            throw new InvalidOperationException(
                "The comparison did not propagate "
                + typeof(TException).FullName
                + ".");
        }

        private sealed class RecordingProgress
            : IProgress<NominalActualMeshComparisonProgress>
        {
            public List<NominalActualMeshComparisonProgress> Events
            {
                get;
            } = new List<NominalActualMeshComparisonProgress>();

            public void Report(NominalActualMeshComparisonProgress value)
            {
                Events.Add(value);
            }
        }
    }
}

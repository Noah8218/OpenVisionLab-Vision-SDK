using OpenVisionLab.Vision3D.FeatureExtraction;
using System;
using System.Collections.Generic;
using System.Threading;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class PointCloudBackgroundFilterSmokeSuite
    {
        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase(
                "Point-cloud background filter removes points at or below nearest distance",
                TestSeparatedCloud);
            yield return new SmokeCase(
                "Point-cloud background filter rejects invalid inputs and honors cancellation",
                TestGuardsAndCancellation);
        }

        private static void TestSeparatedCloud()
        {
            var current = new[]
            {
                Point(0.0, 0.0, 0.0),
                Point(0.4, 0.0, 0.0),
                Point(2.0, 0.0, 0.0),
                Point(5.0, 0.0, 0.0)
            };
            var background = new[]
            {
                Point(0.0, 0.0, 0.0),
                Point(3.0, 0.0, 0.0)
            };

            var result = new PointCloudBackgroundFilterTool().Execute(
                current,
                background,
                new PointCloudBackgroundFilterOptions
                {
                    MaximumBackgroundDistance = 0.5
                });

            Require(result.Success, "A separated finite cloud must filter successfully.");
            Require(result.InputPointCount == 4 && result.BackgroundPointCount == 2,
                "The filter did not report both input point counts.");
            Require(result.RetainedPointCount == 2 && result.RemovedPointCount == 2,
                "The filter did not classify points at the inclusive threshold correctly.");
            Require(result.RetainedPoints[0].SourceIndex == 2
                && result.RetainedPoints[1].SourceIndex == 3,
                "Retained points must preserve original current-point order.");
            RequireApproximately(result.RetainedPoints[0].NearestBackgroundDistance, 1.0, 1e-12,
                "The first retained nearest distance is incorrect.");
            RequireApproximately(result.RetainedPoints[1].NearestBackgroundDistance, 2.0, 1e-12,
                "The second retained nearest distance is incorrect.");
            RequireApproximately(result.MinimumNearestBackgroundDistance, 0.0, 1e-12,
                "The minimum nearest distance is incorrect.");
            RequireApproximately(result.MaximumNearestBackgroundDistance, 2.0, 1e-12,
                "The maximum nearest distance is incorrect.");
            RequireApproximately(result.MeanNearestBackgroundDistance, 0.85, 1e-12,
                "The mean nearest distance is incorrect.");
        }

        private static void TestGuardsAndCancellation()
        {
            var current = new[] { Point(0.0, 0.0, 0.0) };
            var background = new[] { Point(1.0, 0.0, 0.0) };
            var tool = new PointCloudBackgroundFilterTool();
            var invalidThreshold = tool.Execute(
                current,
                background,
                new PointCloudBackgroundFilterOptions { MaximumBackgroundDistance = double.NaN });
            var emptyBackground = tool.Execute(
                current,
                Array.Empty<ThreeDPoint>(),
                new PointCloudBackgroundFilterOptions { MaximumBackgroundDistance = 0.5 });
            var nonFinite = tool.Execute(
                new[] { Point(double.PositiveInfinity, 0.0, 0.0) },
                background,
                new PointCloudBackgroundFilterOptions { MaximumBackgroundDistance = 0.5 });

            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var canceled = false;
            try
            {
                _ = tool.Execute(
                    current,
                    background,
                    new PointCloudBackgroundFilterOptions { MaximumBackgroundDistance = 0.5 },
                    cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                canceled = true;
            }

            Require(!invalidThreshold.Success && !emptyBackground.Success && !nonFinite.Success,
                "Invalid point-cloud filter inputs must fail without a result.");
            Require(invalidThreshold.RetainedPoints.Count == 0
                && emptyBackground.RetainedPoints.Count == 0
                && nonFinite.RetainedPoints.Count == 0,
                "Rejected point-cloud filter inputs must not retain output points.");
            Require(canceled, "Point-cloud filtering must honor cancellation before processing.");
        }

        private static ThreeDPoint Point(double x, double y, double z) =>
            new ThreeDPoint(x, y, z);
    }
}

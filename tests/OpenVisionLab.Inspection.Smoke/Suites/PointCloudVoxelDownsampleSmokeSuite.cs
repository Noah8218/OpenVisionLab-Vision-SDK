using OpenVisionLab.Vision3D.FeatureExtraction;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class PointCloudVoxelDownsampleSmokeSuite
    {
        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase(
                "Point-cloud voxel reduction keeps first representatives and source order",
                TestReductionAndBounds);
            yield return new SmokeCase(
                "Point-cloud voxel reduction is deterministic and honors guards",
                TestDeterminismAndGuards);
        }

        private static void TestReductionAndBounds()
        {
            var points = new[]
            {
                Point(0.1, 0.1, 0.1),
                Point(0.9, 0.2, 0.4),
                Point(1.0, 0.0, 0.0),
                Point(-0.1, 0.0, 0.0),
                Point(2.1, 2.1, 2.1)
            };
            var result = new PointCloudVoxelDownsampleTool().Execute(
                points,
                new PointCloudVoxelDownsampleOptions
                {
                    VoxelEdgeLength = 1.0,
                    OriginX = 0.0,
                    OriginY = 0.0,
                    OriginZ = 0.0
                });

            Require(result.Success, "A finite point cloud with a positive edge must reduce successfully.");
            Require(result.InputPointCount == 5 && result.OutputPointCount == 4 && result.ReducedPointCount == 1,
                "Voxel reduction counts are incorrect.");
            Require(result.Representatives.Select(item => item.SourceIndex).SequenceEqual(new[] { 0, 2, 3, 4 }),
                "Voxel representatives must preserve first-source appearance order.");
            Require(result.Representatives[0].VoxelX == 0 && result.Representatives[0].VoxelY == 0 && result.Representatives[0].VoxelZ == 0,
                "The first representative voxel index is incorrect.");
            Require(result.Representatives[2].VoxelX == -1,
                "Negative coordinates must use floor-based voxel indexing.");
            Require(result.Representatives[0].Point.X == 0.1 && result.Representatives[0].Point.Y == 0.1,
                "The first source point must represent its occupied voxel without averaging.");
            RequireApproximately(result.InputBounds.MinimumX, -0.1, 1e-12,
                "Input minimum X bound is incorrect.");
            RequireApproximately(result.OutputBounds.MaximumZ, 2.1, 1e-12,
                "Output maximum Z bound is incorrect.");
        }

        private static void TestDeterminismAndGuards()
        {
            var points = new[]
            {
                Point(-2.0, 1.0, 0.0),
                Point(-1.1, 1.2, 0.0),
                Point(3.0, 4.0, 5.0)
            };
            var options = new PointCloudVoxelDownsampleOptions
            {
                VoxelEdgeLength = 0.5,
                OriginX = -2.0,
                OriginY = 1.0,
                OriginZ = 0.0
            };
            var first = new PointCloudVoxelDownsampleTool().Execute(points, options);
            var second = new PointCloudVoxelDownsampleTool().Execute(points, options);
            Require(first.Success && second.Success, "Repeated finite voxel reductions must succeed.");
            Require(first.Representatives.Count == second.Representatives.Count
                && first.Representatives.Zip(second.Representatives, (left, right) =>
                    left.SourceIndex == right.SourceIndex
                    && left.VoxelX == right.VoxelX
                    && left.VoxelY == right.VoxelY
                    && left.VoxelZ == right.VoxelZ
                    && left.Point.X == right.Point.X
                    && left.Point.Y == right.Point.Y
                    && left.Point.Z == right.Point.Z).All(value => value),
                "Repeated voxel reductions must produce identical representatives.");

            var tool = new PointCloudVoxelDownsampleTool();
            var empty = tool.Execute(
                Array.Empty<ThreeDPoint>(),
                new PointCloudVoxelDownsampleOptions { VoxelEdgeLength = 1.0 });
            var zeroEdge = tool.Execute(
                points,
                new PointCloudVoxelDownsampleOptions { VoxelEdgeLength = 0.0 });
            var nonFiniteOrigin = tool.Execute(
                points,
                new PointCloudVoxelDownsampleOptions { VoxelEdgeLength = 1.0, OriginX = double.NaN });
            var nonFinitePoint = tool.Execute(
                new[] { Point(double.PositiveInfinity, 0.0, 0.0) },
                new PointCloudVoxelDownsampleOptions { VoxelEdgeLength = 1.0 });
            var overflowingIndex = tool.Execute(
                points,
                new PointCloudVoxelDownsampleOptions
                {
                    VoxelEdgeLength = 1e-300,
                    OriginX = -1e300
                });

            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var canceled = false;
            try
            {
                _ = tool.Execute(points, options, cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                canceled = true;
            }

            Require(!empty.Success && !zeroEdge.Success && !nonFiniteOrigin.Success
                && !nonFinitePoint.Success && !overflowingIndex.Success,
                "Invalid voxel reduction inputs must fail without a result.");
            Require(canceled, "Voxel reduction must honor cancellation before processing.");
        }

        private static ThreeDPoint Point(double x, double y, double z) =>
            new ThreeDPoint(x, y, z);
    }
}

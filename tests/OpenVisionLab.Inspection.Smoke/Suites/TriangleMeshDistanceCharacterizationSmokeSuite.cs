using OpenVisionLab.Vision3D.FeatureExtraction;
using System;
using System.Collections.Generic;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class TriangleMeshDistanceCharacterizationSmokeSuite
    {
        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase(
                "Triangle-mesh distance characterizes face, edge, and vertex evidence",
                TestClosestFeatureEvidence);
            yield return new SmokeCase(
                "Triangle-mesh distance characterizes cross-BVH exact ties",
                TestCrossBvhTieAndInputOrder);
            yield return new SmokeCase(
                "Triangle-mesh robust sign characterizes epsilon and boundary ranking",
                TestRobustSignCandidateRanking);
            yield return new SmokeCase(
                "Triangle-mesh distance characterizes invalid contracts",
                TestInvalidContracts);
        }

        private static void TestClosestFeatureEvidence()
        {
            TriangleMeshDistanceTool tool = new TriangleMeshDistanceTool(
                new[]
                {
                    new MeshTriangle(
                        7,
                        Point(0.0, 0.0, 0.0),
                        Point(2.0, 0.0, 0.0),
                        Point(0.0, 2.0, 0.0))
                });

            PointMeshDistance face = tool.Execute(Point(0.5, 0.5, 1.0));
            PointMeshDistance edge = tool.Execute(Point(1.0, -1.0, 1.0));
            PointMeshDistance robustEdge = tool.ExecuteRobustSign(
                Point(1.0, -1.0, 1.0),
                edge.UnsignedDistance);
            PointMeshDistance vertex = tool.Execute(Point(-1.0, -1.0, -2.0));
            PointMeshDistance robustVertex = tool.ExecuteRobustSign(
                Point(-1.0, -1.0, -2.0),
                vertex.UnsignedDistance);

            Require(tool.TriangleCount == 1,
                "The characterization mesh must retain its triangle count.");
            RequireDistanceEvidence(
                face,
                7,
                MeshClosestFeature.FaceInterior,
                Point(0.5, 0.5, 0.0),
                Point(0.0, 0.0, 1.0),
                1.0,
                1.0,
                true,
                "face");
            RequireDistanceEvidence(
                edge,
                7,
                MeshClosestFeature.Edge,
                Point(1.0, 0.0, 0.0),
                Point(0.0, 0.0, 1.0),
                Math.Sqrt(2.0),
                null,
                false,
                "edge");
            RequireDistanceEvidence(
                robustEdge,
                7,
                MeshClosestFeature.Edge,
                Point(1.0, 0.0, 0.0),
                Point(0.0, 0.0, 1.0),
                Math.Sqrt(2.0),
                Math.Sqrt(2.0),
                true,
                "robust edge");
            RequireDistanceEvidence(
                vertex,
                7,
                MeshClosestFeature.Vertex,
                Point(0.0, 0.0, 0.0),
                Point(0.0, 0.0, 1.0),
                Math.Sqrt(6.0),
                null,
                false,
                "vertex");
            RequireDistanceEvidence(
                robustVertex,
                7,
                MeshClosestFeature.Vertex,
                Point(0.0, 0.0, 0.0),
                Point(0.0, 0.0, 1.0),
                Math.Sqrt(6.0),
                -Math.Sqrt(6.0),
                true,
                "robust vertex");
        }

        private static void TestCrossBvhTieAndInputOrder()
        {
            MeshTriangle[] triangles =
            {
                XPlane(100, -100.0),
                XPlane(101, -90.0),
                XPlane(102, -80.0),
                XPlane(90, -1.0),
                XPlane(10, 1.0),
                XPlane(103, 80.0),
                XPlane(104, 90.0),
                XPlane(105, 100.0),
                XPlane(106, 110.0)
            };
            ThreeDPoint query = Point(0.0, -0.25, -0.25);

            PointMeshDistance forward =
                new TriangleMeshDistanceTool(triangles).Execute(query);
            Array.Reverse(triangles);
            PointMeshDistance reversed =
                new TriangleMeshDistanceTool(triangles).Execute(query);

            RequireDistanceEvidence(
                forward,
                10,
                MeshClosestFeature.FaceInterior,
                Point(1.0, -0.25, -0.25),
                Point(1.0, 0.0, 0.0),
                1.0,
                -1.0,
                true,
                "forward cross-BVH tie");
            RequireDistanceEvidence(
                reversed,
                10,
                MeshClosestFeature.FaceInterior,
                Point(1.0, -0.25, -0.25),
                Point(1.0, 0.0, 0.0),
                1.0,
                -1.0,
                true,
                "reversed cross-BVH tie");
        }

        private static void TestRobustSignCandidateRanking()
        {
            double epsilon = TriangleMeshDistanceTool.RobustSignDistanceEpsilon;
            ThreeDPoint origin = Point(0.0, 0.0, 0.0);
            MeshTriangle boundary = new MeshTriangle(
                70,
                Point(-1.0, 0.0, 1.0),
                Point(1.0, 0.0, 1.0),
                Point(0.0, 2.0, 1.0));
            MeshTriangle inclusiveInterior = HorizontalInterior(
                30,
                1.0 + epsilon);
            TriangleMeshDistanceTool inclusiveTool =
                new TriangleMeshDistanceTool(
                    new[] { boundary, inclusiveInterior });
            PointMeshDistance nearest = inclusiveTool.Execute(origin);
            PointMeshDistance inclusive = inclusiveTool.ExecuteRobustSign(
                origin,
                nearest.UnsignedDistance);

            Require(nearest.SourceTriangleIndex == 70
                    && nearest.ClosestFeature == MeshClosestFeature.Edge
                    && !nearest.SignResolved,
                "The nearest boundary must remain unresolved before robust recovery.");
            RequireDistanceEvidence(
                inclusive,
                30,
                MeshClosestFeature.FaceInterior,
                Point(0.0, 0.0, 1.0 + epsilon),
                Point(0.0, 0.0, 1.0),
                1.0 + epsilon,
                -(1.0 + epsilon),
                true,
                "inclusive robust epsilon");

            TriangleMeshDistanceTool excludedTool =
                new TriangleMeshDistanceTool(
                    new[]
                    {
                        boundary,
                        HorizontalInterior(31, 1.0 + 2.0 * epsilon)
                    });
            PointMeshDistance excluded = excludedTool.ExecuteRobustSign(
                origin,
                1.0);
            RequireDistanceEvidence(
                excluded,
                70,
                MeshClosestFeature.Edge,
                Point(0.0, 0.0, 1.0),
                Point(0.0, 0.0, 1.0),
                1.0,
                -1.0,
                true,
                "excluded robust epsilon");

            ThreeDPoint boundaryQuery = Point(1.0, -1.0, 0.0);
            MeshTriangle lowOrthogonality = new MeshTriangle(
                10,
                Point(0.0, 0.0, 0.0),
                Point(2.0, 0.0, 0.0),
                Point(0.0, 2.0, 0.0));
            MeshTriangle highOrthogonality = VerticalBoundary(90, epsilon);
            TriangleMeshDistanceTool orthogonalityTool =
                new TriangleMeshDistanceTool(
                    new[] { lowOrthogonality, highOrthogonality });
            PointMeshDistance orthogonalityWinner =
                orthogonalityTool.ExecuteRobustSign(boundaryQuery, 1.0);
            RequireDistanceEvidence(
                orthogonalityWinner,
                90,
                MeshClosestFeature.Edge,
                Point(1.0, epsilon, 0.0),
                Point(0.0, -1.0, 0.0),
                1.0 + epsilon,
                1.0 + epsilon,
                true,
                "boundary orthogonality tie");

            TriangleMeshDistanceTool sourceTieTool =
                new TriangleMeshDistanceTool(
                    new[]
                    {
                        VerticalBoundary(91, epsilon),
                        VerticalBoundary(12, epsilon)
                    });
            PointMeshDistance sourceTie = sourceTieTool.ExecuteRobustSign(
                boundaryQuery,
                1.0 + epsilon);
            Require(sourceTie.SourceTriangleIndex == 12
                    && sourceTie.ClosestFeature == MeshClosestFeature.Edge
                    && sourceTie.SignResolved,
                "An exact robust boundary tie must choose the lower source index.");
        }

        private static void TestInvalidContracts()
        {
            ArgumentNullException nullSource = CaptureException<ArgumentNullException>(
                () => new TriangleMeshDistanceTool(null));
            ArgumentException emptySource = CaptureException<ArgumentException>(
                () => new TriangleMeshDistanceTool(Array.Empty<MeshTriangle>()));
            ArgumentNullException nullTriangle =
                CaptureException<ArgumentNullException>(
                    () => new TriangleMeshDistanceTool(
                        new MeshTriangle[] { null }));
            ArgumentException degenerate = CaptureException<ArgumentException>(
                () => new TriangleMeshDistanceTool(
                    new[]
                    {
                        new MeshTriangle(
                            1,
                            Point(0.0, 0.0, 0.0),
                            Point(1.0, 1.0, 1.0),
                            Point(2.0, 2.0, 2.0))
                    }));
            ArgumentException nonFiniteTriangle =
                CaptureException<ArgumentException>(
                    () => new TriangleMeshDistanceTool(
                        new[]
                        {
                            new MeshTriangle(
                                2,
                                Point(double.NaN, 0.0, 0.0),
                                Point(1.0, 0.0, 0.0),
                                Point(0.0, 1.0, 0.0))
                        }));

            TriangleMeshDistanceTool tool = new TriangleMeshDistanceTool(
                new[]
                {
                    new MeshTriangle(
                        3,
                        Point(0.0, 0.0, 1.0),
                        Point(2.0, 0.0, 1.0),
                        Point(0.0, 2.0, 1.0))
                });
            ArgumentException nonFiniteQuery = CaptureException<ArgumentException>(
                () => tool.Execute(Point(double.PositiveInfinity, 0.0, 0.0)));
            ArgumentOutOfRangeException negativeNearest =
                CaptureException<ArgumentOutOfRangeException>(
                    () => tool.ExecuteRobustSign(Point(0.0, 0.0, 0.0), -1.0));
            ArgumentOutOfRangeException nanNearest =
                CaptureException<ArgumentOutOfRangeException>(
                    () => tool.ExecuteRobustSign(
                        Point(0.0, 0.0, 0.0),
                        double.NaN));
            InvalidOperationException noCandidate =
                CaptureException<InvalidOperationException>(
                    () => tool.ExecuteRobustSign(Point(0.0, 0.0, 0.0), 0.0));

            Require(nullSource.ParamName == "source"
                    && emptySource.ParamName == "source"
                    && nullTriangle.ParamName == "triangle"
                    && degenerate.ParamName == "triangle"
                    && nonFiniteTriangle.ParamName == "triangle"
                    && nonFiniteQuery.ParamName == "point"
                    && negativeNearest.ParamName == "nearestUnsignedDistance"
                    && nanNearest.ParamName == "nearestUnsignedDistance"
                    && noCandidate != null,
                "Invalid contracts must preserve exception types and parameter names.");
        }

        private static MeshTriangle XPlane(long sourceTriangleIndex, double x)
        {
            return new MeshTriangle(
                sourceTriangleIndex,
                Point(x, -1.0, -1.0),
                Point(x, 1.0, -1.0),
                Point(x, -1.0, 1.0));
        }

        private static MeshTriangle HorizontalInterior(
            long sourceTriangleIndex,
            double z)
        {
            return new MeshTriangle(
                sourceTriangleIndex,
                Point(-1.0, -1.0, z),
                Point(1.0, -1.0, z),
                Point(0.0, 1.0, z));
        }

        private static MeshTriangle VerticalBoundary(
            long sourceTriangleIndex,
            double y)
        {
            return new MeshTriangle(
                sourceTriangleIndex,
                Point(0.0, y, 0.0),
                Point(2.0, y, 0.0),
                Point(0.0, y, 2.0));
        }

        private static ThreeDPoint Point(double x, double y, double z)
        {
            return new ThreeDPoint(x, y, z);
        }

        private static void RequireDistanceEvidence(
            PointMeshDistance actual,
            long sourceTriangleIndex,
            MeshClosestFeature feature,
            ThreeDPoint closestPoint,
            ThreeDPoint triangleNormal,
            double unsignedDistance,
            double? signedDistance,
            bool signResolved,
            string label)
        {
            Require(actual.SourceTriangleIndex == sourceTriangleIndex
                    && actual.ClosestFeature == feature
                    && actual.SignResolved == signResolved,
                "Unexpected " + label + " identity, feature, or sign state.");
            RequirePoint(actual.ClosestPoint, closestPoint, label + " closest point");
            RequirePoint(actual.TriangleNormal, triangleNormal, label + " normal");
            RequireApproximately(
                actual.UnsignedDistance,
                unsignedDistance,
                1e-12,
                "Unexpected " + label + " unsigned distance.");
            Require(actual.SignedDistance.HasValue == signedDistance.HasValue,
                "Unexpected " + label + " signed-distance availability.");
            if (signedDistance.HasValue)
            {
                RequireApproximately(
                    actual.SignedDistance.Value,
                    signedDistance.Value,
                    1e-12,
                    "Unexpected " + label + " signed distance.");
            }
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
                    "The invalid contract returned an unexpected derived exception type.");
                return exception;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "The invalid contract returned "
                    + exception.GetType().FullName
                    + " instead of "
                    + typeof(TException).FullName
                    + ".",
                    exception);
            }

            throw new InvalidOperationException(
                "The invalid contract did not throw "
                + typeof(TException).FullName
                + ".");
        }
    }
}

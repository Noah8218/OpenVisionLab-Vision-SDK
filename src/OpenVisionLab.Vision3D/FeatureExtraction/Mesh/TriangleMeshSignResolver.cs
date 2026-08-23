using System;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    internal static class TriangleMeshSignResolver
    {
        internal static SignResolution ResolveDirect(
            Vector3 query,
            TriangleMeshBvhIndex.TriangleEntry triangle,
            TriangleClosestPointKernel.ClosestPointResult closest,
            double unsignedDistance)
        {
            double? signedDistance = null;
            bool signResolved =
                closest.Feature == MeshClosestFeature.FaceInterior;
            if (signResolved)
            {
                double side = Vector3.Dot(
                    query - closest.Point,
                    triangle.Normal);
                if (unsignedDistance == 0.0)
                {
                    signedDistance = 0.0;
                }
                else if (side == 0.0)
                {
                    signResolved = false;
                }
                else
                {
                    signedDistance = side < 0.0
                        ? -unsignedDistance
                        : unsignedDistance;
                }
            }

            return new SignResolution(signedDistance, signResolved);
        }

        internal static RobustResolution ResolveRobust(
            TriangleMeshBvhIndex index,
            Vector3 query,
            double nearestUnsignedDistance,
            double distanceEpsilon)
        {
            double maximumCandidateDistance =
                nearestUnsignedDistance + distanceEpsilon;
            RobustSearchState state = new RobustSearchState(
                query,
                maximumCandidateDistance,
                distanceEpsilon);
            index.VisitBoundsCandidates(
                query,
                state.MaximumDistanceSquared,
                state.ConsiderTriangle);
            RobustCandidate selected = state.BestInterior
                ?? state.BestBoundary;
            if (selected == null)
            {
                throw new InvalidOperationException(
                    "No robust sign candidate was found within the nearest-distance tolerance.");
            }

            double side = Vector3.Dot(
                query - selected.Closest.Point,
                selected.Triangle.Normal);
            double signedDistance = selected.Distance == 0.0
                ? 0.0
                : side < 0.0
                    ? -selected.Distance
                    : selected.Distance;
            return new RobustResolution(
                selected.Triangle,
                selected.Closest,
                selected.Distance,
                signedDistance);
        }

        private static void ConsiderCandidate(
            Vector3 query,
            TriangleMeshBvhIndex.TriangleEntry triangle,
            RobustSearchState state)
        {
            TriangleClosestPointKernel.ClosestPointResult closest =
                TriangleClosestPointKernel.Find(
                    query,
                    triangle.A,
                    triangle.B,
                    triangle.C);
            double distance = Math.Sqrt(
                Math.Max(
                    0.0,
                    Vector3.DistanceSquared(query, closest.Point)));
            if (distance > state.MaximumDistance)
            {
                return;
            }

            double orthogonality = distance == 0.0
                ? 1.0
                : Math.Min(
                    1.0,
                    Math.Abs(
                        Vector3.Dot(
                            query - closest.Point,
                            triangle.Normal)) / distance);
            state.Consider(
                new RobustCandidate(
                    triangle,
                    closest,
                    distance,
                    orthogonality));
        }

        internal struct SignResolution
        {
            internal SignResolution(
                double? signedDistance,
                bool signResolved)
            {
                SignedDistance = signedDistance;
                SignResolved = signResolved;
            }

            internal double? SignedDistance { get; }
            internal bool SignResolved { get; }
        }

        internal struct RobustResolution
        {
            internal RobustResolution(
                TriangleMeshBvhIndex.TriangleEntry triangle,
                TriangleClosestPointKernel.ClosestPointResult closest,
                double distance,
                double signedDistance)
            {
                Triangle = triangle;
                Closest = closest;
                Distance = distance;
                SignedDistance = signedDistance;
            }

            internal TriangleMeshBvhIndex.TriangleEntry Triangle { get; }
            internal TriangleClosestPointKernel.ClosestPointResult Closest { get; }
            internal double Distance { get; }
            internal double SignedDistance { get; }
        }

        private sealed class RobustCandidate
        {
            internal RobustCandidate(
                TriangleMeshBvhIndex.TriangleEntry triangle,
                TriangleClosestPointKernel.ClosestPointResult closest,
                double distance,
                double orthogonality)
            {
                Triangle = triangle;
                Closest = closest;
                Distance = distance;
                Orthogonality = orthogonality;
            }

            internal TriangleMeshBvhIndex.TriangleEntry Triangle { get; }
            internal TriangleClosestPointKernel.ClosestPointResult Closest { get; }
            internal double Distance { get; }
            internal double Orthogonality { get; }
        }

        private sealed class RobustSearchState
        {
            private readonly Vector3 query;
            private readonly double distanceEpsilon;

            internal RobustSearchState(
                Vector3 query,
                double maximumDistance,
                double distanceEpsilon)
            {
                this.query = query;
                MaximumDistance = maximumDistance;
                MaximumDistanceSquared = maximumDistance * maximumDistance;
                this.distanceEpsilon = distanceEpsilon;
            }

            internal double MaximumDistance { get; }
            internal double MaximumDistanceSquared { get; }
            internal RobustCandidate BestInterior { get; private set; }
            internal RobustCandidate BestBoundary { get; private set; }

            internal void ConsiderTriangle(
                TriangleMeshBvhIndex.TriangleEntry triangle)
            {
                ConsiderCandidate(query, triangle, this);
            }

            internal void Consider(RobustCandidate candidate)
            {
                if (candidate.Closest.Feature
                    == MeshClosestFeature.FaceInterior)
                {
                    if (BestInterior == null
                        || candidate.Distance < BestInterior.Distance
                        || candidate.Distance == BestInterior.Distance
                        && candidate.Triangle.Source.SourceTriangleIndex
                            < BestInterior.Triangle.Source.SourceTriangleIndex)
                    {
                        BestInterior = candidate;
                    }

                    return;
                }

                if (BestBoundary == null)
                {
                    BestBoundary = candidate;
                    return;
                }

                double distanceDifference =
                    candidate.Distance - BestBoundary.Distance;
                if (Math.Abs(distanceDifference) <= distanceEpsilon)
                {
                    if (candidate.Orthogonality
                            > BestBoundary.Orthogonality
                        || candidate.Orthogonality
                            == BestBoundary.Orthogonality
                        && candidate.Triangle.Source.SourceTriangleIndex
                            < BestBoundary.Triangle.Source.SourceTriangleIndex)
                    {
                        BestBoundary = candidate;
                    }
                }
                else if (distanceDifference < 0.0)
                {
                    BestBoundary = candidate;
                }
            }
        }
    }
}

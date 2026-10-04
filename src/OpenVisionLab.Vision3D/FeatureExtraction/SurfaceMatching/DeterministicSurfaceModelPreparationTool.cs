using System;
using System.Collections.Generic;
using System.Threading;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    public sealed class SurfaceModelTriangleInput
    {
        public SurfaceModelTriangleInput(
            int firstPointIndex,
            int secondPointIndex,
            int thirdPointIndex)
        {
            FirstPointIndex = firstPointIndex;
            SecondPointIndex = secondPointIndex;
            ThirdPointIndex = thirdPointIndex;
        }

        public int FirstPointIndex { get; }
        public int SecondPointIndex { get; }
        public int ThirdPointIndex { get; }
    }

    public sealed class PreparedSurfaceModelSample
    {
        public PreparedSurfaceModelSample(
            int order,
            int sourceTriangleIndex,
            ThreeDPoint position,
            ThreeDPoint normal)
        {
            Order = order;
            SourceTriangleIndex = sourceTriangleIndex;
            Position = position;
            Normal = normal;
        }

        public int Order { get; }
        public int SourceTriangleIndex { get; }
        public ThreeDPoint Position { get; }
        public ThreeDPoint Normal { get; }
    }

    public sealed class DeterministicSurfaceModelPreparationOptions
    {
        public int MaximumSampleCount { get; set; }
    }

    public sealed class DeterministicSurfaceModelPreparationResult
    {
        private DeterministicSurfaceModelPreparationResult(
            bool success,
            string message,
            IReadOnlyList<PreparedSurfaceModelSample> samples)
        {
            Success = success;
            Message = message ?? string.Empty;
            Samples = samples ?? Array.Empty<PreparedSurfaceModelSample>();
        }

        public bool Success { get; }
        public string Message { get; }
        public IReadOnlyList<PreparedSurfaceModelSample> Samples { get; }

        internal static DeterministicSurfaceModelPreparationResult Completed(
            IReadOnlyList<PreparedSurfaceModelSample> samples)
        {
            return new DeterministicSurfaceModelPreparationResult(
                true,
                string.Empty,
                samples);
        }

        internal static DeterministicSurfaceModelPreparationResult Failed(
            string message)
        {
            return new DeterministicSurfaceModelPreparationResult(
                false,
                message,
                Array.Empty<PreparedSurfaceModelSample>());
        }
    }

    /// <summary>
    /// Selects a deterministic even-index triangle subset and derives one
    /// centroid and declared-normal average per selected triangle. Source
    /// identity, unit, frame, normal-quality admission, and persistence stay
    /// with the caller.
    /// </summary>
    public sealed class DeterministicSurfaceModelPreparationTool
    {
        public const string Semantics =
            "deterministic-triangle-centroid-even-index-v1";

        public DeterministicSurfaceModelPreparationResult Execute(
            IReadOnlyList<ThreeDPoint> points,
            IReadOnlyList<SurfaceModelTriangleInput> triangles,
            IReadOnlyList<ThreeDPoint> declaredNormals,
            DeterministicSurfaceModelPreparationOptions options,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Validate(points, triangles, declaredNormals, options, cancellationToken);
                int sampleCount = Math.Min(
                    options.MaximumSampleCount,
                    triangles.Count);
                PreparedSurfaceModelSample[] samples =
                    new PreparedSurfaceModelSample[sampleCount];
                for (int sampleOrder = 0;
                     sampleOrder < sampleCount;
                     sampleOrder++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int triangleIndex = EvenIndex(
                        sampleOrder,
                        sampleCount,
                        triangles.Count);
                    SurfaceModelTriangleInput triangle = triangles[triangleIndex];
                    ThreeDPoint first = points[triangle.FirstPointIndex];
                    ThreeDPoint second = points[triangle.SecondPointIndex];
                    ThreeDPoint third = points[triangle.ThirdPointIndex];
                    ThreeDPoint centroid = new ThreeDPoint(
                        AverageCoordinate(first.X, second.X, third.X),
                        AverageCoordinate(first.Y, second.Y, third.Y),
                        AverageCoordinate(first.Z, second.Z, third.Z));
                    ThreeDPoint normal = AverageDeclaredNormal(
                        declaredNormals[triangle.FirstPointIndex],
                        declaredNormals[triangle.SecondPointIndex],
                        declaredNormals[triangle.ThirdPointIndex]);
                    samples[sampleOrder] = new PreparedSurfaceModelSample(
                        sampleOrder,
                        triangleIndex,
                        centroid,
                        normal);
                }

                cancellationToken.ThrowIfCancellationRequested();
                return DeterministicSurfaceModelPreparationResult.Completed(
                    samples);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return DeterministicSurfaceModelPreparationResult.Failed(
                    "Deterministic surface-model preparation failed: "
                    + exception.Message);
            }
        }

        private static void Validate(
            IReadOnlyList<ThreeDPoint> points,
            IReadOnlyList<SurfaceModelTriangleInput> triangles,
            IReadOnlyList<ThreeDPoint> declaredNormals,
            DeterministicSurfaceModelPreparationOptions options,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (points == null || points.Count == 0)
            {
                throw new ArgumentException(
                    "Surface-model preparation requires finite source points.");
            }

            if (triangles == null || triangles.Count == 0)
            {
                throw new ArgumentException(
                    "Surface-model preparation requires source triangles.");
            }

            if (declaredNormals == null
                || declaredNormals.Count != points.Count)
            {
                throw new ArgumentException(
                    "Surface-model preparation requires one declared normal per point.");
            }

            if (options == null || options.MaximumSampleCount <= 0)
            {
                throw new ArgumentException(
                    "Surface-model preparation requires a positive maximum sample count.");
            }

            for (int pointIndex = 0; pointIndex < points.Count; pointIndex++)
            {
                if ((pointIndex & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                if (points[pointIndex] == null
                    || !points[pointIndex].IsFinite
                    || declaredNormals[pointIndex] == null
                    || !declaredNormals[pointIndex].IsFinite)
                {
                    throw new ArgumentException(
                        "Surface-model preparation requires finite points and declared normals.");
                }
            }

            for (int triangleIndex = 0;
                 triangleIndex < triangles.Count;
                 triangleIndex++)
            {
                if ((triangleIndex & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                SurfaceModelTriangleInput triangle = triangles[triangleIndex];
                if (triangle == null
                    || triangle.FirstPointIndex < 0
                    || triangle.SecondPointIndex < 0
                    || triangle.ThirdPointIndex < 0
                    || triangle.FirstPointIndex >= points.Count
                    || triangle.SecondPointIndex >= points.Count
                    || triangle.ThirdPointIndex >= points.Count)
                {
                    throw new ArgumentException(
                        "Surface-model triangles must reference existing zero-based points.");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        private static double AverageCoordinate(double first, double second, double third)
        {
            double mean = (first + second + third) / 3.0;
            if (SurfaceMatchingContractValidation.IsFinite(mean)) return mean;

            double scale = Math.Max(Math.Abs(first), Math.Max(Math.Abs(second), Math.Abs(third)));
            mean = ((first / scale + second / scale + third / scale) / 3.0) * scale;
            if (!SurfaceMatchingContractValidation.IsFinite(mean))
                throw new ArgumentException("Selected triangle centroid must be finite.");
            return mean;
        }

        private static int EvenIndex(
            int sampleOrder,
            int sampleCount,
            int sourceCount)
        {
            return checked((int)(
                ((long)sampleOrder * 2L + 1L)
                * sourceCount
                / (sampleCount * 2L)));
        }

        private static ThreeDPoint AverageDeclaredNormal(
            ThreeDPoint first,
            ThreeDPoint second,
            ThreeDPoint third)
        {
            // Imported mesh normals are single-precision. Keeping Vector3
            // arithmetic here preserves the established persisted-artifact
            // contract while exposing source-neutral doubles at the boundary.
            float x = (float)first.X
                + (float)second.X
                + (float)third.X;
            float y = (float)first.Y
                + (float)second.Y
                + (float)third.Y;
            float z = (float)first.Z
                + (float)second.Z
                + (float)third.Z;
            float squaredLength = x * x + y * y + z * z;
            float length = (float)Math.Sqrt(squaredLength);
            float normalizedX = x / length;
            float normalizedY = y / length;
            float normalizedZ = z / length;
            if (float.IsNaN(normalizedX)
                || float.IsInfinity(normalizedX)
                || float.IsNaN(normalizedY)
                || float.IsInfinity(normalizedY)
                || float.IsNaN(normalizedZ)
                || float.IsInfinity(normalizedZ))
            {
                throw new ArgumentException(
                    "Selected triangle declared normals must have a non-zero finite sum.");
            }

            return new ThreeDPoint(
                normalizedX,
                normalizedY,
                normalizedZ);
        }
    }
}

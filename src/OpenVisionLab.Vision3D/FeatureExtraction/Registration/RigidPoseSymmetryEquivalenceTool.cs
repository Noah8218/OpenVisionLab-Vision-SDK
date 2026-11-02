using System;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    public enum RigidPoseSymmetryKind
    {
        None,
        DiscreteRotation
    }

    public enum RigidPoseSymmetryAxis
    {
        None,
        X,
        Y,
        Z
    }

    /// <summary>
    /// Source-neutral model-space symmetry declaration. A discrete rotation
    /// is applied after the reference model-to-scene rotation.
    /// </summary>
    public sealed class RigidPoseSymmetry
    {
        public RigidPoseSymmetry(
            RigidPoseSymmetryKind kind,
            RigidPoseSymmetryAxis axis,
            int order)
        {
            Kind = kind;
            Axis = axis;
            Order = order;
        }

        public RigidPoseSymmetryKind Kind { get; }

        public RigidPoseSymmetryAxis Axis { get; }

        public int Order { get; }
    }

    public sealed class RigidPoseSymmetryEquivalenceOptions
    {
        public RigidPoseSymmetry Symmetry { get; set; }

        public double MaximumTranslationDifference { get; set; }

        public double MaximumRotationDifferenceDegrees { get; set; }

        public double RigidTransformTolerance { get; set; }
    }

    public sealed class RigidPoseSymmetryEquivalenceResult
    {
        private RigidPoseSymmetryEquivalenceResult(
            bool success,
            string message,
            bool equivalent,
            int symmetryOperationIndex,
            double symmetryOperationAngleDegrees,
            double translationDifference,
            double rotationDifferenceDegrees)
        {
            Success = success;
            Message = message ?? string.Empty;
            Equivalent = equivalent;
            SymmetryOperationIndex = symmetryOperationIndex;
            SymmetryOperationAngleDegrees = symmetryOperationAngleDegrees;
            TranslationDifference = translationDifference;
            RotationDifferenceDegrees = rotationDifferenceDegrees;
        }

        public bool Success { get; }

        public string Message { get; }

        public bool Equivalent { get; }

        public int SymmetryOperationIndex { get; }

        public double SymmetryOperationAngleDegrees { get; }

        public double TranslationDifference { get; }

        public double RotationDifferenceDegrees { get; }

        internal static RigidPoseSymmetryEquivalenceResult Completed(
            bool equivalent,
            int symmetryOperationIndex,
            double symmetryOperationAngleDegrees,
            double translationDifference,
            double rotationDifferenceDegrees)
        {
            return new RigidPoseSymmetryEquivalenceResult(
                true,
                string.Empty,
                equivalent,
                symmetryOperationIndex,
                symmetryOperationAngleDegrees,
                translationDifference,
                rotationDifferenceDegrees);
        }

        internal static RigidPoseSymmetryEquivalenceResult Failed(
            string message)
        {
            return new RigidPoseSymmetryEquivalenceResult(
                false,
                message,
                false,
                0,
                0.0,
                double.NaN,
                double.NaN);
        }
    }

    /// <summary>
    /// Compares two model-to-scene rigid poses under a declared cyclic
    /// model-space rotation group. Translation is compared at the model
    /// origin; rotation uses the nearest declared group operation.
    /// </summary>
    public sealed class RigidPoseSymmetryEquivalenceTool
    {
        public RigidPoseSymmetryEquivalenceResult Execute(
            RigidSurfacePose referencePose,
            RigidSurfacePose candidatePose,
            RigidPoseSymmetryEquivalenceOptions options)
        {
            try
            {
                Validate(referencePose, candidatePose, options);

                double translationDifference = SurfaceSceneIndex.Distance(
                    new ThreeDPoint(referencePose.TranslationX, referencePose.TranslationY, referencePose.TranslationZ),
                    new ThreeDPoint(candidatePose.TranslationX, candidatePose.TranslationY, candidatePose.TranslationZ));

                RelativeRotation relative = Relative(
                    referencePose,
                    candidatePose);
                int operationIndex = NearestOperationIndex(
                    relative,
                    options.Symmetry);
                double operationAngleDegrees =
                    360.0 * operationIndex / options.Symmetry.Order;
                double operationAngleRadians =
                    operationAngleDegrees * Math.PI / 180.0;
                double trace = OperationTrace(
                    relative,
                    options.Symmetry.Axis,
                    operationAngleRadians);
                double cosine = (trace - 1.0) / 2.0;
                cosine = cosine < -1.0
                    ? -1.0
                    : cosine > 1.0
                        ? 1.0
                        : cosine;
                double rotationDifferenceDegrees =
                    Math.Acos(cosine) * 180.0 / Math.PI;
                if (!SurfaceMatchingContractValidation.IsFinite(translationDifference)
                    || !SurfaceMatchingContractValidation.IsFinite(operationAngleDegrees)
                    || !SurfaceMatchingContractValidation.IsFinite(trace)
                    || !SurfaceMatchingContractValidation.IsFinite(rotationDifferenceDegrees))
                {
                    return RigidPoseSymmetryEquivalenceResult.Failed(
                        "Rigid pose symmetry equivalence produced non-finite comparison evidence.");
                }

                bool equivalent =
                    translationDifference
                        <= options.MaximumTranslationDifference
                    && rotationDifferenceDegrees
                        <= options.MaximumRotationDifferenceDegrees;
                return RigidPoseSymmetryEquivalenceResult.Completed(
                    equivalent,
                    operationIndex,
                    operationAngleDegrees,
                    translationDifference,
                    rotationDifferenceDegrees);
            }
            catch (Exception exception)
            {
                return RigidPoseSymmetryEquivalenceResult.Failed(
                    "Rigid pose symmetry equivalence failed: "
                    + exception.Message);
            }
        }

        private static void Validate(
            RigidSurfacePose referencePose,
            RigidSurfacePose candidatePose,
            RigidPoseSymmetryEquivalenceOptions options)
        {
            if (referencePose == null || candidatePose == null)
            {
                throw new ArgumentNullException(
                    referencePose == null
                        ? nameof(referencePose)
                        : nameof(candidatePose));
            }

            if (options == null || options.Symmetry == null)
            {
                throw new ArgumentException(
                    "Pose equivalence requires options and a symmetry declaration.");
            }

            if (!SurfaceMatchingContractValidation.IsFinite(
                    options.RigidTransformTolerance)
                || options.RigidTransformTolerance <= 0.0
                || !referencePose.IsRigid(options.RigidTransformTolerance)
                || !candidatePose.IsRigid(options.RigidTransformTolerance))
            {
                throw new ArgumentException(
                    "Pose equivalence requires finite rigid transforms and a positive rigid-transform tolerance.");
            }

            if (!SurfaceMatchingContractValidation.IsFinite(
                    options.MaximumTranslationDifference)
                || options.MaximumTranslationDifference < 0.0
                || !SurfaceMatchingContractValidation.IsFinite(
                    options.MaximumRotationDifferenceDegrees)
                || options.MaximumRotationDifferenceDegrees < 0.0
                || options.MaximumRotationDifferenceDegrees > 180.0)
            {
                throw new ArgumentException(
                    "Pose equivalence tolerances require a non-negative translation difference and a rotation difference from zero through 180 degrees.");
            }

            RigidPoseSymmetry symmetry = options.Symmetry;
            if (symmetry.Kind == RigidPoseSymmetryKind.None)
            {
                if (symmetry.Axis != RigidPoseSymmetryAxis.None
                    || symmetry.Order != 1)
                {
                    throw new ArgumentException(
                        "None symmetry requires axis None and order 1.");
                }

                return;
            }

            if (symmetry.Kind != RigidPoseSymmetryKind.DiscreteRotation
                || symmetry.Axis == RigidPoseSymmetryAxis.None
                || symmetry.Order < 2)
            {
                throw new ArgumentException(
                    "Discrete rotational symmetry requires axis X, Y, or Z and order at least 2.");
            }
        }

        private static RelativeRotation Relative(
            RigidSurfacePose referencePose,
            RigidSurfacePose candidatePose)
        {
            return new RelativeRotation(
                referencePose.M11 * candidatePose.M11
                    + referencePose.M21 * candidatePose.M21
                    + referencePose.M31 * candidatePose.M31,
                referencePose.M11 * candidatePose.M12
                    + referencePose.M21 * candidatePose.M22
                    + referencePose.M31 * candidatePose.M32,
                referencePose.M11 * candidatePose.M13
                    + referencePose.M21 * candidatePose.M23
                    + referencePose.M31 * candidatePose.M33,
                referencePose.M12 * candidatePose.M11
                    + referencePose.M22 * candidatePose.M21
                    + referencePose.M32 * candidatePose.M31,
                referencePose.M12 * candidatePose.M12
                    + referencePose.M22 * candidatePose.M22
                    + referencePose.M32 * candidatePose.M32,
                referencePose.M12 * candidatePose.M13
                    + referencePose.M22 * candidatePose.M23
                    + referencePose.M32 * candidatePose.M33,
                referencePose.M13 * candidatePose.M11
                    + referencePose.M23 * candidatePose.M21
                    + referencePose.M33 * candidatePose.M31,
                referencePose.M13 * candidatePose.M12
                    + referencePose.M23 * candidatePose.M22
                    + referencePose.M33 * candidatePose.M32,
                referencePose.M13 * candidatePose.M13
                    + referencePose.M23 * candidatePose.M23
                    + referencePose.M33 * candidatePose.M33);
        }

        private static int NearestOperationIndex(
            RelativeRotation relative,
            RigidPoseSymmetry symmetry)
        {
            if (symmetry.Kind == RigidPoseSymmetryKind.None)
            {
                return 0;
            }

            double a;
            double b;
            switch (symmetry.Axis)
            {
                case RigidPoseSymmetryAxis.X:
                    a = relative.M22 + relative.M33;
                    b = relative.M32 - relative.M23;
                    break;
                case RigidPoseSymmetryAxis.Y:
                    a = relative.M11 + relative.M33;
                    b = relative.M13 - relative.M31;
                    break;
                case RigidPoseSymmetryAxis.Z:
                    a = relative.M11 + relative.M22;
                    b = relative.M21 - relative.M12;
                    break;
                default:
                    return 0;
            }

            if (a == 0.0 && b == 0.0)
            {
                return 0;
            }

            double phase = Math.Atan2(b, a);
            if (phase < 0.0)
            {
                phase += 2.0 * Math.PI;
            }

            double step = 2.0 * Math.PI / symmetry.Order;
            int first = (int)Math.Floor(phase / step);
            if (first >= symmetry.Order)
            {
                first = 0;
            }

            int second = first + 1 == symmetry.Order
                ? 0
                : first + 1;
            double firstTrace = OperationTrace(
                relative,
                symmetry.Axis,
                first * step);
            double secondTrace = OperationTrace(
                relative,
                symmetry.Axis,
                second * step);
            if (secondTrace > firstTrace
                || secondTrace == firstTrace && second < first)
            {
                return second;
            }

            return first;
        }

        private static double OperationTrace(
            RelativeRotation relative,
            RigidPoseSymmetryAxis axis,
            double angleRadians)
        {
            double cosine = Math.Cos(angleRadians);
            double sine = Math.Sin(angleRadians);
            switch (axis)
            {
                case RigidPoseSymmetryAxis.X:
                    return relative.M11
                        + cosine * (relative.M22 + relative.M33)
                        + sine * (relative.M32 - relative.M23);
                case RigidPoseSymmetryAxis.Y:
                    return relative.M22
                        + cosine * (relative.M11 + relative.M33)
                        + sine * (relative.M13 - relative.M31);
                case RigidPoseSymmetryAxis.Z:
                    return relative.M33
                        + cosine * (relative.M11 + relative.M22)
                        + sine * (relative.M21 - relative.M12);
                default:
                    return relative.M11 + relative.M22 + relative.M33;
            }
        }

        private sealed class RelativeRotation
        {
            public RelativeRotation(
                double m11, double m12, double m13,
                double m21, double m22, double m23,
                double m31, double m32, double m33)
            {
                M11 = m11;
                M12 = m12;
                M13 = m13;
                M21 = m21;
                M22 = m22;
                M23 = m23;
                M31 = m31;
                M32 = m32;
                M33 = m33;
            }

            public double M11 { get; }
            public double M12 { get; }
            public double M13 { get; }
            public double M21 { get; }
            public double M22 { get; }
            public double M23 { get; }
            public double M31 { get; }
            public double M32 { get; }
            public double M33 { get; }
        }
    }
}

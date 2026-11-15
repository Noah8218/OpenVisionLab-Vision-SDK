using System.Collections.Generic;
using OpenCvSharp;

namespace OpenVisionLab.Vision2D.Property
{
    public enum CaliperShape { Line, Circle }
    public enum CaliperPolarity { Rising, Falling, Either }
    public enum CaliperSelection { Strongest, First, Last }

    /// <summary>Explicit pixel sampling and optional quality gates. Angles increase clockwise in x-right/y-down input pixels.</summary>
    /// <remarks>No implicit preprocessing or physical-unit conversion. Profiles use unsmoothed adjacent intensity differences.</remarks>
    public sealed class RotatedCaliperToolProperty : OpenCvToolPropertyBase
    {
        public RotatedCaliperToolProperty() : base("Rotated caliper") { }
        public CaliperShape Shape { get; set; }
        public double CenterX { get; set; } = 32;
        public double CenterY { get; set; } = 32;
        /// <summary>Line tangent angle; its scan normal is (-sin(angle), cos(angle)).</summary>
        public double AngleDegrees { get; set; }
        public double Radius { get; set; } = 16;
        public double StartAngleDegrees { get; set; }
        /// <summary>Positive angular spacing for circle stations; the last station must precede the duplicated 360-degree endpoint.</summary>
        public double AngularPitchDegrees { get; set; } = 15;
        public int StationCount { get; set; } = 16;
        public double StationPitch { get; set; } = 2;
        /// <summary>Centered normal-scan length in pixels; ScanSampleCount includes both endpoints.</summary>
        public double ScanLength { get; set; } = 20;
        public int ScanSampleCount { get; set; } = 21;
        /// <summary>Centered tangent averaging width in pixels. A single tangent sample uses the center.</summary>
        public double TangentWidth { get; set; } = 2;
        public int TangentSampleCount { get; set; } = 3;
        /// <summary>Minimum valid fraction of tangent samples per profile bin. Each sample requires four source neighbors.</summary>
        public double MinimumTangentCoverage { get; set; } = 1;
        /// <summary>Minimum valid profile-bin fraction across the complete normal scan, in (0,1]. Default 1 rejects clipped scans before edge selection.</summary>
        /// <remarks>Separate from tangent coverage per bin. Partial normal scans require an explicit lower value; invalid gaps are never bridged.</remarks>
        public double MinimumProfileCoverage { get; set; } = 1;
        public CaliperPolarity Polarity { get; set; } = CaliperPolarity.Either;
        public CaliperSelection Selection { get; set; } = CaliperSelection.Strongest;
        public double MinimumContrast { get; set; } = 5;
        public double TieEpsilon { get; set; } = 1e-9;
        public double ConditioningEpsilon { get; set; } = 1e-12;
        /// <summary>Opt-in bounded removal of one worst residual per iteration; exact residual ties use station ID.</summary>
        public bool OutlierRemovalEnabled { get; set; }
        public double OutlierResidualCutoff { get; set; } = 1;
        public int MaximumOutlierIterations { get; set; } = 4;
        /// <summary>False produces NotEvaluated even when a valid geometry is established.</summary>
        public bool QualityGateEnabled { get; set; }
        public int MinimumPointCount { get; set; } = 3;
        public double MaximumRmsResidual { get; set; } = 1;
        public double MaximumResidual { get; set; } = 2;
        public double MinimumCircleCoverageDegrees { get; set; } = 180;
        public double MaximumCircleGapDegrees { get; set; } = 180;

        internal RotatedCaliperToolProperty Copy()
        {
            RotatedCaliperToolProperty copy = (RotatedCaliperToolProperty)MemberwiseClone();
            copy.CvROIS = CvROIS == null ? null : new List<Rect>(CvROIS);
            copy.CvMASKS = CvMASKS == null ? null : new List<Rect>(CvMASKS);
            return copy;
        }
    }
}

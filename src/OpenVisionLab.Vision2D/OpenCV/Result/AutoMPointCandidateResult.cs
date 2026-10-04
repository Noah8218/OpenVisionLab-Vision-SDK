using OpenCvSharp;
using System.Collections.Generic;
using System.Drawing;

namespace OpenVisionLab.Vision2D.Result
{
    public sealed class AutoMPointCandidateResult
    {
        public int Index { get; set; }
        public int Rank { get; set; }
        public double Score { get; set; }
        public RectangleF Bounding { get; set; }
        public Point2f Center { get; set; }
        public Rect PatternRoi { get; set; }
        public Point2f PatternCenter { get; set; }
        public Point2f NativeMatchCenter { get; set; }
        public double NativeMatchAngle { get; set; }
        public double NativeMatchScale { get; set; } = 1d;
        public double NativeToPatternOffsetX { get; set; }
        public double NativeToPatternOffsetY { get; set; }

        public bool Accepted { get; set; }
        public string RejectReason { get; set; } = string.Empty;
        public double ContrastStdDev { get; set; }
        public double EdgeDensity { get; set; }
        public double QuadrantBalance { get; set; }
        public double OrientationBalance { get; set; }
        public double FeatureQuality { get; set; }

        public double ModelEdgePointCount { get; set; }
        public double ModelEdgeCoverageArea { get; set; }
        public double ModelQuadrantBalance { get; set; }
        public double ModelHighestUsablePyramidLevel { get; set; }
        public double SelfMatchScore { get; set; }
        public double AlternativeMatchScore { get; set; }
        public double UniquenessMargin { get; set; }

        public double SyntheticSuccessRate { get; set; }
        public double PositionErrorMeanPixels { get; set; }
        public double PositionErrorMaxPixels { get; set; }
        public double AngleErrorMaxDegrees { get; set; }
        public double ScaleErrorMaxRatio { get; set; }
        public double RuntimeMedianMilliseconds { get; set; }
        public double RuntimeP95Milliseconds { get; set; }

        /// <summary>Success means correctly rejected target-absent input; a failure is a false positive or an execution error.</summary>
        public List<AutoMPointRepresentativeMatchResult> NegativeRepresentativeMatches { get; }
            = new List<AutoMPointRepresentativeMatchResult>();

        public int RepresentativeImageCount { get; set; }
        public int RepresentativeSuccessCount { get; set; }
        public double RepresentativeSuccessRate { get; set; }
        public double RepresentativeMeanScore { get; set; }
        public double RepresentativeMinimumScore { get; set; }
        public double RepresentativeMeanUniquenessMargin { get; set; }
        public double RepresentativeMinimumUniquenessMargin { get; set; }
        public double RepresentativeRuntimeP95Milliseconds { get; set; }
        public List<AutoMPointRepresentativeMatchResult> RepresentativeMatches { get; }
            = new List<AutoMPointRepresentativeMatchResult>();
    }

    public sealed class AutoMPointRepresentativeMatchResult
    {
        public int ImageIndex { get; set; }
        public bool Success { get; set; }
        public string Outcome { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public Point2f Center { get; set; }
        public double Score { get; set; }
        public double UniquenessMargin { get; set; }
        public double Angle { get; set; }
        public double Scale { get; set; }
        public double RuntimeMilliseconds { get; set; }
    }
}

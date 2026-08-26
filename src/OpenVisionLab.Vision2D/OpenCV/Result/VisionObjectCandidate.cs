using OpenVisionLab.Vision2D.Tool;
using OpenCvSharp;
using System;
using System.Drawing;

namespace OpenVisionLab.Vision2D.Result
{
    /// <summary>Identifies the stage that produced an object candidate.</summary>
    public enum VisionObjectCandidateGenerationStage
    {
        BlobLabeling,
        ContourExtraction
    }

    /// <summary>Identifies the coordinate frame used by candidate geometry.</summary>
    public enum VisionObjectCandidateCoordinateFrame
    {
        SourceImage
    }

    /// <summary>Stable reason codes for object-candidate rejection.</summary>
    public enum VisionObjectCandidateRejectReasonCode
    {
        None,
        AreaBelowMinimum,
        AreaAboveMaximum,
        WidthBelowMinimum,
        WidthAboveMaximum,
        HeightBelowMinimum,
        HeightAboveMaximum,
        Masked
    }

    /// <summary>Area and bounding-box limits applied to one object candidate.</summary>
    public sealed class VisionObjectCandidateLimits
    {
        public int MinimumArea { get; set; }
        public int MaximumArea { get; set; }
        public int MinimumWidth { get; set; }
        public int MaximumWidth { get; set; }
        public int MinimumHeight { get; set; }
        public int MaximumHeight { get; set; }

        public VisionObjectCandidateLimits()
        {
        }

        public VisionObjectCandidateLimits(
            int minimumArea,
            int maximumArea,
            int minimumWidth,
            int maximumWidth,
            int minimumHeight,
            int maximumHeight)
        {
            MinimumArea = minimumArea;
            MaximumArea = maximumArea;
            MinimumWidth = minimumWidth;
            MaximumWidth = maximumWidth;
            MinimumHeight = minimumHeight;
            MaximumHeight = maximumHeight;
        }
    }

    /// <summary>Stable decision returned by the candidate limit evaluator.</summary>
    public sealed class VisionObjectCandidateDecision
    {
        public VisionObjectCandidateRejectReasonCode Code { get; }
        public string Text { get; }
        public bool Accepted => Code == VisionObjectCandidateRejectReasonCode.None;

        public VisionObjectCandidateDecision(
            VisionObjectCandidateRejectReasonCode code,
            string text)
        {
            Code = code;
            Text = text ?? string.Empty;
        }
    }

    /// <summary>Evaluates the stable area and bounding-box candidate contract.</summary>
    public static class VisionObjectCandidateEvaluator
    {
        public static VisionObjectCandidateDecision Evaluate(
            double area,
            int width,
            int height,
            VisionObjectCandidateLimits limits)
        {
            VisionObjectCandidateLimits resolved = limits ?? new VisionObjectCandidateLimits();
            if (area < resolved.MinimumArea)
            {
                return Reject(
                    VisionObjectCandidateRejectReasonCode.AreaBelowMinimum,
                    $"Area {area:0.###} < MIN_AREA {resolved.MinimumArea}");
            }

            if (area > resolved.MaximumArea)
            {
                return Reject(
                    VisionObjectCandidateRejectReasonCode.AreaAboveMaximum,
                    $"Area {area:0.###} > MAX_AREA {resolved.MaximumArea}");
            }

            if (width < resolved.MinimumWidth)
            {
                return Reject(
                    VisionObjectCandidateRejectReasonCode.WidthBelowMinimum,
                    $"Width {width} < MIN_WIDTH {resolved.MinimumWidth}");
            }

            if (width > resolved.MaximumWidth)
            {
                return Reject(
                    VisionObjectCandidateRejectReasonCode.WidthAboveMaximum,
                    $"Width {width} > MAX_WIDTH {resolved.MaximumWidth}");
            }

            if (height < resolved.MinimumHeight)
            {
                return Reject(
                    VisionObjectCandidateRejectReasonCode.HeightBelowMinimum,
                    $"Height {height} < MIN_HEIGHT {resolved.MinimumHeight}");
            }

            if (height > resolved.MaximumHeight)
            {
                return Reject(
                    VisionObjectCandidateRejectReasonCode.HeightAboveMaximum,
                    $"Height {height} > MAX_HEIGHT {resolved.MaximumHeight}");
            }

            return new VisionObjectCandidateDecision(
                VisionObjectCandidateRejectReasonCode.None,
                string.Empty);
        }

        private static VisionObjectCandidateDecision Reject(
            VisionObjectCandidateRejectReasonCode code,
            string text)
        {
            return new VisionObjectCandidateDecision(code, text);
        }
    }

    /// <summary>One source-coordinate object candidate from a Blob or Contour execution.</summary>
    public sealed class VisionObjectCandidate
    {
        public string CandidateId { get; set; } = string.Empty;
        public int RegionIndex { get; set; }
        public int NativeIndex { get; set; }
        public double Area { get; set; }
        public Point2d Center { get; set; } = new Point2d();
        public Rectangle Bounding { get; set; } = new Rectangle();
        public double Angle { get; set; }
        public bool Accepted { get; set; }
        public VisionObjectCandidateRejectReasonCode RejectReasonCode { get; set; }
        public string RejectReasonText { get; set; } = string.Empty;
        public VisionObjectCandidateLimits AppliedLimits { get; set; } = new VisionObjectCandidateLimits();
        public VisionToolOverlay Drawing { get; set; }
        public VisionObjectCandidateGenerationStage GenerationStage { get; set; }
        public VisionObjectCandidateCoordinateFrame CoordinateFrame { get; set; }

        public static string CreateCandidateId(
            VisionObjectCandidateGenerationStage stage,
            int regionIndex,
            int nativeIndex)
        {
            return $"{stage}:{regionIndex}:{nativeIndex}";
        }
    }
}

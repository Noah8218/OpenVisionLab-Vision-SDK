using OpenVisionLab.Inspection;
using OpenVisionLab.Vision2D;
using OpenVisionLab.Vision2D.Pipeline;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Tool;
using OpenVisionLab.Vision3D.FeatureExtraction;
using OpenVisionLab.Vision3D.Geometry;
using OpenVisionLab.Vision3D.Inspection;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenVisionLab.Inspection.Smoke
{
    internal sealed class PassThroughVisionTool : IVisionTool
    {
        public string Name => "Pass-through 2D";

        public bool WasExecuted { get; private set; }

        public VisionToolResult Execute(Mat source)
        {
            WasExecuted = true;
            return VisionToolResult.Passed(null, TimeSpan.Zero, new Dictionary<string, double> { { "Executed", 1.0 } });
        }
    }

    internal sealed class FailingVisionTool : IVisionTool
    {
        public string Name => "Failing 2D";

        public bool WasExecuted { get; private set; }

        public VisionToolResult Execute(Mat source)
        {
            WasExecuted = true;
            return VisionToolResult.Failed(VisionToolErrorCode.Unknown, "Controlled 2D failure.", TimeSpan.Zero);
        }
    }

    internal sealed class TrackingDisposableVisionTool : IVisionTool, IDisposable
    {
        public string Name => "Tracking disposable 2D";

        public Mat LastSource { get; private set; }

        public Mat ResultSnapshot { get; private set; }

        public bool WasDisposed { get; private set; }

        public VisionToolResult Execute(Mat source)
        {
            LastSource = source;
            ResultSnapshot = source?.Clone();
            return VisionToolResult.Passed(ResultSnapshot, TimeSpan.Zero);
        }

        public void Dispose()
        {
            WasDisposed = true;
        }
    }

    internal sealed class ThrowingDisposableVisionTool : IVisionTool, IDisposable
    {
        public string Name => "Throwing disposable 2D";

        public Mat LastSource { get; private set; }

        public bool WasDisposed { get; private set; }

        public VisionToolResult Execute(Mat source)
        {
            LastSource = source;
            throw new InvalidOperationException("Controlled pipeline exception.");
        }

        public void Dispose()
        {
            WasDisposed = true;
        }
    }

    internal sealed class ImageReturningVisionTool : IVisionTool
    {
        public string Name => "Image-returning 2D";

        public VisionToolResult Execute(Mat source)
        {
            return VisionToolResult.Passed(source?.Clone(), TimeSpan.Zero);
        }
    }

    internal sealed class NullResultVisionTool : IVisionTool
    {
        public string Name => "Null-result 2D";

        public VisionToolResult Execute(Mat source)
        {
            return null;
        }
    }

    internal sealed class ThrowingThreeDTool : IThreeDInspectionTool
    {
        public string Name => "Throwing 3D";

        public ThreeDInspectionResult Execute(HeightMap3D source)
        {
            throw new InvalidOperationException("Controlled 3D exception.");
        }
    }

    internal sealed class ThrowingNameThreeDTool : IThreeDInspectionTool
    {
        public string Name
        {
            get
            {
                throw new InvalidOperationException("Controlled name exception.");
            }
        }

        public ThreeDInspectionResult Execute(HeightMap3D source)
        {
            ThreeDInspectionResult result = ThreeDInspectionResult.CreateMeasurement(source, HeightMapRoi.Full(source), TimeSpan.Zero);
            result.Success = true;
            result.ResultStatus = ThreeDInspectionResultStatus.Passed;
            result.Message = "Controlled success.";
            return result;
        }
    }
}

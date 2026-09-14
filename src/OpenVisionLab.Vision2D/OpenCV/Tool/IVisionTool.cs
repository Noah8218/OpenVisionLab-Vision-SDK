using OpenCvSharp;
using System;
using System.Threading;

namespace OpenVisionLab.Vision2D.Tool
{
    public interface IVisionTool
    {
        string Name { get; }
        /// <summary>Runs synchronously. The caller owns source and must dispose the returned result.</summary>
        /// <remarks>
        /// Built-in OpenCvAlgorithmBase tools copy the source and report validation/execution failures in the result.
        /// Custom implementations may throw. Do not execute, configure, or dispose the same tool concurrently.
        /// This contract provides neither cancellation nor a hard timeout.
        /// </remarks>
        VisionToolResult Execute(Mat source);
    }

    /// <summary>Identifies a 2D Tool that can cooperatively stop synchronous execution.</summary>
    /// <remarks>
    /// Cancellation is observed at managed checkpoints and around native OpenCV calls. It does not
    /// forcibly interrupt a native call already in progress. The caller owns source and must dispose
    /// a returned result. A canceled execution throws <see cref="OperationCanceledException"/>.
    /// </remarks>
    public interface ICancellableVisionTool : IVisionTool
    {
        VisionToolResult Execute(Mat source, CancellationToken cancellationToken);
    }
}

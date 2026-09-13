using OpenCvSharp;

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
}

using OpenVisionLab.Compute;
using System.Runtime.InteropServices;
using System.Threading;
using OpenVisionLab.Vision2D;
using OpenVisionLab.Vision2D.Property;
using OpenCvSharp;
using System;

namespace OpenVisionLab.Vision2D.Tool
{
    public class EdgeDetectionTool : OpenCvAlgorithmBase, ICancellableVisionTool
    {
        private readonly VisionComputeSession computeSession;
        private ComputeExecutionEvidence computeEvidence;
        public IOpenCVPropertyEdgeDetection property;

        public EdgeDetectionTool() : this(null) { }

        /// <summary>Borrows a host-owned session without probing. Disposing this tool does not dispose the session.</summary>
        public EdgeDetectionTool(VisionComputeSession computeSession) { this.computeSession = computeSession; }

        public VisionToolResult Execute(Mat source, CancellationToken cancellationToken) => ExecuteWithCancellation(source, cancellationToken);

        protected override void AttachResultEvidence(VisionToolResult result) { result.ComputeDiagnostics = computeEvidence; }

        public void SetProperty(IOpenCVPropertyEdgeDetection property) => this.property = property;

        protected override bool TryValidateBeforeRun(out VisionToolErrorCode errorCode, out string message)
        {
            if (!base.TryValidateBeforeRun(out errorCode, out message))
            {
                return false;
            }

            switch (property.EdgeType)
            {
                case EdgeDetectionToolType.Canny:
                    if (property.CannyThresholdLow < 0
                        || property.CannyThresholdHigh < 0
                        || property.CannyThresholdLow > property.CannyThresholdHigh)
                    {
                        errorCode = VisionToolErrorCode.EdgeDetectionInvalidThreshold;
                        message = $"Canny thresholds must satisfy 0 <= Low <= High. Low={property.CannyThresholdLow}, High={property.CannyThresholdHigh}.";
                        return false;
                    }

                    if (!IsCannyApertureSize(property.CannyApertureSize))
                    {
                        errorCode = VisionToolErrorCode.EdgeDetectionInvalidKernel;
                        message = $"Canny aperture size must be 3, 5, or 7. Aperture={property.CannyApertureSize}.";
                        return false;
                    }
                    break;
                case EdgeDetectionToolType.Sobel:
                    if (!IsValidDerivative(property.SobelDegreeX, property.SobelDegreeY))
                    {
                        errorCode = VisionToolErrorCode.EdgeDetectionInvalidDerivative;
                        message = $"Sobel derivative must be non-negative and not both zero. X={property.SobelDegreeX}, Y={property.SobelDegreeY}.";
                        return false;
                    }

                    if (!IsOddKernelInRange(property.SobelKernelSize, 1, 31))
                    {
                        errorCode = VisionToolErrorCode.EdgeDetectionInvalidKernel;
                        message = $"Sobel kernel size must be an odd number from 1 to 31. Kernel={property.SobelKernelSize}.";
                        return false;
                    }
                    break;
                case EdgeDetectionToolType.Scharr:
                    if (!IsValidScharrDerivative(property.ScharrDegreeX, property.ScharrDegreeY))
                    {
                        errorCode = VisionToolErrorCode.EdgeDetectionInvalidDerivative;
                        message = $"Scharr derivative must be (1,0) or (0,1). X={property.ScharrDegreeX}, Y={property.ScharrDegreeY}.";
                        return false;
                    }
                    break;
                case EdgeDetectionToolType.Laplacian:
                    if (!IsOddKernelInRange(property.LaplacianKernelSize, 1, 31))
                    {
                        errorCode = VisionToolErrorCode.EdgeDetectionInvalidKernel;
                        message = $"Laplacian kernel size must be an odd number from 1 to 31. Kernel={property.LaplacianKernelSize}.";
                        return false;
                    }
                    break;
                default:
                    errorCode = VisionToolErrorCode.InvalidParameter;
                    message = $"Unsupported edge detection type: {property.EdgeType}.";
                    return false;
            }

            errorCode = VisionToolErrorCode.None;
            message = string.Empty;
            return true;
        }

        public override void Run()
        {
            computeEvidence = null;
            if (property == null)
            {
                throw new InvalidOperationException("Edge detection property is not configured.");
            }

            if (OpenCvHelper.IsImageEmpty(imageSource))
            {
                throw new InvalidOperationException("Source image is not loaded.");
            }

            if (!TryValidateBeforeRun(out _, out string validationMessage))
            {
                throw new InvalidOperationException(validationMessage);
            }

            ReplaceResultImage(imageSource.Clone());

            computeEvidence = computeSession?.DescribeCpuExecution(CudaFallbackReason.KernelNotImplemented, ExecutionCancellationToken);
            if (property.EdgeType == EdgeDetectionToolType.Scharr && imageResult.Depth() == MatType.CV_8U
                && (imageResult.Channels() == 1 || imageResult.Channels() == 3) && computeSession?.RequestedCuda == true)
            {
                if (!computeSession.EvaluateUnqualifiedWorkloads)
                {
                    computeEvidence = computeSession.DescribeCpuExecution(CudaFallbackReason.NoMeasuredBenefit, ExecutionCancellationToken);
                }
                else
                {
                    long length = (long)imageResult.Width * imageResult.Height * imageResult.Channels();
                    if (length <= 64 * 1024 * 1024)
                    {
                        byte[] input;
                        try { input = new byte[(int)length]; }
                        catch (OutOfMemoryException) { input = null; }
                        if (input != null)
                        {
                            // Run clones the source first; row packing also respects a retained Mat stride.
                            int rowBytes = imageResult.Width * imageResult.Channels();
                            for (int row = 0; row < imageResult.Height; row++)
                            {
                                ExecutionCancellationToken.ThrowIfCancellationRequested();
                                Marshal.Copy(imageResult.Ptr(row), input, row * rowBytes, rowBytes);
                            }
                            if (computeSession.TryScharr8U(input, imageResult.Width, imageResult.Height, imageResult.Channels(),
                                property.ScharrDegreeX == 1 ? 0 : 1, ExecutionCancellationToken, out byte[] output, out computeEvidence))
                            {
                                for (int row = 0; row < imageResult.Height; row++)
                                {
                                    ExecutionCancellationToken.ThrowIfCancellationRequested();
                                    Marshal.Copy(output, row * rowBytes, imageResult.Ptr(row), rowBytes);
                                }
                                return;
                            }
                        }
                        else computeEvidence = computeSession.DescribeCpuExecution(CudaFallbackReason.GpuMemoryUnavailable, ExecutionCancellationToken);
                    }
                    else computeEvidence = computeSession.DescribeCpuExecution(CudaFallbackReason.GpuMemoryUnavailable, ExecutionCancellationToken);
                }
            }

            switch (property.EdgeType)
            {
                case EdgeDetectionToolType.Canny:
                    // Canny can reallocate color output to one channel; retain a distinct input until it finishes.
                    using (Mat cannySource = imageResult.Channels() == 3 ? imageResult.Clone() : null)
                    {
                        Cv2.Canny(
                            cannySource ?? imageResult,
                            imageResult,
                            property.CannyThresholdLow,
                            property.CannyThresholdHigh,
                            property.CannyApertureSize,
                            property.UseL2Gradient);
                    }
                    break;
                case EdgeDetectionToolType.Sobel:
                    Cv2.Sobel(
                        imageResult,
                        imageResult,
                        MatType.CV_8U,
                        property.SobelDegreeX,
                        property.SobelDegreeY,
                        property.SobelKernelSize,
                        1,
                        0,
                        BorderTypes.Default);
                    break;
                case EdgeDetectionToolType.Scharr:
                    // Preserve input lifetime when OpenCV reallocates the destination to 8U.
                    using (Mat scharrSource = imageResult.Depth() == MatType.CV_8U ? null : imageResult.Clone())
                    {
                        Cv2.Scharr(
                            scharrSource ?? imageResult,
                            imageResult,
                            MatType.CV_8U,
                            property.ScharrDegreeX,
                            property.ScharrDegreeY,
                            1,
                            0,
                            BorderTypes.Default);
                    }
                    break;
                case EdgeDetectionToolType.Laplacian:
                    Cv2.Laplacian(
                        imageResult,
                        imageResult,
                        MatType.CV_8U,
                        property.LaplacianKernelSize,
                        1,
                        0,
                        BorderTypes.Default);
                    break;
            }
        }

        private static bool IsCannyApertureSize(int kernelSize)
        {
            return kernelSize == 3 || kernelSize == 5 || kernelSize == 7;
        }

        private static bool IsOddKernelInRange(int kernelSize, int min, int max)
        {
            return kernelSize >= min && kernelSize <= max && kernelSize % 2 == 1;
        }

        private static bool IsValidDerivative(int degreeX, int degreeY)
        {
            return degreeX >= 0 && degreeY >= 0 && degreeX + degreeY > 0;
        }

        private static bool IsValidScharrDerivative(int degreeX, int degreeY)
        {
            return (degreeX == 1 && degreeY == 0)
                || (degreeX == 0 && degreeY == 1);
        }
    }
}

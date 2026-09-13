using OpenVisionLab.Vision2D;
using OpenVisionLab.Vision2D.Property;
using OpenCvSharp;
using System;

namespace OpenVisionLab.Vision2D.Tool
{
    public class FilterTool : OpenCvAlgorithmBase
    {
        public IOpenCVPropertyFilter property;

        public void SetProperty(IOpenCVPropertyFilter property) => this.property = property;

        protected override bool TryValidateBeforeRun(out VisionToolErrorCode errorCode, out string message)
        {
            if (!base.TryValidateBeforeRun(out errorCode, out message))
            {
                return false;
            }

            switch (property.FilterType)
            {
                case FilterToolType.Blur:
                case FilterToolType.BoxFilter:
                    if (!IsPositiveKernel(property.KernelWidth, property.KernelHeight))
                    {
                        errorCode = VisionToolErrorCode.FilterInvalidKernel;
                        message = $"Filter kernel size must be greater than 0. Width={property.KernelWidth}, Height={property.KernelHeight}.";
                        return false;
                    }
                    break;
                case FilterToolType.GaussianBlur:
                    if (!IsPositiveOddKernel(property.KernelWidth, property.KernelHeight))
                    {
                        errorCode = VisionToolErrorCode.FilterInvalidKernel;
                        message = $"Gaussian kernel size must be positive odd numbers. Width={property.KernelWidth}, Height={property.KernelHeight}.";
                        return false;
                    }
                    break;
                case FilterToolType.MedianBlur:
                    if (!IsPositiveOddKernel(property.MedianKernelSize) || property.MedianKernelSize <= 1)
                    {
                        errorCode = VisionToolErrorCode.FilterInvalidKernel;
                        message = $"Median kernel size must be an odd number greater than 1. Kernel={property.MedianKernelSize}.";
                        return false;
                    }
                    break;
                case FilterToolType.BilateralFilter:
                    if (property.Diameter <= 0)
                    {
                        errorCode = VisionToolErrorCode.FilterInvalidKernel;
                        message = $"Bilateral diameter must be greater than 0. Diameter={property.Diameter}.";
                        return false;
                    }

                    if (!IsFinite(property.SigmaColor) || !IsFinite(property.SigmaSpace)
                        || property.SigmaColor <= 0 || property.SigmaSpace <= 0)
                    {
                        errorCode = VisionToolErrorCode.FilterInvalidSigma;
                        message = $"Bilateral sigma values must be greater than 0. SigmaColor={property.SigmaColor}, SigmaSpace={property.SigmaSpace}.";
                        return false;
                    }
                    break;
                default:
                    errorCode = VisionToolErrorCode.InvalidParameter;
                    message = $"Unsupported filter type: {property.FilterType}.";
                    return false;
            }

            errorCode = VisionToolErrorCode.None;
            message = string.Empty;
            return true;
        }

        public override void Run()
        {
            if (property == null)
            {
                throw new InvalidOperationException("Filter property is not configured.");
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

            switch (property.FilterType)
            {
                case FilterToolType.Blur:
                    Cv2.Blur(imageResult, imageResult, new Size(property.KernelWidth, property.KernelHeight), new Point(-1, -1), property.BorderType);
                    break;
                case FilterToolType.GaussianBlur:
                    Cv2.GaussianBlur(imageResult, imageResult, new Size(property.KernelWidth, property.KernelHeight), 1, 1, property.BorderType);
                    break;
                case FilterToolType.MedianBlur:
                    Cv2.MedianBlur(imageResult, imageResult, property.MedianKernelSize);
                    break;
                case FilterToolType.BoxFilter:
                    Cv2.BoxFilter(imageResult, imageResult, MatType.CV_8UC3, new Size(property.KernelWidth, property.KernelHeight), new Point(-1, -1));
                    break;
                case FilterToolType.BilateralFilter:
                    {
                        Mat bilateralResult = new Mat();
                        try
                        {
                            Cv2.BilateralFilter(imageResult, bilateralResult, property.Diameter, property.SigmaColor, property.SigmaSpace, property.BorderType);
                            ReplaceResultImage(bilateralResult);
                            bilateralResult = null;
                        }
                        finally
                        {
                            bilateralResult?.Dispose();
                        }
                        break;
                    }
            }
        }

        private static bool IsPositiveKernel(int width, int height)
        {
            return width > 0 && height > 0;
        }

        private static bool IsPositiveOddKernel(int width, int height)
        {
            return IsPositiveOddKernel(width) && IsPositiveOddKernel(height);
        }

        private static bool IsPositiveOddKernel(int kernel)
        {
            return kernel > 0 && kernel % 2 == 1;
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

using OpenVisionLab.Vision2D;
using OpenVisionLab.Vision2D.Property;
using OpenCvSharp;
using System;

namespace OpenVisionLab.Vision2D.Tool
{
    public class ThresholdTool : OpenCvAlgorithmBase
    {
        public IOpenCVPropertyThreshold property;

        public void SetProperty(IOpenCVPropertyThreshold property) => this.property = property;

        protected override bool TryValidateBeforeRun(out VisionToolErrorCode errorCode, out string message)
        {
            if (!base.TryValidateBeforeRun(out errorCode, out message))
            {
                return false;
            }

            if (!IsFinite(property.MaxValue) || property.MaxValue <= 0)
            {
                errorCode = VisionToolErrorCode.ThresholdInvalidMaxValue;
                message = $"Threshold MaxValue must be greater than 0. MaxValue={property.MaxValue}.";
                return false;
            }

            if (property.Mode == ThresholdToolMode.Threshold && !IsFinite(property.Threshold))
            {
                errorCode = VisionToolErrorCode.InvalidParameter;
                message = $"Threshold value must be finite. Threshold={property.Threshold}.";
                return false;
            }

            if (property.Mode == ThresholdToolMode.Range && property.RangeMin > property.RangeMax)
            {
                errorCode = VisionToolErrorCode.ThresholdInvalidRange;
                message = $"Threshold range is invalid. Min={property.RangeMin}, Max={property.RangeMax}.";
                return false;
            }

            if (property.Mode == ThresholdToolMode.Adaptive && !IsValidAdaptiveBlockSize(property.BlockSize))
            {
                errorCode = VisionToolErrorCode.ThresholdInvalidAdaptiveBlockSize;
                message = $"Adaptive threshold BlockSize must be an odd number greater than 1. BlockSize={property.BlockSize}.";
                return false;
            }

            if (!Enum.IsDefined(typeof(ThresholdToolMode), property.Mode))
            {
                errorCode = VisionToolErrorCode.InvalidParameter;
                message = $"Unsupported threshold mode: {property.Mode}.";
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
                throw new InvalidOperationException("Threshold property is not configured.");
            }

            if (OpenCvHelper.IsImageEmpty(imageSource))
            {
                throw new InvalidOperationException("Source image is not loaded.");
            }

            if (!TryValidateBeforeRun(out _, out string validationMessage))
            {
                throw new InvalidOperationException(validationMessage);
            }
            
            switch (property.Mode)
            {
                case ThresholdToolMode.Threshold:
                    RunThreshold();
                    break;
                case ThresholdToolMode.Range:
                    RunRange();
                    break;
                case ThresholdToolMode.Adaptive:
                    RunAdaptive();
                    break;
            }
        }

        private void RunThreshold()
        {
            using (Mat graySource = imageSource.Clone())
            {
                OpenCvHelper.SetImageChannel1(graySource);
                ReplaceResultImage(new Mat());
                Cv2.Threshold(graySource, imageResult, property.Threshold, property.MaxValue, property.ThresholdType);
            }
        }

        private void RunRange()
        {
            using (Mat graySource = imageSource.Clone())
            {
                OpenCvHelper.SetImageChannel1(graySource);
                ReplaceResultImage(new Mat());
                Cv2.InRange(graySource, new Scalar(property.RangeMin), new Scalar(property.RangeMax), imageResult);
                if (property.Invert)
                {
                    Cv2.BitwiseNot(imageResult, imageResult);
                }
            }
        }

        private void RunAdaptive()
        {
            using (Mat graySource = imageSource.Clone())
            {
                OpenCvHelper.SetImageChannel1(graySource);
                ReplaceResultImage(new Mat());
                Cv2.AdaptiveThreshold(
                    graySource,
                    imageResult,
                    property.MaxValue,
                    property.AdaptiveType,
                    property.AdaptiveThresholdType,
                    property.BlockSize,
                    property.Weight);
            }
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

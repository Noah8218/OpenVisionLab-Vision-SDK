using OpenVisionLab.Core;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace OpenVisionLab.Vision2D
{
    /// <summary>Channel order of an unsigned 8-bit input. Alpha is ignored during gray conversion.</summary>
    public enum ImageChannelOrder { Gray = 0, Rgb = 1, Bgr = 2, Rgba = 3, Bgra = 4 }

    public class OpenCvHelper
    {
        /// <summary>Creates an owned gray snapshot without modifying the source, including strided ROI inputs.</summary>
        /// <remarks>Only CV_8U is supported. The caller disposes the returned Mat; alpha is ignored.</remarks>
        public static Mat CreateGray8(Mat source, ImageChannelOrder order)
        {
            if (IsImageEmpty(source)) throw new ArgumentException("A non-empty, live source image is required.", nameof(source));
            if (source.Depth() != MatType.CV_8U) throw new ArgumentException("Explicit scaling is required for non-8-bit input.", nameof(source));
            int channels;
            ColorConversionCodes conversion;
            switch (order)
            {
                case ImageChannelOrder.Gray: channels = 1; conversion = ColorConversionCodes.RGB2GRAY; break;
                case ImageChannelOrder.Rgb: channels = 3; conversion = ColorConversionCodes.RGB2GRAY; break;
                case ImageChannelOrder.Bgr: channels = 3; conversion = ColorConversionCodes.BGR2GRAY; break;
                case ImageChannelOrder.Rgba: channels = 4; conversion = ColorConversionCodes.RGBA2GRAY; break;
                case ImageChannelOrder.Bgra: channels = 4; conversion = ColorConversionCodes.BGRA2GRAY; break;
                default: throw new ArgumentOutOfRangeException(nameof(order));
            }
            if (source.Channels() != channels) throw new ArgumentException("Channel count does not match the declared order.", nameof(source));
            if (channels == 1) return source.Clone();
            Mat gray = new Mat();
            try { Cv2.CvtColor(source, gray, conversion); return gray; }
            catch { gray.Dispose(); throw; }
        }

        public static bool IsMatEmpty(Mat MatSource)
        {
                        if (MatSource.IsDisposed)
            {
                return true;
            }

            if (MatSource == null)
            {
                return true;
            }

            if (MatSource.Width == 0 || MatSource.Height == 0)
            {
                return true;
            }
            return false;
        
        }

        public static bool IsRectEmpty(Rect rt)
        {
            if (rt == null)
            {

                Debug.WriteLine("Rect is null");
                return true;
            }

            if (rt.Width == 0 || rt.Height == 0)
            {

                Debug.WriteLine("Rect Size Empty");
                return true;
            }

            return false;
        }

        public static bool IsImageEmpty(Mat image)
        {
            if (image == null)
            {

                Debug.WriteLine("Image is null");
                return true;
            }

            if (image.IsDisposed)
            {

                Debug.WriteLine("Image Disposed");
                return true;
            }

            if (image.Width == 0 || image.Height == 0)
            {

                Debug.WriteLine("Image Size Empty");
                return true;
            }

            return false;
        }

        public static bool SetImageChannel4(Mat image)
        {
            if (IsImageEmpty(image)) return false;

            if (image.Channels() == 1) ConvertChannelInPlace(image, ColorConversionCodes.GRAY2RGBA);
            if (image.Channels() == 3) ConvertChannelInPlace(image, ColorConversionCodes.RGB2RGBA);

            return true;
        }

        public static bool SetImageChannel3(Mat image)
        {
            if (IsImageEmpty(image)) return false;

            if (image.Channels() == 1) ConvertChannelInPlace(image, ColorConversionCodes.GRAY2RGB);
            if (image.Channels() == 4) ConvertChannelInPlace(image, ColorConversionCodes.RGBA2RGB);

            return true;
        }

        public static Mat SetImageChannel3ToMat(Mat image)
        {
            if (IsImageEmpty(image)) return null;

            SetImageChannel3(image);

            return image;
        }

        public static bool SetImageChannel1(Mat image)
        {
            if (IsImageEmpty(image)) return false;

            if (image.Channels() == 3) ConvertChannelInPlace(image, ColorConversionCodes.RGB2GRAY);
            if (image.Channels() == 4) ConvertChannelInPlace(image, ColorConversionCodes.RGBA2GRAY);

            return true;
        }

        private static void ConvertChannelInPlace(Mat image, ColorConversionCodes conversion)
        {
            using (Mat converted = new Mat())
            {
                Cv2.CvtColor(image, converted, conversion);
                converted.CopyTo(image);
            }
        }
    }
}

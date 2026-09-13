using OpenCvSharp;
using System;
using System.Collections.Generic;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Result;
using OpenVisionLab.Core;
using System.Reflection;

namespace OpenVisionLab.Vision2D.Tool
{
    [Obsolete("Legacy compatibility API. Use MeanTool and MeanResult for new OpenVisionLab code.", false)]
    public class CVMean : COpenCVAlgorithmBase
    {
        public IOpenCVPropertyMean property;
        public List<CResultMean> results = new List<CResultMean>();

        public CVMean() { }        
        
        public void SetProperty(IOpenCVPropertyMean property) => this.property = property;

        public override void Run()
        {
            if(property.USE_MULTI_ROI)
            {
                MultiRun();
            }
            else
            {
                SingleRun();
            }
        }

        public void MultiRun()
        {
            try
            {
                results.Clear();

                if (COpenCVHelper.IsImageEmpty(imageSource))
                {
                    Console.Error.WriteLine("Image is Empty");
                    return;
                }
                using (Mat ImageSrc = imageSource.Clone())
                {
                    if (COpenCVHelper.IsImageEmpty(imageSource)) return;

                    OpenVisionLab.Vision2D.COpenCVHelper.SetImageChannel1(ImageSrc);

                    for (int i = 0; i < property.CvROIS.Count; i++)
                    {
                        if (property.CvROIS[i].Width == 0 || property.CvROIS[i].Height == 0)
                        {
                            property.CvROIS[i] = new OpenCvSharp.Rect(0, 0, imageSource.Width, imageSource.Height);
                        }

                        Mat ImageMean = property.USE_ROI ? ImageSrc.SubMat(property.CvROIS[i]) : ImageSrc.Clone();

                        if (property.USE_THRESHOLD) { Cv2.Threshold(ImageMean, ImageMean, property.THRESHOLD, 255, property.THRESHOLD_TYPES); }
                        else if (property.USE_ADAPTIVE_THRESHOLD) { Cv2.AdaptiveThreshold(ImageMean, ImageMean, property.ADAPTIVE_THRESHOLD, property.ADAPTIVE_THRESHOLD_ALGORITHM, property.ADAPTIVE_THRESHOLD_TYPES, property.BlockSize, property.Weight); }

                        if (property.USE_BITWISENOT) Cv2.BitwiseNot(ImageMean, ImageMean);

                        double Mean = 0;
                        double MeanStdDev = 0;

                        switch (property.MEAN_TYPES)
                        {
                            case MeanType.Mean:
                                Mean = Cv2.Mean(ImageMean).Val0;
                                Mean = Math.Round(Mean, 1);
                                results.Add(new CResultMean(0, Mean, OpenVisionLab.Core.CConverter.RectToRectangle(property.CvROIS[i])));
                                break;
                            case MeanType.MeanStdDev:
                                Cv2.MeanStdDev(ImageMean, out Scalar mean, out Scalar stddev);
                                MeanStdDev = Math.Round(stddev[0], 1);
                                results.Add(new CResultMean(0, MeanStdDev, OpenVisionLab.Core.CConverter.RectToRectangle(property.CvROIS[i])));
                                break;
                        }
                    }
                }
            }
            catch (Exception Desc)
            {
                Console.Error.WriteLine($"[ERROR] {MethodBase.GetCurrentMethod().ReflectedType.Name}==>{MethodBase.GetCurrentMethod().Name} Ex ==> {Desc.Message}");
                return;
            }

            return;
        }

        public void SingleRun()
        {
            try
            {                
                results.Clear();

                if (COpenCVHelper.IsImageEmpty(imageSource))
                {
                    Console.Error.WriteLine("Image is Empty");
                    return;
                }

                if (property.CvROI.Width == 0 || property.CvROI.Height == 0)
                {
                    property.CvROI = new OpenCvSharp.Rect(0, 0, imageSource.Width, imageSource.Height);
                }

                using (Mat ImageSrc = imageSource.Clone())
                {
                    if (COpenCVHelper.IsImageEmpty(imageSource)) return;
                    
                    OpenVisionLab.Vision2D.COpenCVHelper.SetImageChannel1(ImageSrc);

                    Mat ImageMean = property.USE_ROI ? ImageSrc.SubMat(property.CvROI) : ImageSrc.Clone();

                    if (property.USE_THRESHOLD) { Cv2.Threshold(ImageMean, ImageMean, property.THRESHOLD, 255, property.THRESHOLD_TYPES); }
                    else if (property.USE_ADAPTIVE_THRESHOLD) { Cv2.AdaptiveThreshold(ImageMean, ImageMean, property.ADAPTIVE_THRESHOLD, property.ADAPTIVE_THRESHOLD_ALGORITHM, property.ADAPTIVE_THRESHOLD_TYPES, property.BlockSize, property.Weight); }

                    if (property.USE_BITWISENOT) Cv2.BitwiseNot(ImageMean, ImageMean);

                    double Mean = 0;
                    double MeanStdDev = 0;

                    switch (property.MEAN_TYPES)
                    {
                        case MeanType.Mean:
                            Mean = Cv2.Mean(ImageMean).Val0;
                            Mean = Math.Round(Mean, 1);
                            results.Add(new CResultMean(0, Mean, OpenVisionLab.Core.CConverter.RectToRectangle(property.CvROI)));          
                            break;
                        case MeanType.MeanStdDev:
                            Cv2.MeanStdDev(ImageMean, out Scalar mean, out Scalar stddev);
                            MeanStdDev = Math.Round(stddev[0], 1);
                            results.Add(new CResultMean(0, MeanStdDev, OpenVisionLab.Core.CConverter.RectToRectangle(property.CvROI)));                  
                            break;
                    }
                }
            }
            catch (Exception Desc)
            {
                Console.Error.WriteLine($"[ERROR] {MethodBase.GetCurrentMethod().ReflectedType.Name}==>{MethodBase.GetCurrentMethod().Name} Ex ==> {Desc.Message}");
                return;
            }

            return;
        }
    }
}

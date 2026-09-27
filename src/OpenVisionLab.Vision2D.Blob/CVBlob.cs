using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using OpenVisionLab.Core;
using OpenVisionLab.Vision2D.Components;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Result;
using OpenCvSharp;

namespace OpenVisionLab.Vision2D.Blob
{
    [Obsolete("Legacy compatibility API. Use BlobTool and BlobResult for new OpenVisionLab code.", false)]
    public partial class CVBlob : COpenCVAlgorithmBase
    {
        public IOpenCVPropertyBlob property;
        public List<CResultBlob> results = new List<CResultBlob>();

        public CVBlob() { }

        public void SetProperty(IOpenCVPropertyBlob propertyBase) => property = propertyBase;

        public override void Run()
        {
            if (property.USE_MULTI_ROI)
            {
                MultiRun();
            }
            else
            {
                SingleRun();
            }
        }

        protected bool SingleRun()
        {
            try
            {
                swTaktTimems.Restart();
                results.Clear();

                if (COpenCVHelper.IsImageEmpty(imageSource))
                {
                    Console.Error.WriteLine("Image is Empty");
                    return false;
                }

                if (property.CvROI.Width == 0 || property.CvROI.Height == 0)
                {
                    property.CvROI = new Rect(0, 0, imageSource.Width, imageSource.Height);
                }

                if (COpenCVHelper.IsImageEmpty(imageSource)) return false;
                COpenCVHelper.SetImageChannel1(imageSource);

                Mat ImageBlob = property.USE_ROI ? imageSource.SubMat(property.CvROI) : imageSource.Clone();

                using (Mat ImageSrc = ImageBlob)
                {
                    if (property.USE_THRESHOLD) { Cv2.Threshold(ImageBlob, ImageBlob, property.THRESHOLD, 255, property.THRESHOLD_TYPES); }
                    else if (property.USE_ADAPTIVE_THRESHOLD) { Cv2.AdaptiveThreshold(ImageBlob, ImageBlob, property.ADAPTIVE_THRESHOLD, property.ADAPTIVE_THRESHOLD_ALGORITHM, property.ADAPTIVE_THRESHOLD_TYPES, property.BlockSize, property.Weight); }

                    // 검은색 영역에서 흰 물체를 라벨링하여 검출하기 때문
                    // 검출하려고 하는 물체가 검은색이면 반전으로 검출해야함
                    if (property.USE_BITWISENOT) Cv2.BitwiseNot(ImageBlob, ImageBlob);

                    IReadOnlyList<BinaryComponent> blobs = BinaryShapeCompatibility.LabelComponents(ImageBlob)
                        .Where(item => item.Area >= property.MIN_AREA && item.Area <= property.MAX_AREA)
                        .ToList();

                    ConcurrentBag<CResultBlob> defectsS = new ConcurrentBag<CResultBlob>();
                    Parallel.ForEach(blobs, (item, state, index) =>
                    {
                        Rect rect = new Rect();
                        Point2d Center = new Point2d();

                        if (property.USE_ROI)
                        {
                            rect.X = item.Bounds.X + property.CvROI.X;
                            rect.Y = item.Bounds.Y + property.CvROI.Y;
                            rect.Width = item.Bounds.Width;
                            rect.Height = item.Bounds.Height;

                            Center.X = item.Center.X + property.CvROI.X;
                            Center.Y = item.Center.Y + property.CvROI.Y;
                        }
                        else
                        {
                            rect = item.Bounds;
                            Center = item.Center;
                        }

                        bool Masking = false;
                        for (int i = 0; i < property.CvMASKS.Count; i++)
                        {
                            // ==> IntersectsWith 사용 이물이 걸치기만해도 필터 나옴                        
                            if (property.CvMASKS[i].Contains(rect))
                            {
                                Masking = true;
                                break;
                            }
                        }

                        if (!Masking)
                        {
                            defectsS.Add(new CResultBlob((int)index, item.Area, Center, rect, item.Angle));
                        }
                    });
                    results = defectsS.OrderBy(c => c.Index).ToList();
                }

                swTaktTimems.Stop();
            }
            catch (Exception Desc)
            {
                Console.Error.WriteLine($"[ERROR] {MethodBase.GetCurrentMethod().ReflectedType.Name}==>{MethodBase.GetCurrentMethod().Name} Ex ==> {Desc.Message}");
                return false;
            }
            return true;
        }

        protected bool MultiRun()
        {
            try
            {
                swTaktTimems.Restart();
                results.Clear();

                if (COpenCVHelper.IsImageEmpty(imageSource))
                {
                    Console.Error.WriteLine("Image is Empty");
                    return false;
                }

                if (COpenCVHelper.IsImageEmpty(imageSource)) return false;
                COpenCVHelper.SetImageChannel1(imageSource);

                for (int i = 0; i < property.CvROIS.Count; i++)
                {
                    if (property.CvROIS[i].Width == 0 || property.CvROIS[i].Height == 0)
                    {
                        property.CvROIS[i] = new Rect(0, 0, imageSource.Width, imageSource.Height);
                    }

                    Mat ImageBlob = null;

                    if (property.USE_ROI) { ImageBlob = imageSource.SubMat(property.CvROIS[i]); }
                    else { ImageBlob = imageSource.Clone(); }

                    using (Mat ImageSrc = ImageBlob)
                    {
                        if (property.USE_THRESHOLD) { Cv2.Threshold(ImageBlob, ImageBlob, property.THRESHOLD, 255, property.THRESHOLD_TYPES); }
                        else if (property.USE_ADAPTIVE_THRESHOLD) { Cv2.AdaptiveThreshold(ImageBlob, ImageBlob, property.ADAPTIVE_THRESHOLD, property.ADAPTIVE_THRESHOLD_ALGORITHM, property.ADAPTIVE_THRESHOLD_TYPES, property.BlockSize, property.Weight); }

                        // 검은색 영역에서 흰 물체를 라벨링하여 검출하기 때문
                        // 검출하려고 하는 물체가 검은색이면 반전으로 검출해야함
                        if (property.USE_BITWISENOT) Cv2.BitwiseNot(ImageBlob, ImageBlob);

                        Stopwatch sw_TaktTimems2 = Stopwatch.StartNew();

                        IReadOnlyList<BinaryComponent> blobs = BinaryShapeCompatibility.LabelComponents(ImageBlob)
                            .Where(item => item.Area >= property.MIN_AREA && item.Area <= property.MAX_AREA)
                            .ToList();

                        ConcurrentBag<CResultBlob> defectsS = new ConcurrentBag<CResultBlob>();
                        Parallel.ForEach(blobs, (item, state, index) =>
                        {
                            Rect rect = new Rect();
                            Point2d Center = new Point2d();

                            if (property.USE_ROI)
                            {
                                rect.X = item.Bounds.X + property.CvROIS[i].X;
                                rect.Y = item.Bounds.Y + property.CvROIS[i].Y;
                                rect.Width = item.Bounds.Width;
                                rect.Height = item.Bounds.Height;

                                Center.X = item.Center.X + property.CvROIS[i].X;
                                Center.Y = item.Center.Y + property.CvROIS[i].Y;
                            }
                            else
                            {
                                rect = item.Bounds;
                                Center = item.Center;
                            }

                            bool Masking = false;
                            for (int j = 0; j < property.CvMASKS.Count; j++)
                            {
                                // ==> IntersectsWith 사용 이물이 걸치기만해도 필터 나옴                        
                                if (property.CvMASKS[j].Contains(rect))
                                {
                                    Masking = true;
                                    break;
                                }
                            }

                            if (!Masking)
                            {
                                defectsS.Add(new CResultBlob((int)index, item.Area, Center, rect, item.Angle));
                            }
                        });
                        results.AddRange(defectsS.OrderBy(c => c.Index).ToList());
                        sw_TaktTimems2.Stop();
                    }
                }

                swTaktTimems.Stop();
            }
            catch (Exception Desc)
            {
                Console.Error.WriteLine($"[ERROR] {MethodBase.GetCurrentMethod().ReflectedType.Name}==>{MethodBase.GetCurrentMethod().Name} Ex ==> {Desc.Message}");
                return false;
            }

            return true;
        }
    }
}


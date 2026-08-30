using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using OpenVisionLab.Core;
using OpenVisionLab.Core.Geometry2D;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Result;
using OpenCvSharp;
using static OpenVisionLab.Core.FormulaUtil;
namespace OpenVisionLab.Vision2D.Tool
{  
    public partial class LineGaugeTool : OpenCvAlgorithmBase
    {
        public IOpenCvPropertyLineGauge property { get; set; }
        public List<LineGaugeResult> resultList { get; set; } = new List<LineGaugeResult>();
        public LineGaugeTool() { }

        public void SetProperty(IOpenCvPropertyLineGauge property) => this.property = property;

        protected override bool TryValidateBeforeRun(out VisionToolErrorCode errorCode, out string message)
        {
            if (!base.TryValidateBeforeRun(out errorCode, out message))
            {
                return false;
            }

            if (imageSource.Depth() != MatType.CV_8U)
            {
                errorCode = VisionToolErrorCode.InputImageInvalid;
                message = $"LineGauge supports only 8-bit unsigned source images. Actual={imageSource.Type()}.";
                return false;
            }

            if (property.SAMPLING_STEP < 1 || property.THICKNESS < 1)
            {
                errorCode = VisionToolErrorCode.LineGaugeInvalidSampling;
                message = $"LineGauge sampling parameters must be at least 1. SamplingStep={property.SAMPLING_STEP}, Thickness={property.THICKNESS}.";
                return false;
            }

            if (!TryValidateAdaptiveThreshold(
                property,
                VisionToolErrorCode.LineGaugeInvalidAdaptiveBlockSize,
                out errorCode,
                out message))
            {
                return false;
            }

            if (!TryValidateRoiSet(
                property,
                true,
                false,
                VisionToolErrorCode.LineGaugeRoiInvalid,
                "LineGauge",
                out errorCode,
                out message))
            {
                return false;
            }

            errorCode = VisionToolErrorCode.None;
            message = string.Empty;
            return true;
        }

        protected override bool TryValidateAfterRun(out VisionToolErrorCode errorCode, out string message)
        {
            int emptyIndex = resultList?.FindIndex(result => result == null || result.edgeList == null || result.edgeList.Count == 0) ?? -1;
            if (emptyIndex >= 0)
            {
                errorCode = VisionToolErrorCode.LineGaugeEdgeNotFound;
                message = $"LineGauge found no stable edge in result #{emptyIndex + 1}. Direction={property.PRJ_DIR}, Polarity={property.PRJ_PORALITY}, Contrast={property.CONTRAST}, ROI={FormatLineGaugeRoi()}";
                return false;
            }

            int edgeCount = resultList?.Sum(result => result?.edgeList?.Count ?? 0) ?? 0;
            if (edgeCount == 0)
            {
                errorCode = VisionToolErrorCode.LineGaugeEdgeNotFound;
                message = $"LineGauge found no stable edge. Direction={property.PRJ_DIR}, Polarity={property.PRJ_PORALITY}, Contrast={property.CONTRAST}, ROI={FormatLineGaugeRoi()}";
                return false;
            }

            int invalidFitIndex = resultList.FindIndex(result => !IsValidFitLine(result?.FitLine));
            if (invalidFitIndex >= 0)
            {
                errorCode = VisionToolErrorCode.LineGaugeFitFailed;
                message = $"LineGauge fit line is invalid in result #{invalidFitIndex + 1}. EdgeCount={resultList[invalidFitIndex]?.edgeList?.Count ?? 0}, Direction={property.PRJ_DIR}.";
                return false;
            }

            errorCode = VisionToolErrorCode.None;
            message = string.Empty;
            return true;
        }

        private static bool IsValidFitLine(LineSegment2D line)
        {
            if (line == null)
            {
                return false;
            }

            double distance = line.Distance();
            return !double.IsNaN(distance) && !double.IsInfinity(distance) && distance > 0;
        }

        private string FormatLineGaugeRoi()
        {
            if (property.USE_MULTI_ROI)
            {
                return $"Multi({property.CvROIS?.Count ?? 0})";
            }

            Rect roi = property.CvROI;
            return $"{roi.X},{roi.Y},{roi.Width},{roi.Height}";
        }

        public override void Run()
        {
            resultList.Clear();

            if (property.USE_MULTI_ROI)
            {
                MultiRun();                
            }
            else
            {
                SingleRun();
            }
        }

        public bool SingleRun()
        {
            if (OpenCvHelper.IsImageEmpty(imageSource))
            {
                return false;
            }

            if (property.CvROI.Width == 0 || property.CvROI.Height == 0)
            {
                return false;
            }

            using (Mat imageSrc = CreatePreparedLineGaugeImage(property.CvROI, property.USE_ROI))
            {
                resultList.Add(CreateLineGaugeResult(1, imageSrc, property.CvROI, property.USE_ROI, property.USE_EXTEND_FIT_LINE));
            }

            return true;
        }

        public bool MultiRun()
        {
            if (OpenCvHelper.IsImageEmpty(imageSource))
            {
                return false;
            }

            for (int index = 0; index < property.CvROIS.Count; index++)
            {
                Rect roi = property.CvROIS[index];
                if (roi.Width == 0 || roi.Height == 0)
                {
                    return false;
                }

                using (Mat imageSrc = CreatePreparedLineGaugeImage(roi, true))
                {
                    resultList.Add(CreateLineGaugeResult(index + 1, imageSrc, roi, true, false));
                }
            }

            return true;
        }

        private LineGaugeResult CreateLineGaugeResult(int index, Mat imageSrc, Rect roi, bool applyRoiOffset, bool useExtendFitLine)
        {
            List<LineGaugeEdge> edgeList = FindEdges(imageSrc, roi, applyRoiOffset);
            List<OpenCvSharp.Point> edgePoints = edgeList.ConvertAll(edge => edge.MeasPos);
            LineSegment2D fitLine = useExtendFitLine
                ? LineFitting.GetFitLineExtend(edgePoints, property.EXTEND_FIT_LINE_VALUE, property.PRJ_DIR)
                : LineFitting.GetFitLine(edgePoints, property.PRJ_DIR);

            return new LineGaugeResult(edgeList, fitLine, index);
        }

        private List<LineGaugeEdge> FindEdges(Mat imageSrc, Rect roi, bool applyRoiOffset)
        {
            ConcurrentBag<LineGaugeEdge> edges = new ConcurrentBag<LineGaugeEdge>();
            byte[,] bytes = ReadGrayBytes(imageSrc);
            int step = (int)property.SAMPLING_STEP;
            int thickness = (int)property.THICKNESS;

            switch (property.PRJ_DIR)
            {
                case PROJECTION_DIR.X_LTOR:
                    ScanLeftToRight(imageSrc, bytes, roi, applyRoiOffset, edges, step, thickness);
                    break;
                case PROJECTION_DIR.X_RTOL:
                    ScanRightToLeft(imageSrc, bytes, roi, applyRoiOffset, edges, step, thickness);
                    break;
                case PROJECTION_DIR.Y_TTOB:
                    ScanTopToBottom(imageSrc, bytes, roi, applyRoiOffset, edges, step, thickness);
                    break;
                case PROJECTION_DIR.Y_BTOT:
                    ScanBottomToTop(imageSrc, bytes, roi, applyRoiOffset, edges, step, thickness);
                    break;
            }

            return SortEdges(edges);
        }

        private delegate bool TryFindEdgeOnScanLine(byte[,] bytes, int scanIndex, int positionLimit, int thickness, out int edgeX, out int edgeY);

        private void ScanProjectedLines(
            int scanCount,
            int positionLimit,
            byte[,] bytes,
            Rect roi,
            bool applyRoiOffset,
            ConcurrentBag<LineGaugeEdge> edges,
            int step,
            int thickness,
            TryFindEdgeOnScanLine tryFindEdge)
        {
            Parallel.For(0, scanCount, scanIndex =>
            {
                if (scanIndex % step != 0) { return; }

                if (tryFindEdge(bytes, scanIndex, positionLimit, thickness, out int edgeX, out int edgeY))
                {
                    AddEdge(edges, edgeX, edgeY, roi, applyRoiOffset);
                }
            });
        }

        private void ScanLeftToRight(Mat imageSrc, byte[,] bytes, Rect roi, bool applyRoiOffset, ConcurrentBag<LineGaugeEdge> edges, int step, int thickness)
        {
            ScanProjectedLines(imageSrc.Rows, imageSrc.Cols, bytes, roi, applyRoiOffset, edges, step, thickness, TryFindLeftToRightEdge);
        }

        private bool TryFindLeftToRightEdge(byte[,] bytes, int y, int cols, int thickness, out int edgeX, out int edgeY)
        {
            edgeX = 0;
            edgeY = 0;

            for (int x = 2; x < cols - thickness - 1; x++)
            {
                int current = bytes[y, x];
                int previous = bytes[y, x - 1];
                if (!JudgeEdge(previous, current)) { continue; }
                if (!IsThicknessStableXForward(bytes, y, x, previous, thickness)) { continue; }

                edgeX = x - 1;
                edgeY = y;
                return true;
            }

            return false;
        }

        private void ScanRightToLeft(Mat imageSrc, byte[,] bytes, Rect roi, bool applyRoiOffset, ConcurrentBag<LineGaugeEdge> edges, int step, int thickness)
        {
            ScanProjectedLines(imageSrc.Rows, imageSrc.Cols, bytes, roi, applyRoiOffset, edges, step, thickness, TryFindRightToLeftEdge);
        }

        private bool TryFindRightToLeftEdge(byte[,] bytes, int y, int cols, int thickness, out int edgeX, out int edgeY)
        {
            edgeX = 0;
            edgeY = 0;

            for (int x = cols - 2; x > thickness; x--)
            {
                int current = bytes[y, x];
                int previous = bytes[y, x + 1];
                if (!JudgeEdge(previous, current)) { continue; }
                if (!IsThicknessStableXBackward(bytes, y, x, previous, thickness)) { continue; }

                edgeX = x + 1;
                edgeY = y;
                return true;
            }

            return false;
        }

        private void ScanTopToBottom(Mat imageSrc, byte[,] bytes, Rect roi, bool applyRoiOffset, ConcurrentBag<LineGaugeEdge> edges, int step, int thickness)
        {
            ScanProjectedLines(imageSrc.Cols, imageSrc.Rows, bytes, roi, applyRoiOffset, edges, step, thickness, TryFindTopToBottomEdge);
        }

        private bool TryFindTopToBottomEdge(byte[,] bytes, int x, int rows, int thickness, out int edgeX, out int edgeY)
        {
            edgeX = 0;
            edgeY = 0;

            for (int y = 2; y < rows - thickness; y++)
            {
                int current = bytes[y, x];
                int previous = bytes[y - 1, x];
                if (!JudgeEdge(previous, current)) { continue; }
                if (!IsThicknessStableYForward(bytes, x, y, previous, thickness)) { continue; }

                edgeX = x;
                edgeY = y - 1;
                return true;
            }

            return false;
        }

        private void ScanBottomToTop(Mat imageSrc, byte[,] bytes, Rect roi, bool applyRoiOffset, ConcurrentBag<LineGaugeEdge> edges, int step, int thickness)
        {
            ScanProjectedLines(imageSrc.Cols, imageSrc.Rows, bytes, roi, applyRoiOffset, edges, step, thickness, TryFindBottomToTopEdge);
        }

        private bool TryFindBottomToTopEdge(byte[,] bytes, int x, int rows, int thickness, out int edgeX, out int edgeY)
        {
            edgeX = 0;
            edgeY = 0;

            for (int y = rows - 2; y > thickness; y--)
            {
                int current = bytes[y, x];
                int previous = bytes[y + 1, x];
                if (!JudgeEdge(previous, current)) { continue; }
                if (!IsThicknessStableYBackward(bytes, x, y, previous, thickness)) { continue; }

                edgeX = x;
                edgeY = y + 1;
                return true;
            }

            return false;
        }

        private bool IsThicknessStableXForward(byte[,] bytes, int y, int x, int previous, int thickness)
        {
            return IsThicknessStable(bytes, x, y, previous, thickness, 1, 0, 2, thickness + 1);
        }

        private bool IsThicknessStableXBackward(byte[,] bytes, int y, int x, int previous, int thickness)
        {
            return IsThicknessStable(bytes, x, y, previous, thickness, -1, 0, 1, thickness);
        }

        private bool IsThicknessStableYForward(byte[,] bytes, int x, int y, int previous, int thickness)
        {
            return IsThicknessStable(bytes, x, y, previous, thickness, 0, 1, 1, thickness - 1);
        }

        private bool IsThicknessStableYBackward(byte[,] bytes, int x, int y, int previous, int thickness)
        {
            return IsThicknessStable(bytes, x, y, previous, thickness, 0, -1, 1, thickness - 1);
        }

        private bool IsThicknessStable(
            byte[,] bytes,
            int x,
            int y,
            int previous,
            int thickness,
            int deltaX,
            int deltaY,
            int startOffset,
            int endOffset)
        {
            for (int offset = startOffset; offset <= endOffset; offset++)
            {
                if (!JudgeEdge(previous, bytes[y + deltaY * offset, x + deltaX * offset]))
                {
                    return false;
                }
            }

            return true;
        }

        private void AddEdge(ConcurrentBag<LineGaugeEdge> edges, int x, int y, Rect roi, bool applyRoiOffset)
        {
            if (applyRoiOffset)
            {
                x += roi.X;
                y += roi.Y;
            }

            edges.Add(new LineGaugeEdge(new OpenCvSharp.Point(x, y)));
        }

        private List<LineGaugeEdge> SortEdges(ConcurrentBag<LineGaugeEdge> edges)
        {
            if (edges.Count <= 2)
            {
                return new List<LineGaugeEdge>();
            }

            switch (property.PRJ_DIR)
            {
                case PROJECTION_DIR.X_LTOR:
                case PROJECTION_DIR.X_RTOL:
                    return edges.OrderBy(edge => edge.MeasPos.Y).ToList();
                case PROJECTION_DIR.Y_TTOB:
                case PROJECTION_DIR.Y_BTOT:
                    return edges.OrderBy(edge => edge.MeasPos.X).ToList();
                default:
                    return edges.ToList();
            }
        }

        private Mat CreatePreparedLineGaugeImage(Rect roi, bool useRoi)
        {
            return CreatePreprocessedImage(roi, useRoi, property);
        }

        private unsafe byte[,] ReadGrayBytes(Mat imageSrc)
        {
            byte[,] bytes = new byte[imageSrc.Rows, imageSrc.Cols];
            Parallel.For(0, imageSrc.Rows,
               i =>
               {
                   byte* ptr = (byte*)imageSrc.Ptr(i).ToPointer();
                   byte[] arr = new byte[imageSrc.Cols];
                   Marshal.Copy((IntPtr)ptr, arr, 0, imageSrc.Cols);

                   for (int j = 0; j < arr.Length; j++)
                   {
                       bytes[i, j] = arr[j];
                   }
               });

            return bytes;
        }

        private bool JudgeEdge(double nGV_Prev, double nGV_Curr)
        {
            bool bEdge = false;
            if (property.PRJ_PORALITY == PROJECTION_POLARITY.ALL)
            {
                if (Math.Abs(nGV_Prev - nGV_Curr) > property.CONTRAST) { bEdge = true; }
            }
            else if (property.PRJ_PORALITY == PROJECTION_POLARITY.BTOW)
            {
                if (-(nGV_Prev - nGV_Curr) > property.CONTRAST) { bEdge = true; }
            }
            else if (property.PRJ_PORALITY == PROJECTION_POLARITY.WTOB)
            {
                if ((nGV_Prev - nGV_Curr) > property.CONTRAST) { bEdge = true; }
            }
            return bEdge;
        }
    }    
}

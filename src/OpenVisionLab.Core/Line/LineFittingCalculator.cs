using OpenVisionLab.Core;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenVisionLab.Core.Geometry2D
{
    /// <summary>Fits y=f(x), or x=f(y) with LineFitY, from finite points with a non-degenerate independent axis.</summary>
    /// <remarks>Coefficients use double arithmetic; PointF inputs/endpoints retain float precision and integer endpoints are truncated.</remarks>
    public class LineFittingCalculator
    {
        public double Slope { get; private set; }
        public double Intercept { get; private set; }

        private void Fit(IEnumerable<OpenCvSharp.Point2f> points)
        {
            CalculateFit(points, point => point.X, point => point.Y, out double slope, out double intercept);
            Slope = slope;
            Intercept = intercept;
        }

        private void Fit(IEnumerable<OpenCvSharp.Point> points)
        {
            CalculateFit(points, point => point.X, point => point.Y, out double slope, out double intercept);
            Slope = slope;
            Intercept = intercept;
        }

        private void Fit(IEnumerable<System.Drawing.Point> points)
        {
            CalculateFit(points, point => point.X, point => point.Y, out double slope, out double intercept);
            Slope = slope;
            Intercept = intercept;
        }

        private void Fit(IEnumerable<System.Drawing.PointF> points)
        {
            CalculateFit(points, point => point.X, point => point.Y, out double slope, out double intercept);
            Slope = slope;
            Intercept = intercept;
        }

        private static void CalculateFit<T>(
            IEnumerable<T> source,
            Func<T, double> getX,
            Func<T, double> getY,
            out double slope,
            out double intercept)
        {
            List<T> points = source.ToList();
            if (points.Count < 2)
            {
                throw new ArgumentException("Line fitting requires at least two points.", nameof(source));
            }

            double meanX = 0.0;
            double meanY = 0.0;
            foreach (T point in points)
            {
                double x = getX(point);
                double y = getY(point);
                if (!IsFinite(x) || !IsFinite(y))
                {
                    throw new ArgumentException("Line fitting points must contain finite coordinates.", nameof(source));
                }

                meanX += x;
                meanY += y;
            }

            meanX /= points.Count;
            meanY /= points.Count;

            double sumXX = 0.0;
            double sumXY = 0.0;
            foreach (T point in points)
            {
                double centeredX = getX(point) - meanX;
                double centeredY = getY(point) - meanY;
                sumXX += centeredX * centeredX;
                sumXY += centeredX * centeredY;
            }

            if (!(sumXX > 0.0) || !IsFinite(sumXX))
            {
                throw new ArgumentException("Line fitting requires at least two distinct X coordinates.", nameof(source));
            }

            slope = sumXY / sumXX;
            intercept = meanY - (slope * meanX);
            if (!IsFinite(slope) || !IsFinite(intercept))
            {
                throw new InvalidOperationException("Line fitting produced a non-finite result.");
            }
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        /// <summary>Returns the square root of the sum of squared vertical residuals, not root mean square error.</summary>
        /// <exception cref="ArgumentException">Fewer than two finite points or no distinct X coordinates.</exception>
        public static double FindLinearLeastSquaresFit(IEnumerable<System.Drawing.PointF> points, out double m, out double b)
        {
            List<PointF> pointList = points.ToList();
            CalculateFit(pointList, point => point.X, point => point.Y, out m, out b);
            return Math.Sqrt(ErrorSquared(pointList, m, b));
        }

        // Return the error squared.
        public static double ErrorSquared(IEnumerable<System.Drawing.PointF> points, double m, double b)
        {
            double total = 0;
            foreach (PointF pt in points)
            {
                double dy = pt.Y - (m * pt.X + b);
                total += dy * dy;
            }
            return total;
        }

        public (System.Drawing.PointF, System.Drawing.PointF) LineFit(IEnumerable<System.Drawing.PointF> points)
        {
            List<PointF> pointList = points.ToList();

            // 직선에 맞추기            
            Fit(pointList);

            // 시작점과 끝점을 찾기 위해 X 좌표의 최소값과 최대값을 사용합니다.
            double minX = pointList.Min(p => p.X);
            double maxX = pointList.Max(p => p.X);

            // 시작점 (x, y) = (minX, lineFitting.Slope * minX + lineFitting.Intercept)
            System.Drawing.PointF startPoint = new System.Drawing.PointF((float)minX, (float)(Slope * minX + Intercept));

            // 끝점 (x, y) = (maxX, lineFitting.Slope * maxX + lineFitting.Intercept)
            System.Drawing.PointF endPoint = new System.Drawing.PointF((float)maxX, (float)(Slope * maxX + Intercept));

            return (startPoint, endPoint);
        }

        public (System.Drawing.PointF, System.Drawing.PointF) LineFit(IEnumerable<System.Drawing.Point> points)
        {
            List<System.Drawing.Point> pointList = points.ToList();

            // 직선에 맞추기            
            Fit(pointList);

            // 시작점과 끝점을 찾기 위해 X 좌표의 최소값과 최대값을 사용합니다.
            double minX = pointList.Min(p => p.X);
            double maxX = pointList.Max(p => p.X);

            // 시작점 (x, y) = (minX, lineFitting.Slope * minX + lineFitting.Intercept)
            System.Drawing.PointF startPoint = new System.Drawing.PointF((float)minX, (float)(Slope * minX + Intercept));

            // 끝점 (x, y) = (maxX, lineFitting.Slope * maxX + lineFitting.Intercept)
            System.Drawing.PointF endPoint = new System.Drawing.PointF((float)maxX, (float)(Slope * maxX + Intercept));

            return (startPoint, endPoint);
        }

        public (System.Drawing.Point, System.Drawing.Point) LinearLeastSquaresFit(List<OpenCvSharp.Point> points, System.Drawing.Size size)
        {
            float Xmin = 0;
            float Xmax = size.Width;
            float Ymax = size.Height;

            // 직선에 맞추기            
            double BestM;
            double BestB;
            List<PointF> PointF = points.ConvertAll(new Converter<OpenCvSharp.Point, PointF>(CommonConverter.CVPointToPointF));
            FindLinearLeastSquaresFit(PointF, out BestM, out BestB);

            double y0 = BestM * Xmin + BestB;
            double y1 = BestM * Xmax + BestB;
            //e.Graphics.DrawLine(thin_pen,
            //    (float)Xmin, (float)y0, (float)Xmax, (float)y1);

            // 시작점 (x, y) = (minX, lineFitting.Slope * minX + lineFitting.Intercept)
            System.Drawing.Point startPoint = new System.Drawing.Point((int)Xmin, (int)(y0));

            // 끝점 (x, y) = (maxX, lineFitting.Slope * maxX + lineFitting.Intercept)
            System.Drawing.Point endPoint = new System.Drawing.Point((int)Xmax, (int)(y1));

            return (startPoint, endPoint);
        }

        public (System.Drawing.Point, System.Drawing.Point) LineFitX(IEnumerable<OpenCvSharp.Point> points)
        {
            List<OpenCvSharp.Point> pointList = points.ToList();

            // 직선에 맞추기            
            Fit(pointList);

            // 시작점과 끝점을 찾기 위해 X 좌표의 최소값과 최대값을 사용합니다.
            double minX = pointList.Min(p => p.X);
            double maxX = pointList.Max(p => p.X);

            // 시작점 (x, y) = (minX, lineFitting.Slope * minX + lineFitting.Intercept)
            System.Drawing.Point startPoint = new System.Drawing.Point((int)minX, (int)(Slope * minX + Intercept));

            // 끝점 (x, y) = (maxX, lineFitting.Slope * maxX + lineFitting.Intercept)
            System.Drawing.Point endPoint = new System.Drawing.Point((int)maxX, (int)(Slope * maxX + Intercept));

            return (startPoint, endPoint);
        }

        public (System.Drawing.Point, System.Drawing.Point) LineFitY(IEnumerable<OpenCvSharp.Point> points)
        {
            List<OpenCvSharp.Point> pointList = points.ToList();

            // 직선에 맞추기            
            Fit(pointList.Select(p => new System.Drawing.Point(p.Y, p.X)));

            // 시작점과 끝점을 찾기 위해 X 좌표의 최소값과 최대값을 사용합니다.
            double minY = pointList.Min(p => p.Y);
            double maxY = pointList.Max(p => p.Y);

            System.Drawing.Point startPointYX = new System.Drawing.Point((int)(Slope * minY + Intercept), (int)minY);
            System.Drawing.Point endPointYX = new System.Drawing.Point((int)(Slope * maxY + Intercept), (int)maxY);

            return (startPointYX, endPointYX);
        }
    }
}

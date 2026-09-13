using System;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using OpenCvSharp;
namespace OpenVisionLab.Core
{
    public static class CConverter
    {              
        public static string RoiToString(OpenCvSharp.Rect ROI) => string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3}", ROI.X, ROI.Y, ROI.Width, ROI.Height);
        public static string RoiToString(Rectangle ROI) => string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3}", ROI.X, ROI.Y, ROI.Width, ROI.Height);
        public static System.Drawing.Point CVPointToPoint(OpenCvSharp.Point pt) => new System.Drawing.Point(pt.X, pt.Y);
        public static PointF CVPointToPointF(OpenCvSharp.Point pt) => new PointF((float)pt.X, (float)pt.Y);
        public static System.Drawing.PointF PointToPointF(System.Drawing.Point pt) => new PointF(pt.X, pt.Y);
        public static System.Drawing.Point PointFToPoint(System.Drawing.PointF pt) => new System.Drawing.Point((int)pt.X, (int)pt.Y);
        public static OpenCvSharp.Point PointToCVPoint(System.Drawing.Point pt) => new OpenCvSharp.Point(pt.X, pt.Y);
        public static byte IntToByte(int nValue) => Convert.ToByte(nValue);
        public static string PointToString(System.Drawing.Point pt) => string.Format(CultureInfo.InvariantCulture, "{0},{1}", pt.X, pt.Y);
        public static string PointFToString(PointF pt) => string.Format(CultureInfo.InvariantCulture, "{0},{1}", pt.X, pt.Y);
        public static string CVPointToString(OpenCvSharp.Point pt) => string.Format(CultureInfo.InvariantCulture, "{0},{1}", pt.X, pt.Y);
        public static OpenCvSharp.Point CeterFromRect(OpenCvSharp.Rect rt) => new OpenCvSharp.Point(rt.X + rt.Width / 2, rt.Y + rt.Height / 2);
        public static System.Drawing.Point CeterFromRectangle(Rectangle rt) => new System.Drawing.Point(rt.X + rt.Width / 2, rt.Y + rt.Height / 2);
        public static OpenCvSharp.Point RectOfCenter(Rect rt) => new OpenCvSharp.Point(rt.X + rt.Width / 2, rt.Y + rt.Height / 2);
        public static OpenCvSharp.Point RectangleOfCenter(OpenCvSharp.Rect rt) => new OpenCvSharp.Point(rt.X + rt.Width / 2, rt.Y + rt.Height / 2);
        public static System.Drawing.Point RectangleOfCenter(Rectangle rt) => new System.Drawing.Point(rt.X + rt.Width / 2, rt.Y + rt.Height / 2);
        public static PointF Center(this Rectangle rect) => new PointF((float)rect.Left + (float)rect.Width / 2f, (float)rect.Top + (float)rect.Height / 2f);    
        public static PointF Center(this RectangleF rect) => new PointF(rect.Left + rect.Width / 2f, rect.Top + rect.Height / 2f);  
        public static string ColorToString(Color cr) => string.Format(CultureInfo.InvariantCulture, "{0},{1},{2}", cr.R, cr.G, cr.B);
        public static Rectangle RectToRectangle(Rect rect) => new Rectangle() { X = rect.X, Y = rect.Y, Width = rect.Width, Height = rect.Height };    
        public static Rect RectangleToRect(Rectangle rectangle) => new Rect() { X = rectangle.X, Y = rectangle.Y, Width = rectangle.Width, Height = rectangle.Height };    
        public static Rect2f RectangleToRect(RectangleF rectangle) => new Rect2f() { X = rectangle.X, Y = rectangle.Y, Width = rectangle.Width, Height = rectangle.Height };    
        public static string ShortToBinaryString(short shValue) => Convert.ToString(shValue, 2).PadLeft(8, '0');
        public static string IntToBinaryString(int nValue, int nZeroCount) => Convert.ToString(nValue, 2).PadLeft(nZeroCount, '0');
        public static string RectToString(Rectangle rt) => string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3}", rt.X, rt.Y, rt.Width, rt.Height);
        public static OpenCvSharp.Rect RectToCVRect(System.Drawing.Rectangle rt) => new OpenCvSharp.Rect(rt.X, rt.Y, rt.Width, rt.Height);
        public static System.Drawing.Rectangle CVRectToRect(OpenCvSharp.Rect rt) => new System.Drawing.Rectangle(rt.X, rt.Y, rt.Width, rt.Height);

        public static OpenCvSharp.Point CenterofRect(Rect rt) => new OpenCvSharp.Point(rt.X + rt.Width / 2, rt.Y + rt.Height / 2);
    
        public static Rectangle StringToRectangle(string strROI)
        {
            Rectangle ROI = new Rectangle();
            string[] strSplit = strROI.Split(',');

            if (strSplit.Length == 4)
            {
                int nX = int.Parse(strSplit[0], CultureInfo.InvariantCulture);
                int nY = int.Parse(strSplit[1], CultureInfo.InvariantCulture);
                int nW = int.Parse(strSplit[2], CultureInfo.InvariantCulture);
                int nH = int.Parse(strSplit[3], CultureInfo.InvariantCulture);

                ROI = new Rectangle(nX, nY, nW, nH);
            }

            return ROI;
        }

        public static Rect StringToRect(string strROI)
        {
            Rect ROI = new Rect();
            string[] strSplit = strROI.Split(',');

            if (strSplit.Length == 4)
            {
                int nX = int.Parse(strSplit[0], CultureInfo.InvariantCulture);
                int nY = int.Parse(strSplit[1], CultureInfo.InvariantCulture);
                int nW = int.Parse(strSplit[2], CultureInfo.InvariantCulture);
                int nH = int.Parse(strSplit[3], CultureInfo.InvariantCulture);

                ROI = new Rect(nX, nY, nW, nH);
            }

            return ROI;
        }

        public static System.Drawing.Point StringToPoint(string strPoint)
        {
            string[] strPointSplit = strPoint.Split(',');
            System.Drawing.Point pt = new System.Drawing.Point(0, 0);

            if (strPointSplit.Length == 2)
            {
                string strX = strPointSplit[0].Trim();
                string strY = strPointSplit[1].Trim();

                int nX = int.Parse(strX, CultureInfo.InvariantCulture);
                int nY = int.Parse(strY, CultureInfo.InvariantCulture);

                pt = new System.Drawing.Point(nX, nY);
            }

            return pt;
        }

        public static System.Drawing.PointF StringToPointF(string strPoint)
        {
            string[] strPointSplit = strPoint.Split(',');
            System.Drawing.PointF pt = new PointF(0, 0);

            if (strPointSplit.Length == 2)
            {
                string strX = strPointSplit[0].Trim();
                string strY = strPointSplit[1].Trim();

                float fX = float.Parse(strX, CultureInfo.InvariantCulture);
                float fY = float.Parse(strY, CultureInfo.InvariantCulture);

                pt = new PointF(fX, fY);
            }

            return pt;
        }

        public static OpenCvSharp.Point StringToCVPoint(string strPoint)
        {
            string[] strPointSplit = strPoint.Split(',');
            OpenCvSharp.Point pt = new OpenCvSharp.Point(0, 0);

            if (strPointSplit.Length == 2)
            {
                string strX = strPointSplit[0].Trim();
                string strY = strPointSplit[1].Trim();

                int nX = int.Parse(strX, CultureInfo.InvariantCulture);
                int nY = int.Parse(strY, CultureInfo.InvariantCulture);

                pt = new OpenCvSharp.Point(nX, nY);
            }

            return pt;
        }

        public static OpenCvSharp.Rect StringToCVRect(string strRect)
        {
            string[] sRT = strRect.Split(',');
            if (sRT.Length == 4)
            {
                int nX = int.Parse(sRT[0], CultureInfo.InvariantCulture);
                int nY = int.Parse(sRT[1], CultureInfo.InvariantCulture);
                int nW = int.Parse(sRT[2], CultureInfo.InvariantCulture);
                int nH = int.Parse(sRT[3], CultureInfo.InvariantCulture);

                return new OpenCvSharp.Rect(nX, nY, nW, nH);
            }


            return new OpenCvSharp.Rect();
        }

    }
}

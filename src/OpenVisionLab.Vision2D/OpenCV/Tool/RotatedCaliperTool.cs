using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using OpenCvSharp;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Result;

namespace OpenVisionLab.Vision2D.Tool
{
    /// <summary>Fits a line or circle to explicitly sampled subpixel edges in a caller-owned CV_8UC1 Mat.</summary>
    /// <remarks>Success means geometry was established, not quality acceptance. Configure/Execute/Dispose must be serialized per instance.
    /// No smoothing, padding, implicit depth/unit conversion, or physical calibration is performed.</remarks>
    public sealed class RotatedCaliperTool : OpenCvAlgorithmBase, ICancellableVisionTool
    {
        public RotatedCaliperToolProperty property { get; private set; } = new RotatedCaliperToolProperty();
        // Per-instance test seam: one completed station before cancellation, no public progress or concurrency contract.
        internal Action SamplingCheckpoint;
        public CaliperMeasurementEvidence LatestMeasurement { get; private set; }

        public void SetProperty(RotatedCaliperToolProperty value) => property = value == null ? throw new ArgumentNullException(nameof(value)) : value.Copy();
        public override VisionToolResult Execute(Mat source) => Execute(source, CancellationToken.None);
        public VisionToolResult Execute(Mat source, CancellationToken cancellationToken)
        {
            ResetMeasurement();
            try
            {
                VisionToolResult result = ExecuteWithCancellation(source, cancellationToken);
                if (result.Exception != null) ResetMeasurement();
                return result;
            }
            catch { ResetMeasurement(); throw; }
        }

        public override void Run()
        {
            ResetMeasurement();
            try
            {
                ExecutionCancellationToken.ThrowIfCancellationRequested();
                if (!TryValidateBeforeRun(out _, out string message)) throw new InvalidOperationException(message);
                RotatedCaliperToolProperty settings = property.Copy();
                CaliperStationEvidence[] stations = new CaliperStationEvidence[settings.StationCount];
                for (int id = 0; id < stations.Length; id++)
                {
                    ExecutionCancellationToken.ThrowIfCancellationRequested();
                    double angle = (settings.Shape == CaliperShape.Line ? settings.AngleDegrees : settings.StartAngleDegrees + id * settings.AngularPitchDegrees) * Math.PI / 180;
                    Point2d tangent = new Point2d(Math.Cos(angle), Math.Sin(angle));
                    Point2d normal = settings.Shape == CaliperShape.Line ? new Point2d(-tangent.Y, tangent.X) : tangent;
                    double offset = (id - (stations.Length - 1) / 2.0) * settings.StationPitch;
                    Point2d center = settings.Shape == CaliperShape.Line
                        ? new Point2d(settings.CenterX + offset * tangent.X, settings.CenterY + offset * tangent.Y)
                        : new Point2d(settings.CenterX + settings.Radius * normal.X, settings.CenterY + settings.Radius * normal.Y);
                    if (settings.Shape == CaliperShape.Circle) tangent = new Point2d(-normal.Y, normal.X);
                    stations[id] = SampleStation(id, center, normal, tangent, settings);
                    if (id == 0)
                    {
                        SamplingCheckpoint?.Invoke();
                        ExecutionCancellationToken.ThrowIfCancellationRequested();
                    }
                }
                List<CaliperStationEvidence> used = stations.Where(s => s.EdgePoint.HasValue).ToList();
                List<int> excluded = new List<int>();
                CaliperLineGeometry line = null;
                CaliperCircleGeometry circle = null;
                double[] residuals = null;
                string reason = "Insufficient observed edge points.";
                bool fitted = false;
                for (int iteration = 0; ; iteration++)
                {
                    ExecutionCancellationToken.ThrowIfCancellationRequested();
                    fitted = TryFit(used, settings, out line, out circle, out residuals);
                    if (!fitted) { reason = "Insufficient or ill-conditioned edge geometry."; break; }
                    reason = string.Empty;
                    if (!settings.OutlierRemovalEnabled || residuals.Max() <= settings.OutlierResidualCutoff) break;
                    if (iteration >= settings.MaximumOutlierIterations) { reason = "Outlier iteration limit reached; residuals remain observable."; break; }
                    // Remove one declared worst residual; exact ties use the stable original station ID.
                    int worst = Enumerable.Range(0, used.Count).OrderByDescending(i => residuals[i]).ThenBy(i => used[i].StationId).First();
                    excluded.Add(used[worst].StationId);
                    used.RemoveAt(worst);
                }
                double? rms = null, max = null, coverage = null, gap = null;
                CaliperAcceptance acceptance = CaliperAcceptance.NotEvaluated;
                if (fitted)
                {
                    rms = Math.Sqrt(residuals.Sum(r => r * r) / residuals.Length);
                    max = residuals.Max();
                    if (circle != null)
                    {
                        double[] angles = used.Select(s => AngleOf(s.EdgePoint.Value, circle)).OrderBy(a => a).ToArray();
                        double maximumGap = angles[0] + 360 - angles[angles.Length - 1];
                        for (int i = 1; i < angles.Length; i++) maximumGap = Math.Max(maximumGap, angles[i] - angles[i - 1]);
                        gap = maximumGap; coverage = 360 - maximumGap;
                    }
                    if (settings.QualityGateEnabled)
                    {
                        bool accepted = used.Count >= settings.MinimumPointCount && rms.Value <= settings.MaximumRmsResidual && max.Value <= settings.MaximumResidual
                            && (circle == null || (coverage.Value >= settings.MinimumCircleCoverageDegrees && gap.Value <= settings.MaximumCircleGapDegrees));
                        acceptance = accepted ? CaliperAcceptance.Accepted : CaliperAcceptance.Rejected;
                        if (!accepted) reason = "Explicit point count, residual or circle coverage/gap quality gate rejected the fit.";
                    }
                }
                else { line = null; circle = null; }
                ExecutionCancellationToken.ThrowIfCancellationRequested();
                ReplaceResultImage(imageSource.Clone());
                LatestMeasurement = new CaliperMeasurementEvidence(imageSource.Size(), settings.Shape, stations, used.Select(s => s.StationId).ToArray(),
                    excluded.ToArray(), line, circle, rms, max, coverage, gap, acceptance, reason);
                ExecutionCancellationToken.ThrowIfCancellationRequested();
            }
            catch { ResetMeasurement(); throw; }
        }

        protected override bool TryValidateBeforeRun(out VisionToolErrorCode errorCode, out string message)
        {
            if (!base.TryValidateBeforeRun(out errorCode, out message)) return false;
            if (imageSource == null || imageSource.IsDisposed || imageSource.Empty() || imageSource.Type() != MatType.CV_8UC1)
            { errorCode = VisionToolErrorCode.InputImageInvalid; message = "Caliper requires a nonempty CV_8UC1 Mat; conversion is explicit at the Host."; return false; }
            bool valid = ValidSettings(property);
            errorCode = valid ? VisionToolErrorCode.None : VisionToolErrorCode.InvalidParameter;
            message = valid ? string.Empty : "Caliper requires finite valid geometry, sampling, selection and quality settings; at most 4,000,000 samples; no common preprocessing/ROI/masks.";
            return valid;
        }

        internal static bool ValidSettings(RotatedCaliperToolProperty p)
        {
            if (p == null) return false;
            double[] values = { p.CenterX, p.CenterY, p.AngleDegrees, p.Radius, p.StartAngleDegrees, p.AngularPitchDegrees, p.StationPitch,
                p.ScanLength, p.TangentWidth, p.MinimumTangentCoverage, p.MinimumProfileCoverage, p.MinimumContrast, p.TieEpsilon, p.ConditioningEpsilon, p.OutlierResidualCutoff,
                p.MaximumRmsResidual, p.MaximumResidual, p.MinimumCircleCoverageDegrees, p.MaximumCircleGapDegrees };
            double extent = p.ScanLength / 2 + p.TangentWidth / 2 + (p.Shape == CaliperShape.Line ? p.StationPitch * (p.StationCount - 1) / 2 : p.Radius);
            bool valid = values.All(Finite) && Finite(extent) && Finite(p.CenterX - extent) && Finite(p.CenterX + extent)
                && Finite(p.CenterY - extent) && Finite(p.CenterY + extent) && Enum.IsDefined(typeof(CaliperShape), p.Shape) && Enum.IsDefined(typeof(CaliperPolarity), p.Polarity)
                && Enum.IsDefined(typeof(CaliperSelection), p.Selection) && p.StationCount >= 1 && p.StationCount <= 10000 && p.StationPitch > 0
                && p.ScanSampleCount >= 5 && p.ScanSampleCount <= 4096 && p.ScanLength > 0 && p.TangentSampleCount >= 1 && p.TangentSampleCount <= 1024
                && p.TangentWidth >= 0 && p.MinimumTangentCoverage > 0 && p.MinimumTangentCoverage <= 1
                && p.MinimumProfileCoverage > 0 && p.MinimumProfileCoverage <= 1 && p.MinimumContrast > 0
                && p.TieEpsilon >= 0 && p.ConditioningEpsilon > 0 && p.ConditioningEpsilon < 1 && p.OutlierResidualCutoff > 0
                && p.MaximumOutlierIterations >= 0 && p.MaximumOutlierIterations <= 10000 && p.MinimumPointCount >= 1 && p.MaximumRmsResidual >= 0
                && p.MaximumResidual >= 0 && p.MinimumCircleCoverageDegrees >= 0 && p.MinimumCircleCoverageDegrees <= 360
                && p.MaximumCircleGapDegrees >= 0 && p.MaximumCircleGapDegrees <= 360
                && (long)p.StationCount * p.ScanSampleCount * p.TangentSampleCount <= 4000000
                && Finite(p.StationPitch * (p.StationCount - 1)) && Finite(p.AngleDegrees * Math.PI / 180) && Finite(p.StartAngleDegrees * Math.PI / 180)
                && (p.Shape != CaliperShape.Circle || (p.Radius > 0 && p.AngularPitchDegrees > 0 && p.AngularPitchDegrees * (p.StationCount - 1) < 360));
            if (p.USE_THRESHOLD || p.USE_ADAPTIVE_THRESHOLD || p.USE_BITWISENOT || p.USE_ROI || p.USE_MULTI_ROI
                || (p.CvMASKS != null && p.CvMASKS.Count > 0)) valid = false;
            return valid;
        }

        protected override bool TryValidateAfterRun(out VisionToolErrorCode errorCode, out string message)
        {
            bool established = LatestMeasurement != null && LatestMeasurement.MeasurementEstablished;
            errorCode = established ? VisionToolErrorCode.None : LatestMeasurement == null || LatestMeasurement.OriginalPointIds.Count == 0
                ? VisionToolErrorCode.CaliperNoEdge : VisionToolErrorCode.CaliperGeometryDegenerate;
            message = established ? string.Empty : LatestMeasurement?.Reason ?? "Caliper did not complete.";
            return established;
        }
        protected override void AttachResultEvidence(VisionToolResult result) => result.CaliperMeasurement = LatestMeasurement;
        protected override IDictionary<string, double> CollectMetrics()
        {
            IDictionary<string, double> metrics = base.CollectMetrics();
            if (LatestMeasurement == null) return metrics;
            metrics["Caliper.ObservedPointCount"] = LatestMeasurement.OriginalPointIds.Count;
            metrics["Caliper.UsedPointCount"] = LatestMeasurement.UsedPointIds.Count;
            metrics["Caliper.MeasurementEstablished"] = LatestMeasurement.MeasurementEstablished ? 1 : 0;
            if (LatestMeasurement.RmsResidual.HasValue) metrics["Caliper.RmsResidualPx"] = LatestMeasurement.RmsResidual.Value;
            if (LatestMeasurement.MaxResidual.HasValue) metrics["Caliper.MaxResidualPx"] = LatestMeasurement.MaxResidual.Value;
            if (LatestMeasurement.Acceptance != CaliperAcceptance.NotEvaluated)
                metrics["Caliper.QualityAccepted"] = LatestMeasurement.Acceptance == CaliperAcceptance.Accepted ? 1D : 0D;
            if (LatestMeasurement.Circle != null)
            {
                metrics["Caliper.RadiusPx"] = LatestMeasurement.Circle.Radius;
                metrics["Caliper.DiameterPx"] = LatestMeasurement.Circle.Radius * 2D;
                metrics["Caliper.CircleCoverageDegrees"] = LatestMeasurement.CircleCoverageDegrees.Value;
                metrics["Caliper.MaximumCircleGapDegrees"] = LatestMeasurement.MaximumCircleGapDegrees.Value;
            }
            if (LatestMeasurement.Line != null)
            {
                metrics["Caliper.LineCenterXPx"] = LatestMeasurement.Line.CenterX;
                metrics["Caliper.LineCenterYPx"] = LatestMeasurement.Line.CenterY;
                metrics["Caliper.LineNormalX"] = LatestMeasurement.Line.NormalX;
                metrics["Caliper.LineNormalY"] = LatestMeasurement.Line.NormalY;
                metrics["Caliper.LineSignedDistancePx"] = LatestMeasurement.Line.SignedDistance;
            }
            return metrics;
        }
        protected override IEnumerable<VisionToolOverlay> CollectOverlays()
        {
            if (LatestMeasurement == null) yield break;
            VisionToolOverlay points = new VisionToolOverlay { Kind = VisionToolOverlayKind.Points, Label = "Caliper observed edges / input px" };
            foreach (CaliperStationEvidence station in LatestMeasurement.Stations)
                if (station.EdgePoint.HasValue) points.Points.Add(new PointF((float)station.EdgePoint.Value.X, (float)station.EdgePoint.Value.Y));
            yield return points;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { LatestMeasurement = null; SamplingCheckpoint = null; }
            base.Dispose(disposing);
        }
        private void ResetMeasurement()
        {
            LatestMeasurement = null;
            if (imageResult != null && !imageResult.IsDisposed) imageResult.Dispose();
            imageResult = null;
        }

        private CaliperStationEvidence SampleStation(int id, Point2d center, Point2d normal, Point2d tangent, RotatedCaliperToolProperty p)
        {
            int valid = 0, validBins = 0;
            double[] profile = new double[p.ScanSampleCount];
            bool[] observed = new bool[profile.Length];
            double step = p.ScanLength / (profile.Length - 1);
            int minimum = (int)Math.Ceiling(p.MinimumTangentCoverage * p.TangentSampleCount);
            for (int j = 0; j < profile.Length; j++)
            {
                ExecutionCancellationToken.ThrowIfCancellationRequested();
                double along = -p.ScanLength / 2 + j * step, sum = 0;
                int count = 0;
                for (int k = 0; k < p.TangentSampleCount; k++)
                {
                    double across = p.TangentSampleCount == 1 ? 0 : -p.TangentWidth / 2 + k * (p.TangentWidth / (p.TangentSampleCount - 1));
                    double x = center.X + along * normal.X + across * tangent.X, y = center.Y + along * normal.Y + across * tangent.Y;
                    if (TrySample(x, y, out double sample)) { sum += sample; count++; }
                }
                valid += count;
                if (count >= minimum) { profile[j] = sum / count; observed[j] = true; validBins++; }
            }
            if ((double)validBins / profile.Length < p.MinimumProfileCoverage)
                return new CaliperStationEvidence(id, center, normal, p.ScanSampleCount * p.TangentSampleCount, valid, validBins, CaliperStationStatus.InsufficientCoverage);
            double[] gradient = new double[profile.Length - 1];
            bool[] derivative = new bool[gradient.Length];
            List<int> peaks = new List<int>();
            for (int j = 0; j < gradient.Length; j++)
            {
                derivative[j] = observed[j] && observed[j + 1];
                if (!derivative[j]) continue;
                double difference = profile[j + 1] - profile[j];
                gradient[j] = p.Polarity == CaliperPolarity.Rising ? difference : p.Polarity == CaliperPolarity.Falling ? -difference : Math.Abs(difference);
            }
            for (int j = 0; j < gradient.Length; j++)
            {
                if (!derivative[j] || gradient[j] < p.MinimumContrast) continue;
                if (j > 0 && derivative[j - 1] && gradient[j - 1] > gradient[j] + p.TieEpsilon) continue;
                if (j + 1 < gradient.Length && derivative[j + 1] && gradient[j + 1] > gradient[j] + p.TieEpsilon) continue;
                peaks.Add(j);
            }
            CaliperStationStatus status = validBins < 4 ? CaliperStationStatus.InsufficientCoverage : CaliperStationStatus.NoEdge;
            double? raw = null, delta = null, contrast = null;
            Point2d? point = null;
            if (peaks.Count > 0)
            {
                int peak = p.Selection == CaliperSelection.First ? peaks[0] : p.Selection == CaliperSelection.Last ? peaks[peaks.Count - 1]
                    : peaks.OrderByDescending(i => gradient[i]).ThenBy(i => i).First();
                raw = peak + 0.5; contrast = profile[peak + 1] - profile[peak];
                bool supported = peak > 0 && peak + 1 < gradient.Length && derivative[peak - 1] && derivative[peak + 1];
                bool flat = supported && (Math.Abs(gradient[peak] - gradient[peak - 1]) <= p.TieEpsilon || Math.Abs(gradient[peak] - gradient[peak + 1]) <= p.TieEpsilon);
                bool ambiguous = p.Selection == CaliperSelection.Strongest && peaks.Any(i => i != peak && Math.Abs(i - peak) > 1 && Math.Abs(gradient[i] - gradient[peak]) <= p.TieEpsilon);
                if (ambiguous) status = CaliperStationStatus.Ambiguous;
                else if (!supported) status = CaliperStationStatus.UnsupportedPeak;
                else if (flat) status = CaliperStationStatus.FlatPeak;
                else
                {
                    double curvature = gradient[peak - 1] - 2 * gradient[peak] + gradient[peak + 1];
                    double offset = 0.5 * (gradient[peak - 1] - gradient[peak + 1]) / curvature;
                    if (!Finite(curvature) || curvature >= 0 || !Finite(offset) || Math.Abs(offset) > 0.5) status = CaliperStationStatus.FlatPeak;
                    else
                    {
                        double along = -p.ScanLength / 2 + (raw.Value + offset) * step;
                        Point2d edge = new Point2d(center.X + along * normal.X, center.Y + along * normal.Y);
                        if (Finite(edge.X) && Finite(edge.Y)) { point = edge; delta = offset; status = CaliperStationStatus.EdgeFound; }
                        else status = CaliperStationStatus.UnsupportedPeak;
                    }
                }
            }
            return new CaliperStationEvidence(id, center, normal, p.ScanSampleCount * p.TangentSampleCount, valid, validBins, status, raw, delta, contrast, point);
        }

        private bool TrySample(double x, double y, out double sample)
        {
            sample = 0;
            // Bounds are checked before floor/int conversion; all four neighbors are mandatory even at exact integer coordinates.
            if (!Finite(x) || !Finite(y) || x < 0 || y < 0 || x >= imageSource.Width - 1 || y >= imageSource.Height - 1) return false;
            int left = (int)Math.Floor(x), top = (int)Math.Floor(y);
            double dx = x - left, dy = y - top;
            sample = (1 - dy) * ((1 - dx) * imageSource.At<byte>(top, left) + dx * imageSource.At<byte>(top, left + 1))
                + dy * ((1 - dx) * imageSource.At<byte>(top + 1, left) + dx * imageSource.At<byte>(top + 1, left + 1));
            return true;
        }

        private static bool TryFit(List<CaliperStationEvidence> points, RotatedCaliperToolProperty p,
            out CaliperLineGeometry line, out CaliperCircleGeometry circle, out double[] residuals)
        {
            line = null; circle = null; residuals = null;
            if (points.Count < (p.Shape == CaliperShape.Line ? 2 : 3)) return false;
            double x = points.Average(s => s.EdgePoint.Value.X), y = points.Average(s => s.EdgePoint.Value.Y);
            double scale = points.Max(s => Math.Max(Math.Abs(s.EdgePoint.Value.X - x), Math.Abs(s.EdgePoint.Value.Y - y)));
            if (!Finite(scale) || scale <= 0) return false;
            double xx = 0, xy = 0, yy = 0, bx = 0, by = 0, qMean = 0;
            foreach (CaliperStationEvidence station in points)
            {
                double u = (station.EdgePoint.Value.X - x) / scale, v = (station.EdgePoint.Value.Y - y) / scale, q = u * u + v * v;
                xx += u * u / points.Count; xy += u * v / points.Count; yy += v * v / points.Count;
                bx += u * q / points.Count; by += v * q / points.Count; qMean += q / points.Count;
            }
            double trace = xx + yy, separation = Math.Sqrt((xx - yy) * (xx - yy) + 4 * xy * xy);
            if (trace <= 0 || !Finite(trace)) return false;
            if (p.Shape == CaliperShape.Line)
            {
                if (separation <= p.ConditioningEpsilon * trace) return false;
                double angle = 0.5 * Math.Atan2(2 * xy, xx - yy), nx = -Math.Sin(angle), ny = Math.Cos(angle);
                if (ny < 0 || (ny == 0 && nx < 0)) { nx = -nx; ny = -ny; }
                line = new CaliperLineGeometry(x, y, nx, ny);
                residuals = points.Select(s => Math.Abs(nx * (s.EdgePoint.Value.X - x) + ny * (s.EdgePoint.Value.Y - y))).ToArray();
            }
            else
            {
                double largest = (trace + separation) / 2, smallest = (trace - separation) / 2;
                if (smallest <= p.ConditioningEpsilon * largest) return false;
                double determinant = xx * yy - xy * xy;
                double cx = (yy * bx - xy * by) / (2 * determinant), cy = (xx * by - xy * bx) / (2 * determinant);
                double radius = scale * Math.Sqrt(cx * cx + cy * cy + qMean);
                double centerX = x + scale * cx, centerY = y + scale * cy;
                if (!Finite(centerX) || !Finite(centerY) || !Finite(radius) || radius <= 0) return false;
                circle = new CaliperCircleGeometry(centerX, centerY, radius);
                residuals = points.Select(s => Math.Abs(Math.Sqrt(Math.Pow(s.EdgePoint.Value.X - centerX, 2) + Math.Pow(s.EdgePoint.Value.Y - centerY, 2)) - radius)).ToArray();
            }
            return residuals.All(Finite);
        }
        private static double AngleOf(Point2d point, CaliperCircleGeometry circle)
        {
            double angle = Math.Atan2(point.Y - circle.CenterY, point.X - circle.CenterX) * 180 / Math.PI;
            return angle < 0 ? angle + 360 : angle;
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using OpenCvSharp;
using OpenVisionLab.Vision2D.Property;

namespace OpenVisionLab.Vision2D.Result
{
    public enum CaliperAcceptance { NotEvaluated, Accepted, Rejected }
    public enum CaliperStationStatus { EdgeFound, InsufficientCoverage, NoEdge, Ambiguous, UnsupportedPeak, FlatPeak }

    /// <summary>Immutable station observation. Invalid footprints never become interpolated observations.</summary>
    public sealed class CaliperStationEvidence
    {
        internal CaliperStationEvidence(int id, Point2d center, Point2d normal, int expected, int valid, int bins,
            CaliperStationStatus status, double? peak = null, double? offset = null, double? contrast = null, Point2d? point = null)
        {
            StationId = id; StationCenter = center; ScanNormal = normal; ExpectedSamples = expected;
            ValidSamples = valid; ValidProfileBins = bins; Status = status; RawPeakPosition = peak;
            SubpixelOffset = offset; Contrast = contrast; EdgePoint = point;
        }
        public int StationId { get; }
        public Point2d StationCenter { get; }
        public Point2d ScanNormal { get; }
        public int ExpectedSamples { get; }
        public int ValidSamples { get; }
        public int ValidProfileBins { get; }
        public CaliperStationStatus Status { get; }
        /// <summary>Forward-gradient peak coordinate in sample-index units (j+0.5).</summary>
        public double? RawPeakPosition { get; }
        /// <summary>Parabolic offset in sample-index units, bounded to [-0.5,0.5].</summary>
        public double? SubpixelOffset { get; }
        /// <summary>Signed adjacent intensity difference at the selected raw peak.</summary>
        public double? Contrast { get; }
        public Point2d? EdgePoint { get; }
    }

    /// <summary>Input-pixel line: Normal dot point = SignedDistance. Unit normal has positive Y, or positive X when Y is zero.</summary>
    public sealed class CaliperLineGeometry
    {
        internal CaliperLineGeometry(double x, double y, double nx, double ny)
        { CenterX = x; CenterY = y; NormalX = nx; NormalY = ny; SignedDistance = nx * x + ny * y; }
        public double CenterX { get; }
        public double CenterY { get; }
        public double NormalX { get; }
        public double NormalY { get; }
        public double SignedDistance { get; }
    }
    /// <summary>Fitted center and positive radius in input pixels.</summary>
    public sealed class CaliperCircleGeometry
    {
        internal CaliperCircleGeometry(double x, double y, double radius) { CenterX = x; CenterY = y; Radius = radius; }
        public double CenterX { get; }
        public double CenterY { get; }
        public double Radius { get; }
    }

    /// <summary>Managed immutable source-pixel evidence; survives subsequent runs and Tool/result disposal.</summary>
    /// <remarks>MeasurementEstablished is geometric solvability, separate from the explicit Host quality gates. No physical accuracy is implied.</remarks>
    public sealed class CaliperMeasurementEvidence
    {
        internal CaliperMeasurementEvidence(Size size, CaliperShape shape, CaliperStationEvidence[] stations, int[] used, int[] excluded,
            CaliperLineGeometry line, CaliperCircleGeometry circle, double? rms, double? max, double? coverage, double? gap,
            CaliperAcceptance acceptance, string reason)
        {
            InputImageSize = size; Shape = shape; Stations = new ReadOnlyCollection<CaliperStationEvidence>(stations.ToArray());
            OriginalPointIds = new ReadOnlyCollection<int>(stations.Where(s => s.EdgePoint.HasValue).Select(s => s.StationId).ToArray());
            UsedPointIds = new ReadOnlyCollection<int>(used.ToArray()); ExcludedPointIds = new ReadOnlyCollection<int>(excluded.ToArray());
            Line = line; Circle = circle; RmsResidual = rms; MaxResidual = max; CircleCoverageDegrees = coverage;
            MaximumCircleGapDegrees = gap; Acceptance = acceptance; Reason = reason;
        }
        public Size InputImageSize { get; }
        public CaliperShape Shape { get; }
        public bool MeasurementEstablished => Line != null || Circle != null;
        public CaliperAcceptance Acceptance { get; }
        public string Reason { get; }
        public IReadOnlyList<CaliperStationEvidence> Stations { get; }
        public IReadOnlyList<int> OriginalPointIds { get; }
        public IReadOnlyList<int> UsedPointIds { get; }
        public IReadOnlyList<int> ExcludedPointIds { get; }
        public CaliperLineGeometry Line { get; }
        public CaliperCircleGeometry Circle { get; }
        public double? RmsResidual { get; }
        public double? MaxResidual { get; }
        public double? CircleCoverageDegrees { get; }
        public double? MaximumCircleGapDegrees { get; }
    }
}

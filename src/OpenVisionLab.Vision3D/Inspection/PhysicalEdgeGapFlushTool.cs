using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace OpenVisionLab.Vision3D.Inspection
{
    public sealed class PhysicalEdgeGapFlushInput
    {
        public PhysicalEdgeGapFlushInput(double firstCoordinate, double secondCoordinate,
            IReadOnlyDictionary<int, double> firstSurfaceHeights, IReadOnlyDictionary<int, double> secondSurfaceHeights)
        {
            FirstCoordinate = firstCoordinate;
            SecondCoordinate = secondCoordinate;
            FirstSurfaceHeights = firstSurfaceHeights ?? throw new ArgumentNullException(nameof(firstSurfaceHeights));
            SecondSurfaceHeights = secondSurfaceHeights ?? throw new ArgumentNullException(nameof(secondSurfaceHeights));
        }

        public double FirstCoordinate { get; }
        public double SecondCoordinate { get; }
        public IReadOnlyDictionary<int, double> FirstSurfaceHeights { get; }
        public IReadOnlyDictionary<int, double> SecondSurfaceHeights { get; }
    }

    public sealed class PhysicalEdgeGapFlushOptions
    {
        public double AmbiguityEpsilon { get; set; }
        public double ExpectedGap { get; set; }
        public double GapTolerance { get; set; }
        public double ExpectedFlush { get; set; }
        public double FlushTolerance { get; set; }
    }

    public sealed class PhysicalEdgeGapFlushResult
    {
        internal PhysicalEdgeGapFlushResult(double signedGap, double signedFlush, double firstSupport, double secondSupport,
            int validPairCount, double coverageRatio, bool isNearTie, bool isExactContact, bool gapWithinTolerance, bool flushWithinTolerance)
        {
            SignedGap = signedGap;
            SignedFlush = signedFlush;
            FirstSupport = firstSupport;
            SecondSupport = secondSupport;
            ValidPairCount = validPairCount;
            CoverageRatio = coverageRatio;
            IsNearTie = isNearTie;
            IsExactContact = isExactContact;
            GapWithinTolerance = gapWithinTolerance;
            FlushWithinTolerance = flushWithinTolerance;
        }

        public static PhysicalEdgeGapFlushResult Unavailable { get; } = new PhysicalEdgeGapFlushResult(
            double.NaN, double.NaN, double.NaN, double.NaN, 0, 0d, false, false, false, false);

        public double SignedGap { get; }
        public double SignedFlush { get; }
        public double FirstSupport { get; }
        public double SecondSupport { get; }
        public int ValidPairCount { get; }
        public double CoverageRatio { get; }
        public bool IsNearTie { get; }
        public bool IsExactContact { get; }
        public bool GapWithinTolerance { get; }
        public bool FlushWithinTolerance { get; }
    }

    /// <summary>
    /// Measures two caller-selected fitted-edge coordinates and finite surface-height
    /// pairs with the same scanline key. Identity, units, inlier selection, candidate
    /// ambiguity acceptance and final inspection disposition remain caller-owned.
    /// Input dictionaries are borrowed only for the synchronous Execute call.
    /// </summary>
    public sealed class PhysicalEdgeGapFlushTool
    {
        public PhysicalEdgeGapFlushResult Execute(PhysicalEdgeGapFlushInput input, PhysicalEdgeGapFlushOptions options,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (options == null) throw new ArgumentNullException(nameof(options));
            cancellationToken.ThrowIfCancellationRequested();

            var signedGap = input.SecondCoordinate - input.FirstCoordinate;
            var firstValues = new List<double>();
            var secondValues = new List<double>();
            foreach (var scanline in input.FirstSurfaceHeights.Keys.Intersect(input.SecondSurfaceHeights.Keys).OrderBy(index => index))
            {
                var first = input.FirstSurfaceHeights[scanline];
                var second = input.SecondSurfaceHeights[scanline];
                if (!IsFinite(first) || !IsFinite(second))
                {
                    continue;
                }

                firstValues.Add(first);
                secondValues.Add(second);
            }

            var count = firstValues.Count;
            var denominator = Math.Max(1, Math.Max(input.FirstSurfaceHeights.Count, input.SecondSurfaceHeights.Count));
            var firstSupport = count == 0 ? double.NaN : firstValues.Average();
            var secondSupport = count == 0 ? double.NaN : secondValues.Average();
            var coverageRatio = count == 0 ? 0d : (double)count / denominator;
            cancellationToken.ThrowIfCancellationRequested();

            var signedFlush = IsFinite(firstSupport) && IsFinite(secondSupport) ? secondSupport - firstSupport : double.NaN;
            return new PhysicalEdgeGapFlushResult(signedGap, signedFlush, firstSupport, secondSupport, count, coverageRatio,
                Math.Abs(signedGap) <= options.AmbiguityEpsilon, signedGap == 0d,
                IsWithinInclusiveTolerance(signedGap, options.ExpectedGap, options.GapTolerance),
                IsWithinInclusiveTolerance(signedFlush, options.ExpectedFlush, options.FlushTolerance));
        }

        private static bool IsWithinInclusiveTolerance(double actual, double expected, double tolerance) =>
            IsFinite(actual) && IsFinite(expected) && IsFinite(tolerance) && tolerance >= 0d && Math.Abs(actual - expected) <= tolerance;

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

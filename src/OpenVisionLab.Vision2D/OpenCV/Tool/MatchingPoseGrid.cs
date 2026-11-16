using System;
using System.Collections.Generic;
using System.Threading;

namespace OpenVisionLab.Vision2D.Tool
{
    // Shared by image and edge matching; bounds iterations before any native/model allocation.
    internal static class MatchingPoseGrid
    {
        internal const long MaximumModels = 10000;

        internal static bool TryValidate(bool useAngle, double minimumAngle, double maximumAngle, double angleStep,
            bool useCoarse, double coarseStep, bool useScale, double minimumScale, double maximumScale, double scaleStep,
            out VisionToolErrorCode errorCode, out string message)
        {
            long angles = 1, coarse = 0, scales = 1;
            if (useAngle && (!TryCount(minimumAngle, maximumAngle, angleStep, false, out angles)
                || (useCoarse && !TryCount(minimumAngle, maximumAngle, coarseStep, false, out coarse))))
            {
                errorCode = VisionToolErrorCode.MatchingInvalidAngleStep;
                message = "Active angle grids require finite bounds, a positive step at six-decimal precision and indices strictly inside Int32 limits.";
                return false;
            }
            if (useScale && !TryCount(minimumScale, maximumScale, scaleStep, true, out scales))
            {
                errorCode = VisionToolErrorCode.MatchingInvalidScale;
                message = "Active scale grids require finite positive bounds at six-decimal precision, a positive finite step and indices strictly inside Int32 limits.";
                return false;
            }
            // Division avoids overflow even for the full signed-index range. Count includes
            // special/fallback poses conservatively; rounding duplicates never enlarge the budget.
            if (angles + coarse > MaximumModels || scales > MaximumModels
                || angles + coarse > MaximumModels / Math.Max(1, scales))
            {
                errorCode = VisionToolErrorCode.InvalidParameter;
                message = $"Matching pose budget exceeded. AngleCount<={angles}, CoarseCount<={coarse}, ScaleCount<={scales}, MaximumModels={MaximumModels}.";
                return false;
            }
            errorCode = VisionToolErrorCode.None;
            message = string.Empty;
            return true;
        }

        private static bool TryCount(double minimum, double maximum, double step, bool scale, out long count)
        {
            count = 0;
            if (!IsFinite(minimum) || !IsFinite(maximum) || !IsFinite(step) || step <= 0D || Math.Round(step, 6) <= 0D
                || (scale && (Math.Round(minimum, 6) <= 0D || Math.Round(maximum, 6) <= 0D))) return false;
            double low = Math.Min(minimum, maximum), high = Math.Max(minimum, maximum);
            double tolerance = scale ? 0.000000001D : 0D;
            double first = Math.Ceiling((low - tolerance) / step), last = Math.Floor((high + tolerance) / step);
            if (first <= int.MinValue || first >= int.MaxValue || last <= int.MinValue || last >= int.MaxValue) return false;
            count = Math.Max(0L, (long)last - (long)first + 1L);
            double special = scale ? 1D : 0D;
            if (low <= special && special <= high && (count == 0 || Math.Round(special / step) * step != special)) count++;
            if (scale) count = Math.Max(1L, count); // Matching's existing minimum-scale fallback.
            return true;
        }

        internal static IEnumerable<double> Angles(double minimum, double maximum, double step, CancellationToken token)
        {
            return Values(minimum, maximum, step, 0D, false, 0D, token);
        }

        internal static IEnumerable<double> Scales(double minimum, double maximum, double step, bool minimumFallback, double tolerance, CancellationToken token)
        {
            return Values(minimum, maximum, step, 1D, minimumFallback, tolerance, token);
        }

        private static IEnumerable<double> Values(double minimum, double maximum, double step, double special, bool minimumFallback, double tolerance, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!TryCount(minimum, maximum, step, special == 1D, out long count) || count > MaximumModels)
                throw new ArgumentOutOfRangeException(nameof(step), "The active pose grid must pass matching validation.");
            double low = Math.Min(minimum, maximum), high = Math.Max(minimum, maximum);
            long first = (long)Math.Ceiling((low - tolerance) / step), last = (long)Math.Floor((high + tolerance) / step);
            HashSet<double> emitted = new HashSet<double>();
            for (long index = first; index <= last; index++)
            {
                token.ThrowIfCancellationRequested(); // Also observe cancellation for rounded duplicates.
                double value = Math.Round(index * step, 6);
                if (value >= low - tolerance && value <= high + tolerance && (special == 0D || value > 0D) && emitted.Add(value)) yield return value;
            }
            if (low <= special && special <= high && emitted.Add(special)) yield return special;
            if (minimumFallback && emitted.Count == 0) yield return Math.Round(low, 6);
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

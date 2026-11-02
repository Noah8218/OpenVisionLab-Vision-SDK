using OpenVisionLab.Vision3D.Inspection;
using System;
using System.Collections.Generic;
using System.Threading;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class PhysicalEdgeGapFlushSmokeSuite
    {
        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase("Physical edge Gap/Flush known paired support and coverage", KnownPair);
            yield return new SmokeCase("Physical edge Gap/Flush reversed roles preserve signed values", ReversedRoles);
            yield return new SmokeCase("Physical edge Gap/Flush no common finite support is unavailable", MissingSupport);
            yield return new SmokeCase("Physical edge Gap/Flush contact and near-tie flags remain distinct", Contact);
            yield return new SmokeCase("Physical edge Gap/Flush inclusive tolerance rejects invalid numeric bounds", Tolerance);
            yield return new SmokeCase("Physical edge Gap/Flush cancellation and invalid input recover", CancellationAndRecovery);
        }

        private static Dictionary<int, double> First() => new Dictionary<int, double>
        {
            [9] = 30d, [3] = 14d, [1] = 10d, [5] = double.NaN
        };

        private static Dictionary<int, double> Second() => new Dictionary<int, double>
        {
            [5] = 99d, [1] = 14d, [3] = 22d
        };

        private static PhysicalEdgeGapFlushOptions Options() => new PhysicalEdgeGapFlushOptions
        {
            ExpectedGap = 3d, ExpectedFlush = 6d, GapTolerance = 0d, FlushTolerance = 0d
        };

        private static void KnownPair()
        {
            var result = new PhysicalEdgeGapFlushTool().Execute(new PhysicalEdgeGapFlushInput(2d, 5d, First(), Second()), Options());
            Require(result.SignedGap == 3d && result.SignedFlush == 6d, "The independently authored gap3/flush6 must be preserved.");
            Require(result.FirstSupport == 12d && result.SecondSupport == 18d && result.ValidPairCount == 2,
                "Only scanlines1/3 have two finite supports; their means are12/18.");
            Require(result.CoverageRatio == 0.5d && result.GapWithinTolerance && result.FlushWithinTolerance,
                "Coverage is2/4; exact authored values meet inclusive zero tolerances.");
        }

        private static void ReversedRoles()
        {
            var result = new PhysicalEdgeGapFlushTool().Execute(new PhysicalEdgeGapFlushInput(5d, 2d, Second(), First()), Options());
            Require(result.SignedGap == -3d && result.SignedFlush == -6d && result.CoverageRatio == 0.5d,
                "Reversing the two roles reverses both signs and preserves coverage.");
            Require(!result.GapWithinTolerance && !result.FlushWithinTolerance, "Reversed signs do not match positive expectations.");
        }

        private static void MissingSupport()
        {
            var tool = new PhysicalEdgeGapFlushTool();
            var result = tool.Execute(new PhysicalEdgeGapFlushInput(2d, 5d,
                new Dictionary<int, double> { [1] = double.PositiveInfinity }, new Dictionary<int, double> { [1] = 2d }), Options());
            Require(result.SignedGap == 3d && double.IsNaN(result.SignedFlush) && double.IsNaN(result.FirstSupport)
                && double.IsNaN(result.SecondSupport) && result.ValidPairCount == 0 && result.CoverageRatio == 0d,
                "A nonfinite pair yields no surface measurement while preserving the coordinate gap.");
            var disjoint = tool.Execute(new PhysicalEdgeGapFlushInput(0d, 1d,
                new Dictionary<int, double> { [1] = 1d }, new Dictionary<int, double> { [2] = 2d }), Options());
            Require(disjoint.ValidPairCount == 0 && double.IsNaN(disjoint.SignedFlush), "Disjoint scanlines must not be paired.");
        }

        private static void Contact()
        {
            var tool = new PhysicalEdgeGapFlushTool();
            var options = Options();
            options.AmbiguityEpsilon = 0.1d;
            var contact = tool.Execute(new PhysicalEdgeGapFlushInput(1d, 1d, First(), Second()), options);
            var near = tool.Execute(new PhysicalEdgeGapFlushInput(1d, 1.05d, First(), Second()), options);
            Require(contact.IsNearTie && contact.IsExactContact && near.IsNearTie && !near.IsExactContact,
                "Only exact equal coordinates establish contact; the caller retains contact acceptance policy.");
        }

        private static void Tolerance()
        {
            var tool = new PhysicalEdgeGapFlushTool();
            var input = new PhysicalEdgeGapFlushInput(2d, 5d, First(), Second());
            var options = Options();
            options.ExpectedGap = 2d; options.GapTolerance = 1d;
            options.ExpectedFlush = 4d; options.FlushTolerance = 2d;
            var boundary = tool.Execute(input, options);
            Require(boundary.GapWithinTolerance && boundary.FlushWithinTolerance, "Both exact tolerance boundaries are inclusive.");
            options.GapTolerance = -1d; options.FlushTolerance = double.PositiveInfinity;
            var invalid = tool.Execute(input, options);
            Require(!invalid.GapWithinTolerance && !invalid.FlushWithinTolerance, "Negative and nonfinite tolerances cannot pass.");
        }

        private static void CancellationAndRecovery()
        {
            var tool = new PhysicalEdgeGapFlushTool();
            var input = new PhysicalEdgeGapFlushInput(2d, 5d, First(), Second());
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                var cancelled = false;
                try { tool.Execute(input, Options(), cancellation.Token); }
                catch (OperationCanceledException) { cancelled = true; }
                Require(cancelled, "A pre-cancelled execution must throw cancellation.");
            }
            var rejected = false;
            try { tool.Execute(null, Options()); }
            catch (ArgumentNullException) { rejected = true; }
            Require(rejected && tool.Execute(input, Options()).SignedFlush == 6d, "A valid call must recover after cancellation and invalid input.");
        }
    }
}

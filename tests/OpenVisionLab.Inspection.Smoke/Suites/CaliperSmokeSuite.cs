using OpenCvSharp;
using OpenVisionLab.FirstInspection;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Pipeline;
using OpenVisionLab.Vision2D.Result;
using OpenVisionLab.Vision2D.Tool;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;
using System;
using System.Collections.Generic;

namespace OpenVisionLab.Inspection.Smoke
{
    // Analytic fixtures use source-pixel coordinates, x right and y down.
    // Line normal tolerance: 1 pixel for rasterized steps (pixel-cell diagonal < 1);
    // circle radial tolerance: 1.5 pixels for rasterization and one-pixel sampling.
    // The axis-aligned five-gradient oracle is exact to 1e-9 before image quantization.
    internal static class CaliperSmokeSuite
    {
        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase("Caliper C01 analytic line normals horizontal vertical and diagonal", TestLines);
            yield return new SmokeCase("Caliper C02 independently authored forward gradient subpixel and polarity", TestSubpixel);
            yield return new SmokeCase("Caliper C03 tied peaks polarity and explicit ordering", TestPeakSelection);
            yield return new SmokeCase("Caliper C04 blank depth and padded ROI equivalence", TestInputBoundaries);
            yield return new SmokeCase("Caliper C05 clipping rejects incomplete four-neighbor samples", TestClipping);
            yield return new SmokeCase("Caliper C05 invalid-bin bridge must not invent an edge", TestInvalidBinBridge);
            yield return new SmokeCase("Caliper C05 normal profile coverage requires explicit partial choice", TestProfileCoverage);
            yield return new SmokeCase("Caliper C06 C07 circle geometry and explicit quality", TestCircles);
            yield return new SmokeCase("Caliper C08 outlier removal cutoff and single refit cap", TestOutlier);
            yield return new SmokeCase("Caliper C08 deterministic equal outlier policy", TestEqualOutliers);
            yield return new SmokeCase("Caliper C11 factory settings and clone contract", TestFactory);
            yield return new SmokeCase("Caliper C07 C10 independent fit degeneracy and translated coordinates", TestFitBoundaries);
            yield return new SmokeCase("Caliper C09 retained result survives absence cancellation and disposal", TestLifetime);
            yield return new SmokeCase("Caliper C09 active sampling cancellation and recovery", TestActiveCancellation);
            yield return new SmokeCase("Caliper C10 invalid finite and allocation boundaries fail safely", TestInvalidGeometry);
        }

        private static RotatedCaliperToolProperty Options(double centerX, double centerY, double angle)
        {
            return new RotatedCaliperToolProperty
            {
                CenterX = centerX, CenterY = centerY, AngleDegrees = angle,
                StationCount = 9, StationPitch = 3, ScanLength = 20, ScanSampleCount = 21,
                TangentWidth = 2, TangentSampleCount = 3, MinimumTangentCoverage = 1,
                Polarity = CaliperPolarity.Rising, Selection = CaliperSelection.Strongest,
                MinimumContrast = 5
            };
        }

        private static void TestLines()
        {
            foreach (double angle in new[] { 0.0, 90.0, 45.0 })
            {
                double radians = angle * Math.PI / 180;
                double nx = angle == 0 ? 0 : -Math.Sin(radians), ny = angle == 90 ? 0 : Math.Cos(radians);
                double offset = nx * 64 + ny * 64;
                using Mat image = LineStep(128, 128, nx, ny, offset);
                using RotatedCaliperTool tool = new RotatedCaliperTool();
                tool.SetProperty(Options(64, 64, angle));
                using VisionToolResult result = tool.Execute(image);
                CaliperMeasurementEvidence evidence = result.CaliperMeasurement;
                Require(result.Success && evidence != null && evidence.Line != null, "Analytic line must produce geometry.");
                Require(evidence.Acceptance == CaliperAcceptance.NotEvaluated, "Absent quality limits must not produce Pass.");
                double dot = evidence.Line.NormalX * nx + evidence.Line.NormalY * ny;
                Require(Math.Abs(dot) >= Math.Cos(2 * Math.PI / 180), "Line normal differs from analytic normal by over two degrees: angle=" + angle + ", actual=" + evidence.Line.NormalX + "," + evidence.Line.NormalY + ", dot=" + dot);
                // Rasterized binary edge lies within one pixel of its analytic boundary.
                double signed = evidence.Line.SignedDistance * Math.Sign(dot);
                RequireApproximately(signed, offset, 1.0, "Line location exceeds rasterization bound.");
            }
        }

        private static void TestSubpixel()
        {
            foreach (bool rising in new[] { true, false })
            foreach (double phase in new[] { -0.25, 0.0, 0.25 })
            {
                const int first = 40;
                using Mat image = FiveGradientProfile(96, 96, first, rising);
                using RotatedCaliperTool tool = new RotatedCaliperTool();
                RotatedCaliperToolProperty options = Options(43 + phase, 48, -90);
                options.Polarity = rising ? CaliperPolarity.Rising : CaliperPolarity.Falling;
                options.TangentWidth = 0;
                options.TangentSampleCount = 1;
                tool.SetProperty(options);
                using VisionToolResult result = tool.Execute(image);
                Require(result.CaliperMeasurement != null, "Authored gradient must retain station evidence.");
                foreach (CaliperStationEvidence station in result.CaliperMeasurement.Stations)
                {
                    // At phase -0.25, adjacent derivative maxima are both 34.5;
                    // the declared flat-peak policy rejects the unresolvable half-bin tie.
                    if (phase == -0.25)
                    {
                        Require(station.Status == CaliperStationStatus.FlatPeak && !station.EdgePoint.HasValue, "Equal adjacent peaks must be rejected as flat.");
                        continue;
                    }
                    Require(result.Success && station.Status == CaliperStationStatus.EdgeFound && station.EdgePoint.HasValue, "Expected subpixel edge at every station.");
                    RequireApproximately(station.SubpixelOffset.Value, phase == 0 ? 0.25 : 0, 1e-9, "Parabolic peak offset differs from the independent quadratic.");
                    RequireApproximately(station.EdgePoint.Value.X, first + 2.75, 1e-9, "Forward-gradient half-pixel coordinate was lost.");
                }
            }
        }

        private static void TestPeakSelection()
        {
            using Mat image = new Mat(96, 96, MatType.CV_8UC1, Scalar.All(0));
            for (int y = 0; y < 96; y++)
            for (int x = 0; x < 96; x++)
                image.Set(y, x, (byte)(x >= 40 && x < 44 || x >= 48 ? 220 : 0));
            using RotatedCaliperTool tool = new RotatedCaliperTool();
            RotatedCaliperToolProperty options = Options(45, 48, -90);
            options.TangentWidth = 0; options.TangentSampleCount = 1;
            options.Selection = CaliperSelection.Strongest;
            tool.SetProperty(options);
            using (VisionToolResult tied = tool.Execute(image))
                Require(tied.CaliperMeasurement.Stations.All(x => x.Status == CaliperStationStatus.Ambiguous), "Equal strongest peaks must be explicit ambiguity.");
            options.Selection = CaliperSelection.First; tool.SetProperty(options);
            using (VisionToolResult first = tool.Execute(image))
            {
                Require(first.Success, "First peak must be selectable.");
                RequireApproximately(first.CaliperMeasurement.Stations[0].EdgePoint.Value.X, 39.5, 1e-9, "First rising edge differs.");
            }
            options.Selection = CaliperSelection.Last; tool.SetProperty(options);
            using (VisionToolResult last = tool.Execute(image))
            {
                Require(last.Success, "Last peak must be selectable.");
                RequireApproximately(last.CaliperMeasurement.Stations[0].EdgePoint.Value.X, 47.5, 1e-9, "Last rising edge differs.");
            }
        }

        private static void TestInputBoundaries()
        {
            using RotatedCaliperTool tool = new RotatedCaliperTool();
            tool.SetProperty(Options(48, 48, 0));
            using Mat blank = new Mat(96, 96, MatType.CV_8UC1, Scalar.All(0));
            using (VisionToolResult result = tool.Execute(blank))
                Require(!result.Success, "Blank image must not establish a line.");
            using Mat noise = new Mat(96, 96, MatType.CV_8UC1);
            for (int y = 0; y < 96; y++)
            for (int x = 0; x < 96; x++) noise.Set(y, x, (byte)(120 + ((x * 17 + y * 31) % 2)));
            using (VisionToolResult result = tool.Execute(noise))
                Require(!result.Success, "Noise with a one-level range must stay below the declared contrast threshold of five.");
            using Mat highDepth = new Mat(96, 96, MatType.CV_16UC1, Scalar.All(0));
            using (VisionToolResult result = tool.Execute(highDepth))
                Require(!result.Success, "16-bit input must be rejected, not implicitly converted.");
            using Mat parent = LineStep(104, 104, 0, 1, 52);
            using Mat roi = parent.SubMat(new Rect(4, 4, 96, 96));
            using Mat clone = roi.Clone();
            Require(!roi.IsContinuous(), "Fixture must exercise padded rows.");
            using VisionToolResult padded = tool.Execute(roi);
            using VisionToolResult contiguous = tool.Execute(clone);
            Require(padded.Success && contiguous.Success, "Both valid views must fit.");
            RequireApproximately(padded.CaliperMeasurement.Line.SignedDistance, contiguous.CaliperMeasurement.Line.SignedDistance, 1e-9, "Stride changed line geometry.");
        }

        private static void TestClipping()
        {
            using Mat image = LineStep(64, 64, 0, 1, 5);
            using RotatedCaliperTool tool = new RotatedCaliperTool();
            RotatedCaliperToolProperty options = Options(32, 2, 0);
            options.ScanLength = 20;
            tool.SetProperty(options);
            using VisionToolResult result = tool.Execute(image);
            Require(result.CaliperMeasurement != null, "Clipped sample must retain diagnostic stations.");
            Require(result.CaliperMeasurement.Stations.All(s => s.ValidSamples < s.ExpectedSamples), "Out-of-image bilinear neighbors must not count as observations.");
        }

        private static void TestInvalidBinBridge()
        {
            // Independent geometry: x = 32 + q*(a+t+offset), y = 1.5 + q*(a-t-offset).
            // Four-neighbor sampling requires 0 <= x < 63 and 0 <= y < 3.
            // With t=-20,+20 and station offsets -0.05,+0.05, exactly the two
            // disconnected scan-index bands [-22..-18] and [18..22] are valid.
            // Each band is constant: respectively 10 and 220. Compressing away
            // invalid bins would create an artificial 210-contrast interior peak.
            double q = 1 / Math.Sqrt(2);
            int[] expected = { -22, -21, -20, -19, -18, 18, 19, 20, 21, 22 };
            foreach (double offset in new[] { -0.05, 0.05 })
            {
                List<int> bins = new List<int>();
                int validSamples = 0;
                for (int a = -30; a <= 30; a++)
                {
                    int validAtBin = 0;
                    foreach (double t in new[] { -20.0, 20.0 })
                    {
                        double x = 32 + q * (a + t + offset);
                        double y = 1.5 + q * (a - t - offset);
                        if (x >= 0 && x < 63 && y >= 0 && y < 3)
                        {
                            validAtBin++; validSamples++;
                            Require(a < 0 ? x < 31 : x >= 32, "Independent fixture must be constant across each valid band.");
                        }
                    }
                    if (validAtBin > 0) bins.Add(a);
                }
                Require(bins.SequenceEqual(expected) && validSamples == 10, "Analytic support no longer matches the authored 10-bin oracle.");
            }

            using Mat image = new Mat(4, 64, MatType.CV_8UC1);
            for (int y = 0; y < 4; y++)
            for (int x = 0; x < 64; x++) image.Set(y, x, (byte)(x < 32 ? 10 : 220));
            RotatedCaliperToolProperty options = Options(32, 1.5, -45);
            options.StationCount = 2; options.StationPitch = 0.1;
            options.ScanLength = 60; options.ScanSampleCount = 61;
            options.TangentWidth = 40; options.TangentSampleCount = 2;
            options.MinimumTangentCoverage = 0.5; options.MinimumProfileCoverage = 0.1;
            using RotatedCaliperTool tool = new RotatedCaliperTool();
            tool.SetProperty(options);
            using VisionToolResult result = tool.Execute(image);
            Require(!result.Success && result.CaliperMeasurement != null, "Disconnected constant bands must not produce geometry.");
            Require(result.CaliperMeasurement.Stations.Count == 2, "Both station observations must be retained.");
            foreach (CaliperStationEvidence station in result.CaliperMeasurement.Stations)
            {
                Require(station.ExpectedSamples == 122 && station.ValidSamples == 10 && station.ValidProfileBins == 10,
                    "Valid support must match the independent four-neighbor oracle.");
                Require(station.Status == CaliperStationStatus.NoEdge && !station.EdgePoint.HasValue,
                    "Missing bins must never be bridged into a spurious edge.");
            }
        }
        private static void TestProfileCoverage()
        {
            // y(j)=-1.5+j. Bilinear four-neighbor support is 0<=y<9;
            // integer bins j=2..10 are therefore exactly 9 of 21.
            using Mat image = new Mat(10, 64, MatType.CV_8UC1);
            for (int y = 0; y < 10; y++)
            for (int x = 0; x < 64; x++)
                image.Set(y, x, (byte)Math.Round(20 + 100 * (1 + Math.Tanh(y - 4.25))));
            using RotatedCaliperTool tool = new RotatedCaliperTool();
            RotatedCaliperToolProperty p = Options(26, 8.5, 0);
            p.StationCount = 4; p.StationPitch = 4; p.ScanLength = 20; p.ScanSampleCount = 21;
            p.TangentWidth = 0; p.TangentSampleCount = 1;
            p.QualityGateEnabled = true; p.MinimumPointCount = 4; p.MaximumRmsResidual = 0.001; p.MaximumResidual = 0.001;
            RequireApproximately(p.MinimumProfileCoverage, 1, 0, "Default coverage must require the full normal profile.");
            tool.SetProperty(p);
            using (VisionToolResult strict = tool.Execute(image))
            {
                Require(!strict.Success && !strict.CaliperMeasurement.MeasurementEstablished, "Incomplete default profile must not establish geometry.");
                Require(strict.CaliperMeasurement.Stations.All(st => st.ExpectedSamples == 21 && st.ValidSamples == 9
                    && st.ValidProfileBins == 9 && st.Status == CaliperStationStatus.InsufficientCoverage && !st.EdgePoint.HasValue),
                    "Default profile gate must reject each 9/21 station before peak selection.");
            }
            p.MinimumProfileCoverage = 0.4; tool.SetProperty(p);
            using (VisionToolResult partial = tool.Execute(image))
            {
                Require(partial.Success && partial.CaliperMeasurement.Acceptance == CaliperAcceptance.Accepted,
                    "Explicit 0.4 profile policy must allow 9/21 coverage and quality evaluation.");
                Require(partial.CaliperMeasurement.Stations.All(st => st.ValidProfileBins == 9 && st.EdgePoint.HasValue),
                    "Partial coverage evidence must preserve the nine valid bins.");
                RequireApproximately(partial.CaliperMeasurement.Line.CenterY, 4.25, 0.1, "Synthetic sigmoid edge differs by more than the declared sampling approximation.");
            }
        }
        private static void TestCircles()
        {
            using Mat image = CircleStep(160, 160, 80, 80, 30);
            using RotatedCaliperTool tool = new RotatedCaliperTool();
            RotatedCaliperToolProperty options = Options(80, 80, 0);
            options.Shape = CaliperShape.Circle; options.Radius = 30; options.StationCount = 24;
            options.StartAngleDegrees = 0; options.AngularPitchDegrees = 15;
            options.ScanLength = 12; options.ScanSampleCount = 13;
            options.TangentWidth = 0; options.TangentSampleCount = 1;
            tool.SetProperty(options);
            using (VisionToolResult result = tool.Execute(image))
            {
                CaliperMeasurementEvidence e = result.CaliperMeasurement;
                Require(result.Success && e.Circle != null && e.Acceptance == CaliperAcceptance.NotEvaluated, "Full circle must fit without assuming quality.");
                RequireApproximately(e.Circle.CenterX, 80, 1.5, "Circle center X exceeds raster bound.");
                RequireApproximately(e.Circle.CenterY, 80, 1.5, "Circle center Y exceeds raster bound.");
                RequireApproximately(e.Circle.Radius, 30, 1.5, "Circle radius exceeds raster bound.");
                Require(e.UsedPointIds.Count == 24, "Every authored circle station must contribute.");
                RequireApproximately(e.CircleCoverageDegrees.Value, 345, 1, "Full-circle coverage differs from the 15 degree station spacing.");
                RequireApproximately(e.MaximumCircleGapDegrees.Value, 15, 1, "Full-circle gap differs from the 15 degree station spacing.");
            }
            // At the four cardinal directions the exact pixel step, sampled at half-pixel
            // intervals, produces equal adjacent derivative maxima. Those four stations
            // are rejected as FlatPeak; 15-degree spacing then leaves a 30-degree gap.
            options.ScanSampleCount = 25;
            options.QualityGateEnabled = true; options.MinimumPointCount = 21;
            tool.SetProperty(options);
            using (VisionToolResult flat = tool.Execute(image))
            {
                CaliperMeasurementEvidence e = flat.CaliperMeasurement;
                Require(flat.Success && e.Acceptance == CaliperAcceptance.Rejected, "Flat cardinal peaks must count against the explicit point gate.");
                Require(e.Stations.Where(st => st.StationId % 6 == 0).All(st => st.Status == CaliperStationStatus.FlatPeak), "Cardinal half-pixel plateaus must be reported.");
                Require(e.UsedPointIds.Count == 20, "Exactly four cardinal stations must be rejected.");
                RequireApproximately(e.CircleCoverageDegrees.Value, 330, 1e-8, "Coverage must reflect cardinal gaps.");
                RequireApproximately(e.MaximumCircleGapDegrees.Value, 30, 1e-8, "Maximum gap must reflect cardinal gaps.");
            }
            options.ScanSampleCount = 13;
            options.StationCount = 8; options.AngularPitchDegrees = 15;
            options.QualityGateEnabled = true; options.MinimumPointCount = 3;
            options.MinimumCircleCoverageDegrees = 120; options.MaximumCircleGapDegrees = 240;
            tool.SetProperty(options);
            using (VisionToolResult partial = tool.Execute(image))
            {
                CaliperMeasurementEvidence e = partial.CaliperMeasurement;
                Require(partial.Success && e.Acceptance == CaliperAcceptance.Rejected, "Partial arc must retain geometry but fail declared support gate.");
                // One-pixel raster position uncertainty at radius 30 gives under 3 degrees
                // per endpoint, hence at most 6 degrees combined angular error.
                RequireApproximately(e.CircleCoverageDegrees.Value, 105, 6, "Partial-arc coverage differs from authored 105 degrees.");
                RequireApproximately(e.MaximumCircleGapDegrees.Value, 255, 6, "Partial-arc gap differs from authored 255 degrees.");
            }
            options.StationCount = 3; options.AngularPitchDegrees = 45;
            options.MinimumPointCount = 4; options.MinimumCircleCoverageDegrees = 0; options.MaximumCircleGapDegrees = 360;
            tool.SetProperty(options);
            using (VisionToolResult three = tool.Execute(image))
                Require(three.Success && three.CaliperMeasurement.Acceptance == CaliperAcceptance.Rejected, "Geometrically solvable three-point circle must not bypass point-count quality.");
            options.StationCount = 24; options.AngularPitchDegrees = 15;
            options.ScanSampleCount = 13;
            options.QualityGateEnabled = true; options.MinimumPointCount = 25;
            tool.SetProperty(options);
            using (VisionToolResult rejected = tool.Execute(image))
                {
                Require(rejected.Success && rejected.CaliperMeasurement.Acceptance == CaliperAcceptance.Rejected, "Quality rejection must retain successful geometry.");
                HostOutcome outcome = HostOutcome.Caliper(rejected, rejected.CaliperMeasurement);
                Require(outcome.Execution == "Completed" && outcome.MeasurementEstablished && outcome.Decision == "Fail", "Host must preserve measured quality failure.");
            }
        }

        private static void TestOutlier()
        {
            using Mat source = new Mat(96, 96, MatType.CV_8UC1, Scalar.All(0));
            for (int y = 0; y < 96; y++)
            for (int x = 0; x < 96; x++)
            {
                int boundary = x >= 47 && x <= 49 ? 54 : 48;
                source.Set(y, x, (byte)(y >= boundary ? 255 : 0));
            }
            using RotatedCaliperTool tool = new RotatedCaliperTool();
            RotatedCaliperToolProperty options = Options(48, 48, 0);
            options.StationPitch = 5; options.TangentWidth = 0; options.TangentSampleCount = 1;
            options.OutlierRemovalEnabled = false; tool.SetProperty(options);
            using (VisionToolResult all = tool.Execute(source))
                Require(all.Success && all.CaliperMeasurement.ExcludedPointIds.Count == 0, "Default fit must not silently trim outliers.");
            options.OutlierRemovalEnabled = true; options.OutlierResidualCutoff = 1; options.MaximumOutlierIterations = 1;
            tool.SetProperty(options);
            using (VisionToolResult trimmed = tool.Execute(source))
            {
                Require(trimmed.Success && trimmed.CaliperMeasurement.ExcludedPointIds.Count == 1 && trimmed.CaliperMeasurement.ExcludedPointIds[0] == 4, "One declared central outlier must be removed.");
                Require(trimmed.CaliperMeasurement.UsedPointIds.Count == 8, "One-iteration cap must remove exactly one point.");
                RequireApproximately(trimmed.CaliperMeasurement.Line.CenterY, 47.5, 1e-9, "Refit must follow the eight authored inlier edges.");
            }
        }

        private static void TestEqualOutliers()
        {
            using Mat source = new Mat(96, 96, MatType.CV_8UC1, Scalar.All(0));
            for (int y = 0; y < 96; y++)
            for (int x = 0; x < 96; x++)
            {
                int boundary = Math.Abs(x - 33) <= 1 || Math.Abs(x - 63) <= 1 ? 54 : 48;
                source.Set(y, x, (byte)(y >= boundary ? 255 : 0));
            }
            using RotatedCaliperTool tool = new RotatedCaliperTool();
            RotatedCaliperToolProperty p = Options(48, 48, 0);
            p.StationPitch = 5; p.TangentWidth = 0; p.TangentSampleCount = 1;
            p.OutlierRemovalEnabled = true; p.OutlierResidualCutoff = 1; p.MaximumOutlierIterations = 0;
            tool.SetProperty(p);
            using (VisionToolResult none = tool.Execute(source))
                Require(none.Success && none.CaliperMeasurement.ExcludedPointIds.Count == 0, "Zero refit cap must remove no point.");
            p.MaximumOutlierIterations = 1; tool.SetProperty(p);
            using (VisionToolResult one = tool.Execute(source))
                Require(one.Success && one.CaliperMeasurement.ExcludedPointIds.Count == 1 && one.CaliperMeasurement.ExcludedPointIds[0] == 1, "Equal residuals must remove lowest station ID first.");
            p.AngleDegrees = 180; p.Polarity = CaliperPolarity.Falling; tool.SetProperty(p);
            using (VisionToolResult reversed = tool.Execute(source))
                Require(reversed.Success && reversed.CaliperMeasurement.ExcludedPointIds.Count == 1 && reversed.CaliperMeasurement.ExcludedPointIds[0] == 1, "Reverse station order must apply the same lowest-ID policy.");
        }
        private static void TestFactory()
        {
            RotatedCaliperToolProperty original = Options(48, 48, 0);
            using RotatedCaliperTool direct = new RotatedCaliperTool();
            direct.SetProperty(original);
            original.CenterX = 12;
            RequireApproximately(direct.property.CenterX, 48, 0, "SetProperty must defensively clone settings.");
            VisionPipeline pipeline = new VisionPipeline();
            VisionPipelineStep step = new VisionPipelineStep { ToolType = "rotatedCaliper" };
            step.Parameters[nameof(RotatedCaliperToolProperty.CenterX)] = "48";
            step.Parameters[nameof(RotatedCaliperToolProperty.Shape)] = "Line";
            pipeline.Steps.Add(step);
            foreach (int schema in new[] { 1, 2 })
            {
                pipeline.SchemaVersion = schema;
                VisionPipeline roundTrip = VisionPipelineSerializer.Deserialize(VisionPipelineSerializer.Serialize(pipeline));
                Require(roundTrip.SchemaVersion == schema, "Schema version must survive round trip.");
                using RotatedCaliperTool factory = (RotatedCaliperTool)VisionPipelineToolFactory.Create(roundTrip.Steps[0]);
                RequireApproximately(factory.property.CenterX, 48, 0, "Factory settings must survive XML round trip.");
                Require(factory.LatestMeasurement == null, "Creating or restoring settings must not execute inspection.");
            }
            step.ToolType = "RotatedCaliperTool";
            using RotatedCaliperTool alias = (RotatedCaliperTool)VisionPipelineToolFactory.Create(step);
            Require(alias.LatestMeasurement == null, "Alias construction must not inspect.");
        }
        private static bool FitPoints(CaliperShape shape, Point2d[] points, out CaliperLineGeometry line, out CaliperCircleGeometry circle)
        {
            ConstructorInfo ctor = typeof(CaliperStationEvidence).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
            var stations = new List<CaliperStationEvidence>();
            for (int i = 0; i < points.Length; i++)
                stations.Add((CaliperStationEvidence)ctor.Invoke(new object[] { i, points[i], new Point2d(0, 1), 1, 1, 1,
                    CaliperStationStatus.EdgeFound, null, null, null, points[i] }));
            MethodInfo fit = typeof(RotatedCaliperTool).GetMethod("TryFit", BindingFlags.NonPublic | BindingFlags.Static);
            object[] args = { stations, new RotatedCaliperToolProperty { Shape = shape }, null, null, null };
            bool result = (bool)fit.Invoke(null, args);
            line = (CaliperLineGeometry)args[2]; circle = (CaliperCircleGeometry)args[3];
            return result;
        }

        private static void TestFitBoundaries()
        {
            const double x = 1000000000000, y = 2000000000000;
            Point2d[] vertical = { new Point2d(x, y), new Point2d(x, y + 10), new Point2d(x, y + 20) };
            Require(FitPoints(CaliperShape.Line, vertical, out CaliperLineGeometry line, out _), "Translated vertical line must remain solvable.");
            RequireApproximately(Math.Abs(line.NormalX), 1, 1e-12, "Vertical line normal differs.");
            RequireApproximately(line.CenterX, x, 0, "Large translation changed line location.");
            Require(!FitPoints(CaliperShape.Circle, vertical, out _, out _), "Three collinear points cannot establish a circle.");
            Point2d[] duplicates = { new Point2d(x, y), new Point2d(x, y), new Point2d(x, y) };
            Require(!FitPoints(CaliperShape.Circle, duplicates, out _, out _), "Duplicate points cannot establish a circle.");
            Point2d[] clustered = { new Point2d(10 * Math.Cos(-1e-7), 10 * Math.Sin(-1e-7)), new Point2d(10, 0), new Point2d(10 * Math.Cos(1e-7), 10 * Math.Sin(1e-7)) };
            Require(!FitPoints(CaliperShape.Circle, clustered, out _, out _), "Near-collinear clustered points must be rejected by the declared conditioning gate.");
            Point2d[] ring = { new Point2d(x + 10, y), new Point2d(x, y + 10), new Point2d(x - 10, y), new Point2d(x, y - 10) };
            Require(FitPoints(CaliperShape.Circle, ring, out _, out CaliperCircleGeometry circle), "Translated analytic circle must remain solvable.");
            RequireApproximately(circle.CenterX, x, 0.001, "Translated circle center X differs by more than the representable ULP.");
            RequireApproximately(circle.CenterY, y, 0.001, "Translated circle center Y differs by more than the representable ULP.");
            RequireApproximately(circle.Radius, 10, 0.001, "Translated circle radius differs by more than the representable ULP.");
        }
        private static void TestLifetime()
        {
            using Mat source = LineStep(96, 96, 0, 1, 48);
            using Mat blank = new Mat(96, 96, MatType.CV_8UC1, Scalar.All(0));
            RotatedCaliperTool tool = new RotatedCaliperTool();
            tool.SetProperty(Options(48, 48, 0));
            VisionToolResult first = tool.Execute(source);
            Require(first.Success && first.CaliperMeasurement != null, "Initial line must succeed.");
            double distance = first.CaliperMeasurement.Line.SignedDistance;
            Require(first.ResultImage != null && !first.ResultImage.IsDisposed, "Successful result must own an image snapshot.");
            byte retainedPixel = first.ResultImage.At<byte>(10, 10);
            Require(first.Overlays.Count == 1 && first.Overlays[0].Points.Count > 0, "Observed points must be represented in the overlay.");
            var retainedPoint = first.Overlays[0].Points[0];
            source.Set(10, 10, (byte)99);
            Require(first.ResultImage.At<byte>(10, 10) == retainedPixel, "Result image must not alias caller source.");
            using (VisionToolResult empty = tool.Execute(blank)) Require(!empty.Success, "Empty frame must not reuse previous line.");
            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                cts.Cancel();
                RequireThrows<OperationCanceledException>(() => tool.Execute(source, cts.Token));
            }
            using (VisionToolResult recovered = tool.Execute(source)) Require(recovered.Success, "Valid input must recover after cancellation.");
            tool.Dispose();
            RequireApproximately(first.CaliperMeasurement.Line.SignedDistance, distance, 0, "Retained immutable geometry changed.");
            Require(!source.IsDisposed, "Tool must not own caller source.");
            Require(!first.ResultImage.IsDisposed && first.ResultImage.At<byte>(10, 10) == retainedPixel, "Retained snapshot must survive later runs and Tool disposal.");
            Require(first.Overlays[0].Points[0] == retainedPoint, "Overlay must survive later runs and Tool disposal.");
            first.Dispose();
        }

        private static void TestActiveCancellation()
        {
            using Mat firstFrame = LineStep(96, 96, 0, 1, 48);
            using Mat secondFrame = LineStep(96, 96, 0, 1, 42);
            using RotatedCaliperTool tool = new RotatedCaliperTool();
            tool.SetProperty(Options(48, 48, 0));
            using VisionToolResult first = tool.Execute(firstFrame);
            Require(first.Success, "Baseline must establish a line.");
            double oldDistance = first.CaliperMeasurement.Line.SignedDistance;
            byte oldPixel = first.ResultImage.At<byte>(10, 10);
            FieldInfo hook = typeof(RotatedCaliperTool).GetField("SamplingCheckpoint", BindingFlags.NonPublic | BindingFlags.Instance);
            Require(hook != null, "The internal post-station checkpoint must be present.");
            using ManualResetEventSlim entered = new ManualResetEventSlim();
            using ManualResetEventSlim release = new ManualResetEventSlim();
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            hook.SetValue(tool, (Action)(() => { entered.Set(); release.Wait(); }));
            Task work = Task.Run(() => { using VisionToolResult unexpected = tool.Execute(secondFrame, cancellation.Token); });
            try
            {
                Require(entered.Wait(5000), "The first station must complete before cancellation.");
                Require(!work.IsCompleted, "Inspection must remain in-flight at the checkpoint.");
                cancellation.Cancel();
            }
            finally { release.Set(); }
            try
            {
                RequireThrows<OperationCanceledException>(() => work.GetAwaiter().GetResult());
                Require(tool.LatestMeasurement == null, "Canceled partial results must not be published as latest.");
            }
            finally
            {
                release.Set();
                try { work.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
                hook.SetValue(tool, null);
            }
            using VisionToolResult recovered = tool.Execute(secondFrame);
            Require(recovered.Success && recovered.CaliperMeasurement != null, "A distinct valid frame must recover after active cancel.");
            RequireApproximately(recovered.CaliperMeasurement.Line.CenterY, 41.5, 1e-9, "Recovered result must match the new frame.");
            RequireApproximately(first.CaliperMeasurement.Line.SignedDistance, oldDistance, 0, "Old immutable measurement changed.");
            Require(first.ResultImage.At<byte>(10, 10) == oldPixel && !firstFrame.IsDisposed && !secondFrame.IsDisposed, "Prior image and caller-owned sources must survive.");
        }
        private static void TestInvalidGeometry()
        {
            using Mat source = LineStep(96, 96, 0, 1, 48);
            using RotatedCaliperTool tool = new RotatedCaliperTool();
            foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.MaxValue })
            {
                RotatedCaliperToolProperty options = Options(48, 48, 0);
                options.ScanLength = invalid;
                tool.SetProperty(options);
                using VisionToolResult result = tool.Execute(source);
                Require(!result.Success, "Unrepresentable scan length must fail without a measurement.");
            }
            RotatedCaliperToolProperty oversized = Options(48, 48, 0);
            oversized.ScanSampleCount = int.MaxValue;
            tool.SetProperty(oversized);
            using (VisionToolResult result = tool.Execute(source))
                Require(!result.Success, "Overflow-scale sample count must be rejected before allocation.");
            oversized = Options(48, 48, 0);
            oversized.StationPitch = double.MaxValue;
            tool.SetProperty(oversized);
            using (VisionToolResult result = tool.Execute(source))
                Require(!result.Success, "Unrepresentable station span must be rejected.");
        }
        private static Mat LineStep(int width, int height, double normalX, double normalY, double offset, bool rising = true)
        {
            Mat image = new Mat(height, width, MatType.CV_8UC1);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                bool high = normalX * x + normalY * y >= offset;
                image.Set(y, x, (byte)((high == rising) ? 255 : 0));
            }
            return image;
        }

        private static Mat CircleStep(int width, int height, double centerX, double centerY, double radius)
        {
            Mat image = new Mat(height, width, MatType.CV_8UC1);
            double radiusSquared = radius * radius;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                double dx = x - centerX;
                double dy = y - centerY;
                image.Set(y, x, (byte)(dx * dx + dy * dy >= radiusSquared ? 255 : 0));
            }
            return image;
        }

        private static Mat FiveGradientProfile(int width, int height, int firstColumn, bool rising = true)
        {
            // Forward differences 16,30,36,34,24 are
            // g(j)=36-4*j*j+2*j, j=-2..2. Vertex j=0.25, with
            // gradient locations at half pixels: x = firstColumn + 2.75.
            byte[] values = { 0, 16, 46, 82, 116, 140 };
            Mat image = new Mat(height, width, MatType.CV_8UC1);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int index = Math.Max(0, Math.Min(values.Length - 1, x - firstColumn));
                image.Set(y, x, rising ? values[index] : (byte)(255 - values[index]));
            }
            return image;
        }
    }
}

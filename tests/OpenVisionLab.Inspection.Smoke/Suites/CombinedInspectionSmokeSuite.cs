using OpenVisionLab.Inspection;
using OpenVisionLab.Vision2D;
using OpenVisionLab.Vision2D.Pipeline;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Tool;
using OpenVisionLab.Vision3D.FeatureExtraction;
using OpenVisionLab.Vision3D.Geometry;
using OpenVisionLab.Vision3D.Inspection;
using OpenCvSharp;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using OpenVisionLab.FirstInspection;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;
using static OpenVisionLab.Inspection.Smoke.SmokeFixtures;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class CombinedInspectionSmokeSuite
    {
        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase("Combined result disposal releases owned 2D result images", TestCombinedResultResourceOwnership);
            yield return new SmokeCase("Combined hardening owns removed results independently of the public mutable list", TestMutableResultOwnership);
            yield return new SmokeCase("Combined hardening cancels at step boundaries drains partial results and preserves ordinary faults", TestCombinedCancellation);
            yield return new SmokeCase("Host hardening separates execution measurement acceptance missing metrics and units", TestHostOutcome);
            yield return new SmokeCase("Combined runner executes 2D and 3D pass steps", TestCombinedRunnerPass);
            yield return new SmokeCase("Combined runner retains later 3D evidence after 2D failure", TestCombinedRunnerContinuesAfterFailure);
            yield return new SmokeCase("Combined runner converts a 3D exception to a result", TestCombinedRunnerCatchesThreeDException);
            yield return new SmokeCase("Combined runner tolerates a throwing tool name", TestCombinedRunnerToleratesThrowingToolName);
            yield return new SmokeCase("Combined runner rejects an empty configuration", TestCombinedRunnerEmptyConfiguration);
            yield return new SmokeCase("Combined lifetime releases partial 2D results when tool enumeration fails", TestTwoDEnumerationLifetime);
            yield return new SmokeCase("Combined lifetime releases earlier 2D results when 3D enumeration fails", TestThreeDEnumerationLifetime);
            yield return new SmokeCase("Combined lifetime preserves retained snapshots across repeated list failures and recovery", TestRepeatedEnumerationRecovery);
            yield return new SmokeCase("Combined lifetime keeps per-Tool faults typed and later evidence ordered", TestControlledFaultLifetime);
        }

        private static void TestCombinedCancellation()
        {
            using Mat image = new Mat(2, 2, MatType.CV_8UC1, Scalar.All(41));
            var input = new CombinedInspectionInput { Image = image, HeightMap = CreatePlaneMap(3, 3, 0, 0, 1) };
            var runner = new CombinedInspectionRunner();
            using CancellationTokenSource before = new CancellationTokenSource();
            before.Cancel();
            var never = new PassThroughVisionTool();
            RequireThrows<OperationCanceledException>(() => runner.Run(input, new[] { never }, null, before.Token));
            Require(!never.WasExecuted, "Pre-cancel must not begin a tool.");
            foreach (bool supportsToken in new[] { false, true })
            {
                using CancellationTokenSource during = new CancellationTokenSource();
                Mat snapshot = null;
                var canceling = new CallbackVisionTool((source, token) =>
                {
                    if (supportsToken) Require(token == during.Token, "Token was not forwarded.");
                    snapshot = source.Clone();
                    during.Cancel();
                    return VisionToolResult.Passed(snapshot, TimeSpan.Zero);
                });
                IVisionTool first = supportsToken ? canceling : new LegacyCallbackVisionTool(source => canceling.Execute(source));
                var last = new PassThroughVisionTool();
                RequireThrows<OperationCanceledException>(() => runner.Run(input, new[] { first, last }, null, during.Token));
                Require(snapshot.IsDisposed && !last.WasExecuted, "Post-return cancel must own and dispose partial results before starting another step.");
            }
            using TrackingDisposableVisionTool retained = new TrackingDisposableVisionTool();
            using CancellationTokenSource inThreeD = new CancellationTokenSource();
            var cancel3d = new CallbackThreeDTool(token => { Require(token == inThreeD.Token, "3D token must be forwarded."); inThreeD.Cancel(); return new ThicknessInspectionTool().Execute(input.HeightMap, CancellationToken.None); });
            RequireThrows<OperationCanceledException>(() => runner.Run(input, new[] { retained }, new IThreeDInspectionTool[] { cancel3d }, inThreeD.Token));
            Require(retained.ResultSnapshot.IsDisposed, "3D cancellation must release earlier images.");
            var unrelated = new CallbackVisionTool((source, token) => throw new OperationCanceledException("unrelated token"));
            var recovery = new PassThroughVisionTool();
            using CombinedInspectionRunResult fault = runner.Run(input, new IVisionTool[] { unrelated, recovery }, null, CancellationToken.None);
            Require(!fault.Success && recovery.WasExecuted && fault.Steps[0].VisionResult.Exception is OperationCanceledException, "Unrelated OCE must follow ordinary continue policy.");
            using CombinedInspectionRunResult good = runner.Run(input, new[] { retained }, new IThreeDInspectionTool[] { new ThicknessInspectionTool() }, CancellationToken.None);
            Require(good.Steps.Count == 2 && !image.IsDisposed, "Runner must recover after cancellation and faults.");
            foreach (ICancellableThreeDInspectionTool tool in new ICancellableThreeDInspectionTool[] { new ThicknessInspectionTool(), new WarpageInspectionTool(), new DatumPlaneRawHeightDeviationInspectionTool() })
                RequireThrows<OperationCanceledException>(() => tool.Execute(input.HeightMap, before.Token));
        }

        private static void TestHostOutcome()
        {
            var map = CreatePlaneMap(3, 3, 0, 0, 5);
            var pass = new ThicknessInspectionTool(new ThicknessInspectionOptions { MinimumThickness = 4, MaximumThickness = 6 }).Execute(map);
            var fail = new ThicknessInspectionTool(new ThicknessInspectionOptions { MinimumThickness = 0, MaximumThickness = 1 }).Execute(map);
            string metric = ThreeDInspectionMetricNames.Thickness.Mean;
            Require(HostOutcome.ThreeD(pass, metric, map.HeightUnit).Decision == "Pass", "Normal OK mapping failed.");
            Require(HostOutcome.ThreeD(fail, metric, map.HeightUnit).Decision == "Fail" && HostOutcome.ThreeD(fail, metric, map.HeightUnit).MeasurementEstablished, "Normal NG must retain valid measurement.");
            foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                pass.Metrics[metric] = invalid;
                Require(HostOutcome.ThreeD(pass, metric, map.HeightUnit).Decision == "NotEvaluated", "Nonfinite metric must not become Pass or zero.");
            }
            pass.Metrics.Remove(metric);
            Require(HostOutcome.ThreeD(pass, metric, map.HeightUnit).Decision == "NotEvaluated", "Missing metric must remain unevaluated.");
            Require(HostOutcome.ThreeD(fail, metric, "wrong-unit").Decision == "NotEvaluated", "A unit mismatch must not be accepted.");
            Require(HostOutcome.ThreeD(new ThicknessInspectionTool().Execute(null), metric, "mm").Execution == "Faulted", "Input failure is not a product NG.");
            using VisionToolResult noMatch = VisionToolResult.Failed(VisionToolErrorCode.MatchingNoResult, "absent", TimeSpan.Zero);
            Require(HostOutcome.Matching(noMatch).Execution == "Completed" && HostOutcome.Matching(noMatch).Decision == "Fail", "NoMatch is a completed observation.");
            using VisionToolResult empty = VisionToolResult.Passed(null, TimeSpan.Zero);
            Require(HostOutcome.Matching(empty).Decision == "NotEvaluated" && HostOutcome.Qr(empty).Decision == "NotEvaluated", "Missing 2D metrics must not pass.");
            Require(HostOutcome.Blob(empty, 0).Decision == "Fail", "Host must choose accepted candidates rather than legacy count.");
            foreach (VisionToolErrorCode code in new[] { VisionToolErrorCode.QrCodeNotDetected, VisionToolErrorCode.QrCodeDecodeFailed, VisionToolErrorCode.QrCodeCountMismatch })
            {
                using VisionToolResult qr = VisionToolResult.Failed(code, "authored", TimeSpan.Zero);
                Require(HostOutcome.Qr(qr).Decision == "Fail", "QR ordinary observation failures must not be execution faults.");
            }
            Require(HostOutcome.Canceled().Execution == "Canceled" && !HostOutcome.Canceled().MeasurementEstablished && HostOutcome.Canceled().Decision == "NotEvaluated", "Canceled output must publish no approved measurement.");
        }

        private sealed class CallbackVisionTool : ICancellableVisionTool
        {
            private readonly Func<Mat, CancellationToken, VisionToolResult> execute;
            internal CallbackVisionTool(Func<Mat, CancellationToken, VisionToolResult> execute) { this.execute = execute; }
            public string Name => "Controlled cancellable step";
            public VisionToolResult Execute(Mat source) => execute(source, CancellationToken.None);
            public VisionToolResult Execute(Mat source, CancellationToken token) => execute(source, token);
        }

        private sealed class LegacyCallbackVisionTool : IVisionTool
        {
            private readonly Func<Mat, VisionToolResult> execute;
            internal LegacyCallbackVisionTool(Func<Mat, VisionToolResult> execute) { this.execute = execute; }
            public string Name => "Controlled legacy step";
            public VisionToolResult Execute(Mat source) => execute(source);
        }

        private sealed class CallbackThreeDTool : ICancellableThreeDInspectionTool
        {
            private readonly Func<CancellationToken, ThreeDInspectionResult> execute;
            internal CallbackThreeDTool(Func<CancellationToken, ThreeDInspectionResult> execute) { this.execute = execute; }
            public string Name => "Controlled 3D step";
            public ThreeDInspectionResult Execute(HeightMap3D source) => execute(CancellationToken.None);
            public ThreeDInspectionResult Execute(HeightMap3D source, CancellationToken token) => execute(token);
        }

        private static void TestMutableResultOwnership()
        {
            using Mat image = new Mat(2, 2, MatType.CV_8UC1, Scalar.All(41));
            using TrackingDisposableVisionTool tool = new TrackingDisposableVisionTool();
            using TrackingDisposableVisionTool other = new TrackingDisposableVisionTool();
            CombinedInspectionRunner runner = new CombinedInspectionRunner();
            using CombinedInspectionRunResult owned = runner.Run(new CombinedInspectionInput { Image = image }, new[] { tool }, null);
            using CombinedInspectionRunResult external = runner.Run(new CombinedInspectionInput { Image = image }, new[] { other }, null);
            CombinedInspectionStepResult removed = owned.Steps[0];
            owned.Steps.Clear();
            owned.Steps.Add(external.Steps[0]);
            owned.Steps.Add(external.Steps[0]);
            owned.Dispose();
            Require(tool.ResultSnapshot.IsDisposed, "Removing a public step must not leak its owned result.");
            Require(!other.ResultSnapshot.IsDisposed, "Externally inserted results remain owned by their original run.");
            owned.Steps.Add(removed);
            owned.Dispose();
            Require(!image.IsDisposed && !tool.WasDisposed, "Only collected results are owned.");
            var snapshots = new List<Mat>();
            var equalResults = new CallbackVisionTool((source, token) =>
            {
                Mat snapshot = source.Clone();
                snapshots.Add(snapshot);
                return new EqualVisionResult { Success = true, ResultImage = snapshot };
            });
            using CombinedInspectionRunResult equalRun = runner.Run(new CombinedInspectionInput { Image = image }, new[] { equalResults, equalResults }, null);
            equalRun.Steps.Clear();
            equalRun.Dispose();
            Require(snapshots.Count == 2 && snapshots.All(snapshot => snapshot.IsDisposed), "Value-equal derived results are distinct resource owners.");
        }

        private sealed class EqualVisionResult : VisionToolResult
        {
            public override bool Equals(object other) => other is EqualVisionResult;
            public override int GetHashCode() => 1;
        }

        private static void TestCombinedResultResourceOwnership()
        {
            using (Mat source = new Mat(3, 3, MatType.CV_8UC1, new Scalar(4)))
            {
                CombinedInspectionRunResult result = new CombinedInspectionRunner().Run(
                    new CombinedInspectionInput { Image = source },
                    new IVisionTool[] { new ImageReturningVisionTool() },
                    null);

                Require(result.Success, "The combined ownership fixture must pass.");
                Mat resultSnapshot = result.Steps[0].VisionResult.ResultImage;
                Require(resultSnapshot != null && !resultSnapshot.IsDisposed, "The combined runner did not retain its 2D result image.");

                result.Dispose();
                Require(resultSnapshot.IsDisposed, "Disposing a combined result did not release its 2D result image.");
                Require(!source.IsDisposed, "Disposing a combined result released the caller-owned input image.");
                result.Dispose();
            }
        }

        private static void TestCombinedRunnerPass()
        {
            PassThroughVisionTool twoDTool = new PassThroughVisionTool();
            CombinedInspectionRunResult result = new CombinedInspectionRunner().Run(
                new CombinedInspectionInput { HeightMap = CreatePlaneMap(3, 3, 0.0, 0.0, 1.0) },
                new IVisionTool[] { twoDTool },
                new IThreeDInspectionTool[]
                {
                    new ThicknessInspectionTool(new ThicknessInspectionOptions
                    {
                        MinimumThickness = 0.0,
                        MaximumThickness = 2.0
                    }),
                    new WarpageInspectionTool(new WarpageInspectionOptions
                    {
                        MaximumPeakToValley = 1e-10,
                        MaximumRms = 1e-10
                    })
                });

            Require(twoDTool.WasExecuted, "The combined runner did not execute the 2D tool.");
            Require(result.Success, "All passing combined steps must produce a passing run.");
            Require(result.Steps.Count == 3, "The combined runner did not preserve every step result.");
            Require(result.Steps[2].Domain == CombinedInspectionDomain.ThreeD && result.Steps[2].Success, "The combined runner did not preserve the 3D result.");
        }

        private static void TestCombinedRunnerContinuesAfterFailure()
        {
            FailingVisionTool twoDTool = new FailingVisionTool();
            CombinedInspectionRunResult result = new CombinedInspectionRunner().Run(
                new CombinedInspectionInput { HeightMap = CreateThicknessMap() },
                new IVisionTool[] { twoDTool },
                new IThreeDInspectionTool[]
                {
                    new ThicknessInspectionTool(new ThicknessInspectionOptions
                    {
                        MinimumThickness = 1.0,
                        MaximumThickness = 1.5
                    })
                });

            Require(twoDTool.WasExecuted, "The failure fixture was not executed.");
            Require(!result.Success, "A failed 2D step must fail the combined run.");
            Require(result.Steps.Count == 2, "The combined runner must continue after a failed 2D step.");
            Require(result.Steps[1].Success, "The later 3D evidence was not retained.");
        }

        private static void TestCombinedRunnerCatchesThreeDException()
        {
            CombinedInspectionRunResult result = new CombinedInspectionRunner().Run(
                new CombinedInspectionInput { HeightMap = CreateThicknessMap() },
                null,
                new IThreeDInspectionTool[] { new ThrowingThreeDTool() });

            Require(!result.Success, "A throwing 3D tool must fail the combined run.");
            Require(result.Steps.Count == 1, "The throwing 3D tool did not produce a controlled result.");
            Require(result.Steps[0].ThreeDResult.ResultStatus == ThreeDInspectionResultStatus.Exception, "The 3D exception did not map to an exception result.");
        }

        private static void TestCombinedRunnerToleratesThrowingToolName()
        {
            CombinedInspectionRunResult result = new CombinedInspectionRunner().Run(
                new CombinedInspectionInput { HeightMap = CreateThicknessMap() },
                null,
                new IThreeDInspectionTool[] { new ThrowingNameThreeDTool() });

            Require(result.Success, "A successful tool with a throwing Name getter must still execute.");
            Require(result.Steps.Count == 1, "The tool with a throwing Name getter did not produce a result.");
            Require(result.Steps[0].ToolName == "Unnamed 3D tool", "The throwing Name getter did not use the stable fallback label.");
        }

        private static void TestTwoDEnumerationLifetime()
        {
            foreach (EnumerationFault fault in Enum.GetValues<EnumerationFault>())
            {
                using Mat source = new Mat(3, 5, MatType.CV_8UC1, Scalar.All(37));
                using TrackingDisposableVisionTool first = new TrackingDisposableVisionTool();
                using TrackingDisposableVisionTool second = new TrackingDisposableVisionTool();
                CombinedInspectionRunner runner = new CombinedInspectionRunner();
                InvalidOperationException expected = new InvalidOperationException("2D enumeration " + fault);
                FailingToolList<IVisionTool> tools = new FailingToolList<IVisionTool>(new IVisionTool[] { first, second }, fault, expected);
                try
                {
                    Exception actual = RequireThrows<InvalidOperationException>(() => runner.Run(new CombinedInspectionInput { Image = source }, tools, null));
                    Require(ReferenceEquals(actual, expected), "Enumeration failure must propagate the exact original exception.");
                    int produced = fault == EnumerationFault.GetEnumerator ? 0 : fault == EnumerationFault.Current ? 1 : 2;
                    Require((first.ResultSnapshot == null ? 0 : 1) + (second.ResultSnapshot == null ? 0 : 1) == produced, "Enumeration must retain its original execution order.");
                    Require(first.ResultSnapshot == null || first.ResultSnapshot.IsDisposed, "The aborted run must release its first unreturned result image.");
                    Require(second.ResultSnapshot == null || second.ResultSnapshot.IsDisposed, "The aborted run must release its second unreturned result image.");
                    Require(tools.DisposeCalled == (fault != EnumerationFault.GetEnumerator), "A created enumerator must be disposed even on failure.");
                    Require(!first.WasDisposed && !second.WasDisposed && !source.IsDisposed && source.At<byte>(0, 0) == 37, "Aborting a run must preserve caller-owned Tools and input pixels.");
                    using CombinedInspectionRunResult recovery = runner.Run(new CombinedInspectionInput { Image = source }, new IVisionTool[] { first, second }, null);
                    Require(recovery.Success && recovery.Steps.Count == 2, "The same runner and Tools must recover with a stable list.");
                    Require(recovery.Steps.All(step => step.VisionResult.ResultImage.At<byte>(0, 0) == 37), "Recovery must use the current input pixels.");
                }
                finally
                {
                    first.ResultSnapshot?.Dispose();
                    second.ResultSnapshot?.Dispose();
                }
            }
        }

        private static void TestThreeDEnumerationLifetime()
        {
            foreach (EnumerationFault fault in Enum.GetValues<EnumerationFault>())
            {
                using Mat source = new Mat(3, 5, MatType.CV_8UC1, Scalar.All(41));
                using TrackingDisposableVisionTool first = new TrackingDisposableVisionTool();
                using TrackingDisposableVisionTool second = new TrackingDisposableVisionTool();
                HeightMap3D map = CreatePlaneMap(2, 3, 0.0, 0.0, 1.0);
                ThicknessInspectionTool thickness = new ThicknessInspectionTool(new ThicknessInspectionOptions { MinimumThickness = 0.9, MaximumThickness = 1.1 });
                CombinedInspectionInput input = new CombinedInspectionInput { Image = source, HeightMap = map };
                CombinedInspectionRunner runner = new CombinedInspectionRunner();
                InvalidOperationException expected = new InvalidOperationException("3D enumeration " + fault);
                FailingToolList<IThreeDInspectionTool> tools = new FailingToolList<IThreeDInspectionTool>(new IThreeDInspectionTool[] { thickness, thickness }, fault, expected);
                try
                {
                    Exception actual = RequireThrows<InvalidOperationException>(() => runner.Run(input, new IVisionTool[] { first, second }, tools));
                    Require(ReferenceEquals(actual, expected), "3D enumeration must propagate its original exception after 2D execution.");
                    Require(first.ResultSnapshot.IsDisposed && second.ResultSnapshot.IsDisposed, "A 3D list failure must release every earlier unreturned 2D image.");
                    Require(!source.IsDisposed && source.At<byte>(0, 0) == 41 && !first.WasDisposed && !second.WasDisposed, "3D enumeration failure must preserve caller ownership.");
                    Require(tools.DisposeCalled == (fault != EnumerationFault.GetEnumerator), "The 3D enumerator lifetime must finish after failure.");
                    using CombinedInspectionRunResult recovery = runner.Run(input, new IVisionTool[] { first, second }, new IThreeDInspectionTool[] { thickness });
                    Require(recovery.Success && recovery.Steps.Count == 3 && recovery.Steps[2].Domain == CombinedInspectionDomain.ThreeD, "Both domains must recover in their existing order.");
                    Require(recovery.Steps[2].ThreeDResult.MeasurementOutcome == ThreeDMeasurementOutcome.Passed, "Recovery must measure the unchanged height map.");
                }
                finally
                {
                    first.ResultSnapshot?.Dispose();
                    second.ResultSnapshot?.Dispose();
                }
            }
        }

        private static void TestRepeatedEnumerationRecovery()
        {
            using Mat source = new Mat(3, 5, MatType.CV_8UC1, Scalar.All(17));
            using TrackingDisposableVisionTool tool = new TrackingDisposableVisionTool();
            CombinedInspectionRunner runner = new CombinedInspectionRunner();
            CombinedInspectionInput input = new CombinedInspectionInput { Image = source };
            CombinedInspectionRunResult retained = runner.Run(input, new IVisionTool[] { tool }, null);
            Mat retainedImage = retained.Steps[0].VisionResult.ResultImage;
            try
            {
                for (int repeat = 0; repeat < 8; repeat++)
                {
                    source.Set(0, 0, (byte)(40 + repeat));
                    InvalidOperationException expected = new InvalidOperationException("Repeated list failure " + repeat);
                    FailingToolList<IVisionTool> failed = new FailingToolList<IVisionTool>(new IVisionTool[] { tool }, EnumerationFault.MoveNext, expected);
                    try
                    {
                        Require(ReferenceEquals(RequireThrows<InvalidOperationException>(() => runner.Run(input, failed, null)), expected), "Repeated failure must preserve the exception identity.");
                        Require(tool.ResultSnapshot.IsDisposed, "Each failed run must release its own native snapshot.");
                    }
                    finally { tool.ResultSnapshot?.Dispose(); }
                    using (CombinedInspectionRunResult recovery = runner.Run(input, new IVisionTool[] { tool }, null))
                    {
                        Require(recovery.Success && recovery.Steps.Count == 1, "Each changed list must recover on the same runner.");
                        Mat current = recovery.Steps[0].VisionResult.ResultImage;
                        Require(!ReferenceEquals(current, retainedImage) && current.At<byte>(0, 0) == 40 + repeat, "Recovery must own an independent snapshot of current pixels.");
                        recovery.Dispose();
                        Require(current.IsDisposed, "Disposing recovery must release its current snapshot.");
                    }
                    Require(!retainedImage.IsDisposed && retainedImage.At<byte>(0, 0) == 17, "Later failures and recovery must preserve the retained successful snapshot.");
                    Require(!source.IsDisposed && !tool.WasDisposed, "Repeated runs must not release caller-owned state.");
                }
            }
            finally { retained.Dispose(); }
            Require(retainedImage.IsDisposed && !source.IsDisposed && !tool.WasDisposed, "Only the retained result's Dispose may release its snapshot.");
        }

        private static void TestControlledFaultLifetime()
        {
            using Mat source = new Mat(3, 5, MatType.CV_8UC1, Scalar.All(43));
            using TrackingDisposableVisionTool first = new TrackingDisposableVisionTool();
            using TrackingDisposableVisionTool last = new TrackingDisposableVisionTool();
            using ThrowingDisposableVisionTool throwing = new ThrowingDisposableVisionTool();
            HeightMap3D map = CreatePlaneMap(2, 3, 0.0, 0.0, 1.0);
            ThicknessInspectionTool thickness = new ThicknessInspectionTool(new ThicknessInspectionOptions { MinimumThickness = 0.9, MaximumThickness = 1.1 });
            using CombinedInspectionRunResult result = new CombinedInspectionRunner().Run(
                new CombinedInspectionInput { Image = source, HeightMap = map },
                new IVisionTool[] { first, null, throwing, new NullResultVisionTool(), last },
                new IThreeDInspectionTool[] { null, new ThrowingThreeDTool(), thickness });
            Require(!result.Success && result.Steps.Count == 8 && result.FailedStepCount == 5, "Per-Tool faults must remain controlled and retain every configured step.");
            Require(result.Steps[0].Success && result.Steps[4].Success && result.Steps[7].Success, "Later independent 2D and 3D evidence must still execute.");
            Require(result.Steps[1].VisionResult.ErrorCode == VisionToolErrorCode.ToolPropertyMissing
                && result.Steps[2].VisionResult.Exception is InvalidOperationException
                && result.Steps[3].VisionResult.ErrorCode == VisionToolErrorCode.ToolExecutionException, "Null and thrown 2D faults must retain their existing classification.");
            Require(first.ResultSnapshot.At<byte>(0, 0) == 43 && last.ResultSnapshot.At<byte>(0, 0) == 43, "A returned failed run must retain its successful snapshots until the caller releases it.");
            result.Dispose();
            Require(first.ResultSnapshot.IsDisposed && last.ResultSnapshot.IsDisposed, "Caller disposal must release all returned snapshots even when the run failed.");
            Require(!source.IsDisposed && !first.WasDisposed && !last.WasDisposed && !throwing.WasDisposed, "Result disposal must not release caller-owned inputs or Tools.");
        }

        private enum EnumerationFault { GetEnumerator, MoveNext, Current, Dispose }

        // Isolated test list: each reachable IEnumerable failure keeps the runner's execution order observable.
        private sealed class FailingToolList<T> : IEnumerable<T>, IEnumerator<T>
        {
            private readonly T[] tools;
            private readonly EnumerationFault fault;
            private readonly Exception error;
            private int index = -1;

            internal FailingToolList(T[] tools, EnumerationFault fault, Exception error)
            {
                this.tools = tools;
                this.fault = fault;
                this.error = error;
            }

            internal bool DisposeCalled { get; private set; }

            public T Current
            {
                get
                {
                    if (fault == EnumerationFault.Current && index == 1) throw error;
                    return tools[index];
                }
            }

            object IEnumerator.Current => Current;

            public IEnumerator<T> GetEnumerator()
            {
                if (fault == EnumerationFault.GetEnumerator) throw error;
                return this;
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public bool MoveNext()
            {
                index++;
                if (fault == EnumerationFault.MoveNext && index == tools.Length) throw error;
                return index < tools.Length;
            }

            public void Reset() => throw new NotSupportedException();

            public void Dispose()
            {
                DisposeCalled = true;
                if (fault == EnumerationFault.Dispose) throw error;
            }
        }

        private static void TestCombinedRunnerEmptyConfiguration()
        {
            CombinedInspectionRunResult result = new CombinedInspectionRunner().Run(new CombinedInspectionInput(), null, null);

            Require(!result.Success, "An empty combined configuration must not pass.");
            Require(result.Steps.Count == 0, "An empty combined configuration must not create synthetic tool results.");
        }
    }
}

using OpenVisionLab.FirstInspection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class FrameHostSmokeSuite
    {
        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase("FrameHost H01 H02 borrowed buffer stride and packed formats", TestPixels);
            yield return new SmokeCase("FrameHost H03 H04 callback shutdown generation and restart", TestCallbackShutdown);
            yield return new SmokeCase("FrameHost H05 H06 bounded queue and settings snapshot", TestQueueAndSettings);
            yield return new SmokeCase("FrameHost H07 H08 worker deadline and recovery", TestWorkerShutdown);
            yield return new SmokeCase("FrameHost H05 H08 bounded overload and producer stop barrier", TestOverloadAndProducer);
            yield return new SmokeCase("FrameHost H08 producer failure requires explicit successful retry", TestFailedProducerRetry);
            yield return new SmokeCase("FrameHost H07 malformed callback failure and recovery", TestMalformedRecovery);
            yield return new SmokeCase("FrameHost H08 dispose ownership and reconnect", TestDisposeOwnership);
            yield return new SmokeCase("FrameHost H09 per camera context and duplicate identifiers", TestCorrelation);
            yield return new SmokeCase("FrameHost DEV02 bounded cancellation callbacks and concurrent Stop recovery", TestCancellationCallbacks);
            yield return new SmokeCase("FrameHost DEV02 throwing cancellation preserves error and owner", TestThrowingCancellation);
            yield return new SmokeCase("FrameHost DEV03 acquisition timeout late callback and single shot recovery", TestAcquisitionTimeout);
            yield return new SmokeCase("FrameHost DEV03 timeout during copy retains byte reservation through drain", TestReceivingTimeout);
            yield return new SmokeCase("FrameHost DEV03 continuous missing frames stop cancel and deadline identity", TestTimeoutRaces);
            yield return new SmokeCase("FrameHost DEV03 host wait expires acquisition while inspection worker is busy", TestBusyWorkerDeadline);
            yield return new SmokeCase("FrameHost DEV04 unrelated OCE faults and actual cancellation cannot publish Pass", TestCancellationClassification);
        }

        private static FrameInspection Passed(FrameSnapshot frame, CancellationToken token) => new FrameInspection("Completed", true, "Pass");

        private static void TestCancellationCallbacks()
        {
            using ManualResetEventSlim inspecting = new ManualResetEventSlim(), callback = new ManualResetEventSlim(), release = new ManualResetEventSlim();
            int notifications = 0, producers = 0;
            FrameSession session = new FrameSession("cam", 2, 100, (frame, token) =>
            {
                using CancellationTokenRegistration registration = token.Register(() => { Interlocked.Increment(ref notifications); callback.Set(); release.Wait(); });
                inspecting.Set(); token.WaitHandle.WaitOne(); return Passed(frame, token);
            });
            session.UpdateSettings(Settings()); session.Connect(); session.Start(FrameRunMode.Continuous);
            Require(session.TryTrigger("shot", out FrameTicket ticket, out _) && session.Receive(ticket, new byte[8], "frame", 0, 0, "clock"), "Accept controlled inspection.");
            try
            {
                Require(inspecting.Wait(5000), "Inspection must register callback.");
                System.Diagnostics.Stopwatch elapsed = System.Diagnostics.Stopwatch.StartNew();
                Task<FrameStopResult> first = Task.Run(() => session.Stop(TimeSpan.FromMilliseconds(250), () => Interlocked.Increment(ref producers)));
                Require(callback.Wait(5000), "Cancellation callback must enter barrier.");
                Task<FrameStopResult> second = Task.Run(() => session.Stop(TimeSpan.FromMilliseconds(250), () => Interlocked.Increment(ref producers)));
                FrameStopResult a = first.GetAwaiter().GetResult(), b = second.GetAwaiter().GetResult();
                Require(!a.Complete && !b.Complete && a.OutstandingOwner.Contains("CancellationCallbacks") && b.OutstandingOwner.Contains("CancellationCallbacks"), "Both stops must observe the same bounded callback operation.");
                Require(elapsed.Elapsed < TimeSpan.FromSeconds(3), "The scheduling allowance must not hide an unbounded Cancel call.");
                RequireThrows<InvalidOperationException>(() => session.Start(FrameRunMode.Continuous));
                RequireThrows<InvalidOperationException>(() => session.Dispose());
            }
            finally { release.Set(); }
            FrameStopResult stopped = session.Stop(TimeSpan.FromSeconds(5));
            Require(stopped.Complete && stopped.Error == null && producers == 1 && notifications == 1, "Retry must join, never repeat producer stop or cancellation notification.");
            Require(session.DrainOutcomes().Single().Inspection.Execution == "Canceled" && session.AcceptedCount == session.TerminalCount, "One terminal per accepted shot.");
            session.Start(FrameRunMode.Continuous);
            Require(session.TryTrigger("fresh", out FrameTicket fresh, out _), "Restart after completed callback.");
            Require(!session.Receive(ticket, new byte[8], "late", 1, 1, "clock"), "Old callback must remain retired.");
            Require(session.Stop(TimeSpan.FromSeconds(5)).Complete && session.DrainOutcomes().Single().Ticket.ShotId == fresh.ShotId, "New generation must stop independently.");
            session.Dispose();
        }

        private static void TestThrowingCancellation()
        {
            using ManualResetEventSlim inspecting = new ManualResetEventSlim(), releaseWorker = new ManualResetEventSlim();
            int calls = 0;
            FrameSession session = new FrameSession("cam", 2, 100, (frame, token) =>
            {
                using CancellationTokenRegistration registration = token.Register(() => { Interlocked.Increment(ref calls); throw new InvalidOperationException("callback-failure"); });
                inspecting.Set(); releaseWorker.Wait(CancellationToken.None); return Passed(frame, token);
            });
            session.UpdateSettings(Settings()); session.Connect(); session.Start(FrameRunMode.Continuous);
            Require(session.TryTrigger("shot", out FrameTicket ticket, out _) && session.Receive(ticket, new byte[8], "frame", 0, 0, "clock"), "Accept controlled inspection.");
            try
            {
                Require(inspecting.Wait(5000), "Inspection must register throwing callback.");
                FrameStopResult stop = session.Stop(TimeSpan.FromMilliseconds(250));
                Require(!stop.Complete && stop.Error?.GetBaseException() is InvalidOperationException && stop.Error.GetBaseException().Message == "callback-failure", "Callback failure type/cause must survive the worker deadline.");
                RequireThrows<InvalidOperationException>(() => session.Dispose());
            }
            finally { releaseWorker.Set(); }
            FrameStopResult done = session.Stop(TimeSpan.FromSeconds(5));
            Require(done.Complete && done.Error != null && calls == 1, "Quiescence must retain the shutdown error and callback must run once.");
            Require(session.Stop(TimeSpan.Zero).Error != null && session.DrainOutcomes().Single().Inspection.Decision != "Pass", "Repeated Stop must not erase error or publish a late Pass.");
            var completion = (TaskCompletionSource<FrameStopResult>)typeof(FrameSession).GetField("stopCompletion", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(session);
            session.Start(FrameRunMode.Continuous);
            Require(ReferenceEquals(completion.Task.GetAwaiter().GetResult().Error, done.Error), "An old generation's shared Stop observation must retain its error after restart.");
            FrameStopResult next = session.Stop(TimeSpan.FromSeconds(5));
            Require(next.Complete && next.Error == null, "Old shutdown failure must not contaminate a new generation without that callback.");
            session.Dispose();
        }

        private static FrameSettings TimedSettings(int milliseconds = 50) => new FrameSettings("timeout", 3, 2, 4, FramePixelFormat.Gray8, 1, 0, acquisitionTimeoutMilliseconds: milliseconds);

        private static void TestCancellationClassification()
        {
            int calls = 0;
            using CancellationTokenSource unrelated = new CancellationTokenSource();
            unrelated.Cancel();
            using FrameSession session = new FrameSession("cam", 2, 100, (frame, token) =>
            {
                if (Interlocked.Increment(ref calls) == 1) throw new OperationCanceledException("foreign-operation", unrelated.Token);
                return Passed(frame, token);
            }, "command-test");
            session.UpdateSettings(Settings()); session.Connect(); session.Start(FrameRunMode.Continuous);
            FrameTerminal fault = Single(session, new byte[8]);
            Require(fault.CommandId == "command-test" && fault.ShotId == fault.Ticket.ShotId && fault.Outcome.Execution == "Faulted"
                && !fault.Outcome.MeasurementEstablished && fault.Outcome.Decision == "NotEvaluated" && fault.Outcome.FailureStage == "InspectionWorker"
                && fault.Outcome.ErrorType == typeof(OperationCanceledException).FullName && fault.Outcome.ErrorMessage == "foreign-operation", "Unrelated OCE must preserve original worker fault and correlation.");
            FrameTerminal recovered = Single(session, new byte[8], "recovered");
            Require(recovered.Outcome.Decision == "Pass" && recovered.CommandId == fault.CommandId && recovered.ShotId != fault.ShotId, "Fault recovery must preserve command and distinguish shots.");
            Require(HostOutcome.ExitCode(fault.Outcome) == 3 && HostOutcome.ExitCode(recovered.Outcome) == 0, "Worker fault is exit 3; valid accepted inspection is exit 0.");
            using (System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(fault)))
            {
                Require(json.RootElement.GetProperty("CommandId").GetString() == "command-test" && json.RootElement.GetProperty("ShotId").GetString() == fault.Ticket.ShotId
                    && json.RootElement.GetProperty("Outcome").GetProperty("ErrorType").GetString() == typeof(OperationCanceledException).FullName,
                    "Worker fault JSON must preserve the original cause and terminal identity.");
            }
            Require(HostOutcome.ExitCode(HostOutcome.Summarize(new[] { HostOutcome.Completed(false, false), HostOutcome.Canceled(), fault.Outcome })) == 3, "Fault must outrank cancellation and rejection.");
            Require(HostOutcome.ExitCode(HostOutcome.Summarize(new[] { HostOutcome.Completed(false, false), HostOutcome.Canceled() })) == 130, "Cancellation must outrank completed rejection.");
            Require(HostOutcome.ExitCode(HostOutcome.Completed(false, false)) == 2 && recovered.Outcome.MachineDecision == "NotProvided", "Completed rejection is example acceptance only.");
            Require(session.Stop(TimeSpan.FromSeconds(5)).Complete, "Recovered worker must stop.");
        }

        private static void TestAcquisitionTimeout()
        {
            int inspections = 0, copies = 0;
            using FrameSession session = new FrameSession("cam", 1, 8, (frame, token) => { inspections++; return Passed(frame, token); });
            session.UpdateSettings(TimedSettings()); session.Connect(); session.Start(FrameRunMode.SingleShot);
            Require(session.TryTrigger("missing", out FrameTicket expired, out _), "Missing shot must first be accepted.");
            Require(session.WaitForOutcomes(TimeSpan.FromSeconds(5)), "An autonomous deadline must terminate a missing callback.");
            FrameTerminal timeout = session.DrainOutcomes().Single();
            Require(timeout.Ticket.ShotId == expired.ShotId && timeout.Inspection.Execution == "AcquisitionTimeout" && !timeout.Inspection.MeasurementEstablished && timeout.Inspection.Decision == "NotEvaluated", "Timeout is neither product NG nor a measured zero.");
            Require(!session.Receive(expired, () => { copies++; return new byte[8]; }, "late", 0, long.MaxValue, "device-clock") && copies == 0 && inspections == 0, "Late callback must be rejected before copying borrowed data.");
            session.UpdateSettings(TimedSettings(5000));
            Require(Single(session, new byte[8]).Inspection.Decision == "Pass", "Drain must recover single-shot capacity for valid input.");
            Require(session.Stop(TimeSpan.FromSeconds(5)).Complete && session.AcceptedCount == session.TerminalCount, "All accepted shots must terminate once.");
        }

        private static void TestReceivingTimeout()
        {
            using ManualResetEventSlim copying = new ManualResetEventSlim(), release = new ManualResetEventSlim();
            int inspections = 0;
            using FrameSession session = new FrameSession("cam", 1, 8, (frame, token) => { inspections++; return Passed(frame, token); });
            session.UpdateSettings(TimedSettings(150)); session.Connect(); session.Start(FrameRunMode.Continuous);
            Require(session.TryTrigger("copying", out FrameTicket ticket, out _), "Accept copying shot.");
            Task<bool> receive = Task.Run(() => session.Receive(ticket, () => { copying.Set(); release.Wait(); return new byte[8]; }, "frame", 0, 0, "clock"));
            try
            {
                Require(copying.Wait(5000) && session.WaitForOutcomes(TimeSpan.FromSeconds(5)), "Copy and acquisition deadline must both be observed.");
                Require(session.DrainOutcomes().Single().Inspection.Execution == "AcquisitionTimeout", "Receiving can time out without releasing its owner.");
                Require(session.OutstandingItems == 1 && session.OutstandingBytes == 8, "Draining terminal must retain the in-progress copy reservation.");
                Require(!session.TryTrigger("overload", out _, out string reason) && reason == "Overload", "Consumed terminal cannot overcommit bytes while copy remains active.");
                FrameStopResult stop = session.Stop(TimeSpan.Zero);
                Require(!stop.Complete, "Stop must retain the active acquisition callback.");
            }
            finally { release.Set(); }
            Require(!receive.GetAwaiter().GetResult() && inspections == 0 && session.OutstandingBytes == 0, "Late copy commit must discard pixels and release consumed reservation only after copy finishes.");
            Require(session.Stop(TimeSpan.FromSeconds(5)).Complete && session.AcceptedCount == session.TerminalCount, "Stop must complete without a duplicate timeout terminal.");
            session.UpdateSettings(TimedSettings(5000)); session.Start(FrameRunMode.Continuous);
            Require(Single(session, new byte[8]).Inspection.Decision == "Pass", "The same session must recover after timed-out copy and Stop.");
            Require(session.Stop(TimeSpan.FromSeconds(5)).Complete, "Recovered session stops.");
        }

        private static void TestTimeoutRaces()
        {
            using CancellationTokenSource cancel = new CancellationTokenSource();
            using FrameSession session = new FrameSession("cam", 4, 32, Passed);
            session.UpdateSettings(TimedSettings(150)); session.Connect(); session.Start(FrameRunMode.Continuous, cancel.Token);
            Require(session.TryTrigger("valid", out FrameTicket valid, out _) && session.TryTrigger("missing-A", out _, out _) && session.TryTrigger("missing-B", out _, out _), "Accept valid and missing shots independently.");
            Require(session.Receive(valid, new byte[8], "frame", 0, 0, "clock"), "Receive only one callback.");
            List<FrameTerminal> outcomes = new List<FrameTerminal>();
            while (outcomes.Count < 3) { Require(session.WaitForOutcomes(TimeSpan.FromSeconds(5)), "Each accepted shot must produce terminal."); outcomes.AddRange(session.DrainOutcomes()); }
            Require(outcomes.Count(o => o.Inspection.Execution == "AcquisitionTimeout") == 2 && outcomes.Count(o => o.Inspection.Decision == "Pass") == 1, "Continuous timeout identities must not overwrite the received shot.");
            Require(session.TryTrigger("race", out _, out _), "Drained capacity must recover.");
            Task canceling = Task.Run(() => cancel.Cancel());
            FrameStopResult stop = session.Stop(TimeSpan.FromSeconds(5)); canceling.GetAwaiter().GetResult();
            outcomes.AddRange(session.DrainOutcomes());
            Require(stop.Complete && outcomes.Count == 4 && outcomes.Select(o => o.Ticket.ShotId).Distinct().Count() == 4 && session.AcceptedCount == session.TerminalCount && session.OutstandingBytes == 0, "Stop/cancel/deadline must leave one terminal per accepted shot and no reservation.");
        }

        private static void TestBusyWorkerDeadline()
        {
            using ManualResetEventSlim entered = new ManualResetEventSlim(), release = new ManualResetEventSlim();
            using FrameSession session = new FrameSession("cam", 3, 24, (frame, token) => { entered.Set(); release.Wait(CancellationToken.None); return Passed(frame, token); });
            session.UpdateSettings(TimedSettings(150)); session.Connect(); session.Start(FrameRunMode.Continuous);
            Require(session.TryTrigger("active", out FrameTicket active, out _) && session.Receive(active, new byte[8], "frame", 0, 0, "clock") && entered.Wait(5000), "Hold the inspection worker with a received frame.");
            try
            {
                Require(session.TryTrigger("missing", out FrameTicket missing, out _), "Accept a separate missing acquisition.");
                System.Diagnostics.Stopwatch elapsed = System.Diagnostics.Stopwatch.StartNew();
                Require(session.WaitForOutcomes(TimeSpan.FromSeconds(5)), "The Host must observe the missing acquisition's own deadline.");
                Require(elapsed.Elapsed < TimeSpan.FromSeconds(2), "A busy inspection must not defer acquisition expiry until the unrelated five-second caller wait expires.");
                FrameTerminal timeout = session.DrainOutcomes().Single();
                Require(timeout.ShotId == missing.ShotId && timeout.Outcome.Execution == "AcquisitionTimeout" && session.OutstandingBytes == 8, "Only the missing shot times out; the active worker retains its frame reservation.");
            }
            catch { release.Set(); session.Stop(TimeSpan.FromSeconds(5)); throw; }
            finally { release.Set(); }
            Require(session.WaitForOutcomes(TimeSpan.FromSeconds(5)) && session.DrainOutcomes().Single().ShotId == active.ShotId, "The active inspection must still produce its own terminal.");
            session.UpdateSettings(TimedSettings(5000));
            Require(Single(session, new byte[8], "recovered").Outcome.Decision == "Pass", "The same session must recover after the independently expired acquisition.");
            Require(session.Stop(TimeSpan.FromSeconds(5)).Complete && session.AcceptedCount == session.TerminalCount, "All accepted identities must terminate once.");
        }
        private static FrameSettings Settings(string revision = "A") => new FrameSettings(revision, 3, 2, 4, FramePixelFormat.Gray8, 1, 0);

        private static FrameTerminal Single(FrameSession session, byte[] raw, string frameId = "frame")
        {
            Require(session.TryTrigger("trigger", out FrameTicket ticket, out _), "Trigger must be accepted.");
            Require(session.Receive(ticket, raw, frameId, 4, 9, "device-clock"), "Frame must be received.");
            Require(session.WaitForOutcomes(TimeSpan.FromSeconds(5)), "Terminal result timed out.");
            FrameTerminal[] terminals = session.DrainOutcomes();
            Require(terminals.Length == 1, "Exactly one terminal result is required.");
            return terminals[0];
        }

        private static void TestPixels()
        {
            FrameSession session = new FrameSession("cam", 4, 1000, Passed);
            session.UpdateSettings(Settings()); session.Connect(); session.Start(FrameRunMode.Continuous);
            byte[] raw = { 1, 2, 3, 99, 4, 5, 6, 98 };
            Require(session.TryTrigger("trigger", out FrameTicket ticket, out _), "Trigger must be accepted.");
            Require(session.Receive(ticket, raw, "frame", 4, 9, "device-clock"), "Frame must be received.");
            Array.Fill(raw, (byte)0); // Emulate vendor reuse immediately after callback return.
            Require(session.WaitForOutcomes(TimeSpan.FromSeconds(5)), "Terminal result timed out.");
            FrameTerminal result = session.DrainOutcomes().Single();
            Require(result.Frame.CopyRaw().SequenceEqual(new byte[] { 1, 2, 3, 99, 4, 5, 6, 98 }), "Borrowed buffer overwrite changed retained pixels or padding.");
            Require(result.Frame.CreateGray8().SequenceEqual(new byte[] { 1, 2, 3, 4, 5, 6 }), "Gray8 must ignore padding.");
            byte[] copy = result.Frame.CopyRaw(); copy[0] = 88;
            Require(result.Frame.CopyRaw()[0] == 1, "CopyRaw must not expose owned storage.");
            byte[] rgba = result.Frame.CreatePreviewRgba();
            Require(rgba.Length == 24 && rgba[0] == 1 && rgba[1] == 1 && rgba[2] == 1 && rgba[3] == 255, "Preview must independently expand gray and alpha.");
            Require(session.Stop(TimeSpan.FromSeconds(5)).Complete, "Stop must release worker.");

            FrameSettings word = new FrameSettings("word", 3, 1, 6, FramePixelFormat.Gray16LittleEndian, 0.5, 1, 65535, 7);
            FrameSession wordSession = new FrameSession("cam", 2, 100, Passed);
            wordSession.UpdateSettings(word); wordSession.Connect(); wordSession.Start(FrameRunMode.Continuous);
            FrameTerminal wordResult = Single(wordSession, new byte[] { 2, 0, 0, 2, 255, 255 });
            Require(wordResult.Frame.CreateGray8().SequenceEqual(new byte[] { 2, 255, 7 }), "16-bit little-endian scale, clamp and missing mapping differ.");
            Require(wordSession.Stop(TimeSpan.FromSeconds(5)).Complete, "Word session must stop.");

            foreach (int bits in new[] { 10, 12 })
            {
                // Independently pack three values into a row-reset, LSB-first stream.
                int[] values = { 1, (1 << bits) - 2, 17, 3, 0, (1 << bits) - 1 };
                int stride = (3 * bits + 7) / 8 + 1; // odd-width rows with one padding byte.
                byte[] packed = new byte[stride * 2];
                for (int i = 0; i < values.Length; i++)
                    for (int b = 0; b < bits; b++)
                        if ((values[i] & (1 << b)) != 0)
                            packed[(i / 3) * stride + ((i % 3) * bits + b) / 8] |= (byte)(1 << (((i % 3) * bits + b) % 8));
                FramePixelFormat format = bits == 10 ? FramePixelFormat.TestPacked10RowLittleEndian : FramePixelFormat.TestPacked12RowLittleEndian;
                FrameSession s = new FrameSession("cam", 2, 100, Passed);
                s.UpdateSettings(new FrameSettings("packed", 3, 2, stride, format, 1, 0));
                s.Connect(); s.Start(FrameRunMode.Continuous);
                FrameTerminal item = Single(s, packed);
                Require(item.Frame.CreateGray8().SequenceEqual(new byte[] { 1, 255, 17, 3, 0, 255 }), "Packed row conversion differs from authored bit pattern.");
                Require(item.Frame.CopyRaw().SequenceEqual(packed), "Conversion must not alter raw packed bytes.");
                Require(s.Stop(TimeSpan.FromSeconds(5)).Complete, "Packed session must stop.");
            }
        }

        private static void TestCallbackShutdown()
        {
            using ManualResetEventSlim entered = new ManualResetEventSlim();
            using ManualResetEventSlim release = new ManualResetEventSlim();
            FrameSession s = new FrameSession("cam", 3, 100, Passed);
            s.UpdateSettings(Settings()); s.Connect();
            long a = s.Start(FrameRunMode.Continuous);
            Require(s.Start(FrameRunMode.Continuous) == a, "Repeated start must be idempotent.");
            Require(s.TryTrigger("A", out FrameTicket old, out _), "First trigger must be accepted.");
            Task<bool> receive = Task.Run(() => s.Receive(old, () => { entered.Set(); release.Wait(); return new byte[8]; }, "frame-A", 1, 100, "clock"));
            try
            {
                Require(entered.Wait(5000), "Callback did not enter controlled copy.");
                FrameStopResult timeout = s.Stop(TimeSpan.FromMilliseconds(250));
                Require(!timeout.Complete && timeout.OutstandingOwner.Contains("AcquisitionCallback"), "Timeout must retain active callback ownership.");
            }
            finally { release.Set(); }
            Require(!receive.GetAwaiter().GetResult(), "Retired callback must not publish a frame.");
            Require(s.Stop(TimeSpan.FromSeconds(5)).Complete, "Stop must finish after callback release.");
            s.DrainOutcomes();
            long b = s.Start(FrameRunMode.Continuous);
            Require(b > a && !s.Receive(old, new byte[8], "late", 2, 101, "clock"), "Retired generation must not enter the new session.");
            FrameTerminal current = Single(s, new byte[] { 1, 2, 3, 0, 4, 5, 6, 0 }, "frame-B");
            Require(current.Frame.Context.Generation == b && current.Frame.Context.FrameId == "frame-B", "New generation must preserve frame identity.");
            Require(s.Stop(TimeSpan.FromSeconds(5)).Complete, "Restarted session must stop.");
        }

        private static void TestQueueAndSettings()
        {
            using ManualResetEventSlim entered = new ManualResetEventSlim();
            using ManualResetEventSlim release = new ManualResetEventSlim();
            FrameSession s = new FrameSession("cam", 1, 8, (frame, token) => { entered.Set(); release.Wait(CancellationToken.None); return Passed(frame, token); });
            s.UpdateSettings(Settings("old")); s.Connect(); s.Start(FrameRunMode.SingleShot);
            Require(s.TryTrigger("one", out FrameTicket first, out _), "First trigger must succeed.");
            Require(s.Receive(first, new byte[] { 1, 2, 3, 0, 4, 5, 6, 0 }, "f1", 1, 4, "clock"), "First callback must succeed.");
            try
            {
                Require(entered.Wait(5000), "Inspection must enter controlled worker.");
                s.UpdateSettings(Settings("new"));
                Require(!s.TryTrigger("two", out _, out string reason) && reason == "Busy", "Single-shot must reject overlapping inspection.");
                Require(first.Settings.Revision == "old", "In-flight settings changed.");
                FrameTeaching taught = s.SelectTeachFrame(first.ShotId, "model-1");
                Require(taught.SettingsRevision == "old" && taught.Frame.Context.ShotId == first.ShotId, "Teaching must bind the explicitly selected frame and settings.");
            }
            finally { release.Set(); }
            Require(s.WaitForOutcomes(TimeSpan.FromSeconds(5)), "First worker must finish.");
            FrameTerminal[] results = s.DrainOutcomes();
            Require(results.Length == 1 && results[0].Ticket.TriggerId == "one", "Outcome must preserve trigger identity.");
            Require(s.Stop(TimeSpan.FromSeconds(5)).Complete, "Queue session must stop.");
        }

        private static void TestWorkerShutdown()
        {
            using ManualResetEventSlim entered = new ManualResetEventSlim();
            using ManualResetEventSlim release = new ManualResetEventSlim();
            FrameSession s = new FrameSession("cam", 3, 100, (frame, token) => { entered.Set(); release.Wait(CancellationToken.None); return Passed(frame, token); });
            s.UpdateSettings(Settings()); s.Connect(); s.Start(FrameRunMode.Continuous);
            Require(s.TryTrigger("active", out FrameTicket active, out _), "Trigger must be accepted.");
            Require(s.Receive(active, new byte[8], "frame", 0, 0, "clock"), "Callback must be accepted.");
            try
            {
                Require(entered.Wait(5000), "Worker must enter barrier.");
                FrameStopResult timeout = s.Stop(TimeSpan.FromMilliseconds(250));
                Require(!timeout.Complete && timeout.OutstandingOwner.Contains("InspectionWorker"), "Timeout must retain worker and dependencies.");
            }
            finally { release.Set(); }
            Require(s.Stop(TimeSpan.FromSeconds(5)).Complete, "Retry must finish after worker release.");
            FrameTerminal[] done = s.DrainOutcomes();
            Require(done.Length == 1 && done[0].Inspection.Execution == "Canceled" && !done[0].Inspection.MeasurementEstablished, "Stop during inspection must not publish Pass.");
            s.Start(FrameRunMode.Continuous);
            FrameTerminal recovered = Single(s, new byte[8], "fresh");
            Require(recovered.Inspection.Execution == "Completed", "Restart must recover.");
            Require(s.Stop(TimeSpan.FromSeconds(5)).Complete, "Recovered session must stop.");
        }

        private static void TestOverloadAndProducer()
        {
            using ManualResetEventSlim workerEntered = new ManualResetEventSlim();
            using ManualResetEventSlim workerRelease = new ManualResetEventSlim();
            using ManualResetEventSlim producerEntered = new ManualResetEventSlim();
            using ManualResetEventSlim producerRelease = new ManualResetEventSlim();
            int producerCalls = 0;
            FrameSession s = new FrameSession("cam", 1, 8, (frame, token) => { workerEntered.Set(); workerRelease.Wait(CancellationToken.None); return Passed(frame, token); });
            s.UpdateSettings(Settings()); s.Connect(); s.Start(FrameRunMode.Continuous);
            Require(s.TryTrigger("one", out FrameTicket first, out _), "First trigger must be accepted.");
            Require(s.Receive(first, new byte[8], "frame", 1, 1, "clock"), "First frame must enter worker.");
            try
            {
                Require(workerEntered.Wait(5000), "Worker barrier did not start.");
                Require(!s.TryTrigger("two", out _, out string reason) && reason == "Overload", "Continuous queue must reject overload explicitly.");
                Require(s.OutstandingItems == 1 && s.OutstandingBytes == 8, "Queue capacity must remain bounded.");
                Task<FrameStopResult> stop = Task.Run(() => s.Stop(TimeSpan.FromMilliseconds(500), () =>
                {
                    Interlocked.Increment(ref producerCalls); producerEntered.Set(); producerRelease.Wait();
                }));
                Require(producerEntered.Wait(5000), "Producer stop barrier did not start.");
                FrameStopResult timeout = stop.GetAwaiter().GetResult();
                Require(!timeout.Complete && timeout.OutstandingOwner.Contains("ProducerStop"), "A blocked producer stop must retain camera ownership.");
            }
            finally { producerRelease.Set(); workerRelease.Set(); }
            Require(s.Stop(TimeSpan.FromSeconds(5), () => Interlocked.Increment(ref producerCalls)).Complete, "Stop retry must complete after both owners release.");
            Require(producerCalls == 1, "Retry must not invoke producer stop twice.");
            FrameTerminal[] outcomes = s.DrainOutcomes();
            Require(outcomes.Length == 1 && outcomes[0].Ticket.TriggerId == "one" && outcomes[0].Inspection.Execution == "Canceled", "Overload or shutdown must not silently relabel a shot.");
        }
        private static void TestFailedProducerRetry()
        {
            foreach (bool canceled in new[] { false, true })
            {
                int calls = 0;
                FrameSession session = new FrameSession("cam", 3, 100, Passed);
                session.UpdateSettings(Settings()); session.Connect(); session.Start(FrameRunMode.Continuous);
                FrameStopResult failed = session.Stop(TimeSpan.FromSeconds(5), () =>
                {
                    Interlocked.Increment(ref calls);
                    if (canceled) throw new OperationCanceledException("authored producer cancellation");
                    throw new IOException("authored producer fault");
                });
                Require(!failed.Complete && failed.OutstandingOwner.Contains("ProducerStop") && calls == 1,
                    "A faulted or canceled producer stop must remain incomplete.");
                FrameStopResult implicitRetry = session.Stop(TimeSpan.FromSeconds(5));
                Require(!implicitRetry.Complete && implicitRetry.OutstandingOwner.Contains("ProducerStop") && calls == 1,
                    "A retry without an explicit producer action must not acknowledge the failure.");
                RequireThrows<InvalidOperationException>(() => session.Dispose());
                FrameStopResult recovered = session.Stop(TimeSpan.FromSeconds(5), () => Interlocked.Increment(ref calls));
                Require(recovered.Complete && calls == 2, "A new explicit successful producer action must complete the stop.");
                Require(session.Disconnect(TimeSpan.FromSeconds(5)).Complete, "Successful stop must permit disconnect.");
                session.Connect(); session.Start(FrameRunMode.Continuous);
                FrameTerminal frame = Single(session, new byte[] { 1, 2, 3, 0, 4, 5, 6, 0 }, "recovered");
                Require(frame.Inspection.Execution == "Completed", "Reconnected session must inspect successfully after producer recovery.");
                Require(session.Stop(TimeSpan.FromSeconds(5)).Complete, "Recovered session must stop.");
                session.Dispose();
            }
        }
        private static void TestMalformedRecovery()
        {
            FrameSession s = new FrameSession("cam", 3, 100, Passed);
            s.UpdateSettings(Settings("known-good")); s.Connect(); s.Start(FrameRunMode.Continuous);
            RequireThrows<ArgumentException>(() => { _ = new FrameSettings("bad", 3, 2, 1, FramePixelFormat.Gray8, 1, 0); });
            Require(s.TryTrigger("bad", out FrameTicket invalid, out _), "Faulting trigger must still receive a terminal result.");
            Require(!s.Receive(invalid, new byte[7], "bad-frame", 1, 2, "clock"), "Truncated borrowed frame must be rejected.");
            Require(s.WaitForOutcomes(TimeSpan.FromSeconds(5)), "Malformed callback must produce a terminal outcome.");
            FrameTerminal[] failed = s.DrainOutcomes();
            Require(failed.Length == 1 && failed[0].Inspection.Execution == "Faulted" && !failed[0].Inspection.MeasurementEstablished,
                "Malformed frame cannot become an approved measurement.");
            FrameTerminal recovered = Single(s, new byte[] { 1, 2, 3, 0, 4, 5, 6, 0 }, "recovered");
            Require(recovered.Inspection.Execution == "Completed" && recovered.Ticket.Settings.Revision == "known-good",
                "A valid frame must recover with the retained configuration.");
            Require(s.Stop(TimeSpan.FromSeconds(5)).Complete, "Recovered session must stop.");
        }
        private static void TestDisposeOwnership()
        {
            FrameSession neverStarted = new FrameSession("unused", 2, 100, Passed);
            neverStarted.Dispose();
            neverStarted.Dispose();
            RequireThrows<ObjectDisposedException>(() => neverStarted.Connect());

            using ManualResetEventSlim entered = new ManualResetEventSlim();
            using ManualResetEventSlim release = new ManualResetEventSlim();
            FrameSession session = new FrameSession("cam", 3, 100, (frame, token) =>
            {
                entered.Set();
                release.Wait(CancellationToken.None);
                return Passed(frame, token);
            });
            session.UpdateSettings(Settings()); session.Connect(); session.Start(FrameRunMode.Continuous);
            Require(session.TryTrigger("active", out FrameTicket ticket, out _), "Active trigger must succeed.");
            Require(session.Receive(ticket, new byte[] { 1, 2, 3, 9, 4, 5, 6, 9 }, "frame", 1, 10, "clock"), "Active callback must succeed.");
            FrameSnapshot retained = session.LatestDisplay;
            try
            {
                Require(entered.Wait(5000), "Worker must enter controlled barrier.");
                RequireThrows<InvalidOperationException>(() => session.Dispose());
                FrameStopResult timeout = session.Stop(TimeSpan.FromMilliseconds(250));
                Require(!timeout.Complete && timeout.OutstandingOwner.Contains("InspectionWorker"), "A blocked worker must retain its owners.");
                RequireThrows<InvalidOperationException>(() => session.Dispose());
                Require(retained.CopyRaw()[0] == 1, "Rejected Dispose must not invalidate the in-use snapshot.");
            }
            finally { release.Set(); }
            Require(session.Stop(TimeSpan.FromSeconds(5)).Complete, "Stop retry must finish after the worker exits.");
            FrameTerminal canceled = session.DrainOutcomes().Single();
            Require(canceled.Inspection.Execution == "Canceled", "Stopping the active worker must not approve its result.");
            Require(session.Disconnect(TimeSpan.FromSeconds(5)).Complete, "Disconnect must succeed after Stop.");
            session.Connect();
            session.Start(FrameRunMode.Continuous);
            FrameTerminal fresh = Single(session, new byte[] { 7, 8, 9, 0, 1, 2, 3, 0 }, "fresh");
            Require(fresh.Inspection.Execution == "Completed", "Disconnect must allow a fresh connection and run.");
            Require(session.Disconnect(TimeSpan.FromSeconds(5)).Complete, "Reconnected session must disconnect.");
            long accepted = session.AcceptedCount;
            session.Dispose();
            session.Dispose();
            Require(session.AcceptedCount == accepted, "Frozen diagnostic counters must remain readable.");
            Require(retained.CopyRaw()[0] == 1 && fresh.Frame.CopyRaw()[0] == 7, "Previously returned snapshots must outlive the session.");
            bool copied = false;
            Require(!session.Receive(ticket, () => { copied = true; return new byte[8]; }, "late", 2, 11, "clock") && !copied,
                "A late callback after Dispose must be rejected without borrowing or copying.");
            RequireThrows<ObjectDisposedException>(() => session.Connect());
            RequireThrows<ObjectDisposedException>(() => session.UpdateSettings(Settings()));
            RequireThrows<ObjectDisposedException>(() => session.Start(FrameRunMode.Continuous));
            RequireThrows<ObjectDisposedException>(() => { session.TryTrigger("late", out _, out _); });
            RequireThrows<ObjectDisposedException>(() => session.SelectTeachFrame(ticket.ShotId, "revision"));
            RequireThrows<ObjectDisposedException>(() => session.WaitForOutcomes(TimeSpan.Zero));
            RequireThrows<ObjectDisposedException>(() => session.DrainOutcomes());
            RequireThrows<ObjectDisposedException>(() => session.Stop(TimeSpan.Zero));
            RequireThrows<ObjectDisposedException>(() => session.Disconnect(TimeSpan.Zero));
        }
        private static void TestCorrelation()
        {
            FrameSession a = new FrameSession("A", 2, 100, Passed);
            FrameSession b = new FrameSession("B", 2, 100, Passed);
            a.UpdateSettings(Settings()); b.UpdateSettings(Settings()); a.Connect(); b.Connect(); a.Start(FrameRunMode.Continuous); b.Start(FrameRunMode.Continuous);
            FrameTerminal one = Single(a, new byte[8], "same-frame-id");
            FrameTerminal two = Single(b, new byte[8], "same-frame-id");
            Require(one.Frame.Context.CameraId == "A" && two.Frame.Context.CameraId == "B" && one.Ticket.SessionId != two.Ticket.SessionId, "Duplicate frame ID across cameras must not conflate sessions.");
            Require(one.Frame.Context.ClockDomain == "device-clock" && one.Frame.Context.Sequence == 4 && one.Frame.Context.DeviceTimestamp == 9, "Context must preserve independent clock and sequence.");
            Require(a.TryTrigger("second", out FrameTicket ticket, out _), "Second trigger must be accepted.");
            Require(a.Receive(ticket, new byte[8], "same-frame-id", 2, 1, "other-clock"), "Out-of-order context must be retained.");
            Require(a.WaitForOutcomes(TimeSpan.FromSeconds(5)), "Second frame must finish.");
            FrameTerminal second = a.DrainOutcomes().Single();
            Require(second.Frame.Context.Sequence == 2 && second.Frame.Context.DeviceTimestamp == 1 && second.Frame.Context.ClockDomain == "other-clock",
                "Duplicate IDs and nonmonotonic independent clock values must not be rewritten.");
            Require(second.Ticket.ShotId != one.Ticket.ShotId && second.Ticket.TriggerId == "second", "Distinct shots must stay distinguishable.");
            Require(a.Stop(TimeSpan.FromSeconds(5)).Complete && b.Stop(TimeSpan.FromSeconds(5)).Complete, "Both sessions must stop.");
        }
    }
}

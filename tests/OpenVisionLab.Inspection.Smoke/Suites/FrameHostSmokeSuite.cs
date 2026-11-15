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
        }

        private static FrameInspection Passed(FrameSnapshot frame, CancellationToken token) => new FrameInspection("Completed", true, "Pass");
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

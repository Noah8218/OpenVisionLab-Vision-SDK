#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OpenVisionLab.FirstInspection
{
    // These packed formats are local test formats: row-reset, least-significant bits first.
    // They do not claim compatibility with a camera vendor or PFNC format.
    internal enum FramePixelFormat { Gray8, Gray16LittleEndian, TestPacked10RowLittleEndian, TestPacked12RowLittleEndian }
    internal enum FrameRunMode { SingleShot, Continuous }

    internal sealed class FrameSettings
    {
        internal FrameSettings(string revision, int width, int height, int stride, FramePixelFormat format, double scale, double offset, ushort? missingValue = null, byte missingGray = 0, string modelRevision = "unassigned", int acquisitionTimeoutMilliseconds = 0)
        {
            if (string.IsNullOrWhiteSpace(revision) || string.IsNullOrWhiteSpace(modelRevision) || width < 1 || height < 1 || !Enum.IsDefined(typeof(FramePixelFormat), format) || !Finite(scale) || scale <= 0 || !Finite(offset)) throw new ArgumentException("Invalid frame settings.");
            int bits = format == FramePixelFormat.Gray8 ? 8 : format == FramePixelFormat.Gray16LittleEndian ? 16 : format == FramePixelFormat.TestPacked10RowLittleEndian ? 10 : 12;
            long rowBytes = ((long)width * bits + 7) / 8;
            if (stride < rowBytes || (long)stride * height > 64 * 1024 * 1024 || (long)width * height > 16 * 1024 * 1024) throw new ArgumentException("Invalid frame dimensions, stride or frame size limit.");
            if (missingValue.HasValue && missingValue.Value > ((1 << bits) - 1)) throw new ArgumentException("Missing sentinel exceeds the raw format.");
            ArgumentOutOfRangeException.ThrowIfNegative(acquisitionTimeoutMilliseconds);
            AcquisitionTimeoutMilliseconds = acquisitionTimeoutMilliseconds;
            Revision = revision; Width = width; Height = height; Stride = stride; Format = format; Scale = scale; Offset = offset; MissingValue = missingValue; MissingGray = missingGray; ModelRevision = modelRevision;
        }
        public int AcquisitionTimeoutMilliseconds { get; }
        public string Revision { get; }
        public string ModelRevision { get; }
        public int RawDepthBits => Format == FramePixelFormat.Gray8 ? 8 : Format == FramePixelFormat.Gray16LittleEndian ? 16 : Format == FramePixelFormat.TestPacked10RowLittleEndian ? 10 : 12;
        public int Width { get; }
        public int Height { get; }
        public int Stride { get; }
        public FramePixelFormat Format { get; }
        public double Scale { get; }
        public double Offset { get; }
        public ushort? MissingValue { get; }
        public byte MissingGray { get; }
        internal int RawBytes => checked(Stride * Height);
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    internal sealed class FrameTicket
    {
        internal FrameTicket(string cameraId, string sessionId, long generation, string shotId, string triggerId, FrameSettings settings, string commandId)
        { CommandId = commandId; CameraId = cameraId; SessionId = sessionId; Generation = generation; ShotId = shotId; TriggerId = triggerId; Settings = settings; AcceptedTimestamp = Stopwatch.GetTimestamp(); AcquisitionDeadline = settings.AcquisitionTimeoutMilliseconds == 0 ? 0 : checked(AcceptedTimestamp + (long)Math.Ceiling(settings.AcquisitionTimeoutMilliseconds * (double)Stopwatch.Frequency / 1000)); }
        public string CommandId { get; }
        public long AcceptedTimestamp { get; }
        public long AcquisitionDeadline { get; }
        public string CameraId { get; }
        public string SessionId { get; }
        public long Generation { get; }
        public string ShotId { get; }
        public string TriggerId { get; }
        public FrameSettings Settings { get; }
    }

    internal sealed class FrameContext
    {
        internal FrameContext(FrameTicket ticket, string frameId, long sequence, long deviceTimestamp, string clockDomain)
        { Ticket = ticket; FrameId = frameId; Sequence = sequence; DeviceTimestamp = deviceTimestamp; ClockDomain = clockDomain; HostReceivedAt = DateTimeOffset.UtcNow; }
        public FrameTicket Ticket { get; }
        public string CommandId => Ticket.CommandId;
        public string CameraId => Ticket.CameraId;
        public string SessionId => Ticket.SessionId;
        public long Generation => Ticket.Generation;
        public string ShotId => Ticket.ShotId;
        public string TriggerId => Ticket.TriggerId;
        public string FrameId { get; }
        public long Sequence { get; }
        public long DeviceTimestamp { get; }
        public string ClockDomain { get; }
        public DateTimeOffset HostReceivedAt { get; }
        public string SettingsRevision => Ticket.Settings.Revision;
        public string ModelRevision => Ticket.Settings.ModelRevision;
        public int RawDepthBits => Ticket.Settings.RawDepthBits;
        public FramePixelFormat PixelFormat => Ticket.Settings.Format;
        public int Width => Ticket.Settings.Width;
        public int Height => Ticket.Settings.Height;
        public int Stride => Ticket.Settings.Stride;
    }

    internal sealed class FrameSnapshot
    {
        private readonly byte[] raw;
        internal FrameSnapshot(FrameContext context, byte[] ownedRaw) { Context = context; raw = ownedRaw; }
        public FrameContext Context { get; }
        public FrameSettings Settings => Context.Ticket.Settings;
        internal byte[] CopyRaw() => (byte[])raw.Clone();
        internal byte[] CreateGray8()
        {
            FrameSettings s = Settings;
            byte[] gray = new byte[checked(s.Width * s.Height)];
            int bits = s.Format == FramePixelFormat.Gray8 ? 8 : s.Format == FramePixelFormat.Gray16LittleEndian ? 16 : s.Format == FramePixelFormat.TestPacked10RowLittleEndian ? 10 : 12;
            for (int y = 0; y < s.Height; y++)
            {
                int row = y * s.Stride;
                for (int x = 0; x < s.Width; x++)
                {
                    int value;
                    if (bits == 8) value = raw[row + x];
                    else if (bits == 16) value = raw[row + x * 2] | raw[row + x * 2 + 1] << 8;
                    else
                    {
                        int bit = x * bits, index = row + bit / 8, shift = bit % 8;
                        int packed = raw[index];
                        if (shift + bits > 8) packed |= raw[index + 1] << 8;
                        if (shift + bits > 16) packed |= raw[index + 2] << 16;
                        value = (packed >> shift) & ((1 << bits) - 1);
                    }
                    double mapped = value * s.Scale + s.Offset;
                    gray[y * s.Width + x] = s.MissingValue == value ? s.MissingGray : (byte)Math.Max(0, Math.Min(255, Math.Round(mapped, MidpointRounding.AwayFromZero)));
                }
            }
            return gray;
        }
        internal byte[] CreatePreviewRgba()
        {
            byte[] gray = CreateGray8(), rgba = new byte[checked(gray.Length * 4)];
            for (int i = 0; i < gray.Length; i++) { rgba[i * 4] = gray[i]; rgba[i * 4 + 1] = gray[i]; rgba[i * 4 + 2] = gray[i]; rgba[i * 4 + 3] = 255; }
            return rgba;
        }
    }

    internal sealed class FrameInspection
    {
        internal FrameInspection(string execution, bool measured, string decision, object? evidence = null,
            string? failureStage = null, Exception? error = null, string? errorCode = null)
        {
            Outcome = HostOutcome.Frame(execution, measured, decision);
            if (execution == "Canceled" && failureStage == null) { failureStage = "Cancellation"; errorCode = "Canceled"; }
            if (failureStage != null) Outcome = Outcome.WithFailure(failureStage, errorCode ?? error?.GetType().Name, error?.GetType().FullName, error?.Message ?? errorCode);
            Evidence = evidence;
        }
        internal FrameInspection(HostOutcome outcome, object? evidence = null) { Outcome = outcome; Evidence = evidence; }
        public HostOutcome Outcome { get; }
        public string Execution => Outcome.Execution;
        public bool MeasurementEstablished => Outcome.MeasurementEstablished;
        public string Decision => Outcome.Decision;
        public string? FailureStage => Outcome.FailureStage;
        public string? ErrorCode => Outcome.ErrorCode;
        public string? ErrorType => Outcome.ErrorType;
        public string? ErrorMessage => Outcome.ErrorMessage;
        public object? Evidence { get; }
    }
    internal sealed class FrameTerminal
    {
        internal FrameTerminal(FrameTicket ticket, FrameSnapshot? frame, FrameInspection inspection) { Ticket = ticket; Frame = frame; Inspection = inspection; }
        public int SchemaVersion { get; } = 2;
        public string CommandId => Ticket.CommandId;
        public string ShotId => Ticket.ShotId;
        public HostOutcome Outcome => Inspection.Outcome;
        public FrameTicket Ticket { get; }
        public FrameSnapshot? Frame { get; }
        public FrameInspection Inspection { get; }
    }
    internal sealed class FrameTeaching
    {
        internal FrameTeaching(FrameSnapshot frame, string modelRevision) { Frame = frame; ModelRevision = modelRevision; }
        public FrameSnapshot Frame { get; }
        public string ModelRevision { get; }
        public string SettingsRevision => Frame.Settings.Revision;
    }
    internal sealed class FrameStopResult
    {
        internal FrameStopResult(bool complete, string outstandingOwner, Exception? error = null) { Complete = complete; OutstandingOwner = outstandingOwner; Error = error; }
        public bool Complete { get; }
        public string OutstandingOwner { get; }
        public Exception? Error { get; }
    }

    // Sole mutable-state owner. Inspection is serialized on its worker, outside the callback and state lock.
    // Raw storage, pending triggers and undrained terminal results share the same item/byte budget.
    internal sealed class FrameSession : IDisposable
    {
        private sealed class Entry
        {
            internal FrameTicket Ticket = null!;
            internal FrameSnapshot? Frame;
            internal FrameTerminal? Terminal;
            internal bool Receiving;
            internal bool Consumed;
        }
        private readonly object gate = new object();
        private readonly string cameraId;
        private readonly string commandId;
        private readonly int maxItems;
        private readonly long maxBytes;
        private readonly Func<FrameSnapshot, CancellationToken, FrameInspection> inspect;
        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();
        private readonly Queue<Entry> queue = new Queue<Entry>();
        private FrameSettings? settings;
        private string sessionId = "";
        private long generation;
        private FrameRunMode mode;
        private bool connected, running, stopping, disposed;
        private Task? producerStop;
        private Task? cancellationCallbacks;
        private CancellationTokenRegistration externalCancellation;
        private bool cancelRequested;
        private Exception? stopError;
        private TaskCompletionSource<FrameStopResult>? stopCompletion;
        private int callbacks;
        private Task worker = Task.CompletedTask;
        private CancellationTokenSource cancellation = new CancellationTokenSource();
        private FrameSnapshot? latestDisplay;
        private FrameTeaching? teaching;
        private long rejected, displayDropped, accepted, terminalCount;

        internal FrameSession(string cameraId, int maxItems, long maxBytes, Func<FrameSnapshot, CancellationToken, FrameInspection> inspect, string? commandId = null)
        {
            if (string.IsNullOrWhiteSpace(cameraId) || maxItems < 1 || maxBytes < 1 || inspect == null) throw new ArgumentException("Invalid Host capacity or inspection delegate.");
            this.commandId = commandId ?? Guid.NewGuid().ToString("N");
            this.cameraId = cameraId; this.maxItems = maxItems; this.maxBytes = maxBytes; this.inspect = inspect;
        }
        public FrameSnapshot? LatestDisplay { get { lock (gate) return latestDisplay; } }
        public FrameTeaching? Teaching { get { lock (gate) return teaching; } }
        public long RejectedCount { get { lock (gate) return rejected; } }
        public long DisplayDroppedCount { get { lock (gate) return displayDropped; } }
        public long AcceptedCount { get { lock (gate) return accepted; } }
        public long TerminalCount { get { lock (gate) return terminalCount; } }
        public int OutstandingItems { get { lock (gate) return entries.Count; } }
        public long OutstandingBytes { get { lock (gate) return entries.Values.Sum(e => (long)e.Ticket.Settings.RawBytes); } }
        public bool IsRunning { get { lock (gate) return running; } }

        internal void UpdateSettings(FrameSettings value) { ArgumentNullException.ThrowIfNull(value); lock (gate) { ThrowIfDisposed(); settings = value; } }
        internal void Connect()
        {
            lock (gate)
            {
                ThrowIfDisposed();
                if (stopping) throw new InvalidOperationException("Stop must complete before reconnect.");
                if (connected) return;
                connected = true; sessionId = Guid.NewGuid().ToString("N");
            }
        }
        internal long Start(FrameRunMode value, CancellationToken externalToken = default)
        {
            CancellationTokenSource previous;
            CancellationTokenRegistration previousRegistration;
            long currentGeneration;
            lock (gate)
            {
                ThrowIfDisposed();
                if (!Enum.IsDefined(typeof(FrameRunMode), value)) throw new ArgumentException("Invalid run mode.");
                if (running && !cancelRequested) return generation;
                if (!connected || running || stopping || settings == null) throw new InvalidOperationException("Connect and configure before explicit Start; complete prior Stop first.");
                externalToken.ThrowIfCancellationRequested();
                currentGeneration = generation = checked(generation + 1); mode = value; running = true; producerStop = null;
                previous = cancellation;
                previousRegistration = externalCancellation;
                cancellation = new CancellationTokenSource();
                cancellationCallbacks = null; cancelRequested = false; stopError = null;
                stopCompletion = new TaskCompletionSource<FrameStopResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                CancellationTokenSource generationSource = cancellation;
                long capturedGeneration = generation;
                externalCancellation = externalToken.Register(() =>
                {
                    lock (gate)
                    {
                        if (generation == capturedGeneration && ReferenceEquals(cancellation, generationSource)) RequestCancellation();
                    }
                });
                // The worker must run even if cancellation arrives before scheduling, to terminate accepted shots.
                worker = Task.Run(Work, CancellationToken.None);
            }
            // Linked-token registration disposal may wait for a callback; never hold the state lock here.
            previousRegistration.Dispose();
            previous.Dispose();
            return currentGeneration;
        }
        internal bool TryTrigger(string triggerId, [NotNullWhen(true)] out FrameTicket? ticket, out string reason)
        {
            lock (gate)
            {
                ThrowIfDisposed();
                ExpireAcquisitions();
                ticket = null;
                if (!running || stopping) reason = "Stopped";
                else if (cancelRequested || cancellation.IsCancellationRequested) reason = "Canceled";
                else if (string.IsNullOrWhiteSpace(triggerId)) reason = "InvalidTriggerId";
                else if (mode == FrameRunMode.SingleShot && entries.Values.Any(e => e.Terminal == null)) reason = "Busy";
                else
                {
                    // ponytail: one latest display slot; drop that optional reference before rejecting inspection capacity.
                    if (latestDisplay != null && (entries.Count >= maxItems || ReservedBytes() + settings!.RawBytes > maxBytes)) { latestDisplay = null; displayDropped++; Prune(); }
                    if (entries.Count >= maxItems || ReservedBytes() + settings!.RawBytes > maxBytes) reason = "Overload";
                    else
                    {
                        ticket = new FrameTicket(cameraId, sessionId, generation, Guid.NewGuid().ToString("N"), triggerId, settings!, commandId);
                        entries.Add(ticket.ShotId, new Entry { Ticket = ticket }); accepted++; reason = "Accepted"; Monitor.PulseAll(gate); return true;
                    }
                }
                rejected++; return false;
            }
        }
        internal bool Receive(FrameTicket ticket, byte[] borrowed, string frameId, long sequence, long deviceTimestamp, string clockDomain)
        {
            ArgumentNullException.ThrowIfNull(borrowed);
            return Receive(ticket, () => (byte[])borrowed.Clone(), frameId, sequence, deviceTimestamp, clockDomain);
        }
        // copyBorrowed transfers a newly allocated, exclusively Host-owned byte array. Native adapters
        // can copy a borrowed pointer here; its callback cannot return until this operation completes.
        internal bool Receive(FrameTicket ticket, Func<byte[]> copyBorrowed, string frameId, long sequence, long deviceTimestamp, string clockDomain)
        {
            Entry? entry;
            lock (gate)
            {
                if (!disposed) ExpireAcquisitions();
                if (disposed || !running || stopping || cancelRequested || ticket == null || ticket.Generation != generation || ticket.SessionId != sessionId || !entries.TryGetValue(ticket.ShotId, out entry) || !ReferenceEquals(entry.Ticket, ticket) || entry.Receiving || entry.Frame != null || entry.Terminal != null) return false;
                entry.Receiving = true; callbacks++;
            }
            FrameSnapshot? snapshot = null;
            Exception? failure = null;
            try
            {
                if (copyBorrowed == null || string.IsNullOrWhiteSpace(frameId) || string.IsNullOrWhiteSpace(clockDomain) || sequence < 0) throw new ArgumentException("Invalid callback context.");
                FrameContext context = new FrameContext(ticket, frameId, sequence, deviceTimestamp, clockDomain);
                byte[] owned = copyBorrowed();
                if (owned == null || owned.Length != ticket.Settings.RawBytes) throw new ArgumentException("Callback byte count differs from the captured dimensions/stride.");
                snapshot = new FrameSnapshot(context, owned);
            }
            catch (Exception error) { failure = error; }
            lock (gate)
            {
                callbacks--; entry.Receiving = false;
                ExpireAcquisitions();
                bool current = running && !stopping && !cancelRequested && !cancellation.IsCancellationRequested && ticket.Generation == generation && ticket.SessionId == sessionId && entry.Terminal == null;
                if (entry.Terminal != null) { Prune(); Monitor.PulseAll(gate); return false; }
                if (!current) Finish(entry, new FrameInspection("Canceled", false, "NotEvaluated"));
                else if (failure != null) Finish(entry, new FrameInspection("Faulted", false, "NotEvaluated", failure.Message, "AcquisitionCopy", failure));
                else
                {
                    entry.Frame = snapshot;
                    if (latestDisplay != null) displayDropped++;
                    latestDisplay = snapshot!; Prune(); queue.Enqueue(entry);
                }
                Monitor.PulseAll(gate);
                return current && failure == null;
            }
        }
        internal FrameTeaching SelectTeachFrame(string shotId, string modelRevision)
        {
            lock (gate)
            {
                ThrowIfDisposed();
                if (string.IsNullOrWhiteSpace(modelRevision) || !entries.TryGetValue(shotId, out Entry? entry) || entry.Frame == null) throw new ArgumentException("Select an explicitly retained frame and model revision.");
                teaching = new FrameTeaching(entry.Frame, modelRevision); Prune(); return teaching;
            }
        }
        internal bool WaitForOutcomes(TimeSpan timeout) => WaitForOutcomes(timeout, CancellationToken.None);
        internal bool WaitForOutcomes(TimeSpan timeout, CancellationToken token)
        {
            if (timeout < TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(timeout));
            long started = Stopwatch.GetTimestamp();
            using CancellationTokenRegistration registration = token.Register(() => { lock (gate) Monitor.PulseAll(gate); });
            lock (gate)
            {
                ThrowIfDisposed();
                while (!entries.Values.Any(e => e.Terminal != null && !e.Consumed))
                {
                    ThrowIfDisposed();
                    ExpireAcquisitions();
                    if (entries.Values.Any(e => e.Terminal != null && !e.Consumed)) break;
                    token.ThrowIfCancellationRequested();
                    TimeSpan remaining = timeout - Stopwatch.GetElapsedTime(started);
                    if (remaining <= TimeSpan.Zero) return false;
                    Monitor.Wait(gate, NextAcquisitionWait(remaining));
                }
                token.ThrowIfCancellationRequested();
                return true;
            }
            // The lock is released before the cancellation registration is disposed.
        }
        internal FrameTerminal[] DrainOutcomes()
        {
            lock (gate)
            {
                ThrowIfDisposed();
                FrameTerminal[] results = entries.Values.Where(e => e.Terminal != null && !e.Consumed).Select(e => e.Terminal!).ToArray();
                foreach (Entry e in entries.Values) if (e.Terminal != null) e.Consumed = true;
                Prune(); return results;
            }
        }
        // Complete describes quiescence. Error is preserved even after every owner has stopped.
        internal FrameStopResult Stop(TimeSpan timeout, Action? stopProducer = null)
        {
            if (timeout < TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(timeout));
            long started = Stopwatch.GetTimestamp();
            Task stopTask, generationWorker;
            Task<FrameStopResult> generationCompletion;
            long stoppedGeneration;
            lock (gate)
            {
                ThrowIfDisposed();
                if (!running && !stopping) return new FrameStopResult(true, "", stopError);
                stoppedGeneration = generation; generationWorker = worker;
                generationCompletion = stopCompletion!.Task;
                running = false; stopping = true;
                // A failed producer acknowledgment requires an explicitly supplied recovery action.
                if (producerStop == null || (stopProducer != null && (producerStop.IsFaulted || producerStop.IsCanceled))) producerStop = Task.Run(() => stopProducer?.Invoke());
                stopTask = producerStop;
                Monitor.PulseAll(gate);
            }
            try
            {
                TimeSpan remaining = timeout - Stopwatch.GetElapsedTime(started);
                if (!stopTask.IsCompleted && (remaining <= TimeSpan.Zero || !stopTask.Wait(remaining))) return new FrameStopResult(false, "ProducerStop; Camera retained");
                stopTask.GetAwaiter().GetResult();
            }
            catch (Exception error) { return new FrameStopResult(false, "ProducerStop: " + error.GetBaseException().Message, error); }
            Task cancelTask;
            lock (gate)
            {
                if (generation != stoppedGeneration) return generationCompletion.GetAwaiter().GetResult();
                while (callbacks != 0 && generation == stoppedGeneration)
                {
                    TimeSpan remaining = timeout - Stopwatch.GetElapsedTime(started);
                    if (remaining <= TimeSpan.Zero) return new FrameStopResult(false, "AcquisitionCallback (" + callbacks + ")");
                    Monitor.Wait(gate, remaining);
                }
                if (generation != stoppedGeneration) return generationCompletion.GetAwaiter().GetResult();
                cancelTask = RequestCancellation();
            }
            Exception? cancellationError = null;
            try
            {
                TimeSpan remaining = timeout - Stopwatch.GetElapsedTime(started);
                if (!cancelTask.IsCompleted && (remaining <= TimeSpan.Zero || !cancelTask.Wait(remaining))) return new FrameStopResult(false, "CancellationCallbacks; Tool/Compute/Camera retained");
                cancelTask.GetAwaiter().GetResult();
            }
            catch (Exception error) { cancellationError = error; }
            lock (gate)
            {
                if (generation != stoppedGeneration) return generationCompletion.GetAwaiter().GetResult();
                stopError ??= cancellationError;
                while (queue.Count != 0) Finish(queue.Dequeue(), new FrameInspection("Canceled", false, "NotEvaluated"));
                foreach (Entry e in entries.Values) if (e.Frame == null && e.Terminal == null) Finish(e, new FrameInspection("Canceled", false, "NotEvaluated"));
                Monitor.PulseAll(gate);
            }
            TimeSpan workerRemaining = timeout - Stopwatch.GetElapsedTime(started);
            if (!generationWorker.IsCompleted && (workerRemaining <= TimeSpan.Zero || !generationWorker.Wait(workerRemaining))) return new FrameStopResult(false, "InspectionWorker; Tool/Compute/Camera retained", cancellationError);
            lock (gate)
            {
                if (generation != stoppedGeneration) return generationCompletion.GetAwaiter().GetResult();
                latestDisplay = null; teaching = null;
                Prune();
                FrameStopResult result = new FrameStopResult(true, "", stopError);
                stopCompletion!.TrySetResult(result);
                stopping = false;
                return result;
            }
        }

        // Called under gate; user callbacks run on one tracked task outside the state lock.
        private Task RequestCancellation()
        {
            cancelRequested = true;
            if (cancellationCallbacks == null)
            {
                CancellationTokenSource source = cancellation;
                cancellationCallbacks = Task.Run(() => source.Cancel());
            }
            Monitor.PulseAll(gate);
            return cancellationCallbacks;
        }

        internal FrameStopResult Disconnect(TimeSpan timeout, Action? stopProducer = null)
        {
            FrameStopResult result = Stop(timeout, stopProducer);
            if (result.Complete && result.Error == null) lock (gate) { ThrowIfDisposed(); connected = false; }
            return result;
        }
        public void Dispose()
        {
            CancellationTokenSource source;
            CancellationTokenRegistration registration;
            lock (gate)
            {
                if (disposed) return;
                if (running || stopping || callbacks != 0 || !worker.IsCompleted || (producerStop != null && !producerStop.IsCompleted) || (cancellationCallbacks != null && !cancellationCallbacks.IsCompleted))
                    throw new InvalidOperationException("Stop must complete before Dispose; in-use callback/worker/Tool/Compute/Camera owners are retained.");
                disposed = true; connected = false; latestDisplay = null; teaching = null;
                entries.Clear(); queue.Clear(); source = cancellation; registration = externalCancellation;
                Monitor.PulseAll(gate);
            }
            // Linked-token registration disposal can wait for callbacks and must stay outside the state lock.
            registration.Dispose();
            source.Dispose();
            GC.SuppressFinalize(this);
        }
        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
        private void Work()
        {
            while (true)
            {
                Entry entry;
                lock (gate)
                {
                    while (queue.Count == 0 && running)
                    {
                        ExpireAcquisitions();
                        Monitor.Wait(gate, NextAcquisitionWait(Timeout.InfiniteTimeSpan));
                    }
                    if (!running) return;
                    entry = queue.Dequeue();
                }
                FrameInspection result;
                try
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    result = inspect(entry.Frame!, cancellation.Token) ?? new FrameInspection("Faulted", false, "NotEvaluated", "Inspection returned no terminal result.", "InspectionWorker", errorCode: "MissingTerminal");
                    cancellation.Token.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException error) when (cancellation.IsCancellationRequested) { result = new FrameInspection("Canceled", false, "NotEvaluated", failureStage: "InspectionWorker", error: error, errorCode: "Canceled"); }
                catch (Exception error) { result = new FrameInspection("Faulted", false, "NotEvaluated", error.GetType().Name + ": " + error.Message, "InspectionWorker", error); }
                lock (gate)
                {
                    if ((!running || cancelRequested || cancellation.IsCancellationRequested) && result.Execution != "Canceled" && result.Execution != "Faulted") result = new FrameInspection("Canceled", false, "NotEvaluated");
                    Finish(entry, result); Monitor.PulseAll(gate);
                }
            }
        }
        private void Finish(Entry entry, FrameInspection result)
        {
            if (entry.Terminal != null) return;
            entry.Terminal = new FrameTerminal(entry.Ticket, entry.Frame, result); terminalCount++; Monitor.PulseAll(gate);
        }
        // No device clock is used for acquisition deadlines. Receiving entries retain their reservation.
        private void ExpireAcquisitions()
        {
            long now = Stopwatch.GetTimestamp();
            foreach (Entry entry in entries.Values)
            {
                if (entry.Frame != null || entry.Terminal != null) continue;
                if (cancelRequested || stopping) Finish(entry, new FrameInspection("Canceled", false, "NotEvaluated"));
                else if (entry.Ticket.AcquisitionDeadline != 0 && now >= entry.Ticket.AcquisitionDeadline)
                    Finish(entry, new FrameInspection("AcquisitionTimeout", false, "NotEvaluated", failureStage: "Acquisition", errorCode: "AcquisitionTimeout"));
            }
        }
        // Under gate: a Host waiter can service deadlines while the inspection worker is busy.
        private TimeSpan NextAcquisitionWait(TimeSpan maximum)
        {
            long next = entries.Values.Where(e => e.Frame == null && e.Terminal == null && e.Ticket.AcquisitionDeadline != 0).Select(e => e.Ticket.AcquisitionDeadline).DefaultIfEmpty(0).Min();
            if (next == 0) return maximum;
            TimeSpan untilDeadline = TimeSpan.FromMilliseconds(Math.Max(1, Math.Min(int.MaxValue, Math.Ceiling((next - Stopwatch.GetTimestamp()) * 1000D / Stopwatch.Frequency))));
            return maximum == Timeout.InfiniteTimeSpan || untilDeadline < maximum ? untilDeadline : maximum;
        }
        private long ReservedBytes() => entries.Values.Sum(e => (long)e.Ticket.Settings.RawBytes);
        private void Prune()
        {
            string[] released = entries.Where(pair => pair.Value.Consumed && !pair.Value.Receiving && (pair.Value.Frame == null || (!ReferenceEquals(pair.Value.Frame, latestDisplay) && !ReferenceEquals(pair.Value.Frame, teaching?.Frame)))).Select(pair => pair.Key).ToArray();
            foreach (string id in released) entries.Remove(id);
        }
    }
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenCvSharp;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Result;
using OpenVisionLab.Vision2D.Tool;

namespace OpenVisionLab.FirstInspection;

// Files and synthetic acquisition are consumer responsibilities. This opt-in console route
// does not connect to hardware, change equipment state, or introduce a camera/UI SDK.
internal static class HostCommand
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
    {
        WriteIndented = true, IncludeFields = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };
    private sealed record Profile(int Version, string CameraId, string SettingsRevision, string ModelRevision, int Width, int Height, int Stride, FramePixelFormat Format, double Scale, double Offset, ushort? MissingValue, byte MissingGray, int MaximumItems, long MaximumRawBytes, FrameRunMode Mode, string SourceFile, string SourceSha256);
    private sealed record CaliperRun(bool Success, string ErrorName, string Message, double InternalElapsedMilliseconds, double TotalExecuteMilliseconds, CaliperMeasurementEvidence? Measurement);

    internal static int Run(List<string> args, CancellationToken token)
    {
        if (args.Count == 3 && args[1] == "prepare")
        {
            string directory = Path.GetFullPath(args[2]);
            if (Directory.Exists(directory) || File.Exists(directory)) throw new ArgumentException("Choose a new Host preparation directory.");
            const int width = 65, height = 49, stride = width * 2 + 3;
            byte[] raw = new byte[stride * height];
            for (int y = 0; y < height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = width * 2; x < stride; x++) raw[y * stride + x] = 0xA5;
            }
            // A smooth sigmoid supplies an isolated derivative peak; padding remains original raw data.
            for (int y = 0; y < height; y++)
            {
                ushort value = (ushort)Math.Round((20 + 200 / (1 + Math.Exp(-(y - 24.25) / .7))) * 256, MidpointRounding.AwayFromZero);
                for (int x = 0; x < width; x++) { raw[y * stride + x * 2] = (byte)value; raw[y * stride + x * 2 + 1] = (byte)(value >> 8); }
            }
            Profile profile = new Profile(1, "SimulatedCamera-A", "settings-v1", "horizontal-caliper-v1", width, height, stride, FramePixelFormat.Gray16LittleEndian, 1.0 / 256, 0, null, 0, 4, raw.Length * 4L, FrameRunMode.Continuous, "source.raw", Hash(raw));
            _ = Validate(profile);
            Directory.CreateDirectory(directory);
            string source = Path.Combine(directory, profile.SourceFile), path = Path.Combine(directory, "host.json");
            WriteNew(source, raw, token);
            SaveProfile(path, profile, token);
            Write(new { Status = "HostPrepared", Profile = path, ProfileSha256 = HashFile(path), Source = source, SourceSha256 = HashFile(source), Settings = profile, Connected = false, Started = false, ProbePerformed = false, InspectionPerformed = false });
            return 0;
        }
        if (args.Count == 3 && args[1] == "load")
        {
            string path = Path.GetFullPath(args[2]);
            Profile profile = LoadProfile(path, token);
            Write(new { Status = "HostLoadedOnly", Settings = profile, ProfileSha256 = HashFile(path), Connected = false, Started = false, ProbePerformed = false, InspectionPerformed = false });
            return 0;
        }
        if (args.Count == 4 && args[1] == "run") return Inspect(Path.GetFullPath(args[2]), Path.GetFullPath(args[3]), token);
        throw new ArgumentException("Usage: host prepare <new-directory> | host load <host.json> | host run <host.json> <new-output-directory>. Device-free simulated acquisition; explicit source-pixel conversion and caliper quality policy; no machine OK.");
    }

    private static int Inspect(string profilePath, string output, CancellationToken token)
    {
        Profile profile = LoadProfile(profilePath, token);
        FrameSettings settings = Validate(profile);
        if (Directory.Exists(output) || File.Exists(output)) throw new ArgumentException("Choose a new Host output directory; existing outputs are never replaced.");
        string sourcePath = Path.Combine(Path.GetDirectoryName(profilePath)!, profile.SourceFile);
        if (!File.Exists(sourcePath) || new FileInfo(sourcePath).Length != settings.RawBytes) throw new InvalidDataException("Source length differs from the confirmed frame profile.");
        byte[] source = File.ReadAllBytes(sourcePath);
        if (Hash(source) != profile.SourceSha256) throw new InvalidDataException("Source bytes differ from the confirmed profile hash.");
        token.ThrowIfCancellationRequested();
        _ = Cv2.GetVersionString(); // Native loading happens only in the explicit run route.
        RotatedCaliperTool tool = new RotatedCaliperTool();
        FrameSession session = new FrameSession(profile.CameraId, profile.MaximumItems, profile.MaximumRawBytes, (frame, cancellation) =>
        {
            Console.Error.WriteLine("HostInspectionStarted|ShotId=" + frame.Context.ShotId);
            cancellation.ThrowIfCancellationRequested();
            byte[] gray = frame.CreateGray8();
            using Mat image = new Mat(frame.Settings.Height, frame.Settings.Width, MatType.CV_8UC1);
            Marshal.Copy(gray, 0, image.Data, gray.Length);
            tool.SetProperty(new RotatedCaliperToolProperty
            {
                CenterX = (frame.Settings.Width - 1) / 2.0, CenterY = (frame.Settings.Height - 1) / 2.0,
                AngleDegrees = 0, StationCount = 9, StationPitch = 3, ScanLength = 16, ScanSampleCount = 17,
                TangentWidth = 2, TangentSampleCount = 3, MinimumContrast = 5,
                Polarity = CaliperPolarity.Rising, QualityGateEnabled = true, MinimumPointCount = 9,
                MaximumRmsResidual = .25, MaximumResidual = .5
            });
            long started = Stopwatch.GetTimestamp();
            using VisionToolResult result = tool.Execute(image, cancellation);
            HostOutcome outcome = HostOutcome.Caliper(result, tool.LatestMeasurement);
            CaliperRun evidence = new CaliperRun(result.Success, result.ErrorName, result.Message, result.Elapsed.TotalMilliseconds, Stopwatch.GetElapsedTime(started).TotalMilliseconds, tool.LatestMeasurement);
            return new FrameInspection(outcome.Execution, outcome.MeasurementEstablished, outcome.Decision, evidence);
        });
        bool stopped = false;
        List<FrameTerminal> outcomes = new List<FrameTerminal>();
        List<object> artifacts = new List<object>();
        FrameTeaching? teaching = null;
        FrameSnapshot? display = null;
        try
        {
            session.UpdateSettings(settings);
            session.Connect();
            long generation = session.Start(profile.Mode, token);
            if (session.Start(profile.Mode, token) != generation) throw new InvalidOperationException("Duplicate Start changed the active generation.");
            int count = profile.Mode == FrameRunMode.SingleShot ? 1 : 3;
            for (int i = 0; i < count; i++)
            {
                token.ThrowIfCancellationRequested();
                if (!session.TryTrigger("trigger-" + i, out FrameTicket? ticket, out string reason))
                {
                    token.ThrowIfCancellationRequested();
                    throw new InvalidOperationException("Explicit trigger rejected: " + reason);
                }
                byte[] borrowed = (byte[])source.Clone();
                if (!session.Receive(ticket, borrowed, "simulated-frame-" + i, i, 1000 - i, "simulated-clock-A"))
                {
                    token.ThrowIfCancellationRequested();
                    throw new InvalidOperationException("Simulated callback rejected.");
                }
                Array.Fill(borrowed, (byte)0xEE); // Producer reuses its borrowed buffer immediately after callback return.
                if (!session.WaitForOutcomes(TimeSpan.FromSeconds(10), token)) throw new InvalidOperationException("Inspection did not produce its correlated terminal outcome.");
                if (i == 0) teaching = session.SelectTeachFrame(ticket.ShotId, profile.ModelRevision);
                display = session.LatestDisplay;
                FrameTerminal[] terminal = session.DrainOutcomes();
                if (terminal.Length != 1 || terminal[0].Ticket.ShotId != ticket.ShotId) throw new InvalidOperationException("Terminal shot correlation differs.");
                outcomes.Add(terminal[0]);
            }
            FrameStopResult stop = session.Stop(TimeSpan.FromSeconds(10));
            stopped = stop.Complete;
            if (!stopped) throw new InvalidOperationException("Shutdown incomplete: " + stop.OutstandingOwner);
            tool.Dispose();
            session.Disconnect(TimeSpan.FromSeconds(1));
            session.Dispose();
            Directory.CreateDirectory(output);
            foreach (FrameTerminal terminal in outcomes)
            {
                token.ThrowIfCancellationRequested();
                FrameSnapshot frame = terminal.Frame!;
                string prefix = Path.Combine(output, frame.Context.ShotId);
                string raw = prefix + ".raw", gray = prefix + ".gray8", preview = prefix + ".rgba";
                WriteNew(raw, frame.CopyRaw(), token); WriteNew(gray, frame.CreateGray8(), token); WriteNew(preview, frame.CreatePreviewRgba(), token);
                artifacts.Add(new { frame.Context.ShotId, Raw = new { Path = raw, Sha256 = HashFile(raw), IncludesRowPadding = true }, Gray8 = new { Path = gray, Sha256 = HashFile(gray) }, PreviewRgba = new { Path = preview, Sha256 = HashFile(preview), Alpha = 255 } });
            }
            string teachRaw = Path.Combine(output, "teach.raw"), teachPath = Path.Combine(output, "teach.json");
            WriteNew(teachRaw, teaching!.Frame.CopyRaw(), token);
            WriteNew(teachPath, JsonSerializer.SerializeToUtf8Bytes(new { teaching.Frame.Context, teaching.ModelRevision, teaching.SettingsRevision, Raw = teachRaw, RawSha256 = HashFile(teachRaw) }, Json), token);
            string restored = Path.Combine(output, "confirmed-host.json");
            WriteNew(Path.Combine(output, profile.SourceFile), source, token);
            SaveProfile(restored, profile, token);
            bool measured = outcomes.All(o => o.Inspection.MeasurementEstablished), accepted = outcomes.All(o => o.Inspection.Execution == "Completed" && o.Inspection.Decision == "Pass");
            object report = new
            {
                Status = accepted ? "HostInspected" : "HostInspectionFailed", Mode = profile.Mode, Producer = "DeviceFreeSimulation", Input = sourcePath, InputSha256 = Hash(source),
                ProfileSha256 = HashFile(profilePath), Settings = profile, Outcomes = outcomes.Select(o => new { o.Ticket, Context = o.Frame!.Context, Outcome = o.Inspection }),
                Display = display!.Context, Teaching = new { teaching.Frame.Context, teaching.ModelRevision, teaching.SettingsRevision, Path = teachPath, Sha256 = HashFile(teachPath) }, Artifacts = artifacts,
                RestoredProfile = restored, RestoredProfileSha256 = HashFile(restored), session.AcceptedCount, session.TerminalCount, session.RejectedCount, session.DisplayDroppedCount,
                ShutdownComplete = stopped, InspectionPerformed = true, MeasurementEstablished = measured, MachineDecision = "NotProvided", Units = "source-pixels", ComputeSession = "NotRequiredByCpuCaliper",
                Packages = Program.PackageIdentities(), NativeRuntimes = Program.NativeIdentities(), Unverified = "Real camera/driver/firmware, hardware timing and physical metrology; no UI framework is implemented."
            };
            string reportPath = Path.Combine(output, "result.json");
            token.ThrowIfCancellationRequested();
            WriteNew(reportPath, JsonSerializer.SerializeToUtf8Bytes(report, Json), token);
            Write(report);
            return accepted ? 0 : 2;
        }
        finally
        {
            if (!stopped)
            {
                FrameStopResult stop = session.Stop(TimeSpan.FromSeconds(10));
                if (stop.Complete) { tool.Dispose(); session.Disconnect(TimeSpan.FromSeconds(1)); session.Dispose(); }
                else Console.Error.WriteLine("Host shutdown incomplete; in-use Tool retained: " + stop.OutstandingOwner);
            }
        }
    }

    private static Profile LoadProfile(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!File.Exists(path) || new FileInfo(path).Length < 1 || new FileInfo(path).Length > 64 * 1024) throw new InvalidDataException("Host profile is missing, empty or exceeds its limit.");
        Profile profile = JsonSerializer.Deserialize<Profile>(File.ReadAllBytes(path), Json) ?? throw new InvalidDataException("Host profile is empty.");
        _ = Validate(profile);
        return profile;
    }
    private static FrameSettings Validate(Profile profile)
    {
        if (profile.Version != 1 || string.IsNullOrWhiteSpace(profile.CameraId) || profile.ModelRevision != "horizontal-caliper-v1" || profile.SourceFile != "source.raw" || profile.SourceSha256 == null || profile.SourceSha256.Length != 64 || !profile.SourceSha256.All(Uri.IsHexDigit)
            || !Enum.IsDefined(profile.Mode) || profile.MaximumItems < 2 || profile.MaximumItems > 32 || profile.MaximumRawBytes < 1 || profile.MaximumRawBytes > 256 * 1024 * 1024 || profile.Width < 32 || profile.Height < 32) throw new InvalidDataException("Unsupported or invalid Host profile.");
        FrameSettings settings = new FrameSettings(profile.SettingsRevision, profile.Width, profile.Height, profile.Stride, profile.Format, profile.Scale, profile.Offset, profile.MissingValue, profile.MissingGray, profile.ModelRevision);
        if (profile.MaximumRawBytes < settings.RawBytes * 2L) throw new InvalidDataException("Host budget must hold the selected teach frame and a new shot.");
        return settings;
    }
    private static void SaveProfile(string path, Profile profile, CancellationToken token)
    {
        _ = Validate(profile);
        string directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, ".host-profile-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            WriteNew(temporary, JsonSerializer.SerializeToUtf8Bytes(profile, Json), token);
            _ = LoadProfile(temporary, token);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void WriteNew(string path, byte[] bytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string temporary = Path.Combine(Path.GetDirectoryName(path)!, ".host-write-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) stream.Write(bytes);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, false); // Successful atomic publication is this write's commit point.
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string HashFile(string path) { using FileStream stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static void Write(object report) => Console.WriteLine(JsonSerializer.Serialize(report, Json));
}

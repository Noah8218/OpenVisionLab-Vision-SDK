using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenCvSharp;
using OpenVisionLab.Compute;
using OpenVisionLab.Vision2D.Blob;
using OpenVisionLab.Vision2D.Pipeline;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Result;
using OpenVisionLab.Vision2D.Tool;

// This console Host owns files, workflow, result snapshots and resource lifetimes.
// SDK Tools retain their existing algorithm/model state. No SDK source is compiled here.
internal static class Program
{
    private const int Width = 640, Height = 480, MaximumImagePixels = 16 * 1024 * 1024;
    private static readonly Rect TemplateRoi = new Rect(80, 60, 64, 64);
    private static readonly Rect PresenceRoi = new Rect(232, 112, 31, 29);
    private static readonly Rect GaugeRoi = new Rect(320, 240, 149, 121);
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true };

    private static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        using CancellationTokenSource cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += handler;
        string command = "";
        try
        {
            bool cuda = args.Contains("--cuda"), cancelBefore = args.Contains("--cancel-before");
            int? cancelAfter = null;
            List<string> positional = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--cuda" || args[i] == "--cancel-before") continue;
                if (args[i] == "--cancel-after-ms")
                {
                    if (++i == args.Length || !int.TryParse(args[i], out int delay) || delay < 1) throw new ArgumentException("--cancel-after-ms requires a positive integer.");
                    cancelAfter = delay;
                }
                else if (args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Unknown option: " + args[i]);
                else positional.Add(args[i]);
            }
            if (cancelBefore) cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
            command = positional.FirstOrDefault() ?? "";
            if ((cuda && command != "teach" && command != "inspect") || (cancelAfter.HasValue && command != "inspect" && command != "qr")) throw new ArgumentException("--cuda is for teach/inspect; --cancel-after-ms is for inspect.");
            object report;
            switch (command)
            {
                case "qr":
                    return RunQr(positional, cancellation, cancelAfter);
                case "prepare" when positional.Count == 2:
                    report = Prepare(Path.GetFullPath(positional[1]), cancellation.Token);
                    break;
                case "teach" when positional.Count == 3:
                    report = Teach(Path.GetFullPath(positional[1]), Path.GetFullPath(positional[2]), cuda, cancellation.Token);
                    break;
                case "load" when positional.Count == 2:
                    Bundle loaded = ReadBundle(Path.GetFullPath(positional[1]), cancellation.Token);
                    report = new { Status = "LoadedOnly", loaded.Host, PipelineXml = loaded.Xml, ModelSha256 = Hash(loaded.Model), BundleSha256 = HashFile(positional[1]), ProbePerformed = false, InspectionPerformed = false, Packages = PackageIdentities() };
                    break;
                case "inspect" when positional.Count == 3:
                    Bundle bundle = ReadBundle(Path.GetFullPath(positional[1]), cancellation.Token);
                    Inspection inspection = Inspect(bundle, Path.GetFullPath(positional[2]), cuda || bundle.Host.UseCuda, cancellation, cancelAfter);
                    Write(inspection);
                    return inspection.Status == "Accepted" ? 0 : 2;
                default:
                    throw new ArgumentException("Usage: prepare <new-work-dir> | teach <work-dir> <new-bundle> [--cuda] | load <bundle> | inspect <bundle> <image> [--cuda] [--cancel-after-ms N] | qr configure <new-profile.xml> [count [x,y,width,height]] | qr load <profile.xml> | qr inspect <profile.xml> <image> [expected-text] | qr inspect-located <profile.xml> <bundle> <image> [expected-text]. --cancel-before demonstrates pre-cancellation. Ctrl+C requests cooperative cancellation.");
            }
            Write(report);
            return 0;
        }
        catch (OperationCanceledException)
        {
            Write(new { Status = "Canceled", Message = "Cancellation requested; no inspection value is published. Native calls may complete before cancellation is observed." });
            return 130;
        }
        catch (Exception error) when (command == "qr" && error.GetBaseException() is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            Exception cause = error.GetBaseException();
            Write(new { Status = "BackendUnavailable", Error = cause.GetType().Name, cause.Message, InspectionPerformed = false });
            return 3;
        }
        catch (Exception error) when (error is ArgumentException or InvalidDataException or JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or OpenCVException or FormatException or OverflowException or NotSupportedException or System.Xml.XmlException)
        {
            Write(new { Status = "InvalidInputOrSettings", Error = error.GetType().Name, error.Message });
            return 3;
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
    }

    private static int RunQr(List<string> args, CancellationTokenSource cancellation, int? cancelAfter)
    {
        CancellationToken token = cancellation.Token;
        string action = args.Count > 1 ? args[1] : "";
        if (cancelAfter.HasValue && action != "inspect" && action != "inspect-located") throw new ArgumentException("QR --cancel-after-ms requires an explicit inspect action.");
        if (action == "configure" && args.Count is >= 3 and <= 5)
        {
            VisionPipeline profile = new VisionPipeline { Name = "QR text / input pixels" };
            VisionPipelineStep step = Step(typeof(QrCodeTool), "Read QR");
            if (args.Count >= 4) step.Parameters["ExpectedCodeCount"] = args[3];
            if (args.Count == 5) Set(step, "USE_ROI", "true", "CvROI", args[4]);
            profile.Steps.Add(step);
            ValidateQrProfile(profile);
            string destination = Path.GetFullPath(args[2]);
            string directory = Path.GetDirectoryName(destination)!;
            Directory.CreateDirectory(directory);
            string temporary = Path.Combine(directory, ".qr-profile-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                token.ThrowIfCancellationRequested();
                File.WriteAllText(temporary, VisionPipelineSerializer.Serialize(profile), new UTF8Encoding(false));
                _ = ReadQrProfile(temporary, token);
                token.ThrowIfCancellationRequested();
                File.Move(temporary, destination, false);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            Write(new { Status = "ConfiguredOnly", Profile = destination, Settings = step.Parameters, InspectionPerformed = false, ProbePerformed = false });
            return 0;
        }
        if (action == "load" && args.Count == 3)
        {
            string path = Path.GetFullPath(args[2]);
            VisionPipelineStep step = ReadQrProfile(path, token).Steps[0];
            Write(new { Status = "LoadedOnly", Profile = path, ProfileSha256 = HashFile(path), Settings = step.Parameters, InspectionPerformed = false, ProbePerformed = false, Packages = PackageIdentities() });
            return 0;
        }
        bool located = action == "inspect-located";
        int count = located ? 5 : 4;
        if ((action != "inspect" && !located) || (args.Count != count && args.Count != count + 1)) throw new ArgumentException("QR usage: configure <new-profile.xml> [count [x,y,width,height]] | load <profile.xml> | inspect <profile.xml> <image> [expected-text] | inspect-located <profile.xml> <bundle> <image> [expected-text].");
        string profilePath = Path.GetFullPath(args[2]);
        VisionPipeline qrProfile = ReadQrProfile(profilePath, token);
        using QrCodeTool reader = (QrCodeTool)VisionPipelineToolFactory.Create(qrProfile.Steps[0]);
        string? expected = args.Count == count + 1 ? args[count] : null;
        if (expected != null && reader.property.ExpectedCodeCount != 1) throw new ArgumentException("A single expected text requires ExpectedCodeCount=1.");
        string imagePath = Path.GetFullPath(args[located ? 4 : 3]);
        using Mat image = ReadQrImage(imagePath, token);
        if (cancelAfter.HasValue) cancellation.CancelAfter(cancelAfter.Value);
        object? location = null;
        if (located)
        {
            if (!reader.property.USE_ROI) throw new ArgumentException("Located QR requires an explicit reference-frame ROI in the profile.");
            Bundle bundle = ReadBundle(Path.GetFullPath(args[3]), token);
            if (image.Width != bundle.Host.Width || image.Height != bundle.Host.Height) throw new ArgumentException("QR image frame size differs from the matching reference frame.");
            using VisionComputeSession session = new VisionComputeSession(new VisionComputeOptions { UseCuda = bundle.Host.UseCuda });
            using EdgeBasedTemplateMatchingTool matcher = (EdgeBasedTemplateMatchingTool)VisionPipelineToolFactory.Create(bundle.Pipeline.Steps[0], _ => bundle.Model, session);
            using Mat gray = image.Clone();
            if (gray.Channels() == 3) Cv2.CvtColor(gray, gray, ColorConversionCodes.BGR2GRAY);
            using VisionToolResult match = matcher.Execute(gray, token);
            if (!match.Success)
            {
                Write(new { Status = "LocationFailed", match.ErrorName, match.Message, QrExecutionPerformed = false, QrCodeReading = (object?)null });
                return 2;
            }
            RequireCurrentMatch(match, matcher);
            Pose pose = PoseOf(matcher.results.Single());
            MatchingCoordinateTransform transform = MatchingCoordinateTransform.FromMatches(MatchOf(bundle.Host.ReferencePose), MatchOf(pose));
            try { reader.property.CvROI = MapRoi(transform, reader.property.CvROI, image.Size()); }
            catch (ArgumentException error)
            {
                Write(new { Status = "RoiRejected", error.Message, QrExecutionPerformed = false, QrCodeReading = (object?)null });
                return 2;
            }
            location = new { CurrentPose = pose, ReferencePose = bundle.Host.ReferencePose, AppliedRoi = RectText(reader.property.CvROI) };
        }
        token.ThrowIfCancellationRequested();
        using VisionToolResult result = reader.Execute(image, token);
        bool? agrees = expected == null || !result.Success ? null : string.Equals(result.QrCodeReading.Candidates.Single().Text, expected, StringComparison.Ordinal);
        string status = !result.Success ? "ReadingFailed" : agrees == false ? "TextMismatch" : agrees == true ? "TextMatched" : "Read";
        Write(new
        {
            Status = status, result.Success, result.ErrorName, result.Message, QrExecutionPerformed = true,
            Units = "px", CoordinateFrame = "current-input-Mat", ExpectedText = expected, TextComparison = "Ordinal", TextAgrees = agrees,
            Input = imagePath, InputSha256 = HashFile(imagePath), ProfileSha256 = HashFile(profilePath), Location = location,
            Reading = result.QrCodeReading == null ? null : new
            {
                InputWidth = result.QrCodeReading.InputImageSize.Width, InputHeight = result.QrCodeReading.InputImageSize.Height,
                AppliedRoi = RectText(result.QrCodeReading.AppliedRoi), result.QrCodeReading.ExpectedCodeCount,
                result.QrCodeReading.DetectedCount, result.QrCodeReading.DecodedCount,
                Candidates = result.QrCodeReading.Candidates.Select(value => new { value.CandidateIndex, value.Text, value.Decoded, Corners = value.Corners.Select(corner => new { corner.X, corner.Y }).ToArray() }).ToArray()
            },
            Packages = PackageIdentities()
        });
        return result.Success && agrees != false ? 0 : 2;
    }

    private static VisionPipeline ReadQrProfile(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("QR profile exceeds the example size limit.");
        VisionPipeline profile = VisionPipelineSerializer.Deserialize(File.ReadAllText(path));
        ValidateQrProfile(profile);
        token.ThrowIfCancellationRequested();
        return profile;
    }

    private static void ValidateQrProfile(VisionPipeline profile)
    {
        if (profile.Steps.Count != 1) throw new InvalidDataException("QR profile requires exactly one enabled QR step.");
        VisionPipelineStep step = profile.Steps[0];
        if (!step.Enabled || !VisionPipelineToolFactory.TryGetDescriptor(step.ToolType, out VisionPipelineToolDescriptor descriptor) || descriptor.ToolTypeName != typeof(QrCodeTool).FullName) throw new InvalidDataException("QR profile requires an enabled QR reader.");
        if (step.UseAcceptance || !step.ExpectedSuccess || step.MaxElapsedMilliseconds != 0 || step.UseAcceptanceMetricMinimum || step.UseAcceptanceMetricMaximum
            || !string.IsNullOrEmpty(step.InputLayer) || !string.IsNullOrEmpty(step.OutputLayer) || !string.IsNullOrEmpty(step.RequiredMessageText) || !string.IsNullOrEmpty(step.AcceptanceMetricName))
            throw new InvalidDataException("The explicit QR Host does not execute Pipeline acceptance, timeout or layer routing settings.");
        using QrCodeTool restored = (QrCodeTool)VisionPipelineToolFactory.Create(step);
        if (restored.LatestReading != null) throw new InvalidDataException("Restoration unexpectedly executed QR.");
    }

    private static Mat ReadQrImage(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!File.Exists(path) || new FileInfo(path).Length == 0 || new FileInfo(path).Length > 64 * 1024 * 1024) throw new ArgumentException("QR image is missing, empty or exceeds the example file limit.");
        Mat image = Cv2.ImRead(path, ImreadModes.Unchanged);
        try
        {
            if (image.Empty() || (image.Type() != MatType.CV_8UC1 && image.Type() != MatType.CV_8UC3) || (long)image.Width * image.Height > MaximumImagePixels) throw new ArgumentException("QR input requires non-empty 8-bit grayscale/BGR; BGRA and other depths are rejected.");
            token.ThrowIfCancellationRequested();
            return image;
        }
        catch { image.Dispose(); throw; }
    }

    private static object Prepare(string directory, CancellationToken token)
    {
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any()) throw new IOException("Work directory must be empty; choose a new path.");
        Directory.CreateDirectory(directory);
        var cases = new List<object>();
        foreach (var item in new[]
        {
            ("reference", 0, 0, true, true, true), ("translated", 18, 11, true, true, true),
            ("pattern-absent", 0, 0, false, true, true), ("mark-absent", 18, 11, true, false, true),
            ("edge-absent", 18, 11, true, true, false), ("border", 488, 0, true, false, false)
        })
        {
            token.ThrowIfCancellationRequested();
            using Mat image = Scene(item.Item2, item.Item3, item.Item4, item.Item5, item.Item6);
            SaveImage(Path.Combine(directory, item.Item1 + ".png"), image);
            cases.Add(new { Name = item.Item1, PatternPresent = item.Item4, MarkPresent = item.Item5, EdgePresent = item.Item6, CenterX = 112 + item.Item2, CenterY = 92 + item.Item3, MarkAreaPx = 165, LineSlope = .03, LineInterceptPx = 320 + item.Item2 + 47.25 - .03 * (240 + item.Item3) });
            if (item.Item1 == "translated")
            {
                using Mat bgr = new Mat();
                using Mat bgra = new Mat();
                Cv2.CvtColor(image, bgr, ColorConversionCodes.GRAY2BGR);
                Cv2.CvtColor(image, bgra, ColorConversionCodes.GRAY2BGRA);
                // Authored channel probe: B=255, G=R=0 must become gray 29, not 76.
                bgr.Set(0, 0, new Vec3b(255, 0, 0));
                bgra.Set(0, 0, new Vec4b(255, 0, 0, 255));
                SaveImage(Path.Combine(directory, "translated-bgr.png"), bgr);
                SaveImage(Path.Combine(directory, "translated-bgra.png"), bgra);
                using Mat depth16 = new Mat();
                image.ConvertTo(depth16, MatType.CV_16U, 256);
                SaveImage(Path.Combine(directory, "unsupported-16bit.png"), depth16);
            }
        }
        File.WriteAllBytes(Path.Combine(directory, "empty.png"), Array.Empty<byte>());
        File.WriteAllBytes(Path.Combine(directory, "truncated.png"), new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0 });
        File.WriteAllText(Path.Combine(directory, "setup.xml"), VisionPipelineSerializer.Serialize(CreateSetup()), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(directory, "expected.json"), JsonSerializer.Serialize(new { Units = "px", Geometry = "translation-only", PositionTolerancePx = 1, LinePointTolerancePx = .125, SlopeTolerance = .002, Cases = cases }, Json), new UTF8Encoding(false));
        return new { Status = "Prepared", Directory = directory, Units = "px", Geometry = "translation-only", InspectionPerformed = false };
    }

    private static Mat Scene(int dx, int dy, bool pattern, bool mark, bool edge)
    {
        Mat image = new Mat(Height, Width, MatType.CV_8UC1, Scalar.All(17));
        if (pattern)
        {
            using Mat template = new Mat(64, 64, MatType.CV_8UC1, Scalar.All(17));
            Cv2.Rectangle(template, new Rect(8, 7, 42, 46), Scalar.All(225), -1);
            Cv2.Rectangle(template, new Rect(29, 7, 21, 16), Scalar.All(17), -1);
            Cv2.Circle(template, new Point(20, 37), 7, Scalar.All(17), -1);
            using Mat target = new Mat(image, new Rect(TemplateRoi.X + dx, TemplateRoi.Y + dy, 64, 64));
            template.CopyTo(target);
        }
        if (mark) Cv2.Rectangle(image, new Rect(240 + dx, 120 + dy, 15, 11), Scalar.All(230), -1);
        if (edge)
        {
            // A margin keeps conservative floor/ceil ROI rounding inside the same analytic profile.
            // The patch boundary is outside the measurement ROI and is not the measured edge.
            for (int y = -16; y < GaugeRoi.Height + 16; y++)
                for (int x = -16; x < GaugeRoi.Width + 16; x++)
                    image.Set(GaugeRoi.Y + dy + y, GaugeRoi.X + dx + x, (byte)Math.Round(120 + 80 * Math.Tanh((x - (47.25 + .03 * y)) / 1.6)));
        }
        return image;
    }

    private static VisionPipeline CreateSetup()
    {
        VisionPipeline pipeline = new VisionPipeline { Name = "First inspection: translation / px" };
        VisionPipelineStep matching = Step(typeof(EdgeBasedTemplateMatchingTool), "Locate datum");
        Set(matching, "SCORE_MIN", ".9", "USE_FIND_ANGLE", "false", "USE_FIND_SCALE", "false", "SEARCH_STEP", "2", "USE_POSITION_REFINE", "true", "USE_SUBPIXEL_REFINE", "false", "MAX_TEMPLATE_POINTS", "400", "USE_DRAW_IMAGE", "false", "USE_UNIQUE_MATCH_VALIDATION", "true");
        VisionPipelineStep blob = Step(typeof(BlobTool), "Check presence");
        Set(blob, "USE_ROI", "true", "CvROI", RectText(PresenceRoi), "THRESHOLD", "128", "MIN_AREA", "100", "MAX_AREA", "220");
        VisionPipelineStep gauge = Step(typeof(LineGaugeTool), "Measure edge in px");
        Set(gauge, "USE_ROI", "true", "CvROI", RectText(GaugeRoi), "PRJ_DIR", "X_LTOR", "PRJ_PORALITY", "ALL", "CONTRAST", "4", "THICKNESS", "2", "SAMPLING_STEP", "1", "UseSubpixelMeasurement", "true", "SubpixelProfileRadius", "6");
        pipeline.Steps.Add(matching);
        pipeline.Steps.Add(blob);
        pipeline.Steps.Add(gauge);
        return pipeline;
    }

    private static VisionPipelineStep Step(Type type, string name)
    {
        VisionPipelineToolDescriptor descriptor = VisionPipelineBlobToolFactory.Descriptors.Single(value => value.ToolTypeName == type.FullName);
        VisionPipelineStep step = new VisionPipelineStep { Name = name, ToolType = descriptor.ToolType };
        foreach (VisionPipelineParameterDescriptor parameter in descriptor.Parameters) step.Parameters[parameter.Name] = parameter.DefaultValue;
        return step;
    }

    private static void Set(VisionPipelineStep step, params string[] values)
    {
        for (int i = 0; i < values.Length; i += 2) step.Parameters[values[i]] = values[i + 1];
    }

    private static object Teach(string directory, string destination, bool cuda, CancellationToken token)
    {
        if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("Bundle destination already exists; choose a new name.");
        using VisionComputeSession session = new VisionComputeSession(new VisionComputeOptions { UseCuda = cuda });
        string reference = Path.Combine(directory, "reference.png");
        using Mat image = ReadGray(reference, token);
        VisionPipeline pipeline = VisionPipelineSerializer.Deserialize(File.ReadAllText(Path.Combine(directory, "setup.xml")));
        ValidateSteps(pipeline);
        string referenceHash = HashFile(reference);
        using Mat template = new Mat(image, TemplateRoi);
        byte[] encodedTemplate = template.ToBytes(".png");
        if (pipeline.Steps[0].Artifacts.Count != 0) throw new InvalidDataException("Teaching setup must not already contain a model artifact.");
        pipeline.Steps[0].Artifacts.Add(new VisionPipelineArtifactReference(VisionPipelineToolFactory.TemplateArtifactRole, "teaching-image", VisionPipelineToolFactory.EncodedImageArtifactFormat, VisionPipelineToolFactory.EncodedImageArtifactFormatVersion, Hash(encodedTemplate)));
        using EdgeBasedTemplateMatchingTool matcher = (EdgeBasedTemplateMatchingTool)VisionPipelineToolFactory.Create(pipeline.Steps[0], _ => encodedTemplate, session);
        ValidateTranslation(matcher);
        // Factory applies properties, then SetTemplateImage; the explicit teach command prepares the model.
        byte[] model = EdgeTemplateModelArtifactCodec.Encode(matcher.PrepareLearnedModel(referenceHash, token), token);
        using VisionToolResult result = matcher.Execute(image, token);
        RequireCurrentMatch(result, matcher);
        Pose pose = PoseOf(matcher.results.Single());
        pipeline.Steps[0].Artifacts.Clear();
        pipeline.Steps[0].Artifacts.Add(new VisionPipelineArtifactReference(VisionPipelineToolFactory.TemplateArtifactRole, "model.bin", EdgeTemplateModelArtifactCodec.ArtifactFormat, EdgeTemplateModelArtifactCodec.SchemaVersion, Hash(model)));
        HostSettings host = new HostSettings(1, referenceHash, "reference-image", image.Width, image.Height, "px", "translation-only", pose, cuda);
        Bundle bundle = new Bundle(VisionPipelineSerializer.Serialize(pipeline), pipeline, model, host);
        ValidateBundle(bundle, token);
        // Self-check is explicit teaching work. Loading remains free of inspection/probing.
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(token);
        Inspection check = Inspect(bundle, reference, cuda, linked, null);
        if (check.Status != "Accepted") throw new InvalidDataException("Teaching self-check failed: " + check.Status);
        WriteBundle(destination, bundle, token);
        return new { Status = "Taught", Bundle = destination, BundleSha256 = HashFile(destination), ModelSha256 = Hash(model), host.ReferencePose, SelfCheck = check };
    }

    private static Inspection Inspect(Bundle bundle, string path, bool cuda, CancellationTokenSource cancellation, int? cancelAfter)
    {
        CancellationToken token = cancellation.Token;
        // Construction does not probe CUDA. Declare the session first so caller Mats and Tools release before it.
        using VisionComputeSession session = new VisionComputeSession(new VisionComputeOptions { UseCuda = cuda });
        using Mat image = ReadGray(path, token);
        if (image.Width != bundle.Host.Width || image.Height != bundle.Host.Height) throw new ArgumentException("Image frame size differs from the taught reference frame.");
        Inspection output = new Inspection
        {
            Status = "ExecutionFailed", Input = path, InputSha256 = HashFile(path), PipelineSha256 = Hash(Encoding.UTF8.GetBytes(bundle.Xml)),
            ModelSha256 = Hash(bundle.Model), RequestedCuda = cuda, InputGray00 = image.At<byte>(0, 0), Packages = PackageIdentities()
        };
        using EdgeBasedTemplateMatchingTool matcher = (EdgeBasedTemplateMatchingTool)VisionPipelineToolFactory.Create(bundle.Pipeline.Steps[0], _ => bundle.Model, session);
        using BlobTool presence = (BlobTool)VisionPipelineBlobToolFactory.Create(bundle.Pipeline.Steps[1]);
        using LineGaugeTool gauge = (LineGaugeTool)VisionPipelineBlobToolFactory.Create(bundle.Pipeline.Steps[2]);
        if (cancelAfter.HasValue) cancellation.CancelAfter(cancelAfter.Value);
        Console.Error.WriteLine("MatchingStarted: translation-only; cooperative cancellation; results in px.");
        using (VisionToolResult located = matcher.Execute(image, token))
        {
            output.Stages.Add(StageOf("Matching", located));
            output.Compute = ComputeOf(located.ComputeDiagnostics);
            if (!located.Success) { output.Status = located.ErrorCode == VisionToolErrorCode.MatchingNoResult ? "PatternNotFound" : "ExecutionFailed"; return output; }
            RequireCurrentMatch(located, matcher);
            output.Pose = PoseOf(matcher.results.Single());
        }
        token.ThrowIfCancellationRequested();
        MatchingCoordinateTransform transform = MatchingCoordinateTransform.FromMatches(MatchOf(bundle.Host.ReferencePose), MatchOf(output.Pose!));
        try
        {
            presence.property.CvROI = MapRoi(transform, presence.property.CvROI, image.Size());
            gauge.property.CvROI = MapRoi(transform, gauge.property.CvROI, image.Size());
        }
        catch (ArgumentException error)
        {
            output.Status = "RoiRejected";
            output.Stages.Add(new Stage("RoiTransfer", false, "HostRoiOutsideImage", error.Message));
            return output;
        }
        output.PresenceRoi = RectText(presence.property.CvROI);
        output.GaugeRoi = RectText(gauge.property.CvROI);
        token.ThrowIfCancellationRequested();
        using (VisionToolResult found = presence.Execute(image))
        {
            token.ThrowIfCancellationRequested(); // Blob is synchronous; it has no cancellable overload.
            output.Stages.Add(StageOf("Presence", found));
            if (!found.Success) { output.Status = found.ErrorCode == VisionToolErrorCode.BlobNoResult ? "MarkNotFound" : "ExecutionFailed"; return output; }
            output.BlobCount = presence.results.Count;
            output.BlobAreaPx = presence.results.Sum(value => value.Area);
        }
        using (VisionToolResult measured = gauge.Execute(image, token))
        {
            output.Stages.Add(StageOf("LineGauge", measured));
            if (!measured.Success) { output.Status = measured.ErrorCode == VisionToolErrorCode.LineGaugeEdgeNotFound ? "EdgeNotFound" : "ExecutionFailed"; return output; }
            LineGaugeSubpixelEvidence evidence = gauge.resultList.Single().SubpixelEvidence;
            if (!evidence.HasFit || !evidence.ProjectsX) throw new InvalidDataException("No accepted X projection subpixel fit.");
            output.Line = new LineEvidence(evidence.Slope, evidence.Intercept, evidence.OrthogonalRmse, evidence.Points.Select(value => new PixelPoint(value.X, value.Y)).ToArray());
        }
        token.ThrowIfCancellationRequested();
        output.Status = "Accepted";
        return output;
    }

    private static Bundle ReadBundle(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using FileStream file = File.OpenRead(path);
        if (file.Length > 70 * 1024 * 1024) throw new InvalidDataException("Bundle exceeds the example size limit.");
        using ZipArchive zip = new ZipArchive(file, ZipArchiveMode.Read);
        string[] names = { "pipeline.xml", "model.bin", "host.json" };
        if (zip.Entries.Count != names.Length || names.Any(name => zip.Entries.Count(entry => entry.FullName == name) != 1)) throw new InvalidDataException("Bundle requires exactly one pipeline.xml, model.bin and host.json; other entries are rejected.");
        string xml = Encoding.UTF8.GetString(ReadEntry(zip.GetEntry("pipeline.xml")!, 1024 * 1024, token));
        byte[] model = ReadEntry(zip.GetEntry("model.bin")!, EdgeTemplateModelArtifactCodec.MaximumArtifactByteCount, token);
        HostSettings host = JsonSerializer.Deserialize<HostSettings>(ReadEntry(zip.GetEntry("host.json")!, 16 * 1024, token), Json) ?? throw new InvalidDataException("Host settings missing.");
        Bundle bundle = new Bundle(xml, VisionPipelineSerializer.Deserialize(xml), model, host);
        ValidateBundle(bundle, token);
        return bundle;
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry, int limit, CancellationToken token)
    {
        if (entry.Length < 1 || entry.Length > limit) throw new InvalidDataException("Invalid size for " + entry.FullName);
        using Stream stream = entry.Open();
        byte[] bytes = new byte[(int)entry.Length];
        int offset = 0;
        while (offset < bytes.Length)
        {
            token.ThrowIfCancellationRequested();
            int count = stream.Read(bytes, offset, bytes.Length - offset);
            if (count == 0) throw new InvalidDataException("Truncated entry: " + entry.FullName);
            offset += count;
        }
        if (stream.ReadByte() != -1) throw new InvalidDataException("Entry exceeds its declared length: " + entry.FullName);
        return bytes;
    }

    private static void ValidateBundle(Bundle bundle, CancellationToken token)
    {
        HostSettings host = bundle.Host;
        if (host.Version != 1 || host.Frame != "reference-image" || host.Units != "px" || host.Geometry != "translation-only") throw new InvalidDataException("Unsupported Host version/frame/units/geometry.");
        if (host.Width != Width || host.Height != Height || host.ReferenceImageSha256 == null || host.ReferenceImageSha256.Length != 64 || !host.ReferenceImageSha256.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid reference image identity/frame size.");
        ValidatePose(host.ReferencePose);
        ValidateSteps(bundle.Pipeline);
        VisionPipelineStep step = bundle.Pipeline.Steps[0];
        if (step.Artifacts.Count != 1 || step.Artifacts[0].Role != VisionPipelineToolFactory.TemplateArtifactRole || step.Artifacts[0].Id != "model.bin" || step.Artifacts[0].Format != EdgeTemplateModelArtifactCodec.ArtifactFormat || step.Artifacts[0].FormatVersion != EdgeTemplateModelArtifactCodec.SchemaVersion) throw new InvalidDataException("Expected the existing Edge learned-model artifact.");
        EdgeTemplateModelArtifact artifact = EdgeTemplateModelArtifactCodec.Decode(bundle.Model, token);
        if (artifact.SourceId != host.ReferenceImageSha256 || artifact.Width != TemplateRoi.Width || artifact.Height != TemplateRoi.Height) throw new InvalidDataException("Model and reference identity/crop differ.");
        // Factory validates model hash and learning-parameter agreement, but performs no Execute/Run/Probe.
        using VisionComputeSession session = new VisionComputeSession(new VisionComputeOptions { UseCuda = host.UseCuda });
        using EdgeBasedTemplateMatchingTool matcher = (EdgeBasedTemplateMatchingTool)VisionPipelineToolFactory.Create(step, _ => bundle.Model, session);
        using BlobTool presence = (BlobTool)VisionPipelineBlobToolFactory.Create(bundle.Pipeline.Steps[1]);
        using LineGaugeTool gauge = (LineGaugeTool)VisionPipelineBlobToolFactory.Create(bundle.Pipeline.Steps[2]);
        ValidateTranslation(matcher);
        ValidateRoi(presence.property.CvROI, new Size(host.Width, host.Height));
        ValidateRoi(gauge.property.CvROI, new Size(host.Width, host.Height));
        if (!presence.property.USE_ROI || !gauge.property.USE_ROI || presence.property.USE_MULTI_ROI || presence.property.CvMASKS.Count != 0 || gauge.property.USE_MULTI_ROI || !((LineGaugeToolProperty)gauge.property).UseSubpixelMeasurement) throw new InvalidDataException("Example requires one taught presence/gauge ROI and subpixel measurement.");
        token.ThrowIfCancellationRequested();
    }

    private static void ValidateSteps(VisionPipeline pipeline)
    {
        Type[] expected = { typeof(EdgeBasedTemplateMatchingTool), typeof(BlobTool), typeof(LineGaugeTool) };
        if (pipeline.Steps.Count != expected.Length) throw new InvalidDataException("Example requires Matching, Blob and LineGauge in that order.");
        for (int i = 0; i < expected.Length; i++)
        {
            VisionPipelineStep step = pipeline.Steps[i];
            if (!step.Enabled || !VisionPipelineBlobToolFactory.TryGetDescriptor(step.ToolType, out VisionPipelineToolDescriptor descriptor) || descriptor.ToolTypeName != expected[i].FullName) throw new InvalidDataException("Unsupported example step order/type/disabled state.");
            if (step.UseAcceptance || !step.ExpectedSuccess || step.MaxElapsedMilliseconds != 0 || step.UseAcceptanceMetricMinimum || step.UseAcceptanceMetricMaximum
                || !string.IsNullOrEmpty(step.InputLayer) || !string.IsNullOrEmpty(step.OutputLayer) || !string.IsNullOrEmpty(step.RequiredMessageText) || !string.IsNullOrEmpty(step.AcceptanceMetricName))
                throw new InvalidDataException("This example Host executes explicit stages; Pipeline runtime acceptance/layer routing is unsupported and must not be silently ignored.");
        }
    }

    private static void ValidateTranslation(EdgeBasedTemplateMatchingTool tool)
    {
        if (tool.property.USE_FIND_ANGLE || tool.property.USE_FIND_SCALE || tool.property.USE_ROI || tool.property.NUM_MATCH != 1 || !tool.property.USE_UNIQUE_MATCH_VALIDATION) throw new InvalidDataException("First example requires full-frame, single unique translation-only matching.");
    }

    private static void WriteBundle(string destination, Bundle bundle, CancellationToken token)
    {
        string directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, ".first-inspection-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (FileStream file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (ZipArchive zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                foreach (var entry in new[] { ("pipeline.xml", Encoding.UTF8.GetBytes(bundle.Xml)), ("model.bin", bundle.Model), ("host.json", JsonSerializer.SerializeToUtf8Bytes(bundle.Host, Json)) })
                {
                    token.ThrowIfCancellationRequested();
                    using Stream stream = zip.CreateEntry(entry.Item1).Open();
                    stream.Write(entry.Item2);
                }
            }
            _ = ReadBundle(temporary, token);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, destination, false); // Never replace a previously valid bundle.
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static Mat ReadGray(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!File.Exists(path) || new FileInfo(path).Length == 0 || new FileInfo(path).Length > 64 * 1024 * 1024) throw new ArgumentException("Image is missing, empty or exceeds the example file limit.");
        using Mat raw = Cv2.ImRead(path, ImreadModes.Unchanged);
        if (raw.Empty() || raw.Depth() != MatType.CV_8U || raw.Channels() is not (1 or 3 or 4) || (long)raw.Width * raw.Height > MaximumImagePixels) throw new ArgumentException("Input must be a nonempty 8-bit gray/BGR/BGRA image within the example size limit; depth is never silently reduced.");
        Mat gray = new Mat();
        try
        {
            if (raw.Channels() == 1) raw.CopyTo(gray);
            else Cv2.CvtColor(raw, gray, raw.Channels() == 3 ? ColorConversionCodes.BGR2GRAY : ColorConversionCodes.BGRA2GRAY);
            token.ThrowIfCancellationRequested();
            return gray;
        }
        catch { gray.Dispose(); throw; }
    }

    private static Rect MapRoi(MatchingCoordinateTransform transform, Rect roi, Size size)
    {
        Point2d[] corners = transform.TransformRoi(new Rect2d(roi.X, roi.Y, roi.Width, roi.Height));
        int left = checked((int)Math.Floor(corners.Min(value => value.X))), top = checked((int)Math.Floor(corners.Min(value => value.Y)));
        int right = checked((int)Math.Ceiling(corners.Max(value => value.X))), bottom = checked((int)Math.Ceiling(corners.Max(value => value.Y)));
        Rect mapped = new Rect(left, top, checked(right - left), checked(bottom - top));
        ValidateRoi(mapped, size);
        return mapped;
    }

    private static void ValidateRoi(Rect roi, Size size)
    {
        if (roi.X < 0 || roi.Y < 0 || roi.Width < 1 || roi.Height < 1 || (long)roi.X + roi.Width > size.Width || (long)roi.Y + roi.Height > size.Height) throw new ArgumentException("Transferred/taught ROI lies outside the image; clipping is not performed.");
    }

    private static void RequireCurrentMatch(VisionToolResult result, EdgeBasedTemplateMatchingTool tool)
    {
        if (!result.Success || tool.results.Count != 1) throw new InvalidDataException("Current matching failed: " + result.ErrorName + ": " + result.Message);
        ValidatePose(PoseOf(tool.results.Single()));
    }

    private static void ValidatePose(Pose? pose)
    {
        if (pose == null || !double.IsFinite(pose.X) || !double.IsFinite(pose.Y) || pose.X < 0 || pose.Y < 0 || pose.X >= Width || pose.Y >= Height || pose.Angle != 0 || pose.Scale != 1) throw new InvalidDataException("Reference/current pose must be finite and translation-only in the declared frame.");
    }

    private static Pose PoseOf(MatchingResult value) => new Pose(value.Center.X, value.Center.Y, value.Angle, value.Scale);
    private static MatchingResult MatchOf(Pose value) => new MatchingResult(0, 0, new Point2f((float)value.X, (float)value.Y), new Rect2f(), value.Angle, value.Scale);
    private static Stage StageOf(string name, VisionToolResult result) => new Stage(name, result.Success, result.ErrorName, result.Message);
    private static object? ComputeOf(ComputeExecutionEvidence? evidence) => evidence == null ? null : new
    {
        evidence.RequestedCuda, evidence.AccelerationApplied, FallbackReason = evidence.FallbackReason.ToString(),
        evidence.Support.DeviceDetected, evidence.Support.SelectedDeviceOrdinal, evidence.Support.ModulePath,
        ModuleSha256 = evidence.Support.ModulePath != null && File.Exists(evidence.Support.ModulePath) ? HashFile(evidence.Support.ModulePath) : null,
        InstallationAction = evidence.Support.InstallationAction.ToString(), evidence.GpuBatchCount, evidence.NativeExecutionErrorCode,
        evidence.InitializationMilliseconds, evidence.UploadMilliseconds, evidence.KernelWaitMilliseconds, evidence.DownloadMilliseconds, evidence.PeakScratchBytes
    };
    private static object[] PackageIdentities() => new[] { typeof(BlobTool).Assembly, typeof(EdgeBasedTemplateMatchingTool).Assembly, typeof(VisionComputeSession).Assembly, typeof(OpenVisionLab.Core.FormulaUtil).Assembly, typeof(Mat).Assembly }.Select(assembly => (object)new
    {
        Name = assembly.GetName().Name, AssemblyVersion = assembly.GetName().Version?.ToString(),
        InformationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        Path = assembly.Location, Sha256 = HashFile(assembly.Location)
    }).ToArray();
    private static void SaveImage(string path, Mat image) { if (!Cv2.ImWrite(path, image)) throw new IOException("Image write failed: " + path); }
    private static string RectText(Rect roi) => FormattableString.Invariant($"{roi.X},{roi.Y},{roi.Width},{roi.Height}");
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string HashFile(string path) { using FileStream stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static void Write(object report) => Console.WriteLine(JsonSerializer.Serialize(report, Json));

    private sealed record Pose(double X, double Y, double Angle, double Scale);
    private sealed record PixelPoint(double X, double Y);
    private sealed record HostSettings(int Version, string ReferenceImageSha256, string Frame, int Width, int Height, string Units, string Geometry, Pose ReferencePose, bool UseCuda);
    private sealed record Bundle(string Xml, VisionPipeline Pipeline, byte[] Model, HostSettings Host);
    private sealed record Stage(string Name, bool Success, string ErrorName, string Message);
    private sealed record LineEvidence(double Slope, double InterceptPx, double OrthogonalRmsePx, PixelPoint[] Points);
    private sealed class Inspection
    {
        public string Status { get; set; } = "";
        public string Units => "px";
        public string Geometry => "translation-only";
        public string Input { get; init; } = "";
        public string InputSha256 { get; init; } = "";
        public string PipelineSha256 { get; init; } = "";
        public string ModelSha256 { get; init; } = "";
        public bool RequestedCuda { get; init; }
        public byte InputGray00 { get; init; }
        public object[] Packages { get; init; } = Array.Empty<object>();
        public List<Stage> Stages { get; } = new List<Stage>();
        public object? Compute { get; set; }
        public Pose? Pose { get; set; }
        public string? PresenceRoi { get; set; }
        public string? GaugeRoi { get; set; }
        public int? BlobCount { get; set; }
        public double? BlobAreaPx { get; set; }
        public LineEvidence? Line { get; set; }
    }
}

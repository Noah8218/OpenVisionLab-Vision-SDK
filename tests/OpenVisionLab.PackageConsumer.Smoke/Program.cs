using OpenCvSharp;
using OpenVisionLab.Inspection;
using OpenVisionLab.Vision2D;
using OpenVisionLab.Vision2D.Blob;
using OpenVisionLab.Vision2D.Pipeline;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Result;
using OpenVisionLab.Vision2D.Tool;
using OpenVisionLab.Vision3D.FeatureExtraction;
using OpenVisionLab.Vision3D.Geometry;
using OpenVisionLab.Vision3D.Inspection;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;

if (args.Length > 0 && args[0] == "--cuda-local")
{
    Environment.ExitCode = CudaPackageConsumerSmoke.Run(args);
    return;
}

CheckCanny();

using Mat image = new Mat(2, 2, MatType.CV_8UC1, new Scalar(100));
using ThresholdTool threshold = new ThresholdTool();
threshold.SetProperty(new ThresholdToolProperty
{
    Threshold = 50,
    MaxValue = 255,
    ThresholdType = ThresholdTypes.Binary
});

HeightMap3D heightMap = HeightMap3D.FromArray(
    new[,] { { 1.0, 1.1 }, { 1.2, 1.3 } },
    0.0,
    0.0,
    1.0,
    1.0,
    "mm",
    "mm",
    "fixture",
    "package-smoke");

HeightMapCropResult crop = new HeightMapCropTool().Execute(
    HeightMap3D.FromArray(
        new[,] { { 1.0, 2.0, 3.0 }, { 4.0, double.NaN, 6.0 } },
        10.0,
        20.0,
        0.5,
        0.25,
        "mm",
        "raw-height",
        "fixture-top",
        "package-crop"),
    new HeightMapRoi(0, 1, 2, 2));
double[] croppedValues = crop.Output?.CopyValues();
if (!crop.Success
    || crop.Output == null
    || crop.ValidSampleCount != 3
    || crop.MissingSampleCount != 1
    || crop.SourceRoi.Row != 0
    || crop.SourceRoi.Column != 1
    || crop.SourceRoi.RowCount != 2
    || crop.SourceRoi.ColumnCount != 2
    || crop.Output.Rows != 2
    || crop.Output.Columns != 2
    || crop.Output.OriginX != 10.5
    || crop.Output.OriginY != 20.0
    || crop.Output.ColumnPitch != 0.5
    || crop.Output.RowPitch != 0.25
    || crop.Output.PlanarUnit != "mm"
    || crop.Output.HeightUnit != "raw-height"
    || crop.Output.FrameId != "fixture-top"
    || crop.Output.SourceId != "package-crop"
    || croppedValues == null
    || croppedValues.Length != 4
    || croppedValues[0] != 2.0
    || croppedValues[1] != 3.0
    || !double.IsNaN(croppedValues[2])
    || croppedValues[3] != 6.0)
{
    throw new InvalidOperationException(
        $"Height-map crop package example failed: {crop.Message}");
}

GridDiagnosticsResult gridDiagnostics = new GridDiagnosticsTool().Execute(2, 2);
if (gridDiagnostics.State != GridDiagnosticState.Pass
    || gridDiagnostics.DeclaredCellCount != 4
    || gridDiagnostics.ObservedSampleCount != 4
    || gridDiagnostics.UniqueLocatorCount != 4
    || gridDiagnostics.Checks.Count != 4
    || gridDiagnostics.Checks[3].Code != GridDiagnosticCode.CoordinateFiniteness)
{
    throw new InvalidOperationException("Grid diagnostics package example failed.");
}

ConnectedRegionResult connectedRegions = new ConnectedRegionTool().Execute(
    new HeightGridMask(
        2,
        4,
        new[] { true, true, false, false, false, false, true, true }));
if (!connectedRegions.Success
    || connectedRegions.RegionCount != 2
    || !connectedRegions.Regions.Select(region => region.CellCount).SequenceEqual(new[] { 2, 2 }))
{
    throw new InvalidOperationException("Connected-region package example failed.");
}

ConnectedRegionMetricsResult connectedRegionMetrics = new ConnectedRegionMetricsTool().Execute(
    connectedRegions,
    new ConnectedRegionMetricsOptions
    {
        OriginX = 10.0,
        OriginY = 20.0,
        ColumnPitch = 0.5,
        RowPitch = 0.25
    });
if (!connectedRegionMetrics.Success
    || connectedRegionMetrics.RegionCount != 2
    || connectedRegionMetrics.TotalArea != 0.5
    || connectedRegionMetrics.Regions.Any(region => region.Bounding == null))
{
    throw new InvalidOperationException("Connected-region metrics package example failed.");
}

ConnectedRegionPresenceResult connectedRegionPresence =
    new ConnectedRegionPresenceTool().Execute(
        connectedRegions,
        2,
        4,
        new[] { 2.0, 2.0, double.NaN, double.NaN, double.NaN, double.NaN, 3.0, 3.0 },
        new ConnectedRegionPresenceOptions
        {
            MinimumFiniteCoverageRatio = 1.0,
            MinimumMeanHeight = 1.0,
            MaximumMeanHeight = 2.5
        });
if (!connectedRegionPresence.Success
    || connectedRegionPresence.RegionCount != 2
    || connectedRegionPresence.PresentRegionCount != 1
    || connectedRegionPresence.MissingRegionCount != 1
    || connectedRegionPresence.AggregateDecision != ConnectedRegionPresenceDecision.Present
    || connectedRegionPresence.Regions[0].Decision != ConnectedRegionPresenceDecision.Present
    || connectedRegionPresence.Regions[1].Decision != ConnectedRegionPresenceDecision.Missing)
{
    throw new InvalidOperationException("Connected-region presence package example failed.");
}

ConnectedRegionFillHeightResult connectedRegionFillHeight =
    new ConnectedRegionFillHeightTool().Execute(
        connectedRegions,
        2,
        4,
        new[] { 3.0, 3.0, double.NaN, double.NaN, double.NaN, double.NaN, 2.0, 2.0 },
        new ConnectedRegionFillHeightOptions
        {
            ReferenceSurface = new ConnectedRegionFillHeightReferenceSurface
            {
                SlopeX = 0.0,
                SlopeZ = 0.0,
                Intercept = 0.0
            },
            MinimumFiniteCoverageRatio = 1.0,
            MinimumMeanFillHeight = 2.5,
            MaximumMeanFillHeight = 3.5
        });
if (!connectedRegionFillHeight.Success
    || connectedRegionFillHeight.RegionCount != 2
    || connectedRegionFillHeight.AcceptedRegionCount != 1
    || connectedRegionFillHeight.RejectedRegionCount != 1
    || connectedRegionFillHeight.Regions[0].MeanFillHeight != 3.0
    || connectedRegionFillHeight.Regions[0].Decision != ConnectedRegionFillHeightDecision.Accepted
    || connectedRegionFillHeight.Regions[1].MeanFillHeight != 2.0
    || connectedRegionFillHeight.Regions[1].FillHeightDisposition
        != ConnectedRegionFillHeightDisposition.BelowMinimum)
{
    throw new InvalidOperationException("Connected-region fill-height package example failed.");
}

ThicknessInspectionTool thickness = new ThicknessInspectionTool(
    new ThicknessInspectionOptions
    {
        MinimumThickness = 0.9,
        MaximumThickness = 1.4,
        MinimumValidSamples = 4,
        MinimumValidCoverageRatio = 1.0,
        InputRequirements = new HeightMapInputRequirements(
            "mm",
            "mm",
            "fixture")
    });

using CombinedInspectionRunResult result = new CombinedInspectionRunner().Run(
    new CombinedInspectionInput
    {
        Image = image,
        HeightMap = heightMap
    },
    new IVisionTool[] { threshold },
    new IThreeDInspectionTool[] { thickness });

if (!result.Success || result.Steps.Count != 2)
{
    throw new InvalidOperationException(
        $"Package-only consumer failed: {result.Message}");
}

TwoPointLineTool packageLineTool = new TwoPointLineTool();
ThreeDToolAdapter<TwoPointLineResult> packageLineAdapter = new ThreeDToolAdapter<TwoPointLineResult>(
    "Package two-point line",
    token => packageLineTool.Execute(
        new TwoPointLineInput(new ThreeDPoint(0.0, 0.0, 0.0), new ThreeDPoint(3.0, 4.0, 0.0)),
        token));
ThreeDToolExecutionReport threeDExecutionReport = ThreeDToolExecutionRunner.Run(
    new IThreeDToolAdapter[] { packageLineAdapter });
ThreeDToolExecutionResult<TwoPointLineResult> packageLineResult =
    threeDExecutionReport.Steps[0] as ThreeDToolExecutionResult<TwoPointLineResult>;
if (threeDExecutionReport.Status != ThreeDToolExecutionStatus.Completed
    || packageLineResult?.Result == null
    || !packageLineResult.Result.Success
    || packageLineResult.Result.SegmentLength != 5.0
    || !packageLineResult.SupportsCancellation)
{
    throw new InvalidOperationException("Typed 3D adapter package contract failed.");
}

using Mat blobImage = new Mat(8, 8, MatType.CV_8UC1, Scalar.All(0));
Cv2.Rectangle(blobImage, new Rect(2, 2, 4, 4), Scalar.All(255), -1);
using BlobTool blob = new BlobTool();
blob.SetProperty(new BlobToolProperty
{
    THRESHOLD = 128,
    MIN_AREA = 4,
    MAX_AREA = 64
});
using VisionToolResult blobResult = blob.Execute(blobImage);
if (!blobResult.Success
    || blob.results.Count != 1
    || blob.candidates.Count != 1
    || !blob.candidates[0].Accepted
    || blob.candidates[0].GenerationStage != VisionObjectCandidateGenerationStage.BlobLabeling
    || blob.candidates[0].CoordinateFrame != VisionObjectCandidateCoordinateFrame.SourceImage
    || blob.candidates[0].Drawing == null)
{
    throw new InvalidOperationException(
        $"Blob package example failed: {blobResult.ErrorName}: {blobResult.Message}");
}

VisionPipeline serializedPipeline = new VisionPipeline { Name = "Package pipeline" };
serializedPipeline.Steps.Add(new VisionPipelineStep
{
    Name = "Threshold",
    ToolType = "threshold",
    InputLayer = "input",
    OutputLayer = "binary"
});
serializedPipeline.Steps[0].Parameters[nameof(ThresholdToolProperty.Threshold)] = "50";
string pipelineXml = VisionPipelineSerializer.Serialize(serializedPipeline);
VisionPipeline restoredPipeline = VisionPipelineSerializer.Deserialize(pipelineXml);
if (restoredPipeline.SchemaVersion != VisionPipeline.CurrentSchemaVersion
    || restoredPipeline.Steps.Count != 1
    || restoredPipeline.Steps[0].Parameters[nameof(ThresholdToolProperty.Threshold)] != "50")
{
    throw new InvalidOperationException("Versioned Pipeline package round-trip failed.");
}

using (VisionPipelineContext missingLayerContext = new VisionPipelineContext())
using (VisionPipelineRunResult missingLayerResult = new VisionPipelineRuntime()
    .RunWithFailureResults(restoredPipeline, missingLayerContext))
{
    if (missingLayerResult.Success
        || missingLayerResult.StepResults.Count != 1
        || missingLayerResult.StepResults[0].ToolResult.ErrorCode != VisionToolErrorCode.InputLayerMissing)
    {
        throw new InvalidOperationException("Pipeline package failure-result contract failed.");
    }
}

using (CancellationTokenSource pipelineCancellation = new CancellationTokenSource())
using (VisionPipelineContext canceledPipelineContext = new VisionPipelineContext())
{
    canceledPipelineContext.SetLayer("input", image);
    pipelineCancellation.Cancel();
    using VisionPipelineRunResult canceledPipelineResult = new VisionPipelineRuntime()
        .RunWithFailureResults(restoredPipeline, canceledPipelineContext, pipelineCancellation.Token);
    if (canceledPipelineResult.Success
        || canceledPipelineResult.StepResults.Count != 1
        || canceledPipelineResult.StepResults[0].ToolResult.ErrorCode != VisionToolErrorCode.StepCanceled
        || canceledPipelineResult.StepResults[0].ToolResult.ResultStatus != VisionToolResultStatus.Canceled)
    {
        throw new InvalidOperationException("Pipeline cooperative cancellation package contract failed.");
    }
}

if (VisionPipelineToolFactory.Descriptors.Count != 15
    || VisionPipelineBlobToolFactory.Descriptors.Count != 16
    || !VisionPipelineToolFactory.TryGetDescriptor("QrCodeTool", out VisionPipelineToolDescriptor qrDescriptor)
    || qrDescriptor.ToolType != "qrcode"
    || qrDescriptor.PackageId != "OpenVisionLab.Vision2D"
    || !VisionPipelineBlobToolFactory.TryGetDescriptor("BlobTool", out VisionPipelineToolDescriptor blobDescriptor)
    || blobDescriptor.PackageId != "OpenVisionLab.Vision2D.Blob")
{
    throw new InvalidOperationException("Pipeline Tool descriptor package contract failed.");
}

using (Mat template = new Mat(16, 16, MatType.CV_8UC1, Scalar.Black))
{
    Cv2.Rectangle(template, new Rect(3, 4, 7, 8), Scalar.White, Cv2.FILLED);
    byte[] encodedTemplate = template.ToBytes(".png", Array.Empty<int>());
    string templateSha256;
    using (SHA256 sha256 = SHA256.Create())
    {
        templateSha256 = BitConverter.ToString(sha256.ComputeHash(encodedTemplate)).Replace("-", string.Empty);
    }

    VisionPipelineStep matchingStep = new VisionPipelineStep { ToolType = "matching" };
    matchingStep.Parameters[nameof(MatchingToolProperty.USE_FIND_ANGLE)] = "false";
    matchingStep.Artifacts.Add(new VisionPipelineArtifactReference(
        VisionPipelineToolFactory.TemplateArtifactRole,
        "package/templates/part-a",
        VisionPipelineToolFactory.EncodedImageArtifactFormat,
        VisionPipelineToolFactory.EncodedImageArtifactFormatVersion,
        templateSha256));

    int resolverCalls = 0;
    using MatchingTool restoredMatching = (MatchingTool)VisionPipelineBlobToolFactory.Create(matchingStep, artifact =>
    {
        resolverCalls++;
        if (artifact.Id != "package/templates/part-a")
        {
            throw new InvalidOperationException("Pipeline artifact resolver received the wrong ID.");
        }

        return encodedTemplate;
    });
    if (resolverCalls != 1 || restoredMatching.property.PATTERN_PATH != string.Empty)
    {
        throw new InvalidOperationException("Pipeline model reconstruction package contract failed.");
    }
}

using ContourTool contour = new ContourTool();
contour.SetProperty(new ContourToolProperty
{
    MIN_AREA = 4,
    MAX_AREA = 64
});
using VisionToolResult contourResult = contour.Execute(blobImage);
if (!contourResult.Success
    || contour.results.Count != 1
    || contour.candidates.Count != 1
    || !contour.candidates[0].Accepted
    || contour.candidates[0].GenerationStage != VisionObjectCandidateGenerationStage.ContourExtraction
    || contour.candidates[0].CoordinateFrame != VisionObjectCandidateCoordinateFrame.SourceImage
    || contour.candidates[0].Drawing == null)
{
    throw new InvalidOperationException(
        $"Contour package example failed: {contourResult.ErrorName}: {contourResult.Message}");
}

using MeanTool mean = new MeanTool();
mean.SetProperty(new MeanToolProperty());
using VisionToolResult meanResult = mean.Execute(blobImage);
if (!meanResult.Success || mean.results.Count != 1)
{
    throw new InvalidOperationException(
        $"Mean package example failed: {meanResult.ErrorName}: {meanResult.Message}");
}

using Mat matchingSource = new Mat(32, 32, MatType.CV_8UC1, Scalar.All(0));
Cv2.Rectangle(matchingSource, new Rect(12, 10, 8, 8), Scalar.All(255), -1);
Cv2.Line(matchingSource, new Point(12, 10), new Point(19, 17), Scalar.All(0), 1);
using Mat matchingTemplate = new Mat(matchingSource, new Rect(12, 10, 8, 8)).Clone();
using MatchingTool matching = new MatchingTool();
matching.SetProperty(new MatchingToolProperty
{
    USE_FIND_ANGLE = false,
    NUM_MATCH = 1,
    SCORE_MIN = 0.9
});
matching.SetTemplateImage(matchingTemplate);
using VisionToolResult matchingResult = matching.Execute(matchingSource);
if (!matchingResult.Success || matching.results.Count != 1)
{
    throw new InvalidOperationException(
        $"Matching package example failed: {matchingResult.ErrorName}: {matchingResult.Message}");
}

using (CancellationTokenSource matchingCancellation = new CancellationTokenSource())
{
    matchingCancellation.Cancel();
    try
    {
        using VisionToolResult unexpected = ((ICancellableVisionTool)matching)
            .Execute(matchingSource, matchingCancellation.Token);
        throw new InvalidOperationException("Pre-canceled Matching package execution did not stop.");
    }
    catch (OperationCanceledException exception) when (exception.CancellationToken == matchingCancellation.Token)
    {
    }
}

EdgeBasedTemplateMatchingToolProperty edgeMatchingProperty = new EdgeBasedTemplateMatchingToolProperty();
SiftToolProperty siftProperty = new SiftToolProperty();
LineGaugeToolProperty lineGaugeProperty = new LineGaugeToolProperty();
if (edgeMatchingProperty.MAX_TEMPLATE_POINTS <= 0
    || siftProperty.RANSAC_REPROJ_THRESHOLD <= 0
    || lineGaugeProperty.SAMPLING_STEP < 1
    || lineGaugeProperty.CvROIS == null)
{
    throw new InvalidOperationException("Ready-to-use 2D property defaults are invalid.");
}

SurfaceMatchSample[] model =
{
    new SurfaceMatchSample(0, new ThreeDPoint(0, 0, 0)),
    new SurfaceMatchSample(1, new ThreeDPoint(2, 0, 0)),
    new SurfaceMatchSample(2, new ThreeDPoint(0, 1, 0)),
    new SurfaceMatchSample(3, new ThreeDPoint(0, 0, 1))
};
SurfaceMatchSample[] scene =
{
    new SurfaceMatchSample(0, new ThreeDPoint(10, -4, 2)),
    new SurfaceMatchSample(1, new ThreeDPoint(12, -4, 2)),
    new SurfaceMatchSample(2, new ThreeDPoint(10, -3, 2)),
    new SurfaceMatchSample(3, new ThreeDPoint(10, -4, 3))
};
DeterministicRigidSurfacePoseSearchResult match =
    new DeterministicRigidSurfacePoseSearchTool().Execute(
        model,
        scene,
        new DeterministicRigidSurfacePoseSearchOptions
        {
            MinimumRotationXDegrees = 0,
            MaximumRotationXDegrees = 0,
            RotationStepXDegrees = 1,
            MinimumRotationYDegrees = 0,
            MaximumRotationYDegrees = 0,
            RotationStepYDegrees = 1,
            MinimumRotationZDegrees = 0,
            MaximumRotationZDegrees = 0,
            RotationStepZDegrees = 1,
            MinimumTranslationX = 9,
            MaximumTranslationX = 11,
            MinimumTranslationY = -5,
            MaximumTranslationY = -3,
            MinimumTranslationZ = 1,
            MaximumTranslationZ = 3,
            MaximumCorrespondenceDistance = 1e-9,
            MinimumMatchedSampleCount = 4,
            MaximumCandidateCount = 1
        });

if (!match.Success || !match.Matched || match.Coverage.MatchedModelSampleCount != 4)
{
    throw new InvalidOperationException(
        $"Surface-match package example failed: {match.Message}{match.RejectionReason}");
}

NominalActualMeshComparisonResult comparison =
    new NominalActualMeshComparisonTool().Execute(
        new[]
        {
            new MeshTriangle(
                3,
                new ThreeDPoint(0, 0, 0),
                new ThreeDPoint(2, 0, 0),
                new ThreeDPoint(0, 2, 0))
        },
        new[]
        {
            new ThreeDPoint(0.5, 0.5, 1.0),
            new ThreeDPoint(0.5, 0.5, -2.0)
        },
        new NominalActualMeshComparisonOptions(2, -1.5, 1.5, 100));

if (!comparison.Success
    || comparison.WithinToleranceCount != 1
    || comparison.BelowToleranceCount != 1)
{
    throw new InvalidOperationException(
        $"Mesh-comparison package example failed: {comparison.Message}");
}

Console.WriteLine(
    "OpenVisionLab package-only 2D properties, tools, Pipeline, Blob, 3D crop, connected-region presence and fill-height, surface-match, and mesh consumer passed.");

Console.WriteLine("RUNTIME|" + Environment.Version + "|" + AppContext.TargetFrameworkName + "|" + (Environment.Is64BitProcess ? "x64" : "x86"));
foreach (string name in new[] { "OpenVisionLab.Core", "OpenVisionLab.Compute", "OpenVisionLab.Vision2D", "OpenVisionLab.Vision2D.Blob", "OpenVisionLab.Vision3D", "OpenVisionLab.Inspection", "OpenCvSharp" })
{
    Assembly assembly = Assembly.Load(name);
    Console.WriteLine("ASSEMBLY|" + name + "|" + assembly.Location + "|" + assembly.ManifestModule.ModuleVersionId + "|" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))));
}
using (Process process = Process.GetCurrentProcess())
{
    ProcessModule native = process.Modules.Cast<ProcessModule>().Single(module => string.Equals(module.ModuleName, "OpenCvSharpExtern.dll", StringComparison.OrdinalIgnoreCase));
    Console.WriteLine("NATIVE|" + native.FileName + "|" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(native.FileName))));
}
Console.WriteLine("PASS|net8-package-consumer");

static void CheckCanny()
{
    using (Mat parent = new Mat(31, 43, MatType.CV_8UC3, Scalar.White))
    using (Mat color = parent.SubMat(new Rect(3, 4, 31, 23)))
    using (Mat gray = new Mat())
    using (Mat expectedColor = new Mat())
    {
        for (int row = 0; row < color.Rows; row++)
            for (int column = 0; column < color.Cols; column++)
                color.Set(row, column, column < 15 ? new Vec3b(255, 0, 0) : new Vec3b(0, 49, 0));
        Cv2.CvtColor(color, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.ImEncode(".png", parent, out byte[] encodedInput);
        using (SHA256 hash = SHA256.Create())
            Console.WriteLine("INPUT|canny-padded-bgr|" + BitConverter.ToString(hash.ComputeHash(encodedInput)).Replace("-", string.Empty));
        using (Mat before = parent.Clone())
        using (Mat referenceColor = color.Clone())
        {
            Cv2.Canny(referenceColor, expectedColor, 100, 200, 3, false);
            VisionToolResult retained = null;
            try
            {
                using (EdgeDetectionTool tool = new EdgeDetectionTool())
                {
                    tool.SetProperty(new EdgeDetectionToolProperty { UseL2Gradient = false });
                    retained = tool.Execute(color);
                    Require(retained.Success && retained.ResultImage.Type() == MatType.CV_8UC1, "Color Canny must return an 8UC1 image: " + retained.Message);
                    foreach (Mat input in new[] { gray, color })
                        using (Mat referenceInput = input.Clone())
                        using (Mat expected = new Mat())
                        using (VisionToolResult actual = tool.Execute(input))
                        {
                            Cv2.Canny(referenceInput, expected, 100, 200, 3, false);
                            Require(actual.Success && actual.ResultImage.Type() == MatType.CV_8UC1 && actual.ResultImage.Size() == input.Size()
                                && Cv2.Norm(actual.ResultImage, expected, NormTypes.L1) == 0, "Packaged Canny must match the distinct-output native reference.");
                            Require(actual.Metrics["SourceImageChannels"] == input.Channels() && actual.Metrics["ResultImageChannels"] == 1, "Canny source/output channel metrics differ.");
                            Require(input == color ? Cv2.CountNonZero(actual.ResultImage) > 0 : Cv2.CountNonZero(actual.ResultImage) == 0, "Color Canny must retain gradients at equal grayscale intensity.");
                        }
                    tool.SetProperty(new EdgeDetectionToolProperty { CannyThresholdLow = 400, CannyThresholdHigh = 200 });
                    using (VisionToolResult invalid = tool.Execute(color))
                        Require(!invalid.Success && invalid.ErrorCode == VisionToolErrorCode.EdgeDetectionInvalidThreshold
                            && invalid.Exception == null && invalid.ResultImage == null, "Invalid Canny settings must return a typed failure.");
                    tool.SetProperty(new EdgeDetectionToolProperty { UseL2Gradient = false });
                    using (Mat unsupported = new Mat(23, 31, MatType.CV_16UC3, Scalar.All(17)))
                    using (Mat unsupportedBefore = unsupported.Clone())
                    using (VisionToolResult failed = tool.Execute(unsupported))
                        Require(!failed.Success && failed.ErrorCode == VisionToolErrorCode.OpenCvExecutionFailed
                            && failed.Exception != null && failed.ResultImage == null && Cv2.Norm(unsupported, unsupportedBefore, NormTypes.L1) == 0,
                            "Unsupported Canny depth must fail without altering caller pixels.");
                    using (VisionToolResult recovered = tool.Execute(color))
                        Require(recovered.Success && Cv2.Norm(recovered.ResultImage, expectedColor, NormTypes.L1) == 0, "Canny must recover on the same instance after failure.");
                    VisionPipelineStep step = new VisionPipelineStep { ToolType = "edgeDetection" };
                    step.Parameters["UseL2Gradient"] = "false";
                    using (EdgeDetectionTool restored = (EdgeDetectionTool)VisionPipelineToolFactory.Create(step))
                    using (VisionToolResult result = restored.Execute(gray))
                        Require(result.Success && result.ResultImage.Type() == MatType.CV_8UC1 && Cv2.CountNonZero(result.ResultImage) == 0, "Restored Canny must preserve the grayscale contract.");
                }
                Require(!retained.ResultImage.IsDisposed && Cv2.Norm(retained.ResultImage, expectedColor, NormTypes.L1) == 0, "A returned Canny snapshot must survive later calls and Tool disposal.");
                Require(Cv2.Norm(parent, before, NormTypes.L1) == 0, "Canny must preserve caller pixels and ROI padding.");
            }
            finally { retained?.Dispose(); }
        }
    }
    Console.WriteLine("PASS|canny-package-consumer|Gray=1|Color=3|PaddedRoi|NativeReference|InvalidDepthRecovery|RetainedResult|SourcePreserved");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

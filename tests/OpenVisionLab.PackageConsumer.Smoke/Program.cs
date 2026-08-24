using OpenCvSharp;
using OpenVisionLab.Inspection;
using OpenVisionLab.Vision2D.Blob;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Tool;
using OpenVisionLab.Vision3D.FeatureExtraction;
using OpenVisionLab.Vision3D.Geometry;
using OpenVisionLab.Vision3D.Inspection;

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
if (!blobResult.Success || blob.results.Count != 1)
{
    throw new InvalidOperationException(
        $"Blob package example failed: {blobResult.ErrorName}: {blobResult.Message}");
}

using ContourTool contour = new ContourTool();
contour.SetProperty(new ContourToolProperty
{
    MIN_AREA = 4,
    MAX_AREA = 64
});
using VisionToolResult contourResult = contour.Execute(blobImage);
if (!contourResult.Success || contour.results.Count != 1)
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
    "OpenVisionLab package-only 2D properties, tools, Blob, 3D crop, surface-match, and mesh consumer passed.");

using OpenVisionLab.Vision3D.Geometry;
using System.Threading;

namespace OpenVisionLab.Vision3D.Inspection
{
    /// <summary>Optional cooperative cancellation; existing implementations remain valid.</summary>
    public interface ICancellableThreeDInspectionTool : IThreeDInspectionTool
    {
        ThreeDInspectionResult Execute(HeightMap3D source, CancellationToken cancellationToken);
    }

    public interface IThreeDInspectionTool
    {
        string Name { get; }

        ThreeDInspectionResult Execute(HeightMap3D source);
    }
}

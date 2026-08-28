using System;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            SmokeRunner runner = new SmokeRunner();

            try
            {
                runner.Run(HeightMapAndGeometrySmokeSuite.Cases());
                runner.Run(ThreeDStatisticsAndEvidenceSmokeSuite.Cases());
                runner.Run(ThreeDSurfaceAndMetrologySmokeSuite.Cases());
                runner.Run(Vision2DSmokeSuite.Cases());
                runner.Run(VisionObjectCandidateSmokeSuite.Cases());
                runner.Run(LegacyApiCompatibilitySmokeSuite.Cases());
                runner.Run(CombinedInspectionSmokeSuite.Cases());
                runner.Run(MatchingCharacterizationSmokeSuite.Cases());
                runner.Run(TriangleMeshDistanceCharacterizationSmokeSuite.Cases());
                runner.Run(PointCloudBackgroundFilterSmokeSuite.Cases());
                runner.Run(PointCloudVoxelDownsampleSmokeSuite.Cases());
                runner.Run(NominalActualMeshComparisonCharacterizationSmokeSuite.Cases());

                Console.WriteLine("OpenVisionLab.Inspection.Smoke | " + runner.Passed + "/" + runner.Total + " passed");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("FAIL | " + exception.Message);
                Console.Error.WriteLine(exception);
                Console.Error.WriteLine("OpenVisionLab.Inspection.Smoke | " + runner.Passed + "/" + runner.Total + " passed before failure");
                return 1;
            }
        }
    }
}

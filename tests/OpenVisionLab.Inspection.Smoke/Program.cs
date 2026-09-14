using System;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            bool listOnly = args.Length == 1 && args[0] == "--list";
            string filter = args.Length == 2 && args[0] == "--filter" ? args[1] : null;
            if (args.Length > 0 && !listOnly && string.IsNullOrWhiteSpace(filter))
            {
                Console.Error.WriteLine("Usage: OpenVisionLab.Inspection.Smoke [--list | --filter <name substring>]");
                return 2;
            }

            SmokeRunner runner = new SmokeRunner(filter, listOnly);

            try
            {
                runner.Run(HeightMapAndGeometrySmokeSuite.Cases());
                runner.Run(ThreeDStatisticsAndEvidenceSmokeSuite.Cases());
                runner.Run(ThreeDSurfaceAndMetrologySmokeSuite.Cases());
                runner.Run(Vision2DSmokeSuite.Cases());
                runner.Run(VisionObjectCandidateSmokeSuite.Cases());
                runner.Run(LegacyApiCompatibilitySmokeSuite.Cases());
                runner.Run(CombinedInspectionSmokeSuite.Cases());
                runner.Run(ThreeDToolExecutionSmokeSuite.Cases());
                runner.Run(MatchingCharacterizationSmokeSuite.Cases());
                runner.Run(TriangleMeshDistanceCharacterizationSmokeSuite.Cases());
                runner.Run(PointCloudBackgroundFilterSmokeSuite.Cases());
                runner.Run(PointCloudVoxelDownsampleSmokeSuite.Cases());
                runner.Run(NominalActualMeshComparisonCharacterizationSmokeSuite.Cases());

                if (runner.Total == 0)
                {
                    throw new ArgumentException("No smoke cases matched the requested filter.");
                }

                Console.WriteLine(listOnly
                    ? "OpenVisionLab.Inspection.Smoke | " + runner.Total + " cases listed"
                    : "OpenVisionLab.Inspection.Smoke | " + runner.Passed + "/" + runner.Total + " passed");
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

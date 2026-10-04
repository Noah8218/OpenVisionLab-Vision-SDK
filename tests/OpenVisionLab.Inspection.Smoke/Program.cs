using System;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            bool listOnly = args.Length == 1 && args[0] == "--list";
            bool qualify = args.Length == 2 && args[0] == "--qualify";
            bool surfaceChallenge = args.Length == 2 && args[0] == "--surface-challenge";
            bool sdkQuality = args.Length == 2 && args[0] == "--sdk-quality";
            bool surfaceRobustness = args.Length == 2 && args[0] == "--surface-robustness";
            bool cudaStudy = args.Length == 2 && args[0] == "--cuda-study";
            bool cudaBenchmark = args.Length == 2 && (args[0] == "--cuda-benchmark" || cudaStudy);
            bool cudaCold = args.Length == 3 && args[0] == "--cuda-cold" && (args[1] == "cpu" || args[1] == "cuda");
            int imageSide = 0;
            bool cudaLarge = args.Length == 4 && args[0] == "--cuda-large" && (args[1] == "cpu" || args[1] == "cuda")
                && int.TryParse(args[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out imageSide);
            string filter = args.Length == 2 && args[0] == "--filter" ? args[1] : null;
            if (args.Length > 0 && !listOnly && !qualify && !sdkQuality && !surfaceChallenge && !surfaceRobustness && !cudaBenchmark && !cudaCold && !cudaLarge && string.IsNullOrWhiteSpace(filter))
            {
                Console.Error.WriteLine("Usage: OpenVisionLab.Inspection.Smoke [--sdk-quality <D: report path> | --list | --filter <name substring> | --qualify <D: report path> | --surface-challenge <D: report path> | --surface-robustness <D: report path> | --cuda-benchmark <D: report path> | --cuda-study <D: report path> | --cuda-cold <cpu|cuda> <D: report path> | --cuda-large <cpu|cuda> <5000|10000|15000> <D: report path>]");
                return 2;
            }

            SmokeRunner runner = new SmokeRunner(filter, listOnly);

            try
            {
                if (sdkQuality) return SdkQualityEvaluationSmokeSuite.RunEvaluation(args[1]);
                if (cudaLarge) return CudaKernelSmokeSuite.LargeImageBenchmark(args[3], args[1] == "cuda", imageSide);
                if (cudaCold) return CudaKernelSmokeSuite.ColdBenchmark(args[2], args[1] == "cuda");
                if (cudaBenchmark) return CudaKernelSmokeSuite.Benchmark(args[1], cudaStudy);
                if (qualify) return MatchingQualificationSmokeSuite.RunQualification(args[1]);
                if (surfaceChallenge) return SurfaceChallengeEvaluationSmokeSuite.RunEvaluation(args[1]);
                if (surfaceRobustness) return SurfaceRobustnessSmokeSuite.RunEvaluation(args[1]);
                runner.Run(HeightMapAndGeometrySmokeSuite.Cases());
                runner.Run(ThreeDStatisticsAndEvidenceSmokeSuite.Cases());
                runner.Run(ThreeDSurfaceAndMetrologySmokeSuite.Cases());
                runner.Run(SurfaceModelWorkflowSmokeSuite.Cases());
                runner.Run(DatumPlaneFitSmokeSuite.Cases());
                runner.Run(SurfacePoseRefinementSmokeSuite.Cases());
                runner.Run(SurfaceChallengeEvaluationSmokeSuite.Cases());
                runner.Run(SurfaceRobustnessSmokeSuite.Cases());
                runner.Run(SurfaceQualitySmokeSuite.Cases());
                runner.Run(PreparedSceneSamplingSmokeSuite.Cases());
                runner.Run(LineGaugeSubpixelSmokeSuite.Cases());
                runner.Run(SdkQualityEvaluationSmokeSuite.Cases());
                runner.Run(SurfaceSearchRobustnessSmokeSuite.Cases());
                runner.Run(MatchingQualificationSmokeSuite.Cases());
                runner.Run(SdkExecutionSmokeSuite.Cases());
                runner.Run(ComputeAccelerationSmokeSuite.Cases());
                runner.Run(CudaKernelSmokeSuite.Cases());
                runner.Run(Vision2DSmokeSuite.Cases());
                runner.Run(EdgePoseRefinementSmokeSuite.Cases());
                runner.Run(EdgeModelArtifactSmokeSuite.Cases());
                runner.Run(Vision2DCancellationSmokeSuite.Cases());
                runner.Run(VisionObjectCandidateSmokeSuite.Cases());
                runner.Run(BinaryShapeCompatibilitySmokeSuite.Cases());
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

using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;
using OpenVisionLab.Vision2D.Blob;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Tool;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class BinaryShapeCompatibilitySmokeSuite
    {
        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase("Blob preserves 8-connected component geometry and source order", TestBlobComponentContract);
            yield return new SmokeCase("Contour preserves branch traversal and approximation", TestContourBranchContract);
            yield return new SmokeCase("Contour preserves hole order across retrieval modes", TestContourHoleContract);
        }

        private static void TestBlobComponentContract()
        {
            using (Mat source = new Mat(10, 12, MatType.CV_8UC1, Scalar.Black))
            using (BlobTool tool = new BlobTool())
            {
                source.Set(1, 1, (byte)255);
                source.Set(2, 2, (byte)255);
                Cv2.Rectangle(source, new Rect(7, 5, 3, 2), Scalar.White, Cv2.FILLED);

                tool.SetProperty(new BlobToolProperty
                {
                    USE_THRESHOLD = true,
                    THRESHOLD = 128D,
                    MIN_AREA = 0,
                    MAX_AREA = int.MaxValue
                });

                using (VisionToolResult execution = tool.Execute(source))
                {
                    Require(execution.Success, "Blob compatibility fixture must execute successfully.");
                }

                Require(tool.candidates.Count == 2 && tool.results.Count == 2,
                    "Diagonal pixels must form one 8-connected component before the rectangle component.");
                Require(tool.candidates[0].NativeIndex == 1 && tool.candidates[0].Area == 2D,
                    "The first Blob component must retain its one-based source-order identity and pixel area.");
                Require(tool.candidates[0].Bounding.X == 1 && tool.candidates[0].Bounding.Y == 1
                    && tool.candidates[0].Bounding.Width == 2 && tool.candidates[0].Bounding.Height == 2,
                    "The diagonal component must retain inclusive pixel bounds.");
                RequireApproximately(tool.candidates[0].Center.X, 1.5D, 0D,
                    "The diagonal component X centroid changed.");
                RequireApproximately(tool.candidates[0].Center.Y, 1.5D, 0D,
                    "The diagonal component Y centroid changed.");
                RequireApproximately(tool.candidates[0].Angle, Math.PI / 4D, 1E-12D,
                    "The diagonal component central-moment angle changed.");

                Require(tool.candidates[1].NativeIndex == 2 && tool.candidates[1].Area == 6D,
                    "The rectangle must remain the second source-order component.");
                Require(tool.candidates[1].Bounding.X == 7 && tool.candidates[1].Bounding.Y == 5
                    && tool.candidates[1].Bounding.Width == 3 && tool.candidates[1].Bounding.Height == 2,
                    "The rectangle component bounds changed.");
                RequireApproximately(tool.candidates[1].Center.X, 8D, 0D,
                    "The rectangle component X centroid changed.");
                RequireApproximately(tool.candidates[1].Center.Y, 5.5D, 0D,
                    "The rectangle component Y centroid changed.");
                RequireApproximately(tool.candidates[1].Angle, 0D, 0D,
                    "The rectangle component angle changed.");
                Require(tool.results.Count == 2 && tool.results[0].Index == 1 && tool.results[1].Index == 2,
                    "Blob results must remain consecutively one-based after filtering.");
            }
        }

        private static void TestContourBranchContract()
        {
            using (Mat source = new Mat(5, 5, MatType.CV_8UC1, Scalar.Black))
            {
                foreach (Point point in new[]
                {
                    new Point(0, 0), new Point(1, 0), new Point(2, 1),
                    new Point(3, 0), new Point(3, 2), new Point(3, 3)
                })
                {
                    source.Set(point.Y, point.X, (byte)255);
                }

                AssertContourPoints(
                    source,
                    RetrievalModes.External,
                    ContourApproximationModes.ApproxNone,
                    new[]
                    {
                        new Point(0, 0), new Point(1, 0), new Point(2, 1),
                        new Point(3, 0), new Point(2, 1), new Point(3, 2),
                        new Point(3, 3), new Point(3, 2), new Point(1, 0)
                    });
                AssertContourPoints(
                    source,
                    RetrievalModes.External,
                    ContourApproximationModes.ApproxSimple,
                    new[]
                    {
                        new Point(0, 0), new Point(2, 1), new Point(3, 0),
                        new Point(2, 1), new Point(3, 3)
                    });
            }
        }

        private static void TestContourHoleContract()
        {
            using (Mat source = new Mat(9, 9, MatType.CV_8UC1, Scalar.Black))
            {
                Cv2.Rectangle(source, new Rect(1, 1, 7, 7), Scalar.White, Cv2.FILLED);
                Cv2.Rectangle(source, new Rect(3, 3, 3, 3), Scalar.Black, Cv2.FILLED);

                Point[] expectedOuter =
                {
                    new Point(1, 1), new Point(7, 1), new Point(7, 7), new Point(1, 7)
                };
                Point[] expectedHole =
                {
                    new Point(3, 2), new Point(5, 2), new Point(6, 3), new Point(6, 5),
                    new Point(5, 6), new Point(3, 6), new Point(2, 5), new Point(2, 3)
                };

                AssertContourSet(
                    source,
                    RetrievalModes.External,
                    ContourApproximationModes.ApproxNone,
                    new[] { expectedOuter });
                foreach (RetrievalModes retrievalMode in new[]
                {
                    RetrievalModes.List, RetrievalModes.CComp, RetrievalModes.Tree
                })
                {
                    AssertContourSet(
                        source,
                        retrievalMode,
                        ContourApproximationModes.ApproxNone,
                        new[] { expectedOuter, expectedHole });
                }

                Point[] expectedSimpleHole =
                {
                    new Point(3, 2), new Point(6, 3), new Point(5, 6), new Point(2, 5)
                };
                AssertContourSet(
                    source,
                    RetrievalModes.Tree,
                    ContourApproximationModes.ApproxSimple,
                    new[] { expectedOuter, expectedSimpleHole });
            }
        }

        private static void AssertContourPoints(
            Mat source,
            RetrievalModes retrievalMode,
            ContourApproximationModes approximationMode,
            Point[] expected)
        {
            AssertContourSet(source, retrievalMode, approximationMode, new[] { expected });
        }

        private static void AssertContourSet(
            Mat source,
            RetrievalModes retrievalMode,
            ContourApproximationModes approximationMode,
            Point[][] expected)
        {
            using (ContourTool tool = new ContourTool())
            {
                tool.SetProperty(new ContourToolProperty
                {
                    DetectMode = retrievalMode,
                    ApproximationModes = approximationMode,
                    MIN_AREA = 0,
                    MAX_AREA = int.MaxValue
                });

                using (VisionToolResult execution = tool.Execute(source))
                {
                    Require(execution.Success, "Contour compatibility fixture must execute successfully.");
                }

                Point[][] actual = tool.candidates
                    .Select(candidate => candidate.Drawing.Points
                        .Select(point => new Point((int)point.X, (int)point.Y))
                        .ToArray())
                    .ToArray();
                Require(actual.Length == expected.Length,
                    $"Contour count changed for {retrievalMode}/{approximationMode}.");
                for (int index = 0; index < expected.Length; index++)
                {
                    Require(actual[index].SequenceEqual(expected[index]),
                        $"Contour points changed at index {index} for {retrievalMode}/{approximationMode}.");
                }
            }
        }
    }
}

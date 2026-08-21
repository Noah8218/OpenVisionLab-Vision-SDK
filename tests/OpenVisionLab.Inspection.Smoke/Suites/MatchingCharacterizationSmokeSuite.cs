using OpenVisionLab.Vision2D;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Result;
using OpenVisionLab.Vision2D.Tool;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class MatchingCharacterizationSmokeSuite
    {
        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase("Matching single ROI publishes global coordinates", TestSingleRoiCoordinates);
            yield return new SmokeCase("Matching multi ROI preserves ROI and result order", TestMultiRoiOrder);
            yield return new SmokeCase("Matching normalized modes retain the same exact location", TestNormalizedMatchModes);
            yield return new SmokeCase("Matching coarse angle search retains the exhaustive result", TestCoarseAngleSearchParity);
            yield return new SmokeCase("Matching pyramid proposal retains the exhaustive result", TestPyramidProposalParity);
            yield return new SmokeCase("Matching scale search publishes the taught scale", TestScaleSearch);
            yield return new SmokeCase("Matching template replacement invalidates cached content deterministically", TestTemplateReplacementAndDeterminism);
        }

        private static void TestSingleRoiCoordinates()
        {
            using (Mat template = CreatePattern(28, 24, false))
            using (Mat source = CreateSource(180, 120, template, new Rect(91, 53, template.Width, template.Height)))
            using (MatchingTool tool = CreateTool(template, CreateProperty()))
            {
                MatchingToolProperty property = (MatchingToolProperty)tool.property;
                property.USE_ROI = true;
                property.CvROI = new Rect(70, 35, 90, 70);

                MatchingResult result = ExecuteOne(tool, source);
                RequireApproximately(result.Bounding.X, 91, 1.1, "Matching ROI X offset changed.");
                RequireApproximately(result.Bounding.Y, 53, 1.1, "Matching ROI Y offset changed.");
                Require(result.Index == 1, "Matching single ROI result index changed.");
            }
        }

        private static void TestMultiRoiOrder()
        {
            using (Mat template = CreatePattern(24, 20, false))
            using (Mat source = new Mat(new Size(220, 100), MatType.CV_8UC1, Scalar.All(17)))
            using (MatchingTool tool = CreateTool(template, CreateProperty()))
            {
                CopyPattern(source, template, new Rect(31, 37, template.Width, template.Height));
                CopyPattern(source, template, new Rect(161, 19, template.Width, template.Height));
                MatchingToolProperty property = (MatchingToolProperty)tool.property;
                property.USE_MULTI_ROI = true;
                property.CvROIS.Add(new Rect(0, 0, 100, 100));
                property.CvROIS.Add(new Rect(120, 0, 100, 100));
                property.NUM_MATCH = 1;

                VisionToolResult execution = tool.Execute(source);
                try
                {
                    Require(execution.Success && tool.results.Count == 2,
                        "Matching multi ROI must retain one result per ROI. " + execution.ErrorName + ": " + execution.Message);
                    Require(tool.results[0].Index == 1 && tool.results[1].Index == 2,
                        "Matching multi ROI result indexes changed.");
                    RequireApproximately(tool.results[0].Bounding.X, 31, 1.1, "First matching ROI order changed.");
                    RequireApproximately(tool.results[1].Bounding.X, 161, 1.1, "Second matching ROI order changed.");
                }
                finally
                {
                    execution.ResultImage?.Dispose();
                }
            }
        }

        private static void TestNormalizedMatchModes()
        {
            using (Mat template = CreatePattern(30, 26, false))
            using (Mat source = CreateSource(160, 110, template, new Rect(67, 42, template.Width, template.Height)))
            {
                TemplateMatchModes[] modes =
                {
                    TemplateMatchModes.CCoeffNormed,
                    TemplateMatchModes.CCorrNormed,
                    TemplateMatchModes.SqDiffNormed
                };

                foreach (TemplateMatchModes mode in modes)
                {
                    MatchingToolProperty property = CreateProperty();
                    property.MATCH_MODE = mode;
                    property.SCORE_MIN = 0.8;
                    using (MatchingTool tool = CreateTool(template, property))
                    {
                        MatchingResult result = ExecuteOne(tool, source);
                        RequireApproximately(result.Bounding.X, 67, 1.1, mode + " matching X changed.");
                        RequireApproximately(result.Bounding.Y, 42, 1.1, mode + " matching Y changed.");
                    }
                }
            }
        }

        private static void TestCoarseAngleSearchParity()
        {
            using (Mat template = CreatePattern(32, 28, false))
            using (Mat source = CreateSource(170, 120, template, new Rect(72, 44, template.Width, template.Height)))
            {
                MatchingToolProperty exhaustiveProperty = CreateProperty();
                exhaustiveProperty.USE_FIND_ANGLE = true;
                exhaustiveProperty.FIND_ANGLE_MIN = -6;
                exhaustiveProperty.FIND_ANGLE_MAX = 6;
                exhaustiveProperty.FIND_ANGLE = 2;
                MatchingToolProperty coarseProperty = CreateProperty();
                coarseProperty.USE_FIND_ANGLE = true;
                coarseProperty.FIND_ANGLE_MIN = -6;
                coarseProperty.FIND_ANGLE_MAX = 6;
                coarseProperty.FIND_ANGLE = 2;
                coarseProperty.USE_COARSE_TO_FINE_ANGLE_SEARCH = true;
                coarseProperty.COARSE_ANGLE_STEP = 4;
                coarseProperty.COARSE_ANGLE_TOP_K = 2;

                MatchingResult exhaustive = ExecuteOne(template, source, exhaustiveProperty);
                MatchingResult coarse = ExecuteOne(template, source, coarseProperty);
                RequireSamePose(coarse, exhaustive, 0.01, "Matching coarse angle search changed the exact-match result.");
            }
        }

        private static void TestPyramidProposalParity()
        {
            using (Mat template = CreatePattern(34, 30, false))
            using (Mat source = CreateSource(260, 180, template, new Rect(173, 107, template.Width, template.Height)))
            {
                MatchingToolProperty exhaustiveProperty = CreateProperty();
                MatchingToolProperty pyramidProperty = CreateProperty();
                pyramidProperty.USE_PYRAMID_POSITION_PROPOSAL = true;
                pyramidProperty.PYRAMID_POSITION_TOP_N = 6;
                pyramidProperty.PYRAMID_POSITION_MIN_SCORE = 0.6;

                MatchingResult exhaustive = ExecuteOne(template, source, exhaustiveProperty);
                MatchingResult pyramid = ExecuteOne(template, source, pyramidProperty);
                RequireSamePose(pyramid, exhaustive, 0.01, "Matching pyramid proposal changed the exact-match result.");
            }
        }

        private static void TestScaleSearch()
        {
            using (Mat template = CreatePattern(32, 24, false))
            using (Mat scaled = new Mat())
            {
                Cv2.Resize(template, scaled, new Size(40, 30), 0, 0, InterpolationFlags.Linear);
                using (Mat source = CreateSource(190, 130, scaled, new Rect(79, 51, scaled.Width, scaled.Height)))
                {
                    MatchingToolProperty property = CreateProperty();
                    property.USE_FIND_SCALE = true;
                    property.FIND_SCALE_MIN = 1.0;
                    property.FIND_SCALE_MAX = 1.5;
                    property.FIND_SCALE_STEP = 0.25;
                    MatchingResult result = ExecuteOne(template, source, property);
                    RequireApproximately(result.Scale, 1.25, 0.001, "Matching scale result changed.");
                    RequireApproximately(result.Bounding.X, 79, 1.1, "Scaled matching X changed.");
                    RequireApproximately(result.Bounding.Y, 51, 1.1, "Scaled matching Y changed.");
                }
            }
        }

        private static void TestTemplateReplacementAndDeterminism()
        {
            using (Mat firstTemplate = CreatePattern(184, 184, false))
            using (Mat secondTemplate = CreatePattern(184, 184, true))
            using (Mat source = new Mat(new Size(620, 250), MatType.CV_8UC1, Scalar.All(17)))
            using (MatchingTool tool = new MatchingTool())
            {
                CopyPattern(source, firstTemplate, new Rect(25, 31, firstTemplate.Width, firstTemplate.Height));
                CopyPattern(source, secondTemplate, new Rect(397, 29, secondTemplate.Width, secondTemplate.Height));
                MatchingToolProperty property = CreateProperty();
                property.USE_FIND_ANGLE = true;
                property.FIND_ANGLE_MIN = -2;
                property.FIND_ANGLE_MAX = 2;
                property.FIND_ANGLE = 2;
                tool.SetProperty(property);
                tool.SetTemplateImage(firstTemplate);
                MatchingResult first = ExecuteOne(tool, source);
                RequireApproximately(first.Bounding.X, 25, 1.1, "Initial cached template location changed.");

                tool.SetTemplateImage(secondTemplate);
                MatchingResult previous = null;
                for (int run = 0; run < 3; run++)
                {
                    MatchingResult current = ExecuteOne(tool, source);
                    RequireApproximately(current.Bounding.X, 397, 1.1, "Replaced template reused stale cached content.");
                    if (previous != null)
                    {
                        RequireSamePose(current, previous, 0.000001, "Repeated matching execution became non-deterministic.");
                    }

                    previous = current;
                }
            }
        }

        private static MatchingTool CreateTool(Mat template, MatchingToolProperty property)
        {
            MatchingTool tool = new MatchingTool();
            tool.SetProperty(property);
            tool.SetTemplateImage(template);
            return tool;
        }

        private static MatchingToolProperty CreateProperty()
        {
            return new MatchingToolProperty
            {
                USE_FIND_ANGLE = false,
                SCORE_MIN = 0.75,
                NUM_MATCH = 1
            };
        }

        private static MatchingResult ExecuteOne(Mat template, Mat source, MatchingToolProperty property)
        {
            using (MatchingTool tool = CreateTool(template, property))
            {
                return ExecuteOne(tool, source);
            }
        }

        private static MatchingResult ExecuteOne(MatchingTool tool, Mat source)
        {
            VisionToolResult execution = tool.Execute(source);
            try
            {
                Require(execution.Success && tool.results.Count > 0,
                    "Matching characterization execution failed. " + execution.ErrorName + ": " + execution.Message);
                return tool.results[0];
            }
            finally
            {
                execution.ResultImage?.Dispose();
            }
        }

        private static void RequireSamePose(MatchingResult actual, MatchingResult expected, double tolerance, string message)
        {
            RequireApproximately(actual.Center.X, expected.Center.X, tolerance, message + " CenterX");
            RequireApproximately(actual.Center.Y, expected.Center.Y, tolerance, message + " CenterY");
            RequireApproximately(actual.Angle, expected.Angle, tolerance, message + " Angle");
            RequireApproximately(actual.Scale, expected.Scale, tolerance, message + " Scale");
            RequireApproximately(actual.Score, expected.Score, tolerance, message + " Score");
        }

        private static Mat CreatePattern(int width, int height, bool alternate)
        {
            Mat pattern = new Mat(new Size(width, height), MatType.CV_8UC1, Scalar.All(alternate ? 37 : 23));
            Cv2.Rectangle(pattern, new Rect(width / 10, height / 8, width / 3, height / 4), Scalar.All(alternate ? 210 : 245), -1);
            Cv2.Circle(pattern, new Point(width * 3 / 4, height / 3), Math.Max(3, Math.Min(width, height) / 9), Scalar.All(alternate ? 245 : 150), -1);
            Cv2.Line(pattern, new Point(width / 5, height * 4 / 5), new Point(width * 9 / 10, height * 3 / 5), Scalar.All(alternate ? 90 : 220), Math.Max(2, width / 45));
            if (alternate)
            {
                Cv2.Line(pattern, new Point(width / 2, height / 10), new Point(width / 3, height * 9 / 10), Scalar.All(180), Math.Max(2, width / 50));
            }

            return pattern;
        }

        private static Mat CreateSource(int width, int height, Mat pattern, Rect target)
        {
            Mat source = new Mat(new Size(width, height), MatType.CV_8UC1, Scalar.All(17));
            CopyPattern(source, pattern, target);
            return source;
        }

        private static void CopyPattern(Mat source, Mat pattern, Rect target)
        {
            using (Mat destination = source.SubMat(target))
            {
                pattern.CopyTo(destination);
            }
        }
    }
}

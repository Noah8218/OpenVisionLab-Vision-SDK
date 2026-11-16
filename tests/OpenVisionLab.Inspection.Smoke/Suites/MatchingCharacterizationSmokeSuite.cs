using OpenVisionLab.Vision2D;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Result;
using OpenVisionLab.Vision2D.Tool;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class MatchingCharacterizationSmokeSuite
    {
        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase("Matching hardening drains faulted and canceled workers before releasing shared images", TestWorkerDrain);
            yield return new SmokeCase("Matching DEV01 exhaustive and coarse obey declared angle ranges", TestPoseGridRanges);
            yield return new SmokeCase("Matching DEV01 active grid validation budget and recovery", TestPoseGridValidation);
            yield return new SmokeCase("Matching DEV01 iterator observes cancellation between rounded poses", TestGridIteratorCancellation);
            yield return new SmokeCase("Matching single ROI publishes global coordinates", TestSingleRoiCoordinates);
            yield return new SmokeCase("Matching multi ROI preserves ROI and result order", TestMultiRoiOrder);
            yield return new SmokeCase("Matching normalized modes retain the same exact location", TestNormalizedMatchModes);
            yield return new SmokeCase("Matching coarse angle search retains the exhaustive result", TestCoarseAngleSearchParity);
            yield return new SmokeCase("Matching pyramid proposal retains the exhaustive result", TestPyramidProposalParity);
            yield return new SmokeCase("Matching scale search publishes the taught scale", TestScaleSearch);
            yield return new SmokeCase("Matching template replacement invalidates cached content deterministically", TestTemplateReplacementAndDeterminism);
        }

        private static void TestWorkerDrain()
        {
            foreach (bool cancellable in new[] { false, true })
            foreach (bool faultPlus in new[] { false, true })
            {
                using MatchingTool tool = new MatchingTool();
                using CancellationTokenSource cancellation = new CancellationTokenSource();
                if (cancellable)
                    typeof(OpenCvAlgorithmBase).GetField("executionCancellationToken", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(tool, cancellation.Token);
                object engine = typeof(MatchingTool).GetField("searchEngine", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(tool);
                MethodInfo join = engine.GetType().GetMethod("WaitForSearchTasks", BindingFlags.Instance | BindingFlags.NonPublic);
                using CountdownEvent entered = new CountdownEvent(2);
                using ManualResetEventSlim release = new ManualResetEventSlim();
                using ManualResetEventSlim joining = new ManualResetEventSlim();
                using ManualResetEventSlim returned = new ManualResetEventSlim();
                using Mat shared = new Mat(1, 1, MatType.CV_8UC1, Scalar.All(42));
                Action reader = () => { entered.Signal(); release.Wait(); Require(!shared.IsDisposed, "A reader outlived the shared image lease."); };
                Task first = Task.Run(reader);
                Task remaining = Task.Run(reader);
                Task fault = Task.FromException(new InvalidOperationException("authored worker fault"));
                Exception observed = null;
                Task waiter = null;
                try
                {
                    Require(entered.Wait(5000), "Controlled readers did not enter.");
                    waiter = Task.Run(() =>
                    {
                        joining.Set();
                        try { join.Invoke(engine, new object[] { first, faultPlus ? fault : remaining, faultPlus ? remaining : fault }); }
                        catch (TargetInvocationException exception) { observed = exception.InnerException; }
                        finally { shared.Dispose(); returned.Set(); }
                    });
                    Require(joining.Wait(5000), "Join did not start.");
                    if (cancellable) cancellation.Cancel();
                    Require(!returned.Wait(150), "Join returned on a fault/cancel while controlled readers still held the shared image.");
                }
                finally
                {
                    release.Set();
                    Task.WaitAll(first, remaining);
                    waiter?.GetAwaiter().GetResult();
                }
                Require(returned.IsSet && first.IsCompleted && remaining.IsCompleted && fault.Exception != null, "Every worker must terminate and its fault be observed.");
                Require(cancellable ? observed is OperationCanceledException : observed is AggregateException, "Caller cancellation priority or fault conversion changed.");
            }
            using MatchingTool single = new MatchingTool();
            object singleEngine = typeof(MatchingTool).GetField("searchEngine", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(single);
            singleEngine.GetType().GetMethod("WaitForSearchTasks", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(singleEngine, new object[] { Task.CompletedTask, null, null });
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

        private static void TestPoseGridRanges()
        {
            using Mat template = CreatePattern(32, 28, false);
            using Mat source = CreateSource(110, 85, template, new Rect(47, 31, template.Width, template.Height));
            foreach (bool edge in new[] { false, true })
            foreach (bool coarse in new[] { false, true })
            foreach (int optional in edge ? new[] { 0, 1, 2, 3 } : new[] { 0 })
            {
                using OpenCvAlgorithmBase tool = CreateGridTool(template, edge, out object settings, out List<MatchingResult> results);
                if (edge)
                {
                    ((EdgeBasedTemplateMatchingTool)tool).UsePoseRefinement = (optional & 1) != 0;
                    ((EdgeBasedTemplateMatchingTool)tool).UsePoseUniquenessValidation = (optional & 2) != 0;
                }
                SetGrid(settings, "USE_FIND_ANGLE", true);
                SetGrid(settings, "USE_COARSE_TO_FINE_ANGLE_SEARCH", coarse);
                SetGrid(settings, "COARSE_ANGLE_STEP", 4D);
                foreach ((int minimum, int maximum, double step) in new[] { (5, 10, 1D), (-10, -5, 1D), (-5, 5, 1D), (0, 0, 1D), (5, 10, 2D), (10, 5, 1D), (5, -5, 1D) })
                {
                    SetGrid(settings, "FIND_ANGLE_MIN", minimum); SetGrid(settings, "FIND_ANGLE_MAX", maximum); SetGrid(settings, "FIND_ANGLE", step);
                    using VisionToolResult run = tool.Execute(source);
                    Require((run.Success && results.Count > 0) || ((optional & 2) != 0 && run.ErrorCode == VisionToolErrorCode.MatchingAmbiguous && results.Count == 0),
                        $"Range edge={edge} coarse={coarse} optional={optional} {minimum}..{maximum}/{step}: " + run.ErrorName + ": " + run.Message + " " + run.Exception);
                    Require(results.All(result => result.Angle >= Math.Min(minimum, maximum) && result.Angle <= Math.Max(minimum, maximum)), "A stronger out-of-range zero pose must never win the search.");
                }
            }
        }

        private static void TestPoseGridValidation()
        {
            using Mat template = CreatePattern(32, 28, false);
            using Mat source = CreateSource(110, 85, template, new Rect(47, 31, template.Width, template.Height));
            foreach (bool edge in new[] { false, true })
            foreach (int optional in edge ? new[] { 0, 1, 2, 3 } : new[] { 0 })
            {
                using OpenCvAlgorithmBase tool = CreateGridTool(template, edge, out object settings, out _);
                if (edge) { ((EdgeBasedTemplateMatchingTool)tool).UsePoseRefinement = (optional & 1) != 0; ((EdgeBasedTemplateMatchingTool)tool).UsePoseUniquenessValidation = (optional & 2) != 0; }
                void Invalid(VisionToolErrorCode code)
                {
                    using VisionToolResult run = tool.Execute(source);
                    Require(!run.Success && run.ErrorCode == code, "Invalid active grid must fail before model allocation: expected " + code + ", got " + run.ErrorName);
                }
                SetGrid(settings, "USE_FIND_ANGLE", true);
                foreach (double step in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0D, -1D, 0.0000001D })
                {
                    SetGrid(settings, "FIND_ANGLE", step); Invalid(VisionToolErrorCode.MatchingInvalidAngleStep);
                }
                SetGrid(settings, "FIND_ANGLE", 1D); SetGrid(settings, "FIND_ANGLE_MIN", int.MaxValue); SetGrid(settings, "FIND_ANGLE_MAX", int.MaxValue);
                Invalid(VisionToolErrorCode.MatchingInvalidAngleStep);
                SetGrid(settings, "FIND_ANGLE_MIN", -10); SetGrid(settings, "FIND_ANGLE_MAX", 10);
                SetGrid(settings, "USE_COARSE_TO_FINE_ANGLE_SEARCH", true);
                foreach (double step in new[] { double.NaN, double.PositiveInfinity, 0D, -1D, 0.0000001D })
                {
                    SetGrid(settings, "COARSE_ANGLE_STEP", step); Invalid(VisionToolErrorCode.MatchingInvalidAngleStep);
                }
                SetGrid(settings, "USE_FIND_ANGLE", false); SetGrid(settings, "USE_FIND_SCALE", true);
                foreach (string field in new[] { "FIND_SCALE_MIN", "FIND_SCALE_MAX", "FIND_SCALE_STEP" })
                {
                    foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0D, -1D, 0.0000001D })
                    {
                        SetGrid(settings, "FIND_SCALE_MIN", 1D); SetGrid(settings, "FIND_SCALE_MAX", 1D); SetGrid(settings, "FIND_SCALE_STEP", 0.1D);
                        SetGrid(settings, field, value); Invalid(VisionToolErrorCode.MatchingInvalidScale);
                    }
                }
                SetGrid(settings, "FIND_SCALE_MIN", 1D); SetGrid(settings, "FIND_SCALE_MAX", 2D); SetGrid(settings, "FIND_SCALE_STEP", 0.01D);
                SetGrid(settings, "USE_FIND_ANGLE", true); SetGrid(settings, "USE_COARSE_TO_FINE_ANGLE_SEARCH", false);
                SetGrid(settings, "FIND_ANGLE_MIN", -100); SetGrid(settings, "FIND_ANGLE_MAX", 100); Invalid(VisionToolErrorCode.InvalidParameter);
                SetGrid(settings, "USE_FIND_SCALE", false); SetGrid(settings, "FIND_ANGLE", 0.000001D); Invalid(VisionToolErrorCode.InvalidParameter);
                // Disabled stale values do not gate execution; the same owner recovers.
                SetGrid(settings, "USE_FIND_ANGLE", false); SetGrid(settings, "FIND_ANGLE", double.NaN);
                using VisionToolResult recovered = tool.Execute(source);
                Require(recovered.Success, "Invalid-to-valid recovery must ignore disabled angle/scale/coarse grids.");
            }
        }

        private static OpenCvAlgorithmBase CreateGridTool(Mat template, bool edge, out object settings, out List<MatchingResult> results)
        {
            if (edge)
            {
                EdgeBasedTemplateMatchingTool tool = new EdgeBasedTemplateMatchingTool();
                EdgeBasedTemplateMatchingToolProperty property = new EdgeBasedTemplateMatchingToolProperty { SCORE_MIN = -1D, USE_DRAW_IMAGE = false, MAX_TEMPLATE_POINTS = 100 };
                tool.SetProperty(property); tool.SetTemplateImage(template); settings = property; results = tool.results; return tool;
            }
            MatchingTool imageTool = CreateTool(template, new MatchingToolProperty { SCORE_MIN = 0D, NUM_MATCH = 1, USE_FIND_ANGLE = false });
            settings = imageTool.property; results = imageTool.results; return imageTool;
        }

        private static void SetGrid(object settings, string name, object value) => settings.GetType().GetProperty(name).SetValue(settings, value);

        private static void TestGridIteratorCancellation()
        {
            Type grid = typeof(MatchingTool).Assembly.GetType("OpenVisionLab.Vision2D.Tool.MatchingPoseGrid", true);
            MethodInfo angles = grid.GetMethod("Angles", BindingFlags.Static | BindingFlags.NonPublic);
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            IEnumerable<double> values = (IEnumerable<double>)angles.Invoke(null, new object[] { 0D, 0.001D, 0.0000006D, cancellation.Token });
            using IEnumerator<double> iterator = values.GetEnumerator();
            Require(iterator.MoveNext(), "The bounded representable grid must emit a pose.");
            cancellation.Cancel();
            RequireThrows<OperationCanceledException>(() => iterator.MoveNext());

            using Mat template = CreatePattern(32, 28, false);
            using Mat source = CreateSource(110, 85, template, new Rect(47, 31, template.Width, template.Height));
            using OpenCvAlgorithmBase tool = CreateGridTool(template, true, out _, out _);
            object cache = tool.GetType().GetMethod("GetTemplateModelCache", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(tool, null);
            using CancellationTokenSource modelCancellation = new CancellationTokenSource();
            IEnumerable<double> modelAngles = (IEnumerable<double>)angles.Invoke(null, new object[] { -2D, 2D, 1D, modelCancellation.Token });
            IEnumerable<double> InterruptAfterOneModel()
            {
                foreach (double angle in modelAngles) { yield return angle; modelCancellation.Cancel(); }
            }
            double[] scales = { 1D };
            TargetInvocationException interrupted = RequireThrows<TargetInvocationException>(() => cache.GetType().GetMethod("GetSearchModels").Invoke(cache, new object[] { "bounded-cancellation", InterruptAfterOneModel(), scales }));
            Require(interrupted.InnerException is OperationCanceledException, "Eager model creation must observe grid cancellation after its first model.");
            object models = cache.GetType().GetField("rotatedModels", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(cache);
            Require((int)models.GetType().GetProperty("Count").GetValue(models) == 1, "The interrupted creation must allocate only the first controlled model.");
            using VisionToolResult recovered = tool.Execute(source);
            Require(recovered.Success && !source.IsDisposed && !template.IsDisposed, "The same model owner must recover without releasing caller input/template.");
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

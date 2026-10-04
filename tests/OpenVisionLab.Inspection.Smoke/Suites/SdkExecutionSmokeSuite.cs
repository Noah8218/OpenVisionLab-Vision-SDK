using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Result;
using OpenVisionLab.Vision2D.Tool;
using OpenVisionLab.Vision3D.FeatureExtraction;
using OpenCvSharp;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class SdkExecutionSmokeSuite
    {
        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase("SDK execution bounds validation cancellation across all surface owners and recovers", TestValidationCancellation);
            yield return new SmokeCase("SDK execution bounds inner scene centroid and available scans after cancellation", TestScanCancellation);
            yield return new SmokeCase("SDK execution observes cancellation on the final scene read instead of returning coverage", TestFinalSceneCancellation);
            yield return new SmokeCase("SDK execution isolates four independent 3D single multiple and refinement workers", TestIndependentSurfaceWorkers);
            yield return new SmokeCase("SDK execution isolates four independent native and edge matchers through failure cancellation and disposal", TestIndependentImageWorkers);
        }

        private static void TestValidationCancellation()
        {
            SurfaceMatchSample[] small = Model(5);
            SurfaceMatchSample[] large = Model(8193);
            foreach (var owner in SurfaceOwners())
            {
                using (CancellationTokenSource cancellation = new CancellationTokenSource())
                {
                    CancelOnReadSamples model = new CancelOnReadSamples(large, cancellation, 1);
                    RequireCanceled(() => owner.Execute(model, small, cancellation.Token), cancellation.Token);
                    Require(model.Reads - 1 <= 1023, owner.Name + " model validation passed a 1024-iteration checkpoint.");
                }
                using (CancellationTokenSource cancellation = new CancellationTokenSource())
                {
                    CancelOnReadSamples scene = new CancelOnReadSamples(large, cancellation, 1);
                    RequireCanceled(() => owner.Execute(small, scene, cancellation.Token), cancellation.Token);
                    Require(scene.Reads - 1 <= 1023, owner.Name + " scene validation passed a 1024-iteration checkpoint.");
                }
                using (CancellationTokenSource cancellation = new CancellationTokenSource())
                {
                    CancelOnReadSamples last = new CancelOnReadSamples(Model(1025), cancellation, 1025);
                    RequireCanceled(() => owner.Execute(last, small, cancellation.Token), cancellation.Token);
                    Require(last.Reads == 1025, owner.Name + " must observe cancellation at validation completion.");
                }
                using (CancellationTokenSource cancellation = new CancellationTokenSource())
                {
                    cancellation.Cancel();
                    CancelOnReadSamples untouched = new CancelOnReadSamples(small, cancellation, int.MaxValue);
                    RequireCanceled(() => owner.Execute(untouched, small, cancellation.Token), cancellation.Token);
                    Require(untouched.Reads == 0, owner.Name + " must not read samples after pre-cancellation.");
                }
                Require(!owner.Execute(Array.Empty<SurfaceMatchSample>(), small, CancellationToken.None), owner.Name + " must retain typed invalid-input failure.");
                Require(owner.Execute(small, small, CancellationToken.None), owner.Name + " must recover on the same instance after invalid and canceled inputs.");
            }
        }

        private static IEnumerable<(string Name, Func<IReadOnlyList<SurfaceMatchSample>, IReadOnlyList<SurfaceMatchSample>, CancellationToken, bool> Execute)> SurfaceOwners()
        {
            DeterministicSurfaceCoverageTool coverage = new DeterministicSurfaceCoverageTool();
            DeterministicRigidSurfacePoseSearchTool search = new DeterministicRigidSurfacePoseSearchTool();
            DeterministicMultipleSurfaceMatchTool multiple = new DeterministicMultipleSurfaceMatchTool();
            DeterministicSurfacePoseRefinementTool refinement = new DeterministicSurfacePoseRefinementTool();
            DeterministicRigidSurfacePoseSearchOptions options = Options();
            yield return ("coverage", (model, scene, token) => coverage.Execute(model, scene, Identity(), 0.5, token).Success);
            yield return ("search", (model, scene, token) => search.Execute(model, scene, options, token).Success);
            yield return ("multiple", (model, scene, token) => multiple.Execute(model, scene, new DeterministicMultipleSurfaceMatchOptions
            {
                PoseSearchOptions = options, MaximumMatchCount = 1, MaximumExpandedCandidateCount = 100000
            }, token).Success);
            yield return ("refinement", (model, scene, token) => refinement.Execute(model, scene, Identity(), options, token).Success);
        }

        private static void TestScanCancellation()
        {
            const int count = 8193;
            SurfaceMatchSample[] large = Model(count);
            SurfaceMatchSample[] small = Model(5);
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                CancelOnReadSamples scene = new CancelOnReadSamples(large, cancellation, count + 1);
                RequireCanceled(() => new DeterministicSurfaceCoverageTool().Execute(small, scene, Identity(), 0.5, cancellation.Token), cancellation.Token);
                Require(scene.Reads - (count + 1) <= 1023, "The inner scene scan must observe cancellation within 1024 iterations.");
            }
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                CancelOnReadSamples model = new CancelOnReadSamples(large, cancellation, count + 1);
                RequireCanceled(() => new DeterministicRigidSurfacePoseSearchTool().Execute(model, large, Options(), cancellation.Token), cancellation.Token);
                Require(model.Reads - (count + 1) <= 3071, "Centroid permits three sample getters per iteration, bounded by 1024 iterations.");
            }
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                CancelOnReadSamples scene = new CancelOnReadSamples(large, cancellation, count + 1);
                RequireCanceled(() => new DeterministicMultipleSurfaceMatchTool().Execute(small, scene, new DeterministicMultipleSurfaceMatchOptions
                {
                    PoseSearchOptions = Options(), MaximumMatchCount = 1, MaximumExpandedCandidateCount = 100000
                }, cancellation.Token), cancellation.Token);
                Require(scene.Reads - (count + 1) <= 2047, "Available-scene construction permits two sample getters per iteration, bounded by 1024 iterations.");
            }
        }

        private static void TestFinalSceneCancellation()
        {
            const int count = 1025;
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                CancelOnReadSamples scene = new CancelOnReadSamples(Model(count), cancellation, count * 2);
                RequireCanceled(() => new DeterministicSurfaceCoverageTool().Execute(Model(1), scene, Identity(), 0.5, cancellation.Token), cancellation.Token);
                Require(scene.Reads == count * 2, "Cancellation at the final scene getter must be observed before successful coverage is returned.");
            }
        }

        private static void TestIndependentSurfaceWorkers()
        {
            RunIndependentWorkers(worker =>
            {
                SurfaceMatchSample[] model = Model(64);
                double yaw = 0.13 + worker * 0.02;
                double tx = 4 + worker;
                SurfaceMatchSample[] scene = Transform(model, yaw, tx);
                SurfaceMatchSample[] originalModel = (SurfaceMatchSample[])model.Clone();
                SurfaceMatchSample[] originalScene = (SurfaceMatchSample[])scene.Clone();
                DeterministicRigidSurfacePoseSearchTool search = new DeterministicRigidSurfacePoseSearchTool();
                DeterministicSurfacePoseRefinementTool refinement = new DeterministicSurfacePoseRefinementTool();
                DeterministicMultipleSurfaceMatchTool multiple = new DeterministicMultipleSurfaceMatchTool();
                DeterministicRigidSurfacePoseSearchOptions options = Options(64, true);
                if (worker == 0)
                {
                    using (CancellationTokenSource cancellation = new CancellationTokenSource())
                    {
                        CancelOnReadSamples canceled = new CancelOnReadSamples(scene, cancellation, 1);
                        RequireCanceled(() => search.Execute(model, canceled, options, cancellation.Token), cancellation.Token);
                    }
                }
                DeterministicRigidSurfacePoseSearchResult retained = null;
                for (int repeat = 0; repeat < 3; repeat++)
                {
                    DeterministicRigidSurfacePoseSearchResult result = search.Execute(model, scene, options);
                    Require(result.Success && result.Matched && result.RefinementEvidence.Improved && result.EvaluatedCandidateCount == 2,
                        "Each independent search must recover its own off-grid pose and keep its own candidate count.");
                    RequirePose(result.Pose, model, scene);
                    Require(result.Coverage.MatchedModelSampleCount == model.Length && result.RefinementEvidence.MaximumIterations == 10,
                        "Worker support or option snapshots leaked.");
                    SurfacePoseRefinementResult direct = refinement.Execute(model, scene, Translation(tx), options);
                    Require(direct.Success && direct.Improved, "Each independent direct refiner must improve its seed.");
                    RequirePose(direct.Pose, model, scene);
                    if (retained != null) RequireApproximately(result.Pose.M21, retained.Pose.M21, 0, "Identical per-worker input changed its result.");
                    retained = result;
                }

                SurfaceMatchSample[] multipleModel = Model(5);
                SurfaceMatchSample[] multipleScene = Transform(multipleModel, 0, tx).Concat(Transform(multipleModel, 0, tx + 10))
                    .Select((sample, index) => new SurfaceMatchSample(index, sample.Position)).ToArray();
                DeterministicMultipleSurfaceMatchResult instances = multiple.Execute(multipleModel, multipleScene, new DeterministicMultipleSurfaceMatchOptions
                {
                    PoseSearchOptions = Options(), MaximumMatchCount = 2, MaximumExpandedCandidateCount = 1000
                });
                Require(instances.Success && instances.Matches.Count == 2, "Independent multiple search must find both authored instances.");
                Require(instances.Matches.SelectMany(match => match.Coverage.Matches).Select(match => match.SceneSampleOrder).Distinct().Count() == 10,
                    "Independent instances must not share or lose original scene evidence.");
                for (int index = 0; index < 2; index++)
                {
                    RequireApproximately(instances.Matches[index].Pose.TranslationX, tx + index * 10, 1e-9, "Multiple worker translation leaked.");
                    Require(instances.Matches[index].RefinementEvidence != null && instances.Matches[index].Coverage.MatchedModelSampleCount == 5,
                        "Multiple worker refinement evidence was lost.");
                }
                options.RefinementOptions.MaximumIterations = 1;
                Require(retained.RefinementEvidence.MaximumIterations == 10, "Editing one worker option changed its retained result.");
                RequirePose(retained.Pose, model, scene);
                Require(model.SequenceEqual(originalModel) && scene.SequenceEqual(originalScene), "Surface workers changed caller-owned immutable samples or their order.");
            });
        }

        private static void TestIndependentImageWorkers()
        {
            RunIndependentWorkers(worker =>
            {
                using (Mat template = Template())
                using (Mat source = new Mat(128, 192, MatType.CV_8UC1, Scalar.All(24)))
                using (Mat recoveredSource = new Mat(source.Size(), MatType.CV_8UC1, Scalar.All(24)))
                using (Mat blank = new Mat(source.Size(), MatType.CV_8UC1, Scalar.All(24)))
                {
                    Rect first = new Rect(21 + worker * 7, 19 + worker * 3, 64, 64);
                    Rect recovered = new Rect(91 - worker * 5, 43 - worker * 2, 64, 64);
                    using (Mat region = new Mat(source, first)) template.CopyTo(region);
                    using (Mat region = new Mat(recoveredSource, recovered)) template.CopyTo(region);
                    using Mat originalSource = source.Clone();
                    using Mat originalRecoveredSource = recoveredSource.Clone();
                    using Mat originalTemplate = template.Clone();
                    for (int matcherIndex = 0; matcherIndex < 2; matcherIndex++)
                    {
                        VisionToolResult retained = null;
                        try
                        {
                            using (OpenCvAlgorithmBase tool = Matcher(template, matcherIndex == 1))
                            {
                                retained = tool.Execute(source);
                                RequireImageMatch(tool, retained, first);
                                using (VisionToolResult noMatch = tool.Execute(blank))
                                {
                                    Require(!noMatch.Success && noMatch.ErrorCode == VisionToolErrorCode.MatchingNoResult,
                                        "Each independent matcher must reject its own blank input.");
                                }
                                if (worker == 0)
                                {
                                    using (CancellationTokenSource cancellation = new CancellationTokenSource())
                                    {
                                        cancellation.Cancel();
                                        RequireCanceled(() => ((ICancellableVisionTool)tool).Execute(recoveredSource, cancellation.Token), cancellation.Token);
                                    }
                                }
                                using (VisionToolResult result = tool.Execute(recoveredSource))
                                {
                                    RequireImageMatch(tool, result, recovered);
                                    Require(result.ResultImage.Data != retained.ResultImage.Data && result.ResultImage.Data != recoveredSource.Data,
                                        "Native result snapshots must have independent lifetimes.");
                                }
                            }
                            Require(retained.ResultImage != null && !retained.ResultImage.IsDisposed && !retained.ResultImage.Empty(),
                                "A retained result must survive later failures, result disposal, and Tool disposal.");
                        }
                        finally
                        {
                            retained?.Dispose();
                        }
                    }
                    Require(!source.IsDisposed && !recoveredSource.IsDisposed && !template.IsDisposed,
                        "Worker Tool and result disposal must preserve caller-owned images.");
                    RequireApproximately(Cv2.Norm(source, originalSource, NormTypes.L1), 0, 0, "Caller source pixels were changed.");
                    RequireApproximately(Cv2.Norm(recoveredSource, originalRecoveredSource, NormTypes.L1), 0, 0, "Caller recovery source pixels were changed.");
                    RequireApproximately(Cv2.Norm(template, originalTemplate, NormTypes.L1), 0, 0, "Caller template pixels were changed.");
                }
            });
        }

        private static void RunIndependentWorkers(Action<int> execute)
        {
            using (Barrier barrier = new Barrier(4))
            {
                Task[] workers = Enumerable.Range(0, 4).Select(worker => Task.Factory.StartNew(() =>
                {
                    Require(barrier.SignalAndWait(TimeSpan.FromSeconds(10)), "Independent worker start synchronization timed out.");
                    execute(worker);
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
                Task.WaitAll(workers);
            }
        }

        private static void RequireCanceled(Action execute, CancellationToken token)
        {
            OperationCanceledException exception = RequireThrows<OperationCanceledException>(execute);
            Require(exception.CancellationToken == token, "Cancellation must retain the caller's token.");
        }

        private static void RequirePose(RigidSurfacePose pose, SurfaceMatchSample[] model, SurfaceMatchSample[] scene)
        {
            Require(pose != null && pose.IsRigid(1e-9), "A finite proper-rigid pose is required.");
            for (int index = 0; index < model.Length; index++)
            {
                ThreeDPoint actual = pose.Transform(model[index].Position);
                ThreeDPoint expected = scene[index].Position;
                RequireApproximately(actual.X, expected.X, 1e-9, "Independent worker X truth changed.");
                RequireApproximately(actual.Y, expected.Y, 1e-9, "Independent worker Y truth changed.");
                RequireApproximately(actual.Z, expected.Z, 1e-9, "Independent worker Z truth changed.");
            }
        }

        private static SurfaceMatchSample[] Model(int count)
        {
            ThreeDPoint[] first = { new ThreeDPoint(0, 0, 0), new ThreeDPoint(3, 0, 0), new ThreeDPoint(0, 4, 0),
                new ThreeDPoint(0, 0, 2), new ThreeDPoint(2, 1, 3) };
            return Enumerable.Range(0, count).Select(index => new SurfaceMatchSample(index, index < first.Length ? first[index]
                : new ThreeDPoint(10 + index % 8 * 3, 10 + index / 8 % 8 * 4, 2 + index / 64 * 5))).ToArray();
        }

        private static SurfaceMatchSample[] Transform(SurfaceMatchSample[] model, double yaw, double tx)
        {
            double radians = yaw * Math.PI / 180;
            return model.Select(sample => new SurfaceMatchSample(sample.Order, new ThreeDPoint(
                sample.Position.X * Math.Cos(radians) - sample.Position.Y * Math.Sin(radians) + tx,
                sample.Position.X * Math.Sin(radians) + sample.Position.Y * Math.Cos(radians) - 3, sample.Position.Z + 2))).ToArray();
        }

        private static RigidSurfacePose Identity() => new RigidSurfacePose(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0);

        private static RigidSurfacePose Translation(double tx) => new RigidSurfacePose(1, 0, 0, 0, 1, 0, 0, 0, 1, tx, -3, 2);

        private static DeterministicRigidSurfacePoseSearchOptions Options(int support = 5, bool rotate = false) => new DeterministicRigidSurfacePoseSearchOptions
        {
            MinimumRotationXDegrees = 0, MaximumRotationXDegrees = 0, RotationStepXDegrees = 1,
            MinimumRotationYDegrees = 0, MaximumRotationYDegrees = 0, RotationStepYDegrees = 1,
            MinimumRotationZDegrees = 0, MaximumRotationZDegrees = rotate ? 1 : 0, RotationStepZDegrees = 1,
            MinimumTranslationX = -100000, MaximumTranslationX = 100000, MinimumTranslationY = -100000, MaximumTranslationY = 100000,
            MinimumTranslationZ = -100000, MaximumTranslationZ = 100000, MaximumCorrespondenceDistance = 0.5,
            MinimumMatchedSampleCount = support, MaximumCandidateCount = 2, RefinementOptions = new SurfacePoseRefinementOptions()
        };

        private static Mat Template()
        {
            Mat template = new Mat(64, 64, MatType.CV_8UC1, Scalar.All(24));
            Cv2.Rectangle(template, new Rect(8, 8, 46, 46), Scalar.All(205), 3);
            Cv2.Line(template, new Point(13, 49), new Point(48, 14), Scalar.All(245), 3, LineTypes.AntiAlias);
            Cv2.Circle(template, new Point(42, 42), 6, Scalar.All(90), -1, LineTypes.AntiAlias);
            return template;
        }

        private static OpenCvAlgorithmBase Matcher(Mat template, bool edge)
        {
            if (!edge)
            {
                MatchingTool tool = new MatchingTool();
                tool.SetProperty(new MatchingToolProperty { USE_FIND_ANGLE = false, NUM_MATCH = 1, SCORE_MIN = 0.9 });
                tool.SetTemplateImage(template);
                return tool;
            }
            EdgeBasedTemplateMatchingTool matcher = new EdgeBasedTemplateMatchingTool();
            matcher.SetProperty(new EdgeBasedTemplateMatchingToolProperty
            {
                USE_FIND_ANGLE = false, USE_UNIQUE_MATCH_VALIDATION = true, SCORE_MIN = 0.5, SEARCH_STEP = 1,
                BlockSize = 11, ADAPTIVE_THRESHOLD = 5, Weight = 2, CANNY_HIGH = 100,
                MAX_TEMPLATE_POINTS = 500, MIN_GRADIENT_MAGNITUDE = 5, USE_POSITION_REFINE = true
            });
            matcher.SetTemplateImage(template);
            return matcher;
        }

        private static void RequireImageMatch(OpenCvAlgorithmBase tool, VisionToolResult result, Rect expected)
        {
            List<MatchingResult> matches = tool is MatchingTool native ? native.results : ((EdgeBasedTemplateMatchingTool)tool).results;
            Require(result.Success && matches.Count == 1, "Independent image worker must return exactly one authored location: " + result.Message);
            RequireApproximately(matches[0].Bounding.X, expected.X, 1.1, "Worker image X location leaked.");
            RequireApproximately(matches[0].Bounding.Y, expected.Y, 1.1, "Worker image Y location leaked.");
        }

        private sealed class CancelOnReadSamples : IReadOnlyList<SurfaceMatchSample>
        {
            private readonly SurfaceMatchSample[] samples;
            private readonly CancellationTokenSource cancellation;
            private readonly int cancelRead;

            internal CancelOnReadSamples(SurfaceMatchSample[] samples, CancellationTokenSource cancellation, int cancelRead)
            {
                this.samples = samples;
                this.cancellation = cancellation;
                this.cancelRead = cancelRead;
            }

            internal int Reads { get; private set; }
            public int Count => samples.Length;
            public SurfaceMatchSample this[int index]
            {
                get
                {
                    if (++Reads == cancelRead) cancellation.Cancel();
                    return samples[index];
                }
            }

            public IEnumerator<SurfaceMatchSample> GetEnumerator() => ((IEnumerable<SurfaceMatchSample>)samples).GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Globalization;
using OpenCvSharp;
using OpenVisionLab.Vision2D;
using OpenVisionLab.Vision2D.Result;
using OpenVisionLab.Vision2D.Tool;
using OpenVisionLab.Vision3D.Inspection;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using OpenVisionLab.Vision3D.FeatureExtraction;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class SurfaceModelWorkflowSmokeSuite
    {
        private static readonly int[] IndependentSampleOrders = { 10, 30, 50 };
        private static readonly System.Text.Json.JsonSerializerOptions WorkflowReportJsonOptions = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase("Model workflow preparation preserves finite extreme centroids and normal arithmetic", TestFiniteCentroids);
            yield return new SmokeCase("Model workflow preparation observes entry validation final-read cancellation and recovery", TestPreparationCancellation);
            yield return new SmokeCase("Model workflow key-point extraction observes single-point validation copy cancellation and recovery", TestKeyPointCancellation);
            yield return new SmokeCase("Model artifacts preserve independent bytes learned samples keypoints and search through a memory round trip", TestArtifactRoundTrip);
            yield return new SmokeCase("Model artifacts reject corrupt versions malformed records non-finite values and oversized input with recovery", TestArtifactMalformed);
            yield return new SmokeCase("Model artifacts enforce metadata snapshot cancellation and lifetime boundaries", TestArtifactBoundaryAndCancellation);
            yield return new SmokeCase("Model qualification connects restored full XYZ partial dense unit and 2D ROI workflows", TestWorkflowQualification);
        }

        private static void TestFiniteCentroids()
        {
            DeterministicSurfaceModelPreparationTool tool = new DeterministicSurfaceModelPreparationTool();
            ThreeDPoint[] normals = Enumerable.Repeat(new ThreeDPoint(1, 0, 0), 3).ToArray();
            SurfaceModelTriangleInput[] triangles = { new SurfaceModelTriangleInput(0, 1, 2) };
            foreach (double value in new[] { 1e308, double.MaxValue, -double.MaxValue, double.Epsilon, 1.0 })
            {
                ThreeDPoint[] points = { new ThreeDPoint(value, 0, 0), new ThreeDPoint(value, 1, 0), new ThreeDPoint(value, 0, 1) };
                DeterministicSurfaceModelPreparationResult result = tool.Execute(points, triangles, normals, new DeterministicSurfaceModelPreparationOptions { MaximumSampleCount = 1 });
                Require(result.Success && result.Samples.Count == 1, result.Message);
                PreparedSurfaceModelSample sample = result.Samples[0];
                Require(sample.Position.X == value, "Finite equal-coordinate mean was not preserved.");
                RequireApproximately(sample.Position.Y, 1.0 / 3.0, 0, "Normal Y arithmetic changed.");
                RequireApproximately(sample.Position.Z, 1.0 / 3.0, 0, "Normal Z arithmetic changed.");
                Require(sample.Normal.X == 1 && sample.Normal.Y == 0 && sample.Normal.Z == 0, "Declared-normal float arithmetic changed.");
            }

            ThreeDPoint[] mixed = { new ThreeDPoint(double.MaxValue, 0, 0), new ThreeDPoint(double.MaxValue, 1, 0), new ThreeDPoint(-double.MaxValue, 0, 1) };
            DeterministicSurfaceModelPreparationResult signed = tool.Execute(mixed, triangles, normals, new DeterministicSurfaceModelPreparationOptions { MaximumSampleCount = 1 });
            Require(signed.Success, signed.Message);
            RequireApproximately(signed.Samples[0].Position.X / double.MaxValue, 1.0 / 3.0, 2e-16, "Overflowing signed sum lost the independent mean.");
        }

        private static void TestPreparationCancellation()
        {
            DeterministicSurfaceModelPreparationTool tool = new DeterministicSurfaceModelPreparationTool();
            ThreeDPoint[] points = { new ThreeDPoint(0, 0, 0), new ThreeDPoint(0, 1, 0), new ThreeDPoint(0, 0, 1) };
            ThreeDPoint[] normals = Enumerable.Repeat(new ThreeDPoint(1, 0, 0), 3).ToArray();
            SurfaceModelTriangleInput[] triangles = { new SurfaceModelTriangleInput(0, 1, 2) };
            DeterministicSurfaceModelPreparationOptions options = new DeterministicSurfaceModelPreparationOptions { MaximumSampleCount = 1 };
            using (CancellationTokenSource canceled = new CancellationTokenSource())
            {
                canceled.Cancel();
                ExpectCanceled(() => tool.Execute(points, triangles, normals, options, canceled.Token), canceled.Token);
            }

            foreach (int cancelRead in new[] { 4, 9 })
            {
                using (CancellationTokenSource canceled = new CancellationTokenSource())
                {
                    CancelOnReadList<ThreeDPoint> source = new CancelOnReadList<ThreeDPoint>(normals, canceled, cancelRead);
                    ExpectCanceled(() => tool.Execute(points, triangles, source, options, canceled.Token), canceled.Token);
                }
            }

            Require(tool.Execute(points, triangles, normals, options).Success, "Preparation did not recover after cancellation.");
        }

        private static void TestKeyPointCancellation()
        {
            DeterministicModelKeyPointExtractionTool tool = new DeterministicModelKeyPointExtractionTool();
            ModelKeyPointInput[] samples = Enumerable.Range(0, 4).Select(i => new ModelKeyPointInput(i, new ThreeDPoint(i, i * i, i % 2), new ThreeDPoint(0, 0, 1))).ToArray();
            using (CancellationTokenSource canceled = new CancellationTokenSource())
            {
                canceled.Cancel();
                foreach (int count in new[] { 1, 4 })
                foreach (int maximum in new[] { 1, 3 })
                    ExpectCanceled(() => tool.Execute(samples.Take(count).ToArray(), new DeterministicModelKeyPointExtractionOptions { MaximumKeyPointCount = maximum }, canceled.Token), canceled.Token);
            }

            foreach (int cancelRead in new[] { 4, 5, 8 })
            {
                using (CancellationTokenSource canceled = new CancellationTokenSource())
                {
                    CancelOnReadList<ModelKeyPointInput> source = new CancelOnReadList<ModelKeyPointInput>(samples, canceled, cancelRead);
                    ExpectCanceled(() => tool.Execute(source, new DeterministicModelKeyPointExtractionOptions { MaximumKeyPointCount = 1 }, canceled.Token), canceled.Token);
                }
            }

            DeterministicModelKeyPointExtractionResult recovered = tool.Execute(samples, new DeterministicModelKeyPointExtractionOptions { MaximumKeyPointCount = 2 });
            Require(recovered.Success && recovered.KeyPoints.Count == 2, "Key-point extraction did not recover after cancellation.");
        }

        internal static SurfaceModelArtifact LearnModel(int count, double unitScale = 1D)
        {
            List<ThreeDPoint> points = new List<ThreeDPoint>();
            List<ThreeDPoint> normals = new List<ThreeDPoint>();
            List<SurfaceModelTriangleInput> triangles = new List<SurfaceModelTriangleInput>();
            for (int i = 0; i < count; i++)
            {
                double x = ((i % 7) * 1.7D + 0.07D * ((i * i) % 13)) * unitScale;
                double y = ((i / 7) * 1.1D + 0.11D * (i % 3)) * unitScale;
                double z = (0.17D * ((i * 5) % 11) + 0.03D * i) * unitScale;
                double side = 0.02D * unitScale;
                int offset = points.Count;
                points.Add(new ThreeDPoint(x - side, y, z));
                points.Add(new ThreeDPoint(x, y + side, z));
                points.Add(new ThreeDPoint(x + side, y - side, z));
                for (int corner = 0; corner < 3; corner++) normals.Add(new ThreeDPoint(0, 0, 1));
                triangles.Add(new SurfaceModelTriangleInput(offset, offset + 1, offset + 2));
            }
            DeterministicSurfaceModelPreparationResult prepared = new DeterministicSurfaceModelPreparationTool().Execute(points, triangles, normals,
                new DeterministicSurfaceModelPreparationOptions { MaximumSampleCount = count });
            Require(prepared.Success, prepared.Message);
            DeterministicModelKeyPointExtractionResult keys = new DeterministicModelKeyPointExtractionTool().Execute(
                prepared.Samples.Select(sample => new ModelKeyPointInput(sample.Order, sample.Position, sample.Normal)).ToArray(),
                new DeterministicModelKeyPointExtractionOptions { MaximumKeyPointCount = Math.Min(8, count) });
            Require(keys.Success, keys.Message);
            return new SurfaceModelArtifact("synthetic/workflow/학습-" + count, unitScale == 1D ? "mm" : "m", "model-local/full-XYZ", prepared.Samples, keys.KeyPoints);
        }

        private static void TestArtifactRoundTrip()
        {
            SurfaceModelArtifact original = LearnModel(8);
            byte[] encoded = SurfaceModelArtifactCodec.Encode(original);
            SurfaceModelArtifact restored = SurfaceModelArtifactCodec.Decode(encoded);
            Require(restored.SourceId == original.SourceId && restored.Unit == original.Unit && restored.CoordinateFrame == original.CoordinateFrame
                && restored.PreparationSemantics == original.PreparationSemantics && restored.KeyPointSemantics == original.KeyPointSemantics, "Model metadata and semantic identity were not preserved.");
            Require(encoded.SequenceEqual(SurfaceModelArtifactCodec.Encode(restored)), "Restored model bytes must be deterministic and exact.");
            for (int i = 0; i < original.Samples.Count; i++)
            {
                PreparedSurfaceModelSample first = original.Samples[i];
                PreparedSurfaceModelSample last = restored.Samples[i];
                Require(first.Order == last.Order && first.SourceTriangleIndex == last.SourceTriangleIndex && SamePoint(first.Position, last.Position)
                    && SamePoint(first.Normal, last.Normal), "A prepared sample lost its original double values or source identity.");
            }
            for (int i = 0; i < original.KeyPoints.Count; i++)
            {
                ExtractedModelKeyPoint first = original.KeyPoints[i];
                ExtractedModelKeyPoint last = restored.KeyPoints[i];
                Require(first.Order == last.Order && first.SourceSampleOrder == last.SourceSampleOrder && first.NearestSelectedDistance == last.NearestSelectedDistance
                    && SamePoint(first.Position, last.Position) && SamePoint(first.Normal, last.Normal), "A learned key point lost its correspondence or selection distance.");
            }
            SurfaceMatchSample[] scene = original.Samples.Select((sample, i) => new SurfaceMatchSample(i, new ThreeDPoint(sample.Position.X + 4, sample.Position.Y - 3, sample.Position.Z + 2))).ToArray();
            DeterministicRigidSurfacePoseSearchOptions options = new DeterministicRigidSurfacePoseSearchOptions
            {
                MinimumTranslationX = -10, MaximumTranslationX = 10, MinimumTranslationY = -10, MaximumTranslationY = 10,
                MinimumTranslationZ = -10, MaximumTranslationZ = 10, MaximumCorrespondenceDistance = 1e-8, MinimumMatchedSampleCount = 8,
                RotationStepXDegrees = 1, RotationStepYDegrees = 1, RotationStepZDegrees = 1,
                MaximumCandidateCount = 17, UseSamplePairTranslationCandidates = true,
                ProposalOptions = new SurfacePoseProposalOptions { MaximumModelAnchorCount = 4, MaximumSceneAnchorCount = 4 }
            };
            DeterministicRigidSurfacePoseSearchTool tool = new DeterministicRigidSurfacePoseSearchTool();
            DeterministicRigidSurfacePoseSearchResult before = tool.Execute(original.Samples.Select((sample, i) => new SurfaceMatchSample(i, sample.Position)).ToArray(), scene, options);
            DeterministicRigidSurfacePoseSearchResult after = tool.Execute(restored.Samples.Select((sample, i) => new SurfaceMatchSample(i, sample.Position)).ToArray(), scene, options);
            Require(before.Success && before.Matched && after.Success && after.Matched && before.Pose.TranslationX == after.Pose.TranslationX
                && before.Pose.TranslationY == after.Pose.TranslationY && before.Pose.TranslationZ == after.Pose.TranslationZ
                && before.Coverage.Matches.Select(pair => pair.SceneSampleOrder).SequenceEqual(after.Coverage.Matches.Select(pair => pair.SceneSampleOrder)), "Model restoration must preserve final search results and original scene IDs: " + before.Message + " / " + after.Message);
            PreparedSurfaceModelSample[] samples = original.Samples.ToArray();
            SurfaceModelArtifact snapshot = new SurfaceModelArtifact("snapshot", "mm", "frame", samples, original.KeyPoints);
            samples[0] = samples[1];
            Require(snapshot.Samples[0].Order == 0, "Model snapshot must own its collection state.");
            RequireThrows<NotSupportedException>(() => ((IList<PreparedSurfaceModelSample>)snapshot.Samples)[0] = samples[1]);
            encoded[0] ^= 255;
            Require(restored.Samples[0].Order == 0, "Decoding must not retain mutable source byte storage.");
        }

        private static bool SamePoint(ThreeDPoint first, ThreeDPoint second) => first.X == second.X && first.Y == second.Y && first.Z == second.Z;

        private static byte[] IndependentArtifactFixture()
        {
            using MemoryStream stream = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter(stream, new UTF8Encoding(false, true), true);
            writer.Write(0x4D53564F);
            writer.Write(1);
            foreach (string text in new[] { "oracle-fixture", "mm", "frame", "deterministic-triangle-centroid-even-index-v1", "deterministic-farthest-model-sample-v1" })
            {
                byte[] value = Encoding.UTF8.GetBytes(text);
                writer.Write(value.Length);
                writer.Write(value);
            }
            writer.Write(3);
            writer.Write(1);
            for (int i = 0; i < 3; i++)
            {
                writer.Write(10 + i * 20);
                writer.Write(i * 2);
                foreach (double value in new[] { (double)i, (double)i * i, -0.5D * i, 0D, 0D, 1D }) writer.Write(value);
            }
            writer.Write(0);
            writer.Write(10);
            foreach (double value in new[] { 0D, 0D, 0D, 0D, 0D, 1D, 0D }) writer.Write(value);
            writer.Write(SHA256.HashData(stream.ToArray()));
            return stream.ToArray();
        }

        private static byte[] MutateArtifact(byte[] original, int offset, byte[] value)
        {
            byte[] bytes = (byte[])original.Clone();
            value.CopyTo(bytes, offset);
            SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32)).CopyTo(bytes, bytes.Length - 32);
            return bytes;
        }

        private static void TestArtifactMalformed()
        {
            byte[] fixture = IndependentArtifactFixture();
            SurfaceModelArtifact independent = SurfaceModelArtifactCodec.Decode(fixture);
            Require(independent.Samples.Select(sample => sample.Order).SequenceEqual(IndependentSampleOrders) && independent.KeyPoints.Single().SourceSampleOrder == 10
                && fixture.SequenceEqual(SurfaceModelArtifactCodec.Encode(independent)), "An independently written format fixture must preserve IDs, record order and exact bytes.");
            using MemoryStream stream = new MemoryStream(fixture);
            using BinaryReader reader = new BinaryReader(stream);
            stream.Position = 8;
            int semanticsOffset = 0;
            for (int i = 0; i < 5; i++)
            {
                int length = reader.ReadInt32();
                if (i == 3) semanticsOffset = (int)stream.Position;
                stream.Position += length;
            }
            int countOffset = (int)stream.Position;
            int sampleOffset = countOffset + 8;
            int keyOffset = sampleOffset + 3 * 56;
            List<byte[]> malformed = new List<byte[]>
            {
                fixture.Take(fixture.Length - 1).ToArray(),
                MutateArtifact(fixture, 0, BitConverter.GetBytes(0)),
                MutateArtifact(fixture, 4, BitConverter.GetBytes(2)),
                MutateArtifact(fixture, 8, BitConverter.GetBytes(-1)),
                MutateArtifact(fixture, 8, BitConverter.GetBytes(4097)),
                MutateArtifact(fixture, 12, new byte[] { 255 }),
                MutateArtifact(fixture, semanticsOffset, new byte[] { 255 }),
                MutateArtifact(fixture, countOffset, BitConverter.GetBytes(1000001)),
                MutateArtifact(fixture, countOffset + 4, BitConverter.GetBytes(4)),
                MutateArtifact(fixture, sampleOffset + 8, BitConverter.GetBytes(double.NaN)),
                MutateArtifact(fixture, sampleOffset + 32, BitConverter.GetBytes(double.PositiveInfinity)),
                MutateArtifact(fixture, sampleOffset + 48, BitConverter.GetBytes(2D)),
                MutateArtifact(fixture, sampleOffset + 56, BitConverter.GetBytes(10)),
                MutateArtifact(fixture, sampleOffset + 4, BitConverter.GetBytes(-1)),
                MutateArtifact(fixture, keyOffset + 4, BitConverter.GetBytes(999)),
                MutateArtifact(fixture, keyOffset + 8, BitConverter.GetBytes(123D)),
                MutateArtifact(fixture, keyOffset + 56, BitConverter.GetBytes(double.NaN)),
                MutateArtifact(fixture, keyOffset + 56, BitConverter.GetBytes(-1D))
            };
            byte[] corrupt = (byte[])fixture.Clone();
            corrupt[sampleOffset] ^= 1;
            malformed.Add(corrupt);
            byte[] trailing = fixture.Take(fixture.Length - 32).Concat(new byte[33]).ToArray();
            SHA256.HashData(trailing.AsSpan(0, trailing.Length - 32)).CopyTo(trailing, trailing.Length - 32);
            malformed.Add(trailing);
            foreach (byte[] bytes in malformed) RequireThrows<FormatException>(() => SurfaceModelArtifactCodec.Decode(bytes));
            RequireThrows<FormatException>(() => SurfaceModelArtifactCodec.Decode(new byte[SurfaceModelArtifactCodec.MaximumArtifactByteCount + 1]));
            SurfaceModelArtifact recovered = SurfaceModelArtifactCodec.Decode(fixture);
            Require(recovered.Samples.Count == 3 && SurfaceModelArtifactCodec.Encode(recovered).SequenceEqual(fixture), "Decoder must recover after every malformed artifact without stale state.");
        }

        private static void TestArtifactBoundaryAndCancellation()
        {
            SurfaceModelArtifact artifact = LearnModel(8);
            foreach (string sourceId in new[] { " ", new string('x', 4097), "\ud800" })
                RequireThrows<ArgumentException>(() => _ = new SurfaceModelArtifact(sourceId, "mm", "frame", artifact.Samples, artifact.KeyPoints));
            RequireThrows<ArgumentException>(() => _ = new SurfaceModelArtifact("bad", "mm", "frame",
                new[] { new PreparedSurfaceModelSample(0, 0, new ThreeDPoint(double.NaN, 0, 0), new ThreeDPoint(0, 0, 1)) }, Array.Empty<ExtractedModelKeyPoint>()));
            RequireThrows<ArgumentException>(() => _ = new SurfaceModelArtifact("bad", "mm", "frame", artifact.Samples,
                new[] { new ExtractedModelKeyPoint(1, 0, artifact.Samples[0].Position, artifact.Samples[0].Normal, 0) }));
            byte[] bytes = SurfaceModelArtifactCodec.Encode(artifact);
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            ExpectCanceled(() => SurfaceModelArtifactCodec.Encode(artifact, cancellation.Token), cancellation.Token);
            ExpectCanceled(() => SurfaceModelArtifactCodec.Decode(bytes, cancellation.Token), cancellation.Token);
            ExpectCanceled(() => _ = new SurfaceModelArtifact("model", "mm", "frame", artifact.Samples, artifact.KeyPoints, cancellation.Token), cancellation.Token);
            using CancellationTokenSource during = new CancellationTokenSource();
            CancelOnReadList<PreparedSurfaceModelSample> samples = new CancelOnReadList<PreparedSurfaceModelSample>(artifact.Samples, during, 1);
            ExpectCanceled(() => _ = new SurfaceModelArtifact("model", "mm", "frame", samples, artifact.KeyPoints, during.Token), during.Token);
            Require(SurfaceModelArtifactCodec.Decode(SurfaceModelArtifactCodec.Encode(artifact)).Samples.Count == artifact.Samples.Count, "Model creation and codec must recover after cancellation.");
        }

        internal sealed class WorkflowEvaluation
        {
            public string CaseId { get; set; }
            public int Repeat { get; set; }
            public string StartingState { get; set; } = "Independent synthetic input; no field approval";
            public string TimedScope { get; set; }
            public string Expected { get; set; }
            public string Outcome { get; set; }
            public string Recovery { get; set; } = "Owned inputs disposed; stateless next execution";
            public string InputSha256 { get; set; }
            public string ArtifactSha256 { get; set; }
            public string Unit { get; set; }
            public bool Passed { get; set; }
            public int ModelSampleCount { get; set; }
            public int SceneSampleCount { get; set; }
            public int ExpectedMatchCount { get; set; }
            public int ActualMatchCount { get; set; }
            public int SearchCandidateCount { get; set; }
            public int ModelAnchorCount { get; set; }
            public int SceneAnchorCount { get; set; }
            public int AdmissibleSceneSampleCount { get; set; }
            public int[] ExpectedOriginalSceneOrders { get; set; }
            public int[] ActualOriginalSceneOrders { get; set; }
            public double? TranslationError { get; set; }
            public double? RotationMatrixMaximumError { get; set; }
            public double? PointMaximumError { get; set; }
            public double? InspectionDistanceError { get; set; }
            public bool? DefectRejected { get; set; }
            public double? PositionErrorPixels { get; set; }
            public double? AngleErrorDegrees { get; set; }
            public double? ScaleError { get; set; }
            public double ElapsedMilliseconds { get; set; }
            public long ManagedAllocatedBytes { get; set; }
            public string Fingerprint { get; set; }
        }

        internal static WorkflowEvaluation[] QualifyWorkflows()
        {
            List<WorkflowEvaluation> rows = new List<WorkflowEvaluation>();
            foreach ((string id, int count, int visible, int sceneCount, double unit, bool offGrid) in new[]
            {
                ("XYZ/restored-clean", 24, 24, 24, 1D, true),
                ("XYZ/restored-partial-background-first", 32, 24, 128, 1D, true),
                ("XYZ/restored-unit-m", 32, 24, 128, 0.001D, true),
                ("XYZ/restored-dense-background-first", 256, 256, 8192, 1D, false)
            })
            {
                SurfaceModelArtifact original = LearnModel(count, unit);
                SurfaceMatchSample[] model = original.Samples.Select((sample, i) => new SurfaceMatchSample(i, sample.Position)).ToArray();
                double rx = offGrid ? 12.5D : 10D;
                double ry = offGrid ? -7.5D : -10D;
                double rz = offGrid ? 17.5D : 15D;
                (SurfaceMatchSample[] scene, int[] tags) = CreateWorkflowScene(model, visible, sceneCount, unit, rx, ry, rz);
                DeterministicRigidSurfacePoseSearchOptions options = WorkflowSearchOptions(visible, unit, offGrid, true);
                byte[] encoded = SurfaceModelArtifactCodec.Encode(original);
                string inputHash = WorkflowInputHash(encoded, scene);
                for (int repeat = 0; repeat < 3; repeat++)
                    rows.Add(EvaluateModelWorkflow(id, repeat, original, model, scene, tags, encoded, inputHash, options, unit, rx, ry, rz, true));
            }
            SurfaceModelArtifact comparison = LearnModel(32);
            SurfaceMatchSample[] comparisonModel = comparison.Samples.Select((sample, i) => new SurfaceMatchSample(i, sample.Position)).ToArray();
            (SurfaceMatchSample[] comparisonScene, int[] comparisonTags) = CreateWorkflowScene(comparisonModel, 32, 256, 1D, 10D, -10D, 15D);
            byte[] comparisonBytes = SurfaceModelArtifactCodec.Encode(comparison);
            string comparisonHash = WorkflowInputHash(comparisonBytes, comparisonScene);
            foreach (bool reduced in new[] { false, true })
            {
                DeterministicRigidSurfacePoseSearchOptions options = WorkflowSearchOptions(32, 1D, false, reduced);
                DeterministicRigidSurfacePoseSearchResult warmup = new DeterministicRigidSurfacePoseSearchTool().Execute(comparisonModel, comparisonScene, options);
                Require(warmup.Success && warmup.Matched, "Untimed proposal JIT warm-up must satisfy the same matching contract.");
                for (int repeat = 0; repeat < 3; repeat++)
                    rows.Add(EvaluateModelWorkflow("proposal-metrics/" + (reduced ? "reduced" : "full"), repeat, comparison,
                        comparisonModel, comparisonScene, comparisonTags, comparisonBytes, comparisonHash, options, 1D, 10D, -10D, 15D, false));
            }
            foreach ((double angle, double scale) in new[] { (3.75D, 1.01D), (-1.75D, 0.99D) })
            for (int repeat = 0; repeat < 3; repeat++) rows.Add(EvaluateTwoDWorkflow(repeat, angle, scale));
            rows.Add(EvaluateWorkflowRecovery());
            return rows.ToArray();
        }

        private static (SurfaceMatchSample[] scene, int[] tags) CreateWorkflowScene(IReadOnlyList<SurfaceMatchSample> model,
            int visible, int count, double unit, double rx, double ry, double rz)
        {
            List<(ThreeDPoint point, int tag)> entries = new List<(ThreeDPoint, int)>();
            for (int i = 0; i < model.Count; i++)
                if (visible == model.Count || i % 4 != 0) entries.Add((WorkflowOracle(model[i].Position, rx, ry, rz, 4 * unit, -3 * unit, 2 * unit), i));
            Require(entries.Count == visible, "The independent occlusion definition changed.");
            for (int i = entries.Count; i < count; i++)
                entries.Add((new ThreeDPoint((100D + i % 101) * unit, (-140D - i % 83) * unit, (90D + i % 71) * unit), -1));
            Random random = new Random(1973 + model.Count);
            for (int i = entries.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (entries[i], entries[j]) = (entries[j], entries[i]);
            }
            if (count > visible)
            {
                int background = entries.FindIndex(entry => entry.tag < 0);
                (entries[0], entries[background]) = (entries[background], entries[0]);
            }
            return (entries.Select((entry, i) => new SurfaceMatchSample(i, entry.point)).ToArray(), entries.Select(entry => entry.tag).ToArray());
        }

        private static ThreeDPoint WorkflowOracle(ThreeDPoint point, double rx, double ry, double rz, double tx, double ty, double tz)
        {
            // Sequential axis rotations independently define Rz * Ry * Rx; no SDK pose construction is used.
            double x = rx * Math.PI / 180D;
            double y = ry * Math.PI / 180D;
            double z = rz * Math.PI / 180D;
            double ax = point.X;
            double ay = point.Y * Math.Cos(x) - point.Z * Math.Sin(x);
            double az = point.Y * Math.Sin(x) + point.Z * Math.Cos(x);
            double bx = ax * Math.Cos(y) + az * Math.Sin(y);
            double by = ay;
            double bz = -ax * Math.Sin(y) + az * Math.Cos(y);
            return new ThreeDPoint(bx * Math.Cos(z) - by * Math.Sin(z) + tx, bx * Math.Sin(z) + by * Math.Cos(z) + ty, bz + tz);
        }

        private static DeterministicRigidSurfacePoseSearchOptions WorkflowSearchOptions(int visible, double unit, bool offGrid, bool reduced)
        {
            return new DeterministicRigidSurfacePoseSearchOptions
            {
                MinimumRotationXDegrees = 10, MaximumRotationXDegrees = offGrid ? 15 : 10, RotationStepXDegrees = 5,
                MinimumRotationYDegrees = -10, MaximumRotationYDegrees = offGrid ? -5 : -10, RotationStepYDegrees = 5,
                MinimumRotationZDegrees = 15, MaximumRotationZDegrees = offGrid ? 20 : 15, RotationStepZDegrees = 5,
                MinimumTranslationX = -10 * unit, MaximumTranslationX = 10 * unit, MinimumTranslationY = -10 * unit, MaximumTranslationY = 10 * unit,
                MinimumTranslationZ = -10 * unit, MaximumTranslationZ = 10 * unit, MaximumCorrespondenceDistance = (offGrid ? 0.65D : 1e-7D) * unit,
                MinimumMatchedSampleCount = offGrid ? Math.Max(4, visible / 2) : visible, MaximumCandidateCount = 1000000,
                UseSamplePairTranslationCandidates = true,
                ProposalOptions = reduced ? new SurfacePoseProposalOptions { MaximumModelAnchorCount = 8, MaximumSceneAnchorCount = 32 } : null,
                RefinementOptions = offGrid ? new SurfacePoseRefinementOptions { MaximumIterations = 10 } : null
            };
        }

        private static WorkflowEvaluation EvaluateModelWorkflow(string id, int repeat, SurfaceModelArtifact artifact, SurfaceMatchSample[] model,
            SurfaceMatchSample[] scene, int[] tags, byte[] bytes, string inputHash, DeterministicRigidSurfacePoseSearchOptions options,
            double unit, double rx, double ry, double rz, bool includeRestoration)
        {
            WorkflowEvaluation row = new WorkflowEvaluation
            {
                CaseId = id, Repeat = repeat, TimedScope = includeRestoration ? "encode/decode/search/transform/inspection; prepared input excluded" : "search only; input and JIT warm-up excluded",
                Expected = "Known full XYZ pose; every visible original scene ID; restored identity; analytic dimensions pass and authored defect fails",
                Unit = artifact.Unit, ModelSampleCount = model.Length, SceneSampleCount = scene.Length, ExpectedMatchCount = tags.Count(tag => tag >= 0),
                InputSha256 = inputHash, ArtifactSha256 = Convert.ToHexString(SHA256.HashData(bytes)),
                ExpectedOriginalSceneOrders = tags.Select((tag, i) => (tag, i)).Where(entry => entry.tag >= 0).Select(entry => entry.i).ToArray()
            };
            long allocated = GC.GetTotalAllocatedBytes(true);
            Stopwatch watch = Stopwatch.StartNew();
            SurfaceModelArtifact restored = includeRestoration ? SurfaceModelArtifactCodec.Decode(SurfaceModelArtifactCodec.Encode(artifact)) : artifact;
            SurfaceMatchSample[] restoredModel = includeRestoration ? restored.Samples.Select((sample, i) => new SurfaceMatchSample(i, sample.Position)).ToArray() : model;
            DeterministicRigidSurfacePoseSearchResult result = new DeterministicRigidSurfacePoseSearchTool().Execute(restoredModel, scene, options);
            if (!includeRestoration) watch.Stop();
            long? searchAllocated = includeRestoration ? (long?)null : GC.GetTotalAllocatedBytes(true) - allocated;
            row.SearchCandidateCount = result.EvaluatedCandidateCount;
            row.ModelAnchorCount = result.ProposalEvidence?.ModelSampleOrders.Count ?? model.Length;
            row.SceneAnchorCount = result.ProposalEvidence?.SceneSampleOrders.Count ?? scene.Length;
            row.AdmissibleSceneSampleCount = result.ProposalEvidence?.AdmissibleSceneSampleCount ?? scene.Length;
            row.ActualMatchCount = result.Coverage?.MatchedModelSampleCount ?? 0;
            row.Outcome = !result.Success ? "ExecutionError: " + result.Message : !result.Matched ? "NoMatch: " + result.RejectionReason : "Matched";
            if (result.Success && result.Matched)
            {
                row.ActualOriginalSceneOrders = result.Coverage.Matches.Select(pair => pair.SceneSampleOrder).OrderBy(order => order).ToArray();
                row.TranslationError = PointDistance(new ThreeDPoint(result.Pose.TranslationX, result.Pose.TranslationY, result.Pose.TranslationZ), new ThreeDPoint(4 * unit, -3 * unit, 2 * unit));
                row.RotationMatrixMaximumError = new[] { new ThreeDPoint(1, 0, 0), new ThreeDPoint(0, 1, 0), new ThreeDPoint(0, 0, 1) }.Max(point =>
                    PointDistance(new ThreeDPoint(result.Pose.Transform(point).X - result.Pose.TranslationX, result.Pose.Transform(point).Y - result.Pose.TranslationY,
                        result.Pose.Transform(point).Z - result.Pose.TranslationZ), WorkflowOracle(point, rx, ry, rz, 0, 0, 0)));
                row.PointMaximumError = model.Max(sample => PointDistance(result.Pose.Transform(sample.Position), WorkflowOracle(sample.Position, rx, ry, rz, 4 * unit, -3 * unit, 2 * unit)));
                SurfaceCoverageMatch correspondence = result.Coverage.Matches[0];
                ThreeDPoint local = model[correspondence.ModelSampleOrder].Position;
                ThreeDPoint actualFirst = scene[correspondence.SceneSampleOrder].Position;
                ThreeDPoint actualSecond = WorkflowOracle(new ThreeDPoint(local.X + 3 * unit, local.Y + 4 * unit, local.Z + 2 * unit), rx, ry, rz, 4 * unit, -3 * unit, 2 * unit);
                ThreeDPoint heightAxis = new ThreeDPoint(result.Pose.M13, result.Pose.M23, result.Pose.M33);
                PointPairDimensionsInspectionOptions dimensions = new PointPairDimensionsInspectionOptions
                {
                    ExpectedDistance = Math.Sqrt(29D) * unit, DistanceTolerance = 1e-7D * unit, ExpectedPlanarWidth = 5 * unit,
                    PlanarWidthTolerance = 1e-7D * unit, ExpectedElevationAngleDegrees = Math.Atan2(2D, 5D) * 180D / Math.PI, ElevationAngleToleranceDegrees = 1e-6D
                };
                PointPairDimensionsInspectionTool inspection = new PointPairDimensionsInspectionTool();
                PointPairDimensionsInspectionResult measured = inspection.Execute(actualFirst, actualSecond, heightAxis, 0, 2 * unit, dimensions);
                ThreeDPoint defect = WorkflowOracle(new ThreeDPoint(local.X + 3 * unit, local.Y + 4 * unit, local.Z + 2.4D * unit), rx, ry, rz, 4 * unit, -3 * unit, 2 * unit);
                row.DefectRejected = !inspection.Execute(actualFirst, defect, heightAxis, 0, 2.4D * unit, dimensions).Passed;
                row.InspectionDistanceError = Math.Abs(measured.Distance - dimensions.ExpectedDistance);
                row.Passed = restored.SourceId == artifact.SourceId && restored.Unit == artifact.Unit && restored.CoordinateFrame == artifact.CoordinateFrame
                    && result.Coverage.ModelSampleCount == model.Length && result.Coverage.SceneSampleCount == scene.Length && row.ActualMatchCount == row.ExpectedMatchCount
                    && row.ExpectedOriginalSceneOrders.SequenceEqual(row.ActualOriginalSceneOrders)
                    && result.Coverage.Matches.All(pair => pair.ModelSampleOrder == tags[pair.SceneSampleOrder])
                    && row.PointMaximumError < 1e-7D * unit && row.RotationMatrixMaximumError < 1e-8D && measured.Passed && row.DefectRejected == true;
                row.Fingerprint = string.Join("|", new[] { result.Pose.M11, result.Pose.M12, result.Pose.M13, result.Pose.M21, result.Pose.M22, result.Pose.M23,
                    result.Pose.M31, result.Pose.M32, result.Pose.M33, result.Pose.TranslationX, result.Pose.TranslationY, result.Pose.TranslationZ }.Select(value => value.ToString("R", CultureInfo.InvariantCulture)))
                    + "|" + string.Join(",", row.ActualOriginalSceneOrders);
            }
            watch.Stop();
            row.ElapsedMilliseconds = watch.Elapsed.TotalMilliseconds;
            row.ManagedAllocatedBytes = searchAllocated ?? GC.GetTotalAllocatedBytes(true) - allocated;
            return row;
        }

        private static double PointDistance(ThreeDPoint first, ThreeDPoint second) => Math.Sqrt(Math.Pow(first.X - second.X, 2) + Math.Pow(first.Y - second.Y, 2) + Math.Pow(first.Z - second.Z, 2));

        private static string WorkflowInputHash(byte[] artifact, IReadOnlyList<SurfaceMatchSample> scene)
        {
            using MemoryStream stream = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter(stream);
            writer.Write(artifact);
            foreach (SurfaceMatchSample sample in scene)
            {
                writer.Write(sample.Order);
                writer.Write(sample.Position.X);
                writer.Write(sample.Position.Y);
                writer.Write(sample.Position.Z);
            }
            return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
        }

        private static WorkflowEvaluation EvaluateWorkflowRecovery()
        {
            SurfaceModelArtifact artifact = LearnModel(24);
            byte[] bytes = SurfaceModelArtifactCodec.Encode(artifact);
            SurfaceMatchSample[] model = artifact.Samples.Select((sample, i) => new SurfaceMatchSample(i, sample.Position)).ToArray();
            (SurfaceMatchSample[] scene, int[] tags) = CreateWorkflowScene(model, 24, 128, 1D, 12.5D, -7.5D, 17.5D);
            DeterministicRigidSurfacePoseSearchOptions options = WorkflowSearchOptions(24, 1D, true, true);
            DeterministicRigidSurfacePoseSearchTool search = new DeterministicRigidSurfacePoseSearchTool();
            string inputHash = WorkflowInputHash(bytes, scene);
            DeterministicRigidSurfacePoseSearchResult before = search.Execute(model, scene, options);
            byte[] corrupt = (byte[])bytes.Clone();
            corrupt[12] ^= 1;
            bool corruptRejected = RequireThrows<FormatException>(() => SurfaceModelArtifactCodec.Decode(corrupt)) != null;
            SurfaceMatchSample[] absent = scene.Select(sample => new SurfaceMatchSample(sample.Order,
                new ThreeDPoint(sample.Position.X + 1000, sample.Position.Y, sample.Position.Z))).ToArray();
            DeterministicRigidSurfacePoseSearchResult noMatch = search.Execute(model, absent, options);
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            ExpectCanceled(() => SurfaceModelArtifactCodec.Decode(bytes, cancellation.Token), cancellation.Token);
            ExpectCanceled(() => search.Execute(model, scene, options, cancellation.Token), cancellation.Token);
            SurfaceModelArtifact restored = SurfaceModelArtifactCodec.Decode(bytes);
            DeterministicRigidSurfacePoseSearchResult recovered = search.Execute(restored.Samples.Select((sample, i) => new SurfaceMatchSample(i, sample.Position)).ToArray(), scene, options);
            return new WorkflowEvaluation
            {
                CaseId = "workflow/corrupt-absent-cancel-recover", Repeat = 0, StartingState = "A successful pose and decoded model retained",
                TimedScope = "Untimed failure and recovery controls", Expected = "Corrupt bytes rejected; absent/canceled run publishes no pose; valid decode/search restores identical current IDs",
                Recovery = "Explicit valid decode and search on the same search instance; caller input hash unchanged",
                InputSha256 = inputHash, ArtifactSha256 = Convert.ToHexString(SHA256.HashData(bytes)), Unit = artifact.Unit,
                ModelSampleCount = model.Length, SceneSampleCount = scene.Length, ExpectedMatchCount = 24, ActualMatchCount = recovered.Coverage?.MatchedModelSampleCount ?? 0,
                Outcome = "Corrupt SHA rejected; valid absence; caller cancellation observed; recovered=" + recovered.Matched,
                Passed = before.Success && before.Matched && corruptRejected && noMatch.Success && !noMatch.Matched && noMatch.Pose == null
                    && noMatch.Coverage.MatchedModelSampleCount == 0 && recovered.Success && recovered.Matched && recovered.Coverage.MatchedModelSampleCount == 24
                    && before.Pose.TranslationX == recovered.Pose.TranslationX && before.Pose.TranslationY == recovered.Pose.TranslationY
                    && before.Pose.TranslationZ == recovered.Pose.TranslationZ && before.Coverage.Matches.Select(pair => pair.SceneSampleOrder).SequenceEqual(recovered.Coverage.Matches.Select(pair => pair.SceneSampleOrder))
                    && inputHash == WorkflowInputHash(bytes, scene),
                Fingerprint = "corrupt-absent-cancel-explicit-valid-recovery"
            };
        }

        private static WorkflowEvaluation EvaluateTwoDWorkflow(int repeat, double angle, double scale)
        {
            using Mat template = EdgePoseRefinementSmokeSuite.CreateTemplate();
            using Mat source = EdgePoseRefinementSmokeSuite.CreateSource(template, angle, scale, 33.15D, 21.6D);
            using EdgeBasedTemplateMatchingTool tool = EdgePoseRefinementSmokeSuite.CreateTool(template, true);
            tool.property.USE_ROI = true;
            tool.property.CvROI = new Rect(8, 6, 128, 100);
            WorkflowEvaluation row = new WorkflowEvaluation
            {
                CaseId = "2D/off-grid-ROI/" + angle.ToString(CultureInfo.InvariantCulture), Repeat = repeat, Unit = "pixel",
                TimedScope = "Execute and matched ROI transform; input preparation excluded", Expected = "Authored center/angle/scale and transformed independent mark",
                InputSha256 = Convert.ToHexString(SHA256.HashData(source.ToBytes(".png"))), ArtifactSha256 = Convert.ToHexString(SHA256.HashData(template.ToBytes(".png")))
            };
            long allocated = GC.GetTotalAllocatedBytes(true);
            Stopwatch watch = Stopwatch.StartNew();
            using VisionToolResult result = tool.Execute(source);
            row.Outcome = result.Success ? "Matched" : result.ErrorCode + ": " + result.Message;
            if (result.Success)
            {
                MatchingResult match = tool.results.Single();
                Point2d expected = EdgePoseRefinementSmokeSuite.ExpectedPoint(new Point2d(32, 32), angle, scale, 33.15D, 21.6D);
                row.PositionErrorPixels = EdgePoseRefinementSmokeSuite.Distance(new Point2d(match.Center.X, match.Center.Y), expected);
                row.AngleErrorDegrees = Math.Abs(match.Angle - angle);
                row.ScaleError = Math.Abs(match.Scale - scale);
                MatchingCoordinateTransform transform = new MatchingCoordinateTransform(new Point2d(32, 32), new Point2d(match.Center.X, match.Center.Y), match.Angle, match.Scale);
                double markError = EdgePoseRefinementSmokeSuite.Distance(transform.TransformPoint(new Point2d(20, 37)),
                    EdgePoseRefinementSmokeSuite.ExpectedPoint(new Point2d(20, 37), angle, scale, 33.15D, 21.6D));
                row.Passed = row.PositionErrorPixels < 0.25D && row.AngleErrorDegrees < 0.15D && row.ScaleError < 0.003D && markError < 0.3D;
                row.Fingerprint = FormattableString.Invariant($"{match.Center.X:R}|{match.Center.Y:R}|{match.Angle:R}|{match.Scale:R}|{match.EdgeScore:R}");
            }
            watch.Stop();
            row.ElapsedMilliseconds = watch.Elapsed.TotalMilliseconds;
            row.ManagedAllocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated;
            return row;
        }

        private static void TestWorkflowQualification()
        {
            WorkflowEvaluation[] rows = QualifyWorkflows();
            string output = Environment.GetEnvironmentVariable("OPENVISIONLAB_WORKFLOW_EVIDENCE_PATH");
            if (!string.IsNullOrWhiteSpace(output))
            {
                string path = Path.GetFullPath(output);
                Require(path.StartsWith(@"D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\", StringComparison.OrdinalIgnoreCase), "Workflow evidence must be on the project D: root.");
                File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(rows, WorkflowReportJsonOptions));
            }
            foreach (WorkflowEvaluation row in rows)
                Console.WriteLine(FormattableString.Invariant($"{row.CaseId}/{row.Repeat} passed={row.Passed} outcome={row.Outcome} coverage={row.ActualMatchCount}/{row.ExpectedMatchCount} pointError={row.PointMaximumError} angleError={row.AngleErrorDegrees} candidates={row.SearchCandidateCount}"));
            Require(rows.All(row => row.Passed), "A newly authored workflow case failed; preserve its input identity and outcome.");
            Require(rows.GroupBy(row => row.CaseId).All(group => group.Select(row => row.Fingerprint).Distinct().Count() == 1), "Repeated workflow results must remain deterministic.");
        }

        private static void ExpectCanceled(Action action, CancellationToken token)
        {
            try { action(); }
            catch (OperationCanceledException exception)
            {
                Require(exception.CancellationToken == token, "Cancellation lost the caller token.");
                return;
            }

            throw new InvalidOperationException("Cancellation returned a result instead of propagating.");
        }

        private sealed class CancelOnReadList<T> : IReadOnlyList<T>
        {
            private readonly IReadOnlyList<T> values;
            private readonly CancellationTokenSource cancellation;
            private readonly int cancelRead;
            private int reads;

            internal CancelOnReadList(IReadOnlyList<T> values, CancellationTokenSource cancellation, int cancelRead)
            {
                this.values = values;
                this.cancellation = cancellation;
                this.cancelRead = cancelRead;
            }

            public int Count => values.Count;
            public T this[int index]
            {
                get
                {
                    T value = values[index];
                    if (++reads == cancelRead) cancellation.Cancel();
                    return value;
                }
            }

            public IEnumerator<T> GetEnumerator() => throw new NotSupportedException("The preparation contract uses indexed reads.");
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}

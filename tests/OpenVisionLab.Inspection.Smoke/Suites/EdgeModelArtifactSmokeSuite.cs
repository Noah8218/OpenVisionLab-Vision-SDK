using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using OpenCvSharp;
using OpenVisionLab.Vision2D.Pipeline;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Result;
using OpenVisionLab.Vision2D.Tool;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class EdgeModelArtifactSmokeSuite
    {
        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase("Edge learned artifact round trips padded gray BGR BGRA without executing or retaining caller buffers", TestRasterRoundTrip);
            yield return new SmokeCase("Edge learned artifact preserves continuous pose polarity hybrid ROI and independent native lifetimes", TestMatching);
            yield return new SmokeCase("Edge learned artifact rejects incompatible settings and cancellation atomically and permits valid recovery", TestRecovery);
            yield return new SmokeCase("Edge learned artifact codec rejects corruption unsupported schemas counts geometry UTF8 and truncation", TestInvalidBytes);
            yield return new SmokeCase("Edge learned artifact Pipeline restores through host resolver and XML while preserving legacy image path", TestPipeline);
        }

        private static object Cache(EdgeBasedTemplateMatchingTool tool)
        {
            object store = typeof(EdgeBasedTemplateMatchingTool).GetField("modelStore", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(tool);
            return store.GetType().GetProperty("Current").GetValue(store);
        }

        private static void TestRasterRoundTrip()
        {
            using Mat gray = EdgePoseRefinementSmokeSuite.CreateTemplate();
            foreach (int channels in new[] { 1, 3, 4 })
            {
                EdgeTemplateModelArtifact artifact;
                byte[] encoded;
                using (Mat storage = new Mat(70, 73, MatType.CV_8UC(channels), Scalar.All(11)))
                using (Mat padded = storage.SubMat(new Rect(3, 2, gray.Width, gray.Height)))
                using (EdgeBasedTemplateMatchingTool learner = new EdgeBasedTemplateMatchingTool())
                {
                    if (channels == 1) gray.CopyTo(padded);
                    else Cv2.CvtColor(gray, padded, channels == 3 ? ColorConversionCodes.GRAY2BGR : ColorConversionCodes.GRAY2BGRA);
                    Require(!padded.IsContinuous(), "Fixture must exercise padded native rows.");
                    learner.SetProperty(new EdgeBasedTemplateMatchingToolProperty());
                    learner.SetTemplateImage(padded);
                    artifact = learner.PrepareLearnedModel("padded-" + channels);
                    object cache = Cache(learner);
                    Require(ReferenceEquals(cache, CacheAfterPrepare(learner)), "Unchanged learning must reuse its cache.");
                    Require(learner.imageSource.Empty() && learner.imageResult.Empty() && learner.results.Count == 0,
                        "Explicit preparation must not run an inspection or populate source/results.");
                    Require(artifact.Width == gray.Width && artifact.Height == gray.Height && artifact.SourceChannels == channels
                        && artifact.Points.Count > 0 && artifact.RawPointCount >= artifact.Points.Count, "Raster/model dimensions changed.");
                    encoded = EdgeTemplateModelArtifactCodec.Encode(artifact);
                    padded.SetTo(Scalar.All(99));
                    Require(artifact.SourcePixels[0] == 17, "Learned snapshot retained a mutable caller raster.");
                }
                EdgeTemplateModelArtifact restored = EdgeTemplateModelArtifactCodec.Decode(encoded);
                byte[] canonical = EdgeTemplateModelArtifactCodec.Encode(restored);
                Require(encoded.SequenceEqual(canonical), "Model round trip must preserve exact learned bytes.");
                encoded[0] ^= 1;
                Require(canonical.SequenceEqual(EdgeTemplateModelArtifactCodec.Encode(restored)), "Decoded model retained its mutable input bytes.");
                RequireThrows<NotSupportedException>(() => ((IList<byte>)restored.SourcePixels)[0] = 5);
                RequireThrows<NotSupportedException>(() => ((IList<EdgeTemplateModelPoint>)restored.Points)[0] = null);
                using EdgeBasedTemplateMatchingTool worker = new EdgeBasedTemplateMatchingTool();
                worker.SetProperty(new EdgeBasedTemplateMatchingToolProperty());
                worker.SetLearnedModel(restored);
                using Mat source = new Mat(100, 110, MatType.CV_8UC1, Scalar.All(17));
                using (Mat region = source.SubMat(new Rect(21, 18, 64, 64))) gray.CopyTo(region);
                object imported = Cache(worker);
                using VisionToolResult result = worker.Execute(source);
                Require(result.Success && ReferenceEquals(imported, Cache(worker)), "First execution relearned the imported base model.");
                Require(Math.Abs(worker.results.Single().Center.X - 53D) <= 1D
                    && Math.Abs(worker.results.Single().Center.Y - 50D) <= 1D, "Restored raster failed independent translated-pose truth.");
            }
        }

        private static object CacheAfterPrepare(EdgeBasedTemplateMatchingTool tool)
        {
            tool.PrepareLearnedModel("same-learning");
            return Cache(tool);
        }

        private static void TestMatching()
        {
            using Mat template = EdgePoseRefinementSmokeSuite.CreateTemplate();
            using Mat ordinary = EdgePoseRefinementSmokeSuite.CreateSource(template, 2.5D, 1.025D);
            using Mat inverted = new Mat();
            Cv2.BitwiseNot(ordinary, inverted);
            foreach (int mode in new[] { 0, 1, 2 })
            {
                using EdgeBasedTemplateMatchingTool reference = EdgePoseRefinementSmokeSuite.CreateTool(template, true);
                reference.property.ALLOW_GLOBAL_POLARITY_REVERSAL = mode == 2;
                reference.property.USE_HYBRID_VERIFY = mode == 1;
                reference.property.USE_ROI = mode == 2;
                reference.property.CvROI = new Rect(20, 10, 110, 95);
                EdgeTemplateModelArtifact artifact = reference.PrepareLearnedModel("pose-pol-roi");
                using EdgeBasedTemplateMatchingTool worker = new EdgeBasedTemplateMatchingTool { UsePoseRefinement = true };
                worker.SetProperty(reference.property);
                worker.SetLearnedModel(EdgeTemplateModelArtifactCodec.Decode(EdgeTemplateModelArtifactCodec.Encode(artifact)));
                object imported = Cache(worker);
                Mat input = mode == 2 ? inverted : ordinary;
                using VisionToolResult before = reference.Execute(input);
                using VisionToolResult after = worker.Execute(input);
                Require(before.Success && after.Success && ReferenceEquals(imported, Cache(worker)), "Restored continuous matching failed or relearned its base model.");
                MatchingResult first = reference.results.Single(), last = worker.results.Single();
                Require(first.Center == last.Center && first.Angle == last.Angle && first.Scale == last.Scale
                    && first.EdgeScore == last.EdgeScore && first.FinalScore == last.FinalScore
                    && first.PolarityReversed == last.PolarityReversed && first.PolarityReversed == (mode == 2),
                    "Restoration changed pose, score, ROI offset or global polarity.");
                Require(after.EdgeBasedMatchingDiagnostics.ModelPoints.SequenceEqual(before.EdgeBasedMatchingDiagnostics.ModelPoints),
                    "Restoration changed sampled model coordinates.");
                Point2d expected = EdgePoseRefinementSmokeSuite.ExpectedPoint(new Point2d(32, 32), 2.5D, 1.025D);
                Require(EdgePoseRefinementSmokeSuite.Distance(new Point2d(last.Center.X, last.Center.Y), expected) < 0.5D,
                    "Restored result missed independent authored pose.");
                reference.Dispose();
                using VisionToolResult repeated = worker.Execute(input);
                Require(repeated.Success && worker.results.Single().Center == last.Center && !after.ResultImage.IsDisposed,
                    "Workers or returned images shared another Tool's native lifetime.");
            }
        }

        private static void TestRecovery()
        {
            using Mat template = EdgePoseRefinementSmokeSuite.CreateTemplate();
            using EdgeBasedTemplateMatchingTool learner = new EdgeBasedTemplateMatchingTool();
            EdgeBasedTemplateMatchingToolProperty settings = new EdgeBasedTemplateMatchingToolProperty { USE_DRAW_IMAGE = false };
            learner.SetProperty(settings);
            learner.SetTemplateImage(template);
            EdgeTemplateModelArtifact artifact = learner.PrepareLearnedModel("recovery");
            using EdgeBasedTemplateMatchingTool worker = new EdgeBasedTemplateMatchingTool();
            worker.SetProperty(new EdgeBasedTemplateMatchingToolProperty { USE_DRAW_IMAGE = false });
            worker.SetLearnedModel(artifact);
            object imported = Cache(worker);
            Mat owned = worker.imageTemplate;
            worker.property.MIN_GRADIENT_MAGNITUDE = 1.00000001D;
            RequireThrows<ArgumentException>(() => worker.SetLearnedModel(artifact));
            Require(ReferenceEquals(imported, Cache(worker)) && ReferenceEquals(owned, worker.imageTemplate) && !owned.IsDisposed,
                "Incompatible import modified the existing native model.");
            worker.property.MIN_GRADIENT_MAGNITUDE = 1D;
            using CancellationTokenSource cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            RequireThrows<OperationCanceledException>(() => worker.SetLearnedModel(artifact, cancelled.Token));
            RequireThrows<OperationCanceledException>(() => worker.PrepareLearnedModel("cancel", cancelled.Token));
            RequireThrows<OperationCanceledException>(() => EdgeTemplateModelArtifactCodec.Encode(artifact, cancelled.Token));
            RequireThrows<OperationCanceledException>(() => EdgeTemplateModelArtifactCodec.Decode(EdgeTemplateModelArtifactCodec.Encode(artifact), cancelled.Token));
            Require(ReferenceEquals(imported, Cache(worker)) && worker.results.Count == 0 && worker.imageSource.Empty(),
                "Cancelled preparation/restoration changed the current model or executed matching.");
            worker.property.USE_BITWISENOT = true;
            EdgeTemplateModelArtifact rebuilt = worker.PrepareLearnedModel("changed");
            settings.USE_BITWISENOT = true;
            EdgeTemplateModelArtifact fresh = learner.PrepareLearnedModel("changed");
            Require(!ReferenceEquals(imported, Cache(worker))
                && EdgeTemplateModelArtifactCodec.Encode(rebuilt).SequenceEqual(EdgeTemplateModelArtifactCodec.Encode(fresh)),
                "Changed learning did not invalidate an imported model or rebuild from the preserved original raster.");
            worker.property.USE_BITWISENOT = false;
            worker.SetLearnedModel(artifact);
            Require(owned.IsDisposed, "Successful replacement failed to dispose the old Tool-owned display template.");
            using VisionToolResult valid = worker.Execute(template);
            Require(valid.Success, "Valid retry after rejected/cancelled imports failed.");
            using Mat floatImage = new Mat(16, 16, MatType.CV_32FC1, Scalar.All(0));
            learner.SetTemplateImage(floatImage);
            RequireThrows<InvalidOperationException>(() => learner.PrepareLearnedModel("unsupported-depth"));
        }

        private static void TestInvalidBytes()
        {
            // Independently authored little-endian v1 fixture pins wire order, rather than only testing our encoder against our decoder.
            using (MemoryStream fixture = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(fixture, Encoding.UTF8, true))
            {
                writer.Write(0x4D45564F);
                writer.Write(1);
                foreach (string text in new[] { "gray-canny-sobel-sampled-edges-v1", "independent-source", "declared-learning" })
                {
                    byte[] utf8 = Encoding.UTF8.GetBytes(text);
                    writer.Write(utf8.Length);
                    writer.Write(utf8);
                }
                writer.Write(2); writer.Write(2); writer.Write(1);
                writer.Write(0D); writer.Write(0D);
                writer.Write(1); writer.Write(1);
                writer.Write(new byte[] { 1, 2, 3, 4 });
                writer.Write(new byte[] { 1, 2, 3, 4 });
                writer.Write(0); writer.Write(0); writer.Write(1D); writer.Write(0D); writer.Write(1D);
                writer.Flush();
                writer.Write(SHA256.HashData(fixture.ToArray()));
                byte[] independent = fixture.ToArray();
                EdgeTemplateModelArtifact parsed = EdgeTemplateModelArtifactCodec.Decode(independent);
                Require(parsed.SourceId == "independent-source" && parsed.Width == 2 && parsed.Points.Single().UnitDx == 1D
                    && parsed.SourcePixels[3] == 4 && EdgeTemplateModelArtifactCodec.Encode(parsed).SequenceEqual(independent),
                    "Independent model fixture no longer matches the bounded v1 wire contract.");
            }
            using Mat template = EdgePoseRefinementSmokeSuite.CreateTemplate();
            using EdgeBasedTemplateMatchingTool learner = new EdgeBasedTemplateMatchingTool();
            learner.SetProperty(new EdgeBasedTemplateMatchingToolProperty());
            learner.SetTemplateImage(template);
            byte[] bytes = EdgeTemplateModelArtifactCodec.Encode(learner.PrepareLearnedModel("bytes"));
            using MemoryStream stream = new MemoryStream(bytes, false);
            using BinaryReader reader = new BinaryReader(stream, Encoding.UTF8, true);
            stream.Position = 8;
            for (int i = 0; i < 3; i++)
            {
                int length = reader.ReadInt32();
                stream.Position += length;
            }
            int dimensions = (int)stream.Position;
            int pointStart = dimensions + 36 + 64 * 64 * 2;
            void Reject(Action<byte[]> mutation)
            {
                byte[] bad = (byte[])bytes.Clone();
                mutation(bad);
                SHA256.HashData(bad.AsSpan(0, bad.Length - 32)).CopyTo(bad.AsSpan(bad.Length - 32));
                RequireThrows<FormatException>(() => EdgeTemplateModelArtifactCodec.Decode(bad));
            }
            Reject(bad => BitConverter.GetBytes(2).CopyTo(bad, 4));
            Reject(bad => BitConverter.GetBytes(int.MaxValue).CopyTo(bad, dimensions));
            Reject(bad => BitConverter.GetBytes(-1).CopyTo(bad, dimensions + 32));
            Reject(bad => BitConverter.GetBytes(double.NaN).CopyTo(bad, dimensions + 12));
            Reject(bad => BitConverter.GetBytes(99999).CopyTo(bad, pointStart));
            Reject(bad => BitConverter.GetBytes(0D).CopyTo(bad, pointStart + 24));
            Reject(bad => BitConverter.GetBytes(4D).CopyTo(bad, pointStart + 8));
            Reject(bad => Array.Copy(bad, pointStart, bad, pointStart + 32, 8));
            Reject(bad => bad[12] = 0xFF);
            Reject(bad => BitConverter.GetBytes(EdgeTemplateModelArtifactCodec.MaximumTextByteCount + 1).CopyTo(bad, 8));
            RequireThrows<FormatException>(() => EdgeTemplateModelArtifactCodec.Decode(bytes.Take(bytes.Length - 1).ToArray()));
            byte[] corrupt = (byte[])bytes.Clone();
            corrupt[^1] ^= 1;
            RequireThrows<FormatException>(() => EdgeTemplateModelArtifactCodec.Decode(corrupt));
            RequireThrows<FormatException>(() => EdgeTemplateModelArtifactCodec.Decode(new byte[EdgeTemplateModelArtifactCodec.MaximumArtifactByteCount + 1]));
            Require(EdgeTemplateModelArtifactCodec.Encode(EdgeTemplateModelArtifactCodec.Decode(bytes)).SequenceEqual(bytes),
                "Malformed byte rejection prevented a valid codec retry.");
        }

        private static VisionPipelineArtifactReference Reference(byte[] bytes, string format) => new VisionPipelineArtifactReference(
            VisionPipelineToolFactory.TemplateArtifactRole, "host/model/one", format, 1, Convert.ToHexString(SHA256.HashData(bytes)));

        private static void TestPipeline()
        {
            using Mat template = EdgePoseRefinementSmokeSuite.CreateTemplate();
            using EdgeBasedTemplateMatchingTool learner = new EdgeBasedTemplateMatchingTool();
            learner.SetProperty(new EdgeBasedTemplateMatchingToolProperty { USE_DRAW_IMAGE = false });
            learner.SetTemplateImage(template);
            byte[] model = EdgeTemplateModelArtifactCodec.Encode(learner.PrepareLearnedModel("pipeline"));
            VisionPipelineStep step = new VisionPipelineStep
            {
                Name = "learned", ToolType = "edgeBasedTemplateMatching", InputLayer = "input", OutputLayer = "output"
            };
            step.Parameters["USE_DRAW_IMAGE"] = "false";
            step.Artifacts.Add(Reference(model, EdgeTemplateModelArtifactCodec.ArtifactFormat));
            VisionPipeline pipeline = new VisionPipeline();
            pipeline.Steps.Add(step);
            int calls = 0;
            VisionPipeline replay = VisionPipelineSerializer.Deserialize(VisionPipelineSerializer.Serialize(pipeline));
            Require(calls == 0, "XML restore must not call the host resolver or execute.");
            using (EdgeBasedTemplateMatchingTool restored = (EdgeBasedTemplateMatchingTool)VisionPipelineToolFactory.Create(replay.Steps[0], _ => { calls++; return model; }))
            {
                Require(calls == 1 && Cache(restored) != null && restored.imageSource.Empty() && restored.results.Count == 0,
                    "Factory failed to restore learned features without executing.");
                object imported = Cache(restored);
                using VisionToolResult result = restored.Execute(template);
                Require(result.Success && ReferenceEquals(imported, Cache(restored)), "Pipeline import relearned base edges.");
            }
            VisionPipelineToolFactory.TryGetDescriptor("edgeBasedTemplateMatching", out VisionPipelineToolDescriptor descriptor);
            Require(descriptor.Artifacts.Count == 1 && descriptor.Artifacts[0].Format == VisionPipelineToolFactory.EncodedImageArtifactFormat
                && descriptor.Artifacts[0].AcceptedFormats.Contains(EdgeTemplateModelArtifactCodec.ArtifactFormat), "Artifact descriptor lost legacy format or learned discoverability.");
            using (VisionPipelineContext context = new VisionPipelineContext())
            {
                context.SetLayer("input", template);
                using VisionPipelineRunResult run = new VisionPipelineRuntime(s => VisionPipelineToolFactory.Create(s, _ => model), true).Run(replay, context);
                Require(run.Success && run.StepResults.Single().ToolResult.Success && !context.GetLayer("output").IsDisposed,
                    "The actual Pipeline execution did not preserve its restored Tool/result/layer lifetimes.");
            }
            replay.Steps[0].UseAcceptance = true;
            replay.Steps[0].ExpectedSuccess = false;
            using (VisionPipelineContext context = new VisionPipelineContext())
            {
                context.SetLayer("input", template);
                using VisionPipelineRunResult failure = new VisionPipelineRuntime(s => VisionPipelineToolFactory.Create(s, _ => new byte[] { 1 }), true)
                    .RunWithFailureResults(replay, context);
                Require(!failure.Success && !failure.StepResults[0].AcceptancePassed
                    && failure.StepResults[0].ToolResult.ErrorCode == VisionToolErrorCode.ToolFactoryFailed,
                    "Artifact infrastructure failure was accepted as an expected product rejection.");
            }
            replay.Steps[0].Parameters["MIN_GRADIENT_MAGNITUDE"] = "2";
            RequireThrows<ArgumentException>(() => VisionPipelineToolFactory.Create(replay.Steps[0], _ => model));
            replay.Steps[0].Parameters.Remove("MIN_GRADIENT_MAGNITUDE");
            replay.Steps[0].Artifacts[0].FormatVersion = 2;
            RequireThrows<NotSupportedException>(() => VisionPipelineToolFactory.Create(replay.Steps[0], _ => model));
            byte[] png = template.ToBytes(".png");
            replay.Steps[0].Artifacts.Clear();
            replay.Steps[0].Artifacts.Add(Reference(png, VisionPipelineToolFactory.EncodedImageArtifactFormat));
            using EdgeBasedTemplateMatchingTool legacy = (EdgeBasedTemplateMatchingTool)VisionPipelineToolFactory.Create(replay.Steps[0], _ => png);
            Require(Cache(legacy) == null, "Legacy image import must remain lazy.");
            using VisionToolResult valid = legacy.Execute(template);
            Require(valid.Success, "Old encoded-image path failed after learned import errors.");
        }
    }
}

using OpenVisionLab.Vision2D.Pipeline;
using OpenVisionLab.Vision2D.Tool;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using static OpenVisionLab.Vision2D.Pipeline.VisionPipelineDescriptorParameter;

namespace OpenVisionLab.Vision2D.Blob
{
    /// <summary>Composes BlobTool with every Tool owned by the base Vision2D package.</summary>
    public static class VisionPipelineBlobToolFactory
    {
        private static readonly VisionPipelineToolDescriptor BlobDescriptor = CreateBlobDescriptor();
        private static readonly IReadOnlyList<VisionPipelineToolDescriptor> AllDescriptors =
            new ReadOnlyCollection<VisionPipelineToolDescriptor>(
                VisionPipelineToolFactory.Descriptors.Concat(new[] { BlobDescriptor }).ToArray());

        /// <summary>Gets the immutable descriptors for all 15 non-legacy 2D Tools.</summary>
        public static IReadOnlyList<VisionPipelineToolDescriptor> Descriptors => AllDescriptors;

        /// <summary>Finds a base Vision2D or Blob descriptor by canonical Tool type or documented alias.</summary>
        public static bool TryGetDescriptor(string toolType, out VisionPipelineToolDescriptor descriptor)
        {
            if (string.Equals(
                VisionPipelineToolFactory.NormalizeToolType(toolType),
                VisionPipelineToolFactory.NormalizeToolType(BlobDescriptor.ToolType),
                StringComparison.Ordinal))
            {
                descriptor = BlobDescriptor;
                return true;
            }

            return VisionPipelineToolFactory.TryGetDescriptor(toolType, out descriptor);
        }

        /// <summary>Creates BlobTool or delegates a non-model Tool to the base Vision2D factory.</summary>
        public static IVisionTool Create(VisionPipelineStep step)
        {
            return Create(step, null);
        }

        /// <summary>Creates BlobTool or delegates a Tool and artifact resolver to the base Vision2D factory.</summary>
        public static IVisionTool Create(
            VisionPipelineStep step,
            Func<VisionPipelineArtifactReference, byte[]> artifactResolver)
        {
            if (step == null)
            {
                throw new ArgumentNullException(nameof(step));
            }

            if (!string.Equals(
                VisionPipelineToolFactory.NormalizeToolType(step.ToolType),
                VisionPipelineToolFactory.NormalizeToolType(BlobDescriptor.ToolType),
                StringComparison.Ordinal))
            {
                return VisionPipelineToolFactory.Create(step, artifactResolver);
            }

            if (step.Artifacts.Count > 0)
            {
                throw new ArgumentException("Vision pipeline BlobTool does not accept artifacts.", nameof(step));
            }

            VisionPipelineParameterReader parameters = new VisionPipelineParameterReader(step.Parameters, BlobDescriptor);
            BlobToolProperty property = new BlobToolProperty();
            property.NAME = parameters.GetString(nameof(property.NAME), property.NAME);
            property.PIXELPERMM = parameters.GetDouble(nameof(property.PIXELPERMM), property.PIXELPERMM);
            property.USE_THRESHOLD = parameters.GetBool(nameof(property.USE_THRESHOLD), property.USE_THRESHOLD);
            property.USE_BITWISENOT = parameters.GetBool(nameof(property.USE_BITWISENOT), property.USE_BITWISENOT);
            property.THRESHOLD_TYPES = parameters.GetEnum(nameof(property.THRESHOLD_TYPES), property.THRESHOLD_TYPES);
            property.THRESHOLD = parameters.GetDouble(nameof(property.THRESHOLD), property.THRESHOLD);
            property.USE_ADAPTIVE_THRESHOLD = parameters.GetBool(nameof(property.USE_ADAPTIVE_THRESHOLD), property.USE_ADAPTIVE_THRESHOLD);
            property.ADAPTIVE_THRESHOLD = parameters.GetDouble(nameof(property.ADAPTIVE_THRESHOLD), property.ADAPTIVE_THRESHOLD);
            property.ADAPTIVE_THRESHOLD_TYPES = parameters.GetEnum(nameof(property.ADAPTIVE_THRESHOLD_TYPES), property.ADAPTIVE_THRESHOLD_TYPES);
            property.ADAPTIVE_THRESHOLD_ALGORITHM = parameters.GetEnum(nameof(property.ADAPTIVE_THRESHOLD_ALGORITHM), property.ADAPTIVE_THRESHOLD_ALGORITHM);
            property.BlockSize = parameters.GetInt(nameof(property.BlockSize), property.BlockSize);
            property.Weight = parameters.GetInt(nameof(property.Weight), property.Weight);
            property.USE_ROI = parameters.GetBool(nameof(property.USE_ROI), property.USE_ROI);
            property.USE_MULTI_ROI = parameters.GetBool(nameof(property.USE_MULTI_ROI), property.USE_MULTI_ROI);
            property.CvROI = parameters.GetRectangle(nameof(property.CvROI), property.CvROI);
            property.CvROIS = parameters.GetRectangleList(nameof(property.CvROIS), property.CvROIS);
            property.CvMASKS = parameters.GetRectangleList(nameof(property.CvMASKS), property.CvMASKS);
            property.MIN_AREA = parameters.GetInt(nameof(property.MIN_AREA), property.MIN_AREA);
            property.MAX_AREA = parameters.GetInt(nameof(property.MAX_AREA), property.MAX_AREA);
            property.MIN_WIDTH = parameters.GetInt(nameof(property.MIN_WIDTH), property.MIN_WIDTH);
            property.MAX_WIDTH = parameters.GetInt(nameof(property.MAX_WIDTH), property.MAX_WIDTH);
            property.MIN_HEIGHT = parameters.GetInt(nameof(property.MIN_HEIGHT), property.MIN_HEIGHT);
            property.MAX_HEIGHT = parameters.GetInt(nameof(property.MAX_HEIGHT), property.MAX_HEIGHT);

            BlobTool tool = new BlobTool();
            tool.SetProperty(property);
            return tool;
        }

        private static VisionPipelineToolDescriptor CreateBlobDescriptor()
        {
            BlobToolProperty value = new BlobToolProperty();
            return new VisionPipelineToolDescriptor(
                "blob",
                typeof(BlobTool).FullName,
                typeof(BlobToolProperty).FullName,
                "OpenVisionLab.Vision2D.Blob",
                new List<string> { "BlobTool" },
                new[]
                {
                    String(nameof(value.NAME), value.NAME),
                    Number(nameof(value.PIXELPERMM), value.PIXELPERMM),
                    Boolean(nameof(value.USE_THRESHOLD), value.USE_THRESHOLD),
                    Boolean(nameof(value.USE_BITWISENOT), value.USE_BITWISENOT),
                    Enum(nameof(value.THRESHOLD_TYPES), value.THRESHOLD_TYPES),
                    Number(nameof(value.THRESHOLD), value.THRESHOLD),
                    Boolean(nameof(value.USE_ADAPTIVE_THRESHOLD), value.USE_ADAPTIVE_THRESHOLD),
                    Number(nameof(value.ADAPTIVE_THRESHOLD), value.ADAPTIVE_THRESHOLD),
                    Enum(nameof(value.ADAPTIVE_THRESHOLD_TYPES), value.ADAPTIVE_THRESHOLD_TYPES),
                    Enum(nameof(value.ADAPTIVE_THRESHOLD_ALGORITHM), value.ADAPTIVE_THRESHOLD_ALGORITHM),
                    Integer(nameof(value.BlockSize), value.BlockSize),
                    Integer(nameof(value.Weight), value.Weight),
                    Boolean(nameof(value.USE_ROI), value.USE_ROI),
                    Boolean(nameof(value.USE_MULTI_ROI), value.USE_MULTI_ROI),
                    Rectangle(nameof(value.CvROI), "0,0,0,0"),
                    RectangleList(nameof(value.CvROIS)),
                    RectangleList(nameof(value.CvMASKS)),
                    Integer(nameof(value.MIN_AREA), value.MIN_AREA),
                    Integer(nameof(value.MAX_AREA), value.MAX_AREA),
                    Integer(nameof(value.MIN_WIDTH), value.MIN_WIDTH),
                    Integer(nameof(value.MAX_WIDTH), value.MAX_WIDTH),
                    Integer(nameof(value.MIN_HEIGHT), value.MIN_HEIGHT),
                    Integer(nameof(value.MAX_HEIGHT), value.MAX_HEIGHT)
                },
                Array.Empty<VisionPipelineArtifactDescriptor>());
        }
    }
}

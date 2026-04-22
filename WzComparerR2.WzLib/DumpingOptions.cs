namespace WzComparerR2.WzLib
{
    public class DumpingOptions
    {
        public bool DumpRaw { get; set; }

        public bool DumpExternal { get; set; }

        public bool LeaveReference { get; set; }

        public bool OmitRedundantCanvasArtifacts { get; set; }

        public bool PreserveFullPathForSingleImage { get; set; }

        public DumpingOptions Clone()
        {
            return new DumpingOptions
            {
                DumpRaw = this.DumpRaw,
                DumpExternal = this.DumpExternal,
                LeaveReference = this.LeaveReference,
                OmitRedundantCanvasArtifacts = this.OmitRedundantCanvasArtifacts,
                PreserveFullPathForSingleImage = this.PreserveFullPathForSingleImage,
            };
        }

        public static DumpingOptions CreateXmlDefaults()
        {
            return new DumpingOptions
            {
                DumpRaw = false,
                DumpExternal = true,
                LeaveReference = true,
                OmitRedundantCanvasArtifacts = false,
                PreserveFullPathForSingleImage = false,
            };
        }

        public static DumpingOptions CreateJsonDefaults()
        {
            return new DumpingOptions
            {
                DumpRaw = false,
                DumpExternal = false,
                LeaveReference = false,
                OmitRedundantCanvasArtifacts = false,
                PreserveFullPathForSingleImage = false,
            };
        }
    }
}
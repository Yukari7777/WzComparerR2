namespace WzComparerR2
{
    public class DumpingOptions
    {
        public bool DumpRaw { get; set; }

        public bool DumpExternal { get; set; }

        public bool LeaveReference { get; set; }

        public DumpingOptions Clone()
        {
            return new DumpingOptions
            {
                DumpRaw = this.DumpRaw,
                DumpExternal = this.DumpExternal,
                LeaveReference = this.LeaveReference,
            };
        }

        public static DumpingOptions CreateXmlDefaults()
        {
            return new DumpingOptions
            {
                DumpRaw = false,
                DumpExternal = true,
                LeaveReference = true,
            };
        }

        public static DumpingOptions CreateJsonDefaults()
        {
            return new DumpingOptions
            {
                DumpRaw = false,
                DumpExternal = true,
                LeaveReference = true,
            };
        }
    }
}

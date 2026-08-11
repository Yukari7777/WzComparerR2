namespace WzComparerR2.WzLib
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

        public static DumpingOptions CreateDefaults()
        {
            return new DumpingOptions
            {
                DumpRaw = false,
                DumpExternal = false,
                LeaveReference = false,
            };
        }
    }
}

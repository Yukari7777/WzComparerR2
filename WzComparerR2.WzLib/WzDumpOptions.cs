namespace WzComparerR2.WzLib
{
    public sealed class WzDumpOptions
    {
        public bool DumpRaw { get; set; }

        public bool DumpExternal { get; set; }

        public bool LeaveReference { get; set; }

        public WzDumpOptions Clone()
        {
            return new WzDumpOptions
            {
                DumpRaw = this.DumpRaw,
                DumpExternal = this.DumpExternal,
                LeaveReference = this.LeaveReference,
            };
        }

        public static WzDumpOptions CreateDefaults(bool dumpExternal)
        {
            return new WzDumpOptions
            {
                DumpRaw = false,
                DumpExternal = dumpExternal,
                LeaveReference = false,
            };
        }
    }
}

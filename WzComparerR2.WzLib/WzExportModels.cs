using System;
using System.Collections.Generic;

namespace WzComparerR2.WzLib
{
    public enum WzDumpFormat
    {
        Json,
        Xml,
    }

    public sealed class WzExportFailure
    {
        public string Code { get; set; }

        public string SourcePath { get; set; }

        public string LinkType { get; set; }

        public string LinkPath { get; set; }

        public string TargetPath { get; set; }

        public IReadOnlyList<string> TargetWzFiles { get; set; } = Array.Empty<string>();

        public string Stage { get; set; }

        public string Reason { get; set; }

        public bool SourceWasLinkStub { get; set; }
    }

    public sealed class WzExportResult
    {
        private readonly List<WzExportFailure> failures = new List<WzExportFailure>();
        private readonly List<string> documentPaths = new List<string>();

        public bool Success => this.failures.Count == 0;

        public string OutputRoot { get; internal set; }

        public IReadOnlyList<string> DocumentPaths => this.documentPaths;

        public int ExternalFileCount { get; internal set; }

        public IReadOnlyList<WzExportFailure> Failures => this.failures;

        internal void AddDocumentPath(string path)
        {
            if (!string.IsNullOrEmpty(path) && !this.documentPaths.Exists(value => string.Equals(value, path, StringComparison.OrdinalIgnoreCase)))
            {
                this.documentPaths.Add(path);
            }
        }

        public void AddFailure(WzExportFailure failure)
        {
            if (failure != null)
            {
                this.failures.Add(failure);
            }
        }

        public void AddFailures(IEnumerable<WzExportFailure> values)
        {
            if (values == null)
            {
                return;
            }

            foreach (var value in values)
            {
                this.AddFailure(value);
            }
        }
    }

    internal sealed class WzDumpSerializationContext
    {
        [ThreadStatic]
        private static WzDumpSerializationContext current;

        private readonly Dictionary<Wz_Node, Wz_Node> resolvedNodes = new Dictionary<Wz_Node, Wz_Node>();
        private readonly Dictionary<Wz_Node, IReadOnlyList<string>> externalFiles = new Dictionary<Wz_Node, IReadOnlyList<string>>();
        private readonly Dictionary<Wz_Node, IReadOnlyList<byte[]>> rawData = new Dictionary<Wz_Node, IReadOnlyList<byte[]>>();

        public static WzDumpSerializationContext Current => current;

        public bool IncludePngDimensions { get; set; }

        public IDisposable Activate()
        {
            WzDumpSerializationContext previous = current;
            current = this;
            return new Activation(() => current = previous);
        }

        public void SetResolvedNode(Wz_Node source, Wz_Node target)
        {
            this.resolvedNodes[source] = target;
        }

        public Wz_Node GetResolvedNode(Wz_Node source)
        {
            return source != null && this.resolvedNodes.TryGetValue(source, out var target) ? target : source;
        }

        public void SetExternalFiles(Wz_Node source, IReadOnlyList<string> files)
        {
            this.externalFiles[source] = files;
        }

        public IReadOnlyList<string> GetExternalFiles(Wz_Node source)
        {
            return source != null && this.externalFiles.TryGetValue(source, out var files) ? files : null;
        }

        public void SetRawData(Wz_Node source, IReadOnlyList<byte[]> data)
        {
            this.rawData[source] = data;
        }

        public IReadOnlyList<byte[]> GetRawData(Wz_Node source)
        {
            return source != null && this.rawData.TryGetValue(source, out var data) ? data : null;
        }

        private sealed class Activation : IDisposable
        {
            private readonly Action onDispose;
            private bool disposed;

            public Activation(Action onDispose)
            {
                this.onDispose = onDispose;
            }

            public void Dispose()
            {
                if (!this.disposed)
                {
                    this.onDispose();
                    this.disposed = true;
                }
            }
        }
    }
}

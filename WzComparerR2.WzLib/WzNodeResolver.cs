using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WzComparerR2.WzLib
{
    public sealed class WzNodeResolver
    {
        private readonly Wz_Structure structure;
        private readonly string dataRoot;
        private readonly HashSet<string> loadedFolders;
        private readonly object loadLock = new object();

        public WzNodeResolver(Wz_Structure structure, string sourcePath = null)
        {
            this.structure = structure ?? throw new ArgumentNullException(nameof(structure));
            this.dataRoot = FindDataRoot(structure, sourcePath);
            this.loadedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public Wz_Node Root => this.structure.WzNode;

        public bool TryResolveExactPath(string fullPath, out ResolvedNode resolvedNode, out WzResolutionFailure failure)
        {
            resolvedNode = null;
            failure = null;

            var scope = new ResolvedNode(this);
            if (!this.TryResolvePath(fullPath, scope, out var node, out failure))
            {
                scope.Dispose();
                return false;
            }

            scope.Node = node;
            scope.RootImage = node.GetNodeWzImage();
            resolvedNode = scope;
            return true;
        }

        internal bool TryResolveLinkedNode(Wz_Node sourceNode, ResolvedNode scope, out WzLinkResolution resolution, out WzResolutionFailure failure)
        {
            resolution = null;
            failure = null;

            if (sourceNode == null)
            {
                failure = CreateFailure("invalid_source", null, null, null, null, "link", "Source node is null.", false);
                return false;
            }

            Wz_Node current = sourceNode;
            string firstLinkType = null;
            string firstLinkPath = null;
            bool sourceWasLinkStub = false;
            var visited = new HashSet<Wz_Node>();

            while (true)
            {
                if (!visited.Add(current))
                {
                    failure = CreateFailure(
                        "link_cycle",
                        sourceNode.FullPathToFile,
                        firstLinkType,
                        firstLinkPath,
                        current?.FullPathToFile,
                        "link",
                        "A link or UOL cycle was detected.",
                        sourceWasLinkStub);
                    return false;
                }

                if (current.Value is Wz_Uol uol)
                {
                    string uolPath = uol.Uol;
                    if (firstLinkType == null)
                    {
                        firstLinkType = "uol";
                        firstLinkPath = uolPath;
                    }

                    Wz_Node target;
                    if (!string.IsNullOrEmpty(uolPath) && uolPath.StartsWith("/", StringComparison.Ordinal))
                    {
                        if (!this.TryResolvePath(uolPath.TrimStart('/'), scope, out target, out failure))
                        {
                            failure = EnrichFailure(failure, sourceNode, "uol", uolPath, sourceWasLinkStub);
                            return false;
                        }
                    }
                    else
                    {
                        target = uol.HandleUol(current);
                        if (target == null)
                        {
                            failure = CreateFailure(
                                "uol_target_not_found",
                                sourceNode.FullPathToFile,
                                "uol",
                                uolPath,
                                null,
                                "link",
                                "The relative UOL target could not be resolved.",
                                sourceWasLinkStub);
                            return false;
                        }
                    }

                    if (!scope.TryEnterImage(target, out current, out failure))
                    {
                        failure = EnrichFailure(failure, sourceNode, "uol", uolPath, sourceWasLinkStub);
                        return false;
                    }
                    continue;
                }

                if (!TryGetLink(current, out string linkType, out string linkPath))
                {
                    resolution = new WzLinkResolution(sourceNode, current, firstLinkType, firstLinkPath, sourceWasLinkStub);
                    return true;
                }

                if (firstLinkType == null)
                {
                    firstLinkType = linkType;
                    firstLinkPath = linkPath;
                    sourceWasLinkStub = IsLinkStub(sourceNode);
                }

                Wz_Node linkedNode;
                if (string.Equals(linkType, "_inlink", StringComparison.Ordinal))
                {
                    Wz_Image ownerImage = current.GetNodeWzImage();
                    linkedNode = ownerImage?.Node.FindNodeByPath(true, true, SplitPath(linkPath));
                    if (linkedNode == null)
                    {
                        failure = CreateFailure(
                            "inlink_target_not_found",
                            sourceNode.FullPathToFile,
                            linkType,
                            linkPath,
                            BuildInlinkTargetPath(ownerImage, linkPath),
                            "link",
                            "The _inlink target could not be found in its owner image.",
                            sourceWasLinkStub);
                        return false;
                    }
                }
                else
                {
                    if (!this.TryResolvePath(linkPath, scope, out linkedNode, out failure))
                    {
                        failure = EnrichFailure(failure, sourceNode, linkType, linkPath, sourceWasLinkStub);
                        return false;
                    }
                }

                if (!scope.TryEnterImage(linkedNode, out current, out failure))
                {
                    failure = EnrichFailure(failure, sourceNode, linkType, linkPath, sourceWasLinkStub);
                    return false;
                }
            }
        }

        private bool TryResolvePath(string fullPath, ResolvedNode scope, out Wz_Node resolvedNode, out WzResolutionFailure failure)
        {
            resolvedNode = null;
            failure = null;

            if (this.Root == null)
            {
                failure = CreateFailure("missing_root", fullPath, null, null, fullPath, "path", "The WZ structure has no root node.", false);
                return false;
            }

            string[] pathSegments = SplitPath(fullPath);
            if (pathSegments.Length == 0)
            {
                failure = CreateFailure("invalid_path", fullPath, null, null, fullPath, "path", "Path cannot be empty.", false);
                return false;
            }
            if (pathSegments.Any(segment => segment == "." || segment == ".."))
            {
                failure = CreateFailure("invalid_path", fullPath, null, null, fullPath, "path", "Relative path traversal is not allowed.", false);
                return false;
            }

            Wz_Node currentNode = this.Root;
            var logicalSegments = new List<string>(pathSegments.Length);
            IReadOnlyList<string> attemptedFiles = Array.Empty<string>();

            for (int i = 0; i < pathSegments.Length; i++)
            {
                string segment = pathSegments[i];
                logicalSegments.Add(segment);
                Wz_Node childNode = FindChildNode(currentNode, segment);
                if (childNode == null)
                {
                    if (!this.TryLoadLogicalFolder(currentNode, logicalSegments, segment, out attemptedFiles, out var loadError))
                    {
                        IReadOnlyList<string> targetFiles = attemptedFiles.Count > 0 ? attemptedFiles : GetNodeFiles(currentNode);
                        failure = CreateFailure(
                            loadError == null ? "path_not_found" : "wz_folder_load_failed",
                            fullPath,
                            null,
                            null,
                            string.Join("/", logicalSegments),
                            loadError == null ? "path" : "wz-load",
                            loadError?.Message ?? $"Path segment not found: {segment}",
                            false,
                            targetFiles);
                        return false;
                    }
                    childNode = FindChildNode(currentNode, segment);
                }

                if (childNode == null)
                {
                    IReadOnlyList<string> targetFiles = attemptedFiles.Count > 0 ? attemptedFiles : GetNodeFiles(currentNode);
                    failure = CreateFailure(
                        "path_not_found",
                        fullPath,
                        null,
                        null,
                        string.Join("/", logicalSegments),
                        "path",
                        $"Path segment not found after loading its WZ folder: {segment}",
                        false,
                        targetFiles);
                    return false;
                }

                if (!scope.TryEnterImage(childNode, out currentNode, out failure))
                {
                    failure.SourcePath = fullPath;
                    return false;
                }
            }

            resolvedNode = currentNode;
            return true;
        }

        private bool TryLoadLogicalFolder(Wz_Node parentNode, IReadOnlyList<string> logicalSegments, string segment, out IReadOnlyList<string> attemptedFiles, out Exception error)
        {
            attemptedFiles = Array.Empty<string>();
            error = null;
            if (string.IsNullOrEmpty(this.dataRoot))
            {
                return false;
            }

            string folderPath = this.dataRoot;
            foreach (string pathSegment in logicalSegments)
            {
                if (pathSegment == "." || pathSegment == ".." || pathSegment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    return false;
                }
                folderPath = Path.Combine(folderPath, pathSegment);
            }

            folderPath = Path.GetFullPath(folderPath);
            if (!IsSubPath(this.dataRoot, folderPath) || !Directory.Exists(folderPath))
            {
                return false;
            }

            string entryFile = Path.Combine(folderPath, Path.GetFileName(folderPath) + ".wz");
            if (!File.Exists(entryFile))
            {
                return false;
            }

            attemptedFiles = Directory.GetFiles(folderPath, Path.GetFileName(folderPath) + "*.wz")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            lock (this.loadLock)
            {
                Wz_Node existing = FindChildNode(parentNode, segment);
                if (existing != null)
                {
                    return true;
                }

                if (this.loadedFolders.Contains(folderPath))
                {
                    return false;
                }

                var loadedNode = new Wz_Node(segment);
                try
                {
                    this.structure.LoadWzFolder(folderPath, ref loadedNode, false);
                    Wz_File loadedFile = loadedNode.GetValue<Wz_File>();
                    if (loadedFile != null && !ReferenceEquals(parentNode, this.Root))
                    {
                        loadedFile.IsSubDir = true;
                    }
                    parentNode.Nodes.Add(loadedNode);
                    this.loadedFolders.Add(folderPath);
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex;
                    return false;
                }
            }
        }

        private static bool TryGetLink(Wz_Node node, out string linkType, out string linkPath)
        {
            foreach (string candidate in new[] { "source", "_inlink", "_outlink" })
            {
                string value = node.Nodes[candidate].GetValueEx<string>(null);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    linkType = candidate;
                    linkPath = value;
                    return true;
                }
            }

            linkType = null;
            linkPath = null;
            return false;
        }

        private static bool IsLinkStub(Wz_Node node)
        {
            return node?.Value is Wz_Png png
                && png.Width == 1
                && png.Height == 1
                && TryGetLink(node, out _, out _);
        }

        private static Wz_Node FindChildNode(Wz_Node parentNode, string name)
        {
            if (parentNode == null)
            {
                return null;
            }

            foreach (var childNode in parentNode.Nodes)
            {
                if (string.Equals(childNode.Text, name, StringComparison.OrdinalIgnoreCase))
                {
                    return childNode;
                }
            }
            return null;
        }

        private static string[] SplitPath(string fullPath)
        {
            return string.IsNullOrWhiteSpace(fullPath)
                ? Array.Empty<string>()
                : fullPath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string FindDataRoot(Wz_Structure structure, string sourcePath)
        {
            string basePath = sourcePath;
            if (string.IsNullOrWhiteSpace(basePath))
            {
                basePath = structure.wz_files
                    .FirstOrDefault(file => file.Type == Wz_Type.Base)?.Header?.FileName;
            }

            if (string.IsNullOrWhiteSpace(basePath))
            {
                return null;
            }

            string directory = Path.GetDirectoryName(Path.GetFullPath(basePath));
            return directory != null && string.Equals(Path.GetFileName(directory), "Base", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(directory)
                : null;
        }

        private static bool IsSubPath(string root, string path)
        {
            string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildInlinkTargetPath(Wz_Image image, string linkPath)
        {
            string imagePath = image?.Node?.FullPathToFile;
            return string.IsNullOrEmpty(imagePath) ? linkPath : imagePath.Replace('\\', '/') + "/" + linkPath?.TrimStart('/');
        }

        private static IReadOnlyList<string> GetNodeFiles(Wz_Node node)
        {
            Wz_Image image = node?.GetNodeWzImage();
            if (image?.WzFile is Wz_File imageFile && !string.IsNullOrEmpty(imageFile.Header?.FileName))
            {
                return new[] { imageFile.Header.FileName };
            }

            Wz_File wzFile = node?.GetNodeWzFile();
            return !string.IsNullOrEmpty(wzFile?.Header?.FileName)
                ? new[] { wzFile.Header.FileName }
                : Array.Empty<string>();
        }

        private static WzResolutionFailure EnrichFailure(WzResolutionFailure failure, Wz_Node source, string linkType, string linkPath, bool sourceWasLinkStub)
        {
            failure ??= new WzResolutionFailure();
            failure.SourcePath = source?.FullPathToFile;
            failure.LinkType = linkType;
            failure.LinkPath = linkPath;
            failure.SourceWasLinkStub = sourceWasLinkStub;
            return failure;
        }

        private static WzResolutionFailure CreateFailure(string code, string sourcePath, string linkType, string linkPath, string targetPath, string stage, string reason, bool sourceWasLinkStub, IReadOnlyList<string> targetWzFiles = null)
        {
            return new WzResolutionFailure
            {
                Code = code,
                SourcePath = sourcePath,
                LinkType = linkType,
                LinkPath = linkPath,
                TargetPath = targetPath,
                TargetWzFiles = targetWzFiles ?? Array.Empty<string>(),
                Stage = stage,
                Reason = reason,
                SourceWasLinkStub = sourceWasLinkStub,
            };
        }

        public sealed class ResolvedNode : IDisposable
        {
            private readonly WzNodeResolver resolver;
            private readonly List<Wz_Image> extractedImages = new List<Wz_Image>();
            private bool disposed;

            internal ResolvedNode(WzNodeResolver resolver)
            {
                this.resolver = resolver;
            }

            public Wz_Node Node { get; internal set; }

            public Wz_Image RootImage { get; internal set; }

            public bool IsInsideImage => this.RootImage != null;

            internal bool TryResolveLinkedNode(Wz_Node sourceNode, out WzLinkResolution resolution, out WzResolutionFailure failure)
            {
                return this.resolver.TryResolveLinkedNode(sourceNode, this, out resolution, out failure);
            }

            internal bool TryEnterImage(Wz_Node node, out Wz_Node result, out WzResolutionFailure failure)
            {
                failure = null;
                result = node;
                Wz_Image image = node?.GetValue<Wz_Image>();
                if (image == null)
                {
                    return true;
                }

                bool wasExtracted = image.Extracted;
                if (!image.TryExtract(out var extractError))
                {
                    failure = CreateFailure(
                        "image_extract_failed",
                        node.FullPathToFile,
                        null,
                        null,
                        node.FullPathToFile,
                        "image-extract",
                        extractError?.Message ?? $"Failed to extract image '{image.Name}'.",
                        false,
                        GetImageFiles(image));
                    return false;
                }

                if (!wasExtracted && !this.extractedImages.Contains(image))
                {
                    this.extractedImages.Add(image);
                }
                result = image.Node;
                return true;
            }

            public void Dispose()
            {
                if (this.disposed)
                {
                    return;
                }

                for (int i = this.extractedImages.Count - 1; i >= 0; i--)
                {
                    this.extractedImages[i].Unextract();
                }
                this.disposed = true;
            }

            private static IReadOnlyList<string> GetImageFiles(Wz_Image image)
            {
                return image?.WzFile is Wz_File wzFile && !string.IsNullOrEmpty(wzFile.Header?.FileName)
                    ? new[] { wzFile.Header.FileName }
                    : Array.Empty<string>();
            }
        }
    }

    public sealed class WzResolutionFailure
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

    internal sealed class WzLinkResolution
    {
        public WzLinkResolution(Wz_Node sourceNode, Wz_Node targetNode, string linkType, string linkPath, bool sourceWasLinkStub)
        {
            this.SourceNode = sourceNode;
            this.TargetNode = targetNode;
            this.LinkType = linkType;
            this.LinkPath = linkPath;
            this.SourceWasLinkStub = sourceWasLinkStub;
        }

        public Wz_Node SourceNode { get; }
        public Wz_Node TargetNode { get; }
        public string LinkType { get; }
        public string LinkPath { get; }
        public bool SourceWasLinkStub { get; }
    }
}

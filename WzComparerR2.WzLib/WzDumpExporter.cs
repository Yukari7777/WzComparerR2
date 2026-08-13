using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml;

namespace WzComparerR2.WzLib
{
    public static class WzDumpExporter
    {
        public static WzExportResult Export(
            Wz_Node node,
            string outputRoot,
            WzDumpFormat format,
            DumpingOptions dumpOptions,
            WzNodeResolver.ResolvedNode resolutionScope)
        {
            var result = new WzExportResult();
            if (node == null)
            {
                result.AddFailure(CreateFailure("invalid_source", null, "request", "The export node is null."));
                return result;
            }

            dumpOptions ??= DumpingOptions.CreateDefaults();
            if (!TryValidateOptions(dumpOptions, result))
            {
                return result;
            }
            if (format != WzDumpFormat.Json && format != WzDumpFormat.Xml)
            {
                result.AddFailure(CreateFailure("invalid_format", node.FullPathToFile, "options", $"Unsupported dump format: {format}."));
                return result;
            }

            try
            {
                result.OutputRoot = Path.GetFullPath(outputRoot ?? throw new ArgumentNullException(nameof(outputRoot)));
            }
            catch (Exception ex)
            {
                result.AddFailure(CreateFailure("invalid_output_root", node.FullPathToFile, "request", ex.Message));
                return result;
            }

            if (File.Exists(result.OutputRoot))
            {
                result.AddFailure(CreateFailure("invalid_output_root", node.FullPathToFile, "request", "Output must be a directory, but an existing file was provided."));
                return result;
            }

            string logicalPath = NormalizeLogicalPath(node.FullPathToFile);
            string extension = format == WzDumpFormat.Xml ? ".xml" : ".json";
            string documentPath;
            try
            {
                documentPath = GetSafeOutputPath(result.OutputRoot, EncodeRelativeOutputPath(logicalPath + extension));
            }
            catch (Exception ex)
            {
                result.AddFailure(CreateFailure("invalid_output_path", logicalPath, "path", ex.Message));
                return result;
            }

            result.DocumentWritten = !(dumpOptions.DumpExternal && (node.IsCanvasImage() || IsResourceValue(node.Value)));
            result.DocumentPath = result.DocumentWritten ? documentPath : null;

            var context = new WzDumpSerializationContext
            {
                IncludePngDimensions = dumpOptions.IncludePngDimensions,
            };
            var resourceTasks = new List<ResourceTask>();
            if (dumpOptions.DumpRaw || dumpOptions.DumpExternal)
            {
                if (resolutionScope == null)
                {
                    result.AddFailure(CreateFailure("missing_resolution_context", logicalPath, "link", "Raw or external resource export requires a WZ resolution context."));
                    return result;
                }
                BuildResourcePlan(node, resolutionScope, context, resourceTasks, result);
                if (!result.Success)
                {
                    return result;
                }
                if (dumpOptions.DumpExternal && !result.DocumentWritten && resourceTasks.Count == 0)
                {
                    result.AddFailure(CreateFailure("resource_not_found", logicalPath, "resource-plan", "The requested resource subtree contains no supported external resources."));
                    return result;
                }
            }

            string stagingRoot = Path.Combine(Path.GetTempPath(), "wcr2-export-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingRoot);
            try
            {
                if (dumpOptions.DumpRaw)
                {
                    PrepareRawResources(resourceTasks, context, result);
                }
                else if (dumpOptions.DumpExternal)
                {
                    PrepareExternalResources(resourceTasks, stagingRoot, context, result);
                }

                if (!result.Success)
                {
                    return result;
                }

                if (result.DocumentWritten)
                {
                    string stagedDocumentPath = GetSafeOutputPath(stagingRoot, EncodeRelativeOutputPath(logicalPath + extension));
                    WriteDocument(node, stagedDocumentPath, format, dumpOptions, context);
                }

                IReadOnlyList<string> stagedFiles = Directory.GetFiles(stagingRoot, "*", SearchOption.AllDirectories);
                if (!TryCommit(stagingRoot, result.OutputRoot, stagedFiles, result.DocumentWritten ? null : documentPath, out var commitError))
                {
                    result.AddFailure(CreateFailure("commit_failed", logicalPath, "commit", commitError?.Message ?? "Failed to publish staged export files."));
                    return result;
                }

                result.ExternalFileCount = dumpOptions.DumpExternal
                    ? resourceTasks.SelectMany(task => context.GetExternalFiles(task.SourceNode) ?? Array.Empty<string>())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count()
                    : 0;
                return result;
            }
            catch (Exception ex)
            {
                result.AddFailure(CreateFailure("serialization_failed", logicalPath, "serialization", ex.Message));
                return result;
            }
            finally
            {
                DeleteDirectory(stagingRoot);
            }
        }

        public static IEnumerable<Wz_Image> EnumerateImages(Wz_Node node)
        {
            if (node == null)
            {
                yield break;
            }

            Wz_Image image = node.GetValue<Wz_Image>();
            if (image != null)
            {
                yield return image;
                yield break;
            }

            foreach (var child in node.Nodes)
            {
                foreach (var childImage in EnumerateImages(child))
                {
                    yield return childImage;
                }
            }
        }

        private static bool TryValidateOptions(DumpingOptions options, WzExportResult result)
        {
            if (options.DumpRaw && options.DumpExternal)
            {
                result.AddFailure(CreateFailure("invalid_options", null, "options", "dumpRaw and dumpExternal are mutually exclusive."));
            }
            if (options.LeaveReference && !options.DumpExternal)
            {
                result.AddFailure(CreateFailure("invalid_options", null, "options", "leaveReference requires dumpExternal."));
            }
            return result.Success;
        }

        private static void BuildResourcePlan(
            Wz_Node root,
            WzNodeResolver.ResolvedNode resolutionScope,
            WzDumpSerializationContext context,
            List<ResourceTask> resourceTasks,
            WzExportResult result)
        {
            foreach (Wz_Node sourceNode in EnumerateNodes(root))
            {
                bool sourceIsResource = IsResourceValue(sourceNode.Value);
                bool hasReference = HasLink(sourceNode) || sourceNode.Value is Wz_Uol;
                if (!sourceIsResource && !hasReference)
                {
                    continue;
                }

                if (!resolutionScope.TryResolveLinkedNode(sourceNode, out var resolution, out var resolutionFailure))
                {
                    result.AddFailure(FromResolutionFailure(resolutionFailure));
                    continue;
                }

                Wz_Node targetNode = resolution.TargetNode;
                bool targetIsResource = IsResourceValue(targetNode?.Value);
                if (sourceIsResource && !AreCompatibleResourceTypes(sourceNode.Value, targetNode?.Value))
                {
                    result.AddFailure(new WzExportFailure
                    {
                        Code = "resource_type_mismatch",
                        SourcePath = sourceNode.FullPathToFile,
                        LinkType = resolution.LinkType,
                        LinkPath = resolution.LinkPath,
                        TargetPath = targetNode?.FullPathToFile,
                        TargetWzFiles = GetNodeFiles(targetNode),
                        Stage = "resource-type",
                        Reason = $"Linked resource type '{targetNode?.Value?.GetType().Name ?? "null"}' does not match '{sourceNode.Value.GetType().Name}'.",
                        SourceWasLinkStub = resolution.SourceWasLinkStub,
                    });
                    continue;
                }

                if (!targetIsResource)
                {
                    continue;
                }

                context.SetResolvedNode(sourceNode, targetNode);
                resourceTasks.Add(new ResourceTask(sourceNode, targetNode, resolution));
            }
        }

        private static void PrepareRawResources(IEnumerable<ResourceTask> tasks, WzDumpSerializationContext context, WzExportResult result)
        {
            var prepared = new Dictionary<string, IReadOnlyList<byte[]>>(StringComparer.OrdinalIgnoreCase);
            foreach (ResourceTask task in tasks)
            {
                string key = GetResourceKey(task.TargetNode);
                if (!prepared.TryGetValue(key, out var data))
                {
                    try
                    {
                        data = ReadResourceData(task.TargetNode);
                        prepared.Add(key, data);
                    }
                    catch (Exception ex)
                    {
                        result.AddFailure(CreateResourceFailure(task, ex, "decode"));
                        continue;
                    }
                }
                context.SetRawData(task.SourceNode, data);
            }
        }

        private static void PrepareExternalResources(IEnumerable<ResourceTask> tasks, string stagingRoot, WzDumpSerializationContext context, WzExportResult result)
        {
            var prepared = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            var failed = new Dictionary<string, Exception>(StringComparer.OrdinalIgnoreCase);
            foreach (ResourceTask task in tasks)
            {
                string key = GetResourceKey(task.TargetNode);
                if (failed.TryGetValue(key, out var previousError))
                {
                    result.AddFailure(CreateResourceFailure(task, previousError, "decode"));
                    continue;
                }

                if (!prepared.TryGetValue(key, out var files))
                {
                    try
                    {
                        files = WriteExternalResource(task.TargetNode, stagingRoot);
                        prepared.Add(key, files);
                    }
                    catch (Exception ex)
                    {
                        failed.Add(key, ex);
                        result.AddFailure(CreateResourceFailure(task, ex, "decode"));
                        continue;
                    }
                }
                context.SetExternalFiles(task.SourceNode, files);
            }
        }

        private static IReadOnlyList<byte[]> ReadResourceData(Wz_Node node)
        {
            if (node.Value is Wz_Png png)
            {
                var pages = new List<byte[]>(Math.Max(png.ActualPages, 1));
                for (int i = 0; i < Math.Max(png.ActualPages, 1); i++)
                {
                    using (var bitmap = png.ExtractPng(i))
                    using (var stream = new MemoryStream())
                    {
                        bitmap.Save(stream, ImageFormat.Png);
                        pages.Add(stream.ToArray());
                    }
                }
                return pages;
            }
            if (node.Value is Wz_Sound sound)
            {
                byte[] data = sound.ExtractSound();
                if (data == null)
                {
                    data = new byte[sound.DataLength];
                    sound.CopyTo(data, 0);
                }
                return new[] { data };
            }
            if (node.Value is Wz_RawData rawData)
            {
                byte[] data = new byte[rawData.Length];
                rawData.CopyTo(data, 0);
                return new[] { data };
            }
            if (node.Value is Wz_Video video)
            {
                byte[] data = new byte[video.Length];
                video.CopyTo(data, 0);
                return new[] { data };
            }
            throw new InvalidOperationException($"Unsupported external resource type: {node.Value?.GetType().Name ?? "null"}");
        }

        private static IReadOnlyList<string> WriteExternalResource(Wz_Node node, string stagingRoot)
        {
            string logicalPath = NormalizeLogicalPath(node.FullPathToFile);
            string outputPath = EncodeRelativeOutputPath(logicalPath);
            if (node.Value is Wz_Png png)
            {
                int pageCount = Math.Max(png.ActualPages, 1);
                var files = new List<string>(pageCount);
                for (int i = 0; i < pageCount; i++)
                {
                    string pagePath = outputPath + ".png";
                    if (i > 0)
                    {
                        string parent = Path.GetDirectoryName(outputPath.Replace('/', Path.DirectorySeparatorChar));
                        string fileName = Path.GetFileName(outputPath);
                        pagePath = NormalizeLogicalPath(Path.Combine(parent ?? string.Empty, i.ToString(), fileName + ".png"));
                    }
                    string filePath = GetSafeOutputPath(stagingRoot, pagePath);
                    EnsureParentDirectory(filePath);
                    using (var bitmap = png.ExtractPng(i))
                    {
                        bitmap.Save(filePath, ImageFormat.Png);
                    }
                    files.Add(pagePath);
                }
                return files;
            }

            string extension;
            byte[] data;
            if (node.Value is Wz_Sound sound)
            {
                extension = sound.SoundType switch
                {
                    Wz_SoundType.Mp3 => ".mp3",
                    Wz_SoundType.Pcm => ".wav",
                    _ => ".bin",
                };
                data = sound.ExtractSound();
                if (data == null)
                {
                    data = new byte[sound.DataLength];
                    sound.CopyTo(data, 0);
                }
            }
            else if (node.Value is Wz_RawData rawData)
            {
                extension = ".bin";
                data = new byte[rawData.Length];
                rawData.CopyTo(data, 0);
            }
            else if (node.Value is Wz_Video video)
            {
                extension = ".mcv";
                data = new byte[video.Length];
                video.CopyTo(data, 0);
            }
            else
            {
                throw new InvalidOperationException($"Unsupported external resource type: {node.Value?.GetType().Name ?? "null"}");
            }

            string relativePath = outputPath + extension;
            string targetPath = GetSafeOutputPath(stagingRoot, relativePath);
            EnsureParentDirectory(targetPath);
            File.WriteAllBytes(targetPath, data);
            return new[] { relativePath };
        }

        private static void WriteDocument(Wz_Node node, string documentPath, WzDumpFormat format, DumpingOptions options, WzDumpSerializationContext context)
        {
            EnsureParentDirectory(documentPath);
            if (format == WzDumpFormat.Xml)
            {
                var settings = new XmlWriterSettings
                {
                    CloseOutput = false,
                    Indent = true,
                    Encoding = Encoding.UTF8,
                    CheckCharacters = true,
                    NewLineChars = Environment.NewLine,
                    NewLineOnAttributes = false,
                };
                using (var stream = new FileStream(documentPath, FileMode.Create, FileAccess.Write))
                using (var writer = XmlWriter.Create(stream, settings))
                {
                    writer.WriteStartDocument(true);
                    node.DumpAsXml(writer, options.DumpRaw, options.LeaveReference, context);
                    writer.WriteEndDocument();
                }
                return;
            }

            var jsonOptions = new JsonWriterOptions
            {
                Indented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                SkipValidation = false,
            };
            using (var stream = new FileStream(documentPath, FileMode.Create, FileAccess.Write))
            using (var writer = new Utf8JsonWriter(stream, jsonOptions))
            {
                node.DumpAsJson(writer, options.DumpRaw, options.LeaveReference, context);
            }
        }

        private static bool TryCommit(string stagingRoot, string outputRoot, IReadOnlyList<string> stagedFiles, string obsoleteDocument, out Exception error)
        {
            error = null;
            string backupRoot = Path.Combine(Path.GetTempPath(), "wcr2-export-backup-" + Guid.NewGuid().ToString("N"));
            var backups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var changed = new List<string>();
            try
            {
                for (int i = 0; i < stagedFiles.Count; i++)
                {
                    string stagedFile = stagedFiles[i];
                    string relativePath = MakeRelativePath(stagingRoot, stagedFile);
                    string destination = GetSafeOutputPath(outputRoot, relativePath);
                    BackupFile(destination, backupRoot, backups);
                    EnsureParentDirectory(destination);
                    File.Copy(stagedFile, destination, true);
                    changed.Add(destination);
                }

                if (!string.IsNullOrEmpty(obsoleteDocument) && File.Exists(obsoleteDocument))
                {
                    BackupFile(obsoleteDocument, backupRoot, backups);
                    File.Delete(obsoleteDocument);
                    changed.Add(obsoleteDocument);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex;
                for (int i = changed.Count - 1; i >= 0; i--)
                {
                    string destination = changed[i];
                    try
                    {
                        if (backups.TryGetValue(destination, out var backup))
                        {
                            EnsureParentDirectory(destination);
                            File.Copy(backup, destination, true);
                        }
                        else if (File.Exists(destination))
                        {
                            File.Delete(destination);
                        }
                    }
                    catch
                    {
                    }
                }
                return false;
            }
            finally
            {
                DeleteDirectory(backupRoot);
            }
        }

        private static void BackupFile(string path, string backupRoot, IDictionary<string, string> backups)
        {
            if (!File.Exists(path) || backups.ContainsKey(path))
            {
                return;
            }
            Directory.CreateDirectory(backupRoot);
            string backup = Path.Combine(backupRoot, backups.Count.ToString("D8"));
            File.Copy(path, backup, true);
            backups[path] = backup;
        }

        private static IEnumerable<Wz_Node> EnumerateNodes(Wz_Node node)
        {
            yield return node;
            foreach (Wz_Node child in node.Nodes)
            {
                foreach (Wz_Node descendant in EnumerateNodes(child))
                {
                    yield return descendant;
                }
            }
        }

        private static bool HasLink(Wz_Node node)
        {
            return node?.Nodes["source"].GetValueEx<string>(null) != null
                || node?.Nodes["_inlink"].GetValueEx<string>(null) != null
                || node?.Nodes["_outlink"].GetValueEx<string>(null) != null;
        }

        private static bool IsResourceValue(object value)
        {
            return value is Wz_Png || value is Wz_Sound || value is Wz_RawData || value is Wz_Video;
        }

        private static bool AreCompatibleResourceTypes(object source, object target)
        {
            return source != null && target != null && source.GetType() == target.GetType();
        }

        private static string GetResourceKey(Wz_Node node)
        {
            return NormalizeLogicalPath(node.FullPathToFile) + "|" + node.Value.GetType().FullName;
        }

        private static WzExportFailure CreateResourceFailure(ResourceTask task, Exception error, string stage)
        {
            string message = error?.Message ?? "Resource decoding failed.";
            bool unsupported = message.IndexOf("unsupported", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("unknown texture format", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("does not support", StringComparison.OrdinalIgnoreCase) >= 0;
            return new WzExportFailure
            {
                Code = unsupported ? "unsupported_resource_format" : "resource_decode_failed",
                SourcePath = task.SourceNode.FullPathToFile,
                LinkType = task.Resolution.LinkType,
                LinkPath = task.Resolution.LinkPath,
                TargetPath = task.TargetNode.FullPathToFile,
                TargetWzFiles = GetNodeFiles(task.TargetNode),
                Stage = stage,
                Reason = message,
                SourceWasLinkStub = task.Resolution.SourceWasLinkStub,
            };
        }

        private static WzExportFailure FromResolutionFailure(WzResolutionFailure failure)
        {
            return new WzExportFailure
            {
                Code = failure?.Code ?? "link_resolution_failed",
                SourcePath = failure?.SourcePath,
                LinkType = failure?.LinkType,
                LinkPath = failure?.LinkPath,
                TargetPath = failure?.TargetPath,
                TargetWzFiles = failure?.TargetWzFiles ?? Array.Empty<string>(),
                Stage = failure?.Stage ?? "link",
                Reason = failure?.Reason ?? "Link resolution failed.",
                SourceWasLinkStub = failure?.SourceWasLinkStub == true,
            };
        }

        private static WzExportFailure CreateFailure(string code, string sourcePath, string stage, string reason)
        {
            return new WzExportFailure
            {
                Code = code,
                SourcePath = sourcePath,
                Stage = stage,
                Reason = reason,
            };
        }

        private static IReadOnlyList<string> GetNodeFiles(Wz_Node node)
        {
            Wz_Image image = node?.GetNodeWzImage();
            return image?.WzFile is Wz_File wzFile && !string.IsNullOrEmpty(wzFile.Header?.FileName)
                ? new[] { wzFile.Header.FileName }
                : Array.Empty<string>();
        }

        private static string NormalizeLogicalPath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/').Trim('/');
        }

        private static string EncodeRelativeOutputPath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            {
                throw new InvalidOperationException("Export path must be a non-empty relative path.");
            }

            var encoded = new List<string>();
            foreach (string segment in relativePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (segment == "." || segment == "..")
                {
                    throw new InvalidOperationException($"Invalid export path segment: {segment}");
                }
                encoded.Add(EncodeOutputPathSegment(segment));
            }
            if (encoded.Count == 0)
            {
                throw new InvalidOperationException("Export path must contain at least one segment.");
            }
            return string.Join("/", encoded);
        }

        private static string EncodeOutputPathSegment(string segment)
        {
            var builder = new StringBuilder(segment.Length);
            for (int i = 0; i < segment.Length; i++)
            {
                char value = segment[i];
                bool trailingDotOrSpace = i == segment.Length - 1 && (value == '.' || value == ' ');
                if (value == '%' || value < 32 || "<>:\"/\\|?*".IndexOf(value) >= 0 || trailingDotOrSpace)
                {
                    AppendPercentEncoded(builder, value);
                }
                else
                {
                    builder.Append(value);
                }
            }

            string encoded = builder.ToString();
            if (IsReservedWindowsFileName(segment))
            {
                builder.Clear();
                AppendPercentEncoded(builder, segment[0]);
                builder.Append(encoded.Substring(1));
                encoded = builder.ToString();
            }
            return encoded;
        }

        private static void AppendPercentEncoded(StringBuilder builder, char value)
        {
            foreach (byte item in Encoding.UTF8.GetBytes(new[] { value }))
            {
                builder.Append('%');
                builder.Append(item.ToString("X2"));
            }
        }

        private static bool IsReservedWindowsFileName(string segment)
        {
            string stem = segment.TrimEnd(' ', '.').Split('.')[0];
            if (string.Equals(stem, "CON", StringComparison.OrdinalIgnoreCase)
                || string.Equals(stem, "PRN", StringComparison.OrdinalIgnoreCase)
                || string.Equals(stem, "AUX", StringComparison.OrdinalIgnoreCase)
                || string.Equals(stem, "NUL", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return stem.Length == 4
                && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                    || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                && stem[3] >= '1'
                && stem[3] <= '9';
        }

        private static string GetSafeOutputPath(string root, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            {
                throw new InvalidOperationException("Export path must be a non-empty relative path.");
            }

            string combined = root;
            foreach (string segment in relativePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (segment == "." || segment == ".." || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    throw new InvalidOperationException($"Invalid export path segment: {segment}");
                }
                combined = Path.Combine(combined, segment);
            }

            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(combined);
            if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Export path escapes the output root.");
            }
            return fullPath;
        }

        private static string MakeRelativePath(string root, string path)
        {
            string rootWithSeparator = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var rootUri = new Uri(rootWithSeparator);
            var pathUri = new Uri(Path.GetFullPath(path));
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString()).Replace('/', Path.DirectorySeparatorChar);
        }

        private static void EnsureParentDirectory(string path)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        private static void DeleteDirectory(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
            }
        }

        private sealed class ResourceTask
        {
            public ResourceTask(Wz_Node sourceNode, Wz_Node targetNode, WzLinkResolution resolution)
            {
                this.SourceNode = sourceNode;
                this.TargetNode = targetNode;
                this.Resolution = resolution;
            }

            public Wz_Node SourceNode { get; }
            public Wz_Node TargetNode { get; }
            public WzLinkResolution Resolution { get; }
        }
    }
}

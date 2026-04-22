using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using WzComparerR2.WzLib;

namespace WzComparerR2.CLI
{
    internal static class Program
    {
        private const int ExitSuccess = 0;
        private const int ExitUsageError = 2;
        private const int ExitLoadError = 3;
        private const int ExitExportError = 4;

        private static readonly HashSet<string> DumpFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "--dump-raw",
            "--no-dump-raw",
            "--dump-external",
            "--no-dump-external",
            "--leave-reference",
            "--no-leave-reference",
            "--omit-redundant-canvas-artifacts",
            "--no-omit-redundant-canvas-artifacts",
            "--preserve-full-path-for-single-image",
            "--no-preserve-full-path-for-single-image",
        };

        private static int Main(string[] args)
        {
            ConfigureProcess();

            if (args.Length == 0)
            {
                WriteUsage(Console.Error);
                return ExitUsageError;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "session":
                    return RunSession(args.Skip(1).ToArray());

                case "export":
                    return RunOneShotExport(args.Skip(1).ToArray());

                case "help":
                case "--help":
                case "-h":
                    WriteUsage(Console.Out);
                    return ExitSuccess;

                default:
                    WriteError($"Unknown command: {args[0]}");
                    WriteUsage(Console.Error);
                    return ExitUsageError;
            }
        }

        private static void ConfigureProcess()
        {
            Console.InputEncoding = new UTF8Encoding(false);
            Console.OutputEncoding = new UTF8Encoding(false);
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            Wz_Structure.DefaultAutoDetectExtFiles = true;
            Wz_Structure.DefaultImgCheckDisabled = false;
        }

        private static int RunSession(string[] args)
        {
            if (!TryParseNamedArguments(args, out var options, out var flags, out var errorMessage))
            {
                WriteError(errorMessage);
                WriteUsage(Console.Error);
                return ExitUsageError;
            }

            string basePath = GetRequiredOption(options, "--base");
            if (basePath == null)
            {
                WriteError("Missing required option: --base");
                WriteUsage(Console.Error);
                return ExitUsageError;
            }

            if (flags.Count > 0)
            {
                WriteError("Session mode does not accept dump flags at startup.");
                WriteUsage(Console.Error);
                return ExitUsageError;
            }

            LoadedWzSession session;
            try
            {
                session = LoadedWzSession.Open(basePath);
            }
            catch (Exception ex)
            {
                WriteJsonResponse(new
                {
                    ok = false,
                    @event = "ready",
                    error = ex.Message,
                });
                return ExitLoadError;
            }

            using (session)
            {
                WriteJsonResponse(new
                {
                    ok = true,
                    @event = "ready",
                    source = session.SourcePath,
                    root = session.RootNode?.Text,
                });

                string line;
                while ((line = Console.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    switch (HandleSessionCommand(session, line))
                    {
                        case SessionCommandResult.Exit:
                            return ExitSuccess;

                        case SessionCommandResult.TerminateWithError:
                            return ExitExportError;
                    }
                }
            }

            return ExitSuccess;
        }

        private static int RunOneShotExport(string[] args)
        {
            if (!TryParseNamedArguments(args, out var options, out var flags, out var errorMessage))
            {
                WriteError(errorMessage);
                WriteUsage(Console.Error);
                return ExitUsageError;
            }

            string basePath = GetRequiredOption(options, "--base");
            string logicalPath = GetRequiredOption(options, "--path");
            string outputPath = GetRequiredOption(options, "--output");
            if (basePath == null || logicalPath == null || outputPath == null)
            {
                WriteError("One-shot export requires --base, --path, and --output.");
                WriteUsage(Console.Error);
                return ExitUsageError;
            }

            try
            {
                using (var session = LoadedWzSession.Open(basePath))
                {
                    if (!session.TryExportJson(logicalPath, outputPath, BuildDumpingOptions(flags), out var error))
                    {
                        WriteError(error?.Message ?? "Export failed.");
                        return ExitExportError;
                    }

                    WriteJsonResponse(new
                    {
                        ok = true,
                        command = "export",
                        path = logicalPath,
                        output = Path.GetFullPath(outputPath),
                    });
                    return ExitSuccess;
                }
            }
            catch (Exception ex)
            {
                WriteError(ex.Message);
                return ExitLoadError;
            }
        }

        private static SessionCommandResult HandleSessionCommand(LoadedWzSession session, string line)
        {
            string requestId = null;

            try
            {
                using (var document = JsonDocument.Parse(line))
                {
                    JsonElement root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        WriteJsonResponse(new
                        {
                            ok = false,
                            error = "Session command must be a JSON object.",
                        });
                        return SessionCommandResult.Continue;
                    }

                    if (root.TryGetProperty("requestId", out var requestIdElement))
                    {
                        requestId = requestIdElement.ToString();
                    }

                    string command = GetRequiredString(root, "command");
                    switch (command.ToLowerInvariant())
                    {
                        case "ping":
                            WriteJsonResponse(new
                            {
                                ok = true,
                                requestId,
                                command = "ping",
                            });
                            return SessionCommandResult.Continue;

                        case "quit":
                        case "exit":
                            WriteJsonResponse(new
                            {
                                ok = true,
                                requestId,
                                @event = "bye",
                            });
                            return SessionCommandResult.Exit;

                        case "export":
                            HandleExportCommand(session, root, requestId);
                            return SessionCommandResult.Continue;

                        default:
                            WriteJsonResponse(new
                            {
                                ok = false,
                                requestId,
                                command,
                                error = $"Unknown session command: {command}",
                            });
                            return SessionCommandResult.Continue;
                    }
                }
            }
            catch (Exception ex)
            {
                WriteJsonResponse(new
                {
                    ok = false,
                    requestId,
                    error = ex.Message,
                });
                return SessionCommandResult.Continue;
            }
        }

        private static void HandleExportCommand(LoadedWzSession session, JsonElement root, string requestId)
        {
            string logicalPath = GetRequiredString(root, "path");
            string outputPath = GetRequiredString(root, "output");
            var options = ReadDumpingOptions(root);
            var stopwatch = Stopwatch.StartNew();

            if (!session.TryExportJson(logicalPath, outputPath, options, out var error))
            {
                WriteJsonResponse(new
                {
                    ok = false,
                    requestId,
                    command = "export",
                    path = logicalPath,
                    output = Path.GetFullPath(outputPath),
                    error = error?.Message ?? "Export failed.",
                });
                return;
            }

            stopwatch.Stop();
            WriteJsonResponse(new
            {
                ok = true,
                requestId,
                command = "export",
                path = logicalPath,
                output = Path.GetFullPath(outputPath),
                durationMs = stopwatch.ElapsedMilliseconds,
            });
        }

        private static DumpingOptions ReadDumpingOptions(JsonElement root)
        {
            var options = DumpingOptions.CreateJsonDefaults();
            if (root.TryGetProperty("dumpRaw", out var dumpRaw)
                && (dumpRaw.ValueKind == JsonValueKind.True || dumpRaw.ValueKind == JsonValueKind.False))
            {
                options.DumpRaw = dumpRaw.GetBoolean();
            }

            if (root.TryGetProperty("dumpExternal", out var dumpExternal)
                && (dumpExternal.ValueKind == JsonValueKind.True || dumpExternal.ValueKind == JsonValueKind.False))
            {
                options.DumpExternal = dumpExternal.GetBoolean();
            }

            if (root.TryGetProperty("leaveReference", out var leaveReference)
                && (leaveReference.ValueKind == JsonValueKind.True || leaveReference.ValueKind == JsonValueKind.False))
            {
                options.LeaveReference = leaveReference.GetBoolean();
            }

            if (root.TryGetProperty("omitRedundantCanvasArtifacts", out var omitRedundantCanvasArtifacts)
                && (omitRedundantCanvasArtifacts.ValueKind == JsonValueKind.True || omitRedundantCanvasArtifacts.ValueKind == JsonValueKind.False))
            {
                options.OmitRedundantCanvasArtifacts = omitRedundantCanvasArtifacts.GetBoolean();
            }

            if (root.TryGetProperty("preserveFullPathForSingleImage", out var preserveFullPathForSingleImage)
                && (preserveFullPathForSingleImage.ValueKind == JsonValueKind.True || preserveFullPathForSingleImage.ValueKind == JsonValueKind.False))
            {
                options.PreserveFullPathForSingleImage = preserveFullPathForSingleImage.GetBoolean();
            }

            return options;
        }

        private static bool TryParseNamedArguments(string[] args, out Dictionary<string, string> options, out HashSet<string> flags, out string errorMessage)
        {
            options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            errorMessage = null;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (!arg.StartsWith("--", StringComparison.Ordinal))
                {
                    errorMessage = $"Unexpected argument: {arg}";
                    return false;
                }

                if (DumpFlags.Contains(arg))
                {
                    flags.Add(arg);
                    continue;
                }

                if (i + 1 >= args.Length)
                {
                    errorMessage = $"Missing value for argument: {arg}";
                    return false;
                }

                options[arg] = args[++i];
            }

            return true;
        }

        private static DumpingOptions BuildDumpingOptions(HashSet<string> flags)
        {
            var options = DumpingOptions.CreateJsonDefaults();
            ApplyFlag(flags, "--dump-raw", "--no-dump-raw", value => options.DumpRaw = value);
            ApplyFlag(flags, "--dump-external", "--no-dump-external", value => options.DumpExternal = value);
            ApplyFlag(flags, "--leave-reference", "--no-leave-reference", value => options.LeaveReference = value);
            ApplyFlag(flags, "--omit-redundant-canvas-artifacts", "--no-omit-redundant-canvas-artifacts", value => options.OmitRedundantCanvasArtifacts = value);
            ApplyFlag(flags, "--preserve-full-path-for-single-image", "--no-preserve-full-path-for-single-image", value => options.PreserveFullPathForSingleImage = value);
            return options;
        }

        private static void ApplyFlag(HashSet<string> flags, string positiveFlag, string negativeFlag, Action<bool> apply)
        {
            if (flags.Contains(positiveFlag))
            {
                apply(true);
            }
            else if (flags.Contains(negativeFlag))
            {
                apply(false);
            }
        }

        private enum SessionCommandResult
        {
            Continue,
            Exit,
            TerminateWithError,
        }

        private static string GetRequiredOption(Dictionary<string, string> options, string name)
        {
            if (options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return null;
        }

        private static string GetRequiredString(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            {
                throw new InvalidOperationException($"Missing required string property: {name}");
            }

            return value.GetString();
        }

        private static void WriteJsonResponse(object payload)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(payload));
            Console.Out.Flush();
        }

        private static void WriteError(string message)
        {
            Console.Error.WriteLine(message);
        }

        private static void WriteUsage(TextWriter writer)
        {
            writer.WriteLine("Usage:");
            writer.WriteLine("  WzComparerR2.CLI session --base <Base.wz>");
            writer.WriteLine("  WzComparerR2.CLI export --base <Base.wz> --path <logical-path> --output <file-or-root> [--dump-raw|--no-dump-raw] [--dump-external|--no-dump-external] [--leave-reference|--no-leave-reference] [--omit-redundant-canvas-artifacts|--no-omit-redundant-canvas-artifacts] [--preserve-full-path-for-single-image|--no-preserve-full-path-for-single-image]");
            writer.WriteLine();
            writer.WriteLine("Session stdin protocol:");
            writer.WriteLine("  {\"command\":\"export\",\"path\":\"Mob/8880450.img\",\"output\":\"D:/MyApp/public/wz/Mob/8880450.img.json\"}");
            writer.WriteLine("  {\"command\":\"export\",\"path\":\"Skill/000.img\",\"output\":\"D:/MyApp/public/wz/Skill/000.img.json\",\"dumpExternal\":true,\"preserveFullPathForSingleImage\":true}");
            writer.WriteLine("  {\"command\":\"export\",\"path\":\"Mob/8880450.img/info\",\"output\":\"D:/MyApp/public/wz/Mob/8880450.info.json\"}");
            writer.WriteLine("  {\"command\":\"quit\"}");
        }

        private sealed class LoadedWzSession : IDisposable
        {
            private readonly Wz_Structure structure;
            private bool disposed;

            private LoadedWzSession(string sourcePath, Wz_Structure structure)
            {
                this.SourcePath = sourcePath;
                this.structure = structure;
            }

            public string SourcePath { get; }

            public Wz_Node RootNode => this.structure.WzNode;

            public static LoadedWzSession Open(string sourcePath)
            {
                string fullPath = Path.GetFullPath(sourcePath);
                if (!File.Exists(fullPath))
                {
                    throw new FileNotFoundException("WZ source file not found.", fullPath);
                }

                var structure = new Wz_Structure();
                try
                {
                    LoadStructure(structure, fullPath);
                    return new LoadedWzSession(fullPath, structure);
                }
                catch
                {
                    structure.Clear();
                    throw;
                }
            }

            public bool TryExportJson(string logicalPath, string outputPath, DumpingOptions options, out Exception error)
            {
                error = null;

                WzNodeResolver.ResolvedNode resolvedNode = null;
                try
                {
                    if (!WzNodeResolver.TryResolveExactPath(this.RootNode, logicalPath, out resolvedNode, out error))
                    {
                        return false;
                    }

                    if (!resolvedNode.IsInsideImage)
                    {
                        error = new InvalidOperationException("Exact path must point to an image or a node inside an image.");
                        return false;
                    }

                    ResolveJsonExportPaths(resolvedNode.Node, outputPath, options, out string fullOutputPath, out string exportRoot);
                    return WzDumpExporter.TryExportNodeAsJson(resolvedNode.Node, fullOutputPath, exportRoot, options, out error);
                }
                finally
                {
                    resolvedNode?.Dispose();
                }
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                this.structure.Clear();
                disposed = true;
            }

            private static void LoadStructure(Wz_Structure structure, string sourcePath)
            {
                string extension = Path.GetExtension(sourcePath);
                if (string.Equals(extension, ".ms", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(extension, ".mn", StringComparison.OrdinalIgnoreCase))
                {
                    structure.LoadMsFile(sourcePath);
                    return;
                }

                if (structure.IsKMST1125WzFormat(sourcePath))
                {
                    structure.LoadKMST1125DataWz(sourcePath);
                    if (string.Equals(Path.GetFileName(sourcePath), "Base.wz", StringComparison.OrdinalIgnoreCase))
                    {
                        string packsDir = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(sourcePath)), "Packs");
                        if (Directory.Exists(packsDir))
                        {
                            foreach (var ext in new[] { ".ms", ".mn" })
                            {
                                foreach (var msFile in Directory.GetFiles(packsDir, $"*{ext}"))
                                {
                                    structure.LoadMsFile(msFile);
                                }
                            }
                        }
                    }
                    return;
                }

                structure.Load(sourcePath, true);
            }

            private static void ResolveJsonExportPaths(Wz_Node node, string outputPath, DumpingOptions options, out string fullOutputPath, out string exportRoot)
            {
                fullOutputPath = Path.GetFullPath(outputPath);
                exportRoot = Path.GetDirectoryName(fullOutputPath);

                if (options?.PreserveFullPathForSingleImage != true)
                {
                    return;
                }

                Wz_Image image = node?.GetValue<Wz_Image>();
                if (image == null)
                {
                    return;
                }

                string relativeOutputPath = image.Node.FullPathToFile.Replace('\\', Path.DirectorySeparatorChar) + ".json";
                if (TryInferExportRootFromPreservedOutput(fullOutputPath, relativeOutputPath, out string inferredExportRoot))
                {
                    exportRoot = inferredExportRoot;
                    fullOutputPath = Path.Combine(exportRoot, relativeOutputPath);
                    return;
                }

                if (LooksLikeDirectoryPath(outputPath))
                {
                    exportRoot = Path.GetFullPath(outputPath);
                    fullOutputPath = Path.Combine(exportRoot, relativeOutputPath);
                }
            }

            private static bool TryInferExportRootFromPreservedOutput(string fullOutputPath, string relativeOutputPath, out string exportRoot)
            {
                string suffix = Path.DirectorySeparatorChar + relativeOutputPath;
                if (fullOutputPath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    exportRoot = fullOutputPath.Substring(0, fullOutputPath.Length - suffix.Length);
                    if (string.IsNullOrEmpty(exportRoot))
                    {
                        exportRoot = Path.GetPathRoot(fullOutputPath);
                    }
                    return !string.IsNullOrEmpty(exportRoot);
                }

                exportRoot = null;
                return false;
            }

            private static bool LooksLikeDirectoryPath(string outputPath)
            {
                if (string.IsNullOrWhiteSpace(outputPath))
                {
                    return false;
                }

                if (outputPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                    || outputPath.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                {
                    return true;
                }

                if (Directory.Exists(outputPath))
                {
                    return true;
                }

                return !Path.HasExtension(outputPath);
            }
        }
    }
}
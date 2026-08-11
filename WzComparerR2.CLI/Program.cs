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
            "--dump-external",
            "--leave-reference",
        };

        private static readonly HashSet<string> ValueOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "--base",
            "--path",
            "--output",
            "--format",
        };

        private static readonly JsonSerializerOptions ResponseJsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
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
            if (!TryParseNamedArguments(args, out var namedOptions, out var flags, out var parseError))
            {
                WriteError(parseError);
                WriteUsage(Console.Error);
                return ExitUsageError;
            }

            string basePath = GetRequiredOption(namedOptions, "--base");
            if (basePath == null || namedOptions.Count != 1 || flags.Count > 0)
            {
                WriteError("Session mode accepts only --base <Base.wz> at startup.");
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
                WriteJsonResponse(new { ok = false, @event = "ready", error = ex.Message });
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
                    if (HandleSessionCommand(session, line) == SessionCommandResult.Exit)
                    {
                        return ExitSuccess;
                    }
                }
            }
            return ExitSuccess;
        }

        private static int RunOneShotExport(string[] args)
        {
            if (!TryParseNamedArguments(args, out var namedOptions, out var flags, out var parseError))
            {
                WriteError(parseError);
                WriteUsage(Console.Error);
                return ExitUsageError;
            }

            string basePath = GetRequiredOption(namedOptions, "--base");
            string logicalPath = GetRequiredOption(namedOptions, "--path");
            string outputRoot = GetRequiredOption(namedOptions, "--output");
            if (basePath == null || logicalPath == null || outputRoot == null)
            {
                WriteError("One-shot export requires --base, --path, and --output.");
                WriteUsage(Console.Error);
                return ExitUsageError;
            }
            if (!TryParseFormat(namedOptions.TryGetValue("--format", out var value) ? value : null, out var format, out parseError))
            {
                WriteError(parseError);
                return ExitUsageError;
            }

            var stopwatch = Stopwatch.StartNew();
            LoadedWzSession session;
            try
            {
                session = LoadedWzSession.Open(basePath);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                var result = new WzExportResult();
                result.AddFailure(new WzExportFailure
                {
                    Code = "source_load_failed",
                    SourcePath = logicalPath,
                    TargetWzFiles = new[] { basePath },
                    Stage = "load",
                    Reason = ex.Message,
                });
                WriteExportResponse(null, logicalPath, outputRoot, format, result, stopwatch.ElapsedMilliseconds);
                return ExitLoadError;
            }

            using (session)
            {
                WzExportResult result;
                try
                {
                    result = session.Export(logicalPath, outputRoot, format, BuildDumpingOptions(flags));
                }
                catch (Exception ex)
                {
                    result = new WzExportResult();
                    result.AddFailure(new WzExportFailure
                    {
                        Code = "export_failed",
                        SourcePath = logicalPath,
                        Stage = "export",
                        Reason = ex.Message,
                    });
                }
                stopwatch.Stop();
                WriteExportResponse(null, logicalPath, outputRoot, format, result, stopwatch.ElapsedMilliseconds);
                return result.Success ? ExitSuccess : ExitExportError;
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
                        throw new InvalidOperationException("Session command must be a JSON object.");
                    }
                    if (root.TryGetProperty("requestId", out var requestIdElement))
                    {
                        requestId = requestIdElement.ToString();
                    }

                    string command = GetRequiredString(root, "command");
                    switch (command.ToLowerInvariant())
                    {
                        case "ping":
                            WriteJsonResponse(new { ok = true, requestId, command = "ping" });
                            return SessionCommandResult.Continue;
                        case "quit":
                        case "exit":
                            WriteJsonResponse(new { ok = true, requestId, @event = "bye" });
                            return SessionCommandResult.Exit;
                        case "export":
                            HandleExportCommand(session, root, requestId);
                            return SessionCommandResult.Continue;
                        default:
                            WriteJsonResponse(new { ok = false, requestId, command, error = $"Unknown session command: {command}" });
                            return SessionCommandResult.Continue;
                    }
                }
            }
            catch (Exception ex)
            {
                WriteJsonResponse(new { ok = false, requestId, error = ex.Message });
                return SessionCommandResult.Continue;
            }
        }

        private static void HandleExportCommand(LoadedWzSession session, JsonElement root, string requestId)
        {
            string logicalPath = GetRequiredString(root, "path");
            string outputRoot = GetRequiredString(root, "output");
            WzDumpFormat format = WzDumpFormat.Json;
            var stopwatch = Stopwatch.StartNew();
            WzExportResult result;
            try
            {
                format = ReadFormat(root);
                DumpingOptions options = ReadDumpingOptions(root);
                result = session.Export(logicalPath, outputRoot, format, options);
            }
            catch (Exception ex)
            {
                result = new WzExportResult();
                result.AddFailure(new WzExportFailure
                {
                    Code = "export_failed",
                    SourcePath = logicalPath,
                    Stage = "request",
                    Reason = ex.Message,
                });
            }
            stopwatch.Stop();
            WriteExportResponse(requestId, logicalPath, outputRoot, format, result, stopwatch.ElapsedMilliseconds);
        }

        private static void WriteExportResponse(string requestId, string logicalPath, string outputRoot, WzDumpFormat format, WzExportResult result, long durationMs)
        {
            string absoluteOutput;
            try
            {
                absoluteOutput = Path.GetFullPath(outputRoot);
            }
            catch
            {
                absoluteOutput = outputRoot;
            }
            if (result.Success)
            {
                WriteJsonResponse(new
                {
                    ok = true,
                    requestId,
                    command = "export",
                    path = logicalPath,
                    output = absoluteOutput,
                    format = FormatName(format),
                    document = result.DocumentPath,
                    documentWritten = result.DocumentWritten,
                    externalFileCount = result.ExternalFileCount,
                    durationMs,
                });
                return;
            }

            WriteJsonResponse(new
            {
                ok = false,
                requestId,
                command = "export",
                path = logicalPath,
                output = absoluteOutput,
                format = FormatName(format),
                error = SummarizeFailures(result.Failures),
                failures = result.Failures,
                durationMs,
            });
        }

        private static DumpingOptions ReadDumpingOptions(JsonElement root)
        {
            return new DumpingOptions
            {
                DumpRaw = ReadOptionalBoolean(root, "dumpRaw"),
                DumpExternal = ReadOptionalBoolean(root, "dumpExternal"),
                LeaveReference = ReadOptionalBoolean(root, "leaveReference"),
            };
        }

        private static bool ReadOptionalBoolean(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out var value))
            {
                return false;
            }
            if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False)
            {
                throw new InvalidOperationException($"Property '{name}' must be a boolean.");
            }
            return value.GetBoolean();
        }

        private static WzDumpFormat ReadFormat(JsonElement root)
        {
            string value = null;
            if (root.TryGetProperty("format", out var formatElement))
            {
                if (formatElement.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidOperationException("Property 'format' must be 'json' or 'xml'.");
                }
                value = formatElement.GetString();
            }
            if (!TryParseFormat(value, out var format, out var error))
            {
                throw new InvalidOperationException(error);
            }
            return format;
        }

        private static bool TryParseFormat(string value, out WzDumpFormat format, out string error)
        {
            if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "json", StringComparison.OrdinalIgnoreCase))
            {
                format = WzDumpFormat.Json;
                error = null;
                return true;
            }
            if (string.Equals(value, "xml", StringComparison.OrdinalIgnoreCase))
            {
                format = WzDumpFormat.Xml;
                error = null;
                return true;
            }
            format = default;
            error = $"Unsupported format: {value}. Expected 'json' or 'xml'.";
            return false;
        }

        private static bool TryParseNamedArguments(string[] args, out Dictionary<string, string> options, out HashSet<string> flags, out string error)
        {
            options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            error = null;
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (DumpFlags.Contains(arg))
                {
                    flags.Add(arg);
                    continue;
                }
                if (!ValueOptions.Contains(arg))
                {
                    error = $"Unknown option: {arg}";
                    return false;
                }
                if (i + 1 >= args.Length)
                {
                    error = $"Missing value for argument: {arg}";
                    return false;
                }
                options[arg] = args[++i];
            }
            return true;
        }

        private static DumpingOptions BuildDumpingOptions(HashSet<string> flags)
        {
            return new DumpingOptions
            {
                DumpRaw = flags.Contains("--dump-raw"),
                DumpExternal = flags.Contains("--dump-external"),
                LeaveReference = flags.Contains("--leave-reference"),
            };
        }

        private static string GetRequiredOption(Dictionary<string, string> options, string name)
        {
            return options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
        }

        private static string GetRequiredString(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            {
                throw new InvalidOperationException($"Missing required string property: {name}");
            }
            return value.GetString();
        }

        private static string SummarizeFailures(IReadOnlyList<WzExportFailure> failures)
        {
            if (failures == null || failures.Count == 0)
            {
                return "Export failed.";
            }
            string reason = failures[0].Reason ?? failures[0].Code ?? "Export failed.";
            return failures.Count == 1 ? reason : $"{failures.Count} export failures. First: {reason}";
        }

        private static string FormatName(WzDumpFormat format)
        {
            return format == WzDumpFormat.Xml ? "xml" : "json";
        }

        private static void WriteJsonResponse(object payload)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(payload, ResponseJsonOptions));
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
            writer.WriteLine("  WzComparerR2.CLI export --base <Base.wz> --path <logical-path> --output <output-root> [--format json|xml] [--dump-raw|--dump-external] [--leave-reference]");
            writer.WriteLine();
            writer.WriteLine("Session stdin protocol:");
            writer.WriteLine("  {\"command\":\"export\",\"path\":\"Mob/8880450.img\",\"output\":\"D:/MyApp/public/wz\",\"dumpExternal\":true}");
            writer.WriteLine("  {\"command\":\"export\",\"path\":\"Mob/8880450.img/info\",\"output\":\"D:/MyApp/public/wz\",\"format\":\"xml\"}");
            writer.WriteLine("  {\"command\":\"quit\"}");
        }

        private enum SessionCommandResult
        {
            Continue,
            Exit,
        }

        private sealed class LoadedWzSession : IDisposable
        {
            private readonly Wz_Structure structure;
            private readonly WzNodeResolver resolver;
            private bool disposed;

            private LoadedWzSession(string sourcePath, Wz_Structure structure)
            {
                this.SourcePath = sourcePath;
                this.structure = structure;
                this.resolver = new WzNodeResolver(structure, sourcePath);
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

            public WzExportResult Export(string logicalPath, string outputRoot, WzDumpFormat format, DumpingOptions options)
            {
                if (!this.resolver.TryResolveExactPath(logicalPath, out var resolvedNode, out var resolutionFailure))
                {
                    var failed = new WzExportResult();
                    failed.AddFailure(new WzExportFailure
                    {
                        Code = resolutionFailure?.Code ?? "path_resolution_failed",
                        SourcePath = resolutionFailure?.SourcePath ?? logicalPath,
                        LinkType = resolutionFailure?.LinkType,
                        LinkPath = resolutionFailure?.LinkPath,
                        TargetPath = resolutionFailure?.TargetPath,
                        TargetWzFiles = resolutionFailure?.TargetWzFiles ?? Array.Empty<string>(),
                        Stage = resolutionFailure?.Stage ?? "path",
                        Reason = resolutionFailure?.Reason ?? "Exact path could not be resolved.",
                        SourceWasLinkStub = resolutionFailure?.SourceWasLinkStub == true,
                    });
                    return failed;
                }

                using (resolvedNode)
                {
                    if (!resolvedNode.IsInsideImage)
                    {
                        var failed = new WzExportResult();
                        failed.AddFailure(new WzExportFailure
                        {
                            Code = "not_inside_image",
                            SourcePath = logicalPath,
                            Stage = "path",
                            Reason = "Exact path must point to an image or a node inside an image.",
                        });
                        return failed;
                    }
                    return WzDumpExporter.Export(resolvedNode.Node, outputRoot, format, options, resolvedNode);
                }
            }

            public void Dispose()
            {
                if (!this.disposed)
                {
                    this.structure.Clear();
                    this.disposed = true;
                }
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
                            foreach (var packExtension in new[] { ".ms", ".mn" })
                            {
                                foreach (var msFile in Directory.GetFiles(packsDir, "*" + packExtension))
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
        }
    }
}

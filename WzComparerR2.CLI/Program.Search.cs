using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using WzComparerR2.WzLib;

namespace WzComparerR2.CLI
{
    internal static partial class Program
    {
        private static void WriteSearchUsage(TextWriter writer)
        {
            writer.WriteLine("  WzComparerR2.CLI search --base <Base.wz> --query <text> [--path <subtree>] [--mode contains|exact|regex] [--field all|name|path|value] [--limit <count>]");
            writer.WriteLine("  Session: {\"command\":\"search\",\"query\":\"림보\",\"path\":\"String/Mob.img\",\"limit\":1000}");
        }

        private static int RunSearchCommand(string[] args)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var allowed = new[] { "--base", "--query", "--path", "--mode", "--field", "--limit" };
            WzSearchOptions options;
            try
            {
                for (int index = 0; index < args.Length; index++)
                {
                    string key = args[index];
                    if (!allowed.Contains(key) || ++index >= args.Length) throw new ArgumentException("Unknown or incomplete search option: " + key);
                    values.Add(key, args[index]);
                }
                if (!values.ContainsKey("--base")) throw new ArgumentException("Search requires --base <Base.wz>.");
                options = new WzSearchOptions
                {
                    Query = values.TryGetValue("--query", out var query) ? query : null,
                    Mode = values.TryGetValue("--mode", out var mode) ? mode : "contains",
                    Field = values.TryGetValue("--field", out var field) ? field : "all",
                    Limit = values.TryGetValue("--limit", out var limit) ? int.Parse(limit) : WzSearchOptions.DefaultLimit,
                };
                // Validate before opening the WZ source.
                WzSearch.Search(Array.Empty<Wz_Node>(), options);
            }
            catch (Exception ex)
            {
                WriteJsonResponse(new { ok = false, command = "search", error = ex.Message });
                return ExitUsageError;
            }
            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler cancel = (s, e) => { e.Cancel = true; cancellation.Cancel(); };
            Console.CancelKeyPress += cancel;
            try
            {
                using var session = LoadedWzSession.Open(values["--base"]);
                var result = session.Search(options, values.TryGetValue("--path", out var path) ? path : null, cancellation.Token);
                WriteSearchResponse(result, null);
                return result.Cancelled ? 130 : result.ErrorCount > 0 ? ExitExportError : ExitSuccess;
            }
            catch (Exception ex)
            {
                WriteJsonResponse(new { ok = false, command = "search", error = ex.Message });
                return ExitExportError;
            }
            finally { Console.CancelKeyPress -= cancel; }
        }

        private static void HandleSearchCommand(LoadedWzSession session, JsonElement root, string requestId)
        {
            var options = new WzSearchOptions
            {
                Query = GetRequiredString(root, "query"),
                Mode = root.TryGetProperty("mode", out var mode) ? mode.GetString() : "contains",
                Field = root.TryGetProperty("field", out var field) ? field.GetString() : "all",
                Limit = root.TryGetProperty("limit", out var limit) ? limit.GetInt32() : WzSearchOptions.DefaultLimit,
            };
            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler cancel = (s, e) => { e.Cancel = true; cancellation.Cancel(); };
            Console.CancelKeyPress += cancel;
            try
            {
                WriteSearchResponse(session.Search(options, root.TryGetProperty("path", out var path) ? path.GetString() : null, cancellation.Token), requestId);
            }
            finally { Console.CancelKeyPress -= cancel; }
        }

        private static void WriteSearchResponse(WzSearchResult result, string requestId)
        {
            WriteJsonResponse(new { ok = result.ErrorCount == 0 && !result.Cancelled, requestId, command = "search", result });
        }

        private sealed partial class LoadedWzSession
        {
            public WzSearchResult Search(WzSearchOptions options, string path, CancellationToken cancellation)
            {
                if (string.IsNullOrWhiteSpace(path)) return WzSearch.Search(WzSearch.OpenRoots(new[] { structure }), options, cancellation);
                if (!resolver.TryResolveExactPath(path, out var scope, out var failure))
                    throw new InvalidOperationException(failure?.Reason ?? "Search root not found: " + path);
                using (scope) return WzSearch.Search(new[] { scope.Node }, options, cancellation);
            }
        }
    }
}

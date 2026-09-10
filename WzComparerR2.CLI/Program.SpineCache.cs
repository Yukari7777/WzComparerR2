using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Xna.Framework;
using WzComparerR2.Animation;

namespace WzComparerR2.CLI
{
    internal static partial class Program
    {
        private static string HashSpineFile(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }

        private static void WriteSpineDocument(string path, string document)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, document);
                File.Move(temporary, path, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static SpineRasterResult ReadSpineCache(string directory, string prefix, string animation, string fingerprint, int fps)
        {
            string manifest = Path.Combine(directory, "clip.json");
            if (!File.Exists(manifest)) return null;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(manifest));
                var root = document.RootElement;
                if (!root.TryGetProperty("inputFingerprint", out var stored) || stored.GetString() != fingerprint ||
                    root.GetProperty("fps").GetInt32() != fps || root.GetProperty("animation").GetString() != animation)
                    return null;
                var hashes = root.GetProperty("frameHashes");
                if (hashes.EnumerateObject().MoveNext() == false) return null;
                foreach (var hash in hashes.EnumerateObject())
                {
                    if (Path.GetFileName(hash.Name) != hash.Name || !hash.Name.EndsWith(".png", StringComparison.Ordinal)) return null;
                    if (!File.Exists(Path.Combine(directory, hash.Name)) || HashSpineFile(Path.Combine(directory, hash.Name)) != hash.Value.GetString()) return null;
                }
                var bounds = root.GetProperty("bounds");
                int left = bounds.GetProperty("left").GetInt32(), top = bounds.GetProperty("top").GetInt32();
                var result = new SpineRasterResult
                {
                    Animation = animation, Fps = fps, InputFingerprint = fingerprint, CacheHit = true,
                    SpineVersion = root.GetProperty("spineVersion").GetString(),
                    DurationMs = root.GetProperty("durationMs").GetInt32(),
                    Bounds = new Rectangle(left, top, bounds.GetProperty("right").GetInt32() - left, bounds.GetProperty("bottom").GetInt32() - top),
                };
                foreach (var hash in hashes.EnumerateObject()) result.FrameFileHashes.Add(hash.Name, hash.Value.GetString());
                long duration = 0;
                foreach (var frame in root.GetProperty("frames").EnumerateArray())
                {
                    string src = frame.GetProperty("src").GetString();
                    if (string.IsNullOrEmpty(src) || !src.StartsWith(prefix + "/", StringComparison.Ordinal)) return null;
                    string fileName = src.Substring(prefix.Length + 1) + ".png";
                    if (!hashes.TryGetProperty(fileName, out _)) return null;
                    int delay = frame.GetProperty("delay").GetInt32();
                    if (delay <= 0) return null;
                    duration += delay;
                    var origin = frame.GetProperty("origin");
                    result.Frames.Add(new SpineRasterFrame { FileName = fileName, Delay = delay, Origin = new Point(origin[0].GetInt32(), origin[1].GetInt32()) });
                }
                return result.Frames.Count > 0 && duration == result.DurationMs ? result : null;
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is InvalidOperationException || ex is System.Collections.Generic.KeyNotFoundException || ex is FormatException || ex is ArgumentException || ex is IndexOutOfRangeException || ex is OverflowException)
            {
                Console.Error.WriteLine($"[Spine] Cache rejected: {manifest}: {ex.Message}");
                return null;
            }
        }
    }
}

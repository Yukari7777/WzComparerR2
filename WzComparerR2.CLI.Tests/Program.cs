using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using WzComparerR2.Animation;
using WzComparerR2.WzLib;

internal static class Program
{
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static int Main()
    {
        try
        {
            TestFingerprint();
            TestCachedFrames();
            Console.WriteLine("PASS Spine fingerprint and frame-cache validation");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void TestFingerprint()
    {
        var method = typeof(SpineFrameExporter).GetMethod("Fingerprint", BindingFlags.NonPublic | BindingFlags.Static);
        var group = new Wz_Node("group");
        group.Nodes.Add(new Wz_Node("atlas") { Value = "atlas pages" });
        group.Nodes.Add(new Wz_Node("skeleton") { Value = new Blob(new byte[] { 1, 2, 3 }) });
        string Fingerprint(int fps = 30) => (string)method.Invoke(null, new object[] { group, fps, null });
        string original = Fingerprint();
        Assert(Fingerprint() == original, "Identical inputs must reuse their fingerprint.");
        Assert(Fingerprint(15) != original, "FPS must invalidate the cache.");
        group.Nodes["atlas"].Value = "changed atlas";
        Assert(Fingerprint() != original, "Atlas contents must invalidate the cache.");
        group.Nodes["atlas"].Value = "atlas pages";
        group.Nodes["skeleton"].Value = new Blob(new byte[] { 1, 2, 4 });
        Assert(Fingerprint() != original, "Same-length skeleton changes must invalidate the cache.");
        group.Nodes["skeleton"].Value = new Blob(new byte[] { 1, 2, 3 });
        Assert(Fingerprint() == original, "Fingerprint should depend on bytes rather than object identity.");
        group.Nodes.Add(new Wz_Node("PMA") { Value = 1 });
        Assert(Fingerprint() != original, "Render metadata must invalidate the cache.");
    }

    private static void TestCachedFrames()
    {
        var assembly = Assembly.Load("WzComparerR2.CLI");
        var read = assembly.GetType("WzComparerR2.CLI.Program").GetMethod("ReadSpineCache", BindingFlags.NonPublic | BindingFlags.Static);
        string directory = Path.Combine(Path.GetTempPath(), "wcr-spine-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            const string prefix = "Map/Obj/_Spine/example.img/group/Hold";
            string png = Path.Combine(directory, "0000.png");
            byte[] bytes = { 1, 2, 3, 4 };
            File.WriteAllBytes(png, bytes);
            string hash = Convert.ToHexString(SHA256.HashData(bytes));
            void Manifest(int delay = 33, string src = prefix + "/0000")
            {
                File.WriteAllText(Path.Combine(directory, "clip.json"), JsonSerializer.Serialize(new
                {
                    inputFingerprint = "input", fps = 30, animation = "Hold", spineVersion = "4.1.24", durationMs = 33,
                    frameHashes = new System.Collections.Generic.Dictionary<string, string> { ["0000.png"] = hash },
                    bounds = new { left = -1, top = -2, right = 3, bottom = 4 },
                    frames = new[] { new { src, delay, origin = new[] { 1, 2 } } },
                }));
            }
            SpineRasterResult Read(string fingerprint = "input", int fps = 30) =>
                (SpineRasterResult)read.Invoke(null, new object[] { directory, prefix, "Hold", fingerprint, fps });
            Manifest();
            var result = Read();
            Assert(result != null && result.CacheHit && result.Frames.Single().Delay == 33, "Valid cache was not reused.");
            Assert(Read("changed") == null && Read(fps: 15) == null, "Changed input/FPS must not reuse frames.");
            File.WriteAllBytes(png, new byte[] { 1, 2, 3, 5 });
            Assert(Read() == null, "Same-size PNG corruption must be rejected.");
            File.Delete(png);
            Assert(Read() == null, "Missing PNGs must be rejected.");
            File.WriteAllBytes(png, bytes);
            Manifest(delay: 32);
            Assert(Read() == null, "Incomplete frame timing must be rejected.");
            Manifest(src: "../outside");
            Assert(Read() == null, "Frame paths outside the canonical animation must be rejected.");
            File.WriteAllText(Path.Combine(directory, "clip.json"), "{");
            Assert(Read() == null, "Incomplete manifest must be rejected.");
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class Blob : IMapleStoryBlob
    {
        private readonly byte[] bytes;
        public Blob(byte[] bytes) { this.bytes = bytes; }
        public int Length => bytes.Length;
        public void CopyTo(byte[] buffer, int offset) => bytes.CopyTo(buffer, offset);
        public void CopyTo(Span<byte> span) => bytes.AsSpan().CopyTo(span);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using WzComparerR2.Common;
using WzComparerR2.Controls;
using WzComparerR2.WzLib;

namespace WzComparerR2.Animation
{
    public sealed class SpineRasterFrame
    {
        public string FileName { get; set; }
        public int Delay { get; set; }
        public Point Origin { get; set; }
    }

    public sealed class SpineRasterResult
    {
        public string SpineVersion { get; set; }
        public string Animation { get; set; }
        public int DurationMs { get; set; }
        public int Fps { get; set; }
        public Rectangle Bounds { get; set; }
        public string InputFingerprint { get; set; }
        public bool CacheHit { get; set; }
        public Dictionary<string, string> FrameFileHashes { get; } = new Dictionary<string, string>();
        public List<SpineRasterFrame> Frames { get; } = new List<SpineRasterFrame>();
    }

    public static partial class SpineFrameExporter
    {
        public static SpineRasterResult Export(
            Wz_Node groupNode,
            string animationName,
            string outputDirectory,
            int fps = 30,
            GlobalFindNodeFunction findNode = null,
            Func<string, string, SpineRasterResult> reuse = null,
            Func<string, string> destination = null,
            Action<int, int> progress = null)
        {
            if (groupNode == null) throw new ArgumentNullException(nameof(groupNode));
            if (fps <= 0 || fps > 120) throw new ArgumentOutOfRangeException(nameof(fps));

            var detection = DetectGroup(groupNode);
            if (!detection.Success)
            {
                throw new InvalidOperationException(detection.ErrorDetail ?? "Spine data was not found in the selected group.");
            }
            if (detection.Version != SpineVersion.V4)
            {
                throw new NotSupportedException($"Spine raster export currently supports Spine 4.x only; detected {detection.Version}.");
            }
            string spineVersion = ReadVersion(detection);
            if (!spineVersion.StartsWith("4.1.", StringComparison.Ordinal))
            {
                throw new NotSupportedException($"Spine raster export currently supports Spine 4.1.x only; detected {spineVersion}.");
            }

            var graphicsService = GraphicsDeviceService.AddRef(IntPtr.Zero, 1, 1);
            try
            {
                var textureLoader = new WzSpineTextureLoader(detection.SourceNode.ParentNode,
                    graphicsService.GraphicsDevice, findNode)
                {
                    EnableTextureMissingFallback = true,
                };
                var data = SpineAnimationDataV4.CreateFromNode(detection.SourceNode, textureLoader);
                if (data == null) throw new InvalidOperationException("Failed to load Spine 4 animation data.");
                var animator = new SpineAnimatorV4(data);
                string selected = string.IsNullOrWhiteSpace(animationName)
                    ? animator.Animations.FirstOrDefault()
                    : animationName;
                if (selected == null || !animator.Animations.Contains(selected))
                {
                    throw new InvalidOperationException($"Spine animation '{animationName}' was not found.");
                }
                animator.SelectedAnimationName = selected;
                int durationMs = animator.Length;
                if (durationMs <= 0) throw new InvalidOperationException($"Spine animation '{selected}' has no duration.");
                string fingerprint = reuse == null ? null : Fingerprint(detection.SourceNode.ParentNode, fps, findNode);
                var cached = reuse?.Invoke(selected, fingerprint);
                if (cached != null) return cached;
                outputDirectory = destination?.Invoke(selected) ?? outputDirectory;

                var sampleTimes = BuildSampleTimes(durationMs, fps);
                Rectangle bounds = MeasureBounds(animator, sampleTimes);
                if (bounds.Width <= 0 || bounds.Height <= 0)
                {
                    throw new InvalidOperationException($"Spine animation '{selected}' produced empty bounds.");
                }

                Directory.CreateDirectory(outputDirectory);
                var result = new SpineRasterResult
                {
                    SpineVersion = spineVersion,
                    Animation = selected,
                    DurationMs = durationMs,
                    Fps = fps,
                    Bounds = bounds,
                    InputFingerprint = fingerprint,
                };
                var recorder = new AnimationRecoder(graphicsService.GraphicsDevice);
                recorder.Items.Add(animator);
                recorder.ItemTimes.Add(Tuple.Create(0, durationMs));
                recorder.GetMaxLength();
                recorder.BackgroundColor = Color.Transparent;
                recorder.Begin(bounds);
                var frameFiles = new Dictionary<string, string>();
                try
                {
                    using var texture = new RenderTarget2D(graphicsService.GraphicsDevice, bounds.Width, bounds.Height,
                        false, SurfaceFormat.Bgra32, DepthFormat.None);
                    var pixels = new byte[checked(bounds.Width * bounds.Height * 4)];
                    for (int index = 0; index < sampleTimes.Count; index++)
                    {
                        int at = sampleTimes[index];
                        int next = index + 1 < sampleTimes.Count ? sampleTimes[index + 1] : durationMs;
                        recorder.ResetAll();
                        recorder.Update(TimeSpan.FromMilliseconds(at));
                        recorder.Draw();
                        recorder.CopyPngTo(texture);
                        texture.GetData(pixels);
                        string hash = Convert.ToHexString(SHA256.HashData(pixels));
                        if (!frameFiles.TryGetValue(hash, out string fileName))
                        {
                            fileName = $"{index:D4}.png";
                            var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                            try
                            {
                                using var bitmap = new System.Drawing.Bitmap(texture.Width, texture.Height,
                                    texture.Width * 4, System.Drawing.Imaging.PixelFormat.Format32bppArgb,
                                    handle.AddrOfPinnedObject());
                                bitmap.Save(Path.Combine(outputDirectory, fileName), System.Drawing.Imaging.ImageFormat.Png);
                            }
                            finally
                            {
                                handle.Free();
                            }
                            frameFiles.Add(hash, fileName);
                        }
                        result.Frames.Add(new SpineRasterFrame
                        {
                            FileName = fileName,
                            Delay = Math.Max(1, next - at),
                            Origin = new Point(-bounds.Left, -bounds.Top),
                        });
                        if (index == 0 || index + 1 == sampleTimes.Count || (index + 1) % fps == 0)
                            progress?.Invoke(index + 1, sampleTimes.Count);
                    }
                }
                finally
                {
                    recorder.End();
                }
                return result;
            }
            finally
            {
                graphicsService.Release(true);
            }
        }

        private static SpineDetectionResult DetectGroup(Wz_Node groupNode)
        {
            var direct = SpineLoader.Detect(groupNode);
            if (direct.Success) return direct;
            foreach (var child in groupNode.Nodes)
            {
                var detected = SpineLoader.Detect(child);
                if (detected.Success) return detected;
            }
            return SpineDetectionResult.Failed("Spine atlas/skeleton pair was not found in the selected group.");
        }

        private static List<int> BuildSampleTimes(int durationMs, int fps)
        {
            int count = Math.Max(1, (int)Math.Ceiling(durationMs * fps / 1000d));
            var times = new List<int>(count);
            for (int index = 0; index < count; index++)
            {
                times.Add((int)Math.Floor(index * 1000d / fps));
            }
            return times;
        }

        private static Rectangle MeasureBounds(SpineAnimatorV4 animator, IReadOnlyList<int> sampleTimes)
        {
            Rectangle? bounds = null;
            foreach (int at in sampleTimes)
            {
                animator.Reset();
                animator.Update(TimeSpan.FromMilliseconds(at));
                var measured = animator.Measure();
                bounds = bounds == null ? measured : Rectangle.Union(bounds.Value, measured);
            }
            return bounds ?? Rectangle.Empty;
        }

        private static string ReadVersion(SpineDetectionResult detection)
        {
            object value = detection.ResolvedSkelNode?.Value;
            if (value is IMapleStoryBlob blob)
            {
                var data = new byte[blob.Length];
                blob.CopyTo(data, 0);
                using var stream = new MemoryStream(data);
                return Spine.SkeletonBinary.GetVersionString(stream);
            }
            return detection.Version.ToString();
        }
    }
}

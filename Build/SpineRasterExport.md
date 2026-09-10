# Spine raster export and reuse

`render-spine` keeps the requested FPS, animation duration, stable bounds, origins,
straight alpha, and lossless PNG output. It reuses one BGRA render target and one
pixel buffer per clip. Frame sampling still resets and evaluates the original
absolute sample times. Identical pixel buffers share a PNG across the entire
clip, including nonconsecutive frames.

Frames are stored under the resolved animation name. A request without an
animation writes a `default/clip.json` alias whose frame references point to that
same canonical directory. Default and explicit requests do not render or store
duplicate frame sets.

## Cache validation

The canonical `clip.json` contains `inputFingerprint` and `frameHashes`. The input
fingerprint hashes the source group's node names, values, atlas text, skeleton
bytes, resolved texture bytes and texture geometry, FPS, runtime version, and
rendering/decoding assembly module IDs. It uses content rather than WZ modification
timestamps. Changes to related WZ content or renderer builds invalidate reuse.

Before reuse, the exporter checks the fingerprint, animation, FPS, frame paths,
timing, and SHA-256 of every referenced PNG. Missing or modified files and invalid
manifests cause rendering. Manifests are committed by atomic replacement after
all frame files are ready. Cache hits are returned as `cacheHit: true` in the CLI
response. Atlas loading and input/output validation still occur on cache hits;
frame rasterization and PNG encoding are skipped.

The CLI reports preparation, frame progress, and cache-hit/rendered status on
stderr. JSON responses remain on stdout, including in session mode.

## Boss asset script integration

The private boss export script renders into `.dev/.spine-cache/<asset-root-name>`.
That directory sits outside the refreshed asset tree and is not uploaded. After
successful export, only the requested manifests and referenced frames are
materialized into the boss asset root. Hard links avoid copying PNG bytes on the
same filesystem; unsupported/cross-filesystem cases use file copies. Treat both
generated trees as read-only between exports because files may share inodes.

The script keeps the current two-pass document/external-asset extraction flow and
the existing upload paths. Full refreshes can remove the boss asset tree without
removing validated Spine caches. Interrupted clips are revalidated on the next
run. Outputs without validation metadata must be rendered once to establish a
cache. Old cache directories are not automatically removed.

Cold export still rasterizes every unique requested animation at full resolution.
It can remain substantial for large map backgrounds; cache reuse does not imply
that the first export or resulting PNG download size is small.

## Verification

```sh
dotnet run --project WzComparerR2.CLI.Tests -c Release
```

Tests cover content/FPS invalidation, same-size PNG corruption, missing frames,
invalid manifests, timing, and path containment. Representative real-WZ checks
compare every PNG, delay, origin, and bounds against a baseline, then verify both
default/explicit alias reuse and reuse across fresh processes. The private script
also has materialization tests and an opt-in single-clip Windows/WSL integration
test in `.dev/scripts/export-boss.spine.test.mjs`.

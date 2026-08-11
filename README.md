*<s>使用前先大喊 niconiconi! poi! duang!以减少bug发生率</s>*  

[![Build Status](https://dev.azure.com/kagamiastudio/WzComparerR2/_apis/build/status/Kagamia.WzComparerR2?branchName=master)](https://dev.azure.com/kagamiastudio/WzComparerR2/_build/latest?definitionId=4&branchName=master)

# Maintenance Status

⚠️ The WzComparerR2 project is now in deep maintenance status. This means that only critical bugs or wz file format breaking changes are being considered for inclusion by owner. Expect slow replies to issues.

# WzComparerR2
这是一个用C# latest/.Net4.62+.Net8组装的冒险岛提取器...  
包含了一些奇怪的机能比如stringWZ搜索 客户端对比 装备模拟 地图模拟等等..  

tips: WcR2将尽力维持每周更新，Releases里**不会**提供最稳定版下载，最新版会通过azure-pipeline自动发布。  
links: [\[更新日志\]](https://github.com/Kagamia/WzComparerR2/tree/master/UpdateLogs)  [\[版本计划\]](https://github.com/Kagamia/WzComparerR2/wiki/Roadmap)  [\[最新版下载\]](https://github.com/Kagamia/WzComparerR2/releases/tag/ci-build)

# Modules
- **WzComparerR2** 主程序
- **WzComparerR2.Common** 一些通用类
- **WzComparerR2.PluginBase** 插件管理器
- **WzComparerR2.WzLib** wz文件读取相关
- **CharaSimResource** 用于装备模拟的资源文件
- **WzComparerR2.LuaConsole** (可选插件)Lua控制台
- **WzComparerR2.MapRender** (可选插件)地图仿真器
- **WzComparerR2.Avatar** (可选插件)纸娃娃
- **WzComparerR2.Network** (可选插件)在线聊天室

# Prerequisite
- **2.x**: Win7sp1+/.net4.6.2+/dx11.0
- **1.x**: WinXp+/.net2.0+/dx9.0

# Installation
```sh
git clone --recurse-submodules -j8 git://github.com/Kagamia/WzComparerR2.git
```
Clone repository with submodules.

# Compile
- vs2022 or higher/.net 8 SDK

# CLI Dump Export

`WzComparerR2.CLI` exports an exact logical WZ path. The recommended workflow is a stateful session that loads `Base.wz` once and accepts JSON Lines requests. Image roots, in-image subnodes, `_Canvas` nodes, and individual binary resource nodes are supported.

`--output` and the JSONL `output` property always name an output root directory. Documents and external resources retain their logical WZ paths below that root:

```text
<output>/<requested logical path>.json|xml
<output>/<resolved resource logical path>.<extension>
```

An `_Canvas` subtree or an individual PNG, Sound, RawData, or Video request writes only resources and returns `documentWritten=false`. It does not create a redundant `_Canvas` document.

One-shot examples:

```sh
dotnet run --project WzComparerR2.CLI -- export --base "D:/MapleStory/Data/Base/Base.wz" --path "Mob/8880450.img" --output "D:/MyApp/public/wz" --dump-external --leave-reference
dotnet run --project WzComparerR2.CLI -- export --base "D:/MapleStory/Data/Base/Base.wz" --path "Skill/FieldSkill.img/100029" --output "D:/MyApp/public/wz" --format xml --dump-external
```

Options:

- `--format json|xml` / `format`: document format. The default is `json`.
- `--dump-raw` / `dumpRaw`: embed PNG, Sound, RawData, and Video payloads in the document.
- `--dump-external` / `dumpExternal`: resolve `source`, `_inlink`, `_outlink`, and UOL chains and save the final resources at their resolved logical paths.
- `--leave-reference` / `leaveReference`: record resolved external paths in `file` or `files` metadata.

`dumpRaw` and `dumpExternal` are mutually exclusive. `leaveReference` is valid only with `dumpExternal`. With neither dump mode enabled, the export is metadata-only and does not force external link resolution.

Session mode:

```sh
dotnet run --project WzComparerR2.CLI -- session --base "D:/MapleStory/Data/Base/Base.wz"
```

The session reads one JSON object per line and writes one JSON response per line:

```json
{"requestId":"mob","command":"export","path":"Mob/8880450.img","output":"D:/MyApp/public/wz","dumpExternal":true,"leaveReference":true}
{"requestId":"field-skill","command":"export","path":"Skill/FieldSkill.img/100029/level/1/areaWarning/0","output":"D:/MyApp/public/wz","format":"xml","dumpExternal":true}
{"requestId":"sound","command":"export","path":"Sound/Mob.img/8880450/Attack1","output":"D:/MyApp/public/wz","dumpExternal":true}
{"command":"quit"}
```

One-shot and session exports use the same success fields:

```json
{"ok":true,"requestId":"mob","command":"export","path":"Mob/8880450.img","output":"D:\\MyApp\\public\\wz","format":"json","document":"D:\\MyApp\\public\\wz\\Mob\\8880450.img.json","documentWritten":true,"externalFileCount":42,"durationMs":298}
```

An unresolved link, type mismatch, unsupported resource, or decode error fails the request instead of writing a link stub. Failures contain both a short summary and structured diagnostics:

```json
{"ok":false,"requestId":"field-skill","command":"export","path":"Skill/FieldSkill.img/100029","output":"D:\\MyApp\\public\\wz","format":"json","error":"Path segment not found: 0","failures":[{"code":"path_not_found","sourcePath":"Skill/FieldSkill.img/100029/level/1/areaWarning/0/0","linkType":"_outlink","linkPath":"Skill/_Canvas/FieldSkill.img/100029/level/1/areaWarning/0/0","targetPath":"Skill/_Canvas/FieldSkill.img/100029/level/1/areaWarning/0/0","targetWzFiles":["D:\\MapleStory\\Data\\Skill\\_Canvas\\_Canvas.wz"],"stage":"path","reason":"Path segment not found: 0","sourceWasLinkStub":true}],"durationMs":31}
```

Files are resolved and decoded in a temporary location before being committed. A failed request does not publish partial files or replace existing output. Do not run multiple exporters against the same output root concurrently; output-root process locking is outside the CLI contract.

The synthetic resolver and exporter regression suite can be run without MapleStory data:

```sh
dotnet run --project WzComparerR2.WzLib.Tests -c Release
```

# Credits and Acknowledgement
- **Fiel** ([Southperry](http://www.southperry.net))  wz文件读取代码改造自WzExtract 以及WzPatcher
- **Index** ([Exrpg](http://bbs.exrpg.com/space-uid-137285.html)) MapRender的原始代码 以及libgif
- **Deneo** For .ms file format and video format
- [DotNetBar](http://www.devcomponents.com/)
- [SharpDX](https://github.com/sharpdx/SharpDX) & [Monogame](https://github.com/MonoGame/MonoGame)
- [BassLibrary](http://www.un4seen.com/)
- [IMEHelper](https://github.com/JLChnToZ/IMEHelper)
- [Spine-Runtime](https://github.com/EsotericSoftware/spine-runtimes)
- [EmptyKeysUI](https://github.com/EmptyKeys)
- [libvpx](https://www.webmproject.org/code/) & [libyuv](https://chromium.googlesource.com/libyuv/libyuv/) for video decoding
- [VC-LTL5](https://github.com/Chuyu-Team/VC-LTL5) for native library build
- All testers from CMST tester group.

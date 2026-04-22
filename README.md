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

# CLI JSON Export
- `WzComparerR2.CLI` is intended for automation that already knows which WZ path should be exported.
- The CLI does **not** scan existing `.img.json` files or decide which dumps should be replaced.
- The primary workflow is a stateful session that loads `Base.wz` once and then serves repeated exact-path export requests.
- Supported v1 targets are exact image roots such as `Mob/8880450.img` and exact in-image subnodes such as `Mob/8880450.img/info`.

One-shot export:

```sh
dotnet run --project WzComparerR2.CLI -- export --base "D:/MapleStory/Data/Base.wz" --path "Mob/8880450.img" --output "D:/MyApp/public/wz/Mob/8880450.img.json"
```

Use `--omit-redundant-canvas-artifacts` when you want to omit placeholder `_Canvas/*.img` dump files and suppress exported `.png` files for non-`_Canvas` images.

Use `--preserve-full-path-for-single-image` when a single `.img` export should keep its full logical path. With this option, `--output` may be either the final preserved `.json` path or an export root directory.

Session mode:

```sh
dotnet run --project WzComparerR2.CLI -- session --base "D:/MapleStory/Data/Base.wz"
```

Session stdin protocol uses one JSON object per line:

```json
{"command":"export","path":"Mob/8880450.img","output":"D:/MyApp/public/wz/Mob/8880450.img.json"}
{"command":"export","path":"Skill/000.img","output":"D:/MyApp/public/wz/Skill/000.img.json","dumpExternal":true,"omitRedundantCanvasArtifacts":true,"preserveFullPathForSingleImage":true}
{"command":"export","path":"Skill/MobSkill/100.img","output":"D:/MyApp/public/wz/Skill/MobSkill/100.img.json","omitRedundantCanvasArtifacts":true}
{"command":"export","path":"Mob/8880450.img/info","output":"D:/MyApp/public/wz/Mob/8880450.info.json"}
{"command":"quit"}
```

Responses are also emitted as single-line JSON objects so another application can detect success or failure without scraping human-readable text.

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

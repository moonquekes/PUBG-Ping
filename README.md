# PUBG-Ping

独立的 Windows 桌面信号轮盘。按住热键，移动鼠标选择，松开后在桌面显示图标并播放提示音。它不是 PUBG 插件，不注入游戏，也不读取游戏内存。

默认打开「表情符号」页。触发键默认是 F8。

## 这个仓库里有什么

只有最终用到的源码：

- `source/TacticalPing.cs`：桌面程序，含输入、轮盘、绘制和播放。
- `source/app.manifest`：普通用户权限和 DPI。
- `build.ps1`：用 Windows 自带的 C# 编译器生成 exe。
- `source/extraction/`：从本机 PUBG 安装只读导出图标、提示音和 24 槽配置的脚本。
- `使用说明.txt`：操作说明。

不包含这些东西：

- 从游戏里导出的 PNG、WAV、目录 JSON。它们不是代码，版权仍属于原权利人，请留在你自己的机器上。
- 编译出的 exe。放在仓库的 Releases 里，不进 git。
- 查找密钥时克隆过的第三方仓库、手绘中间版、提取缓存和本机密钥文件。

## 编译

需要 Windows 10/11 x64 和系统自带的 .NET Framework 4.8，不需要 Visual Studio。

```powershell
.\build.ps1
```

生成的 `PUBG-Ping-Learning.exe` 要和 `assets\game` 放在同一套目录里才能启动。素材用下面的提取脚本在本机生成。

## 本机素材

先完全退出正在运行的程序，再在 PowerShell 里执行：

```powershell
.\source\extraction\extract-assets.ps1 -GameRoot 'D:\SteamLibrary\steamapps\common\PUBG'
```

脚本只读游戏文件。Oodle 和 vgmstream 会在缓存目录里按固定版本下载，不会写进本仓库。密钥写到 `data/extraction-cache/local-pak-key.hex`，不要提交。

已在本机 Steam buildid 25595147 上跑通。换游戏版本后不保证资源路径还一样。

## 参考了哪些仓库

最终程序是另写的 C# / Python，没有把下列仓库的代码或素材复制进来。

做成桌面轮盘时对照的是 [darrenprx/lolPing](https://github.com/darrenprx/lolPing)：

- 交互是按住热键、移动选择、松开发送，中心松开则取消。
- 每个屏幕一个透明、置顶、点击穿透的覆盖层。
- 素材从本机已安装的游戏里只读提取，而不是把游戏引擎搬出来。它的 `tools/extract-assets` 和 [设计说明](https://github.com/darrenprx/lolPing/blob/main/docs/design.md) 是这个做法的来源。
- lolPing 用 Electron 加一个 C++ 钩子进程。这里收成一个 C# / WPF 程序，钩子和绘制都在进程内。

读取 PUBG 的 Pak、纹理和数据表时，格式对照的是 [FabianFG/CUE4Parse](https://github.com/FabianFG/CUE4Parse)（Apache-2.0）。`pubg_pak.py`、`pubg_uasset.py` 和纹理导出是按公开格式写的，不是把 CUE4Parse 移植过来。

提取脚本在运行时另外下载、但不放进本仓库的工具：

- [WorkingRobot/OodleUE](https://github.com/WorkingRobot/OodleUE) `2026-06-04-1357`：解 Oodle 压缩块。
- [vgmstream/vgmstream](https://github.com/vgmstream/vgmstream) `r2117`：把 Wwise 媒体解成 PCM WAV。

查本机 Pak 密钥时看过下面这些仓库，最终没有用它们的程序，密钥来自对本机游戏 EXE 的静态字符串扫描，再用索引 SHA-1 校验：

- [4sval/FModel](https://github.com/4sval/FModel)
- [clauadv/ue_aes_finder](https://github.com/clauadv/ue_aes_finder)
- [GHFear/AESKeyFinder-By-GHFear](https://github.com/GHFear/AESKeyFinder-By-GHFear)
- [GHFear/AESDumpster](https://github.com/GHFear/AESDumpster)
- [illusorysoftware/UnrealKey](https://github.com/illusorysoftware/UnrealKey)

## 许可

应用和提取脚本使用 MIT 许可证，见 `LICENSE`。

这不覆盖 PUBG 的图标、文字、音效，也不表示本程序得到 KRAFTON / PUBG 的认可。不要把导出的游戏素材或提取缓存推到公开仓库。

# PUBG PING

独立的 Windows 桌面信号轮盘。按住热键，移动鼠标选择，松开后在桌面显示图标并播放提示音。它不是 PUBG 插件，不读游戏内存，不注入进程，也不联网。

默认打开「表情符号」页，触发键是 F8。三页共 24 个槽位：战斗、准备完毕、表情符号。

## 直接使用

Windows 10/11 x64，系统自带 .NET Framework 4.8。不需要 Visual Studio，也不需要先安装 PUBG。

下载 [Releases](https://github.com/moonquekes/PUBG-Ping/releases/latest) 里的 `PUBG-Ping-windows.zip`，解压整个文件夹，双击 `PUBG PING.exe`。不要把 exe 单独挪走，它要和旁边的 `assets\game` 放在一起。

克隆后自己编译：

```powershell
.\build.ps1
& '.\PUBG PING.exe'
```

## 操作

- 按住 F8，把鼠标移出中心，松开即发送。在中心松开，或按住期间按 Esc / 右键，会取消。只点一下、不移动，也是取消。
- 按住期间用滚轮、左右方向键或 Tab 换页。当前页会记住。
- 设置里可以把触发改成 F6–F12 或鼠标中键。中键启用时会占用普通中键点击，暂停后恢复。
- 「预览信号」在鼠标位置播放当前槽位的图标和提示音。
- 暂停快捷键先用 Ctrl+Alt+P，被占用时改用 Ctrl+Alt+F12。以窗口和托盘上显示的为准。
- 点窗口关闭按钮时，选择「最小化到托盘」或「完全退出」。勾选「不再询问」后记住这一次的选择，写在 `data/settings.json`。托盘菜单里的「退出」和设置里的「完全退出」会直接结束程序。
- 双击托盘图标重新打开设置。

提示音是标点音，不是角色口播。表情符号页用的是较短的 Normal 音。

## 打开原始素材会去哪

按钮打开的是**这个程序旁边的** `assets\game`，也就是解压或克隆下来的那份图标和提示音。

它不会跳到别人电脑上的 PUBG 安装目录。文件夹不存在时，程序只弹出这个路径，不打开资源管理器。

自己导入的图片和声音在 `assets/custom/`，设置在 `data/settings.json`。「恢复此信号」只去掉引用，不删磁盘上的文件。图片用 PNG、JPG、BMP，最长边 4096，不超过 10 MB。声音用 8/16 位 PCM WAV，8–96 kHz，最长 5 秒，不超过 2 MB。

## 仓库内容

- `source/TacticalPing.cs`：程序本身。
- `source/app.ico`、`source/app.manifest`：图标、普通用户权限和 DPI。
- `build.ps1`：用系统自带的 C# 编译器生成 exe。
- `assets/game/`：24 个槽位用到的图标、世界标记、15 段提示音和 `catalog.json`。
- `source/extraction/`：只在游戏更新、需要从本机 PUBG 重新导出时使用。

不包含提取缓存、本机密钥、没有挂到槽位上的图片，以及编译出的 exe。exe 在 Releases 里。

## 重新提取

日常运行不用这一步。本机要有 PUBG、Python 3.9+、Pillow 和 cryptography。先退出 PUBG PING，再运行：

```powershell
.\source\extraction\extract-assets.ps1 -GameRoot 'D:\SteamLibrary\steamapps\common\PUBG'
```

虚拟环境可以加 `-Python '路径\Scripts\python.exe'`。脚本只读游戏文件，并把 Oodle、vgmstream 下载到 `data/extraction-cache`（也可用环境变量 `PUBG_ASSET_CACHE`）。密钥在 `data/extraction-cache/local-pak-key.hex`，不要提交。重新提取会覆盖 `assets/game`，不会改 `assets/custom`。

这套素材来自 Steam buildid 25595147。脚本依次做：从游戏 EXE 静态扫描密钥、读 UE4 Pak、解 Oodle、解析数据表和中文、导出槽位用到的 PNG，并把 Wwise 提示音解成 WAV。

## 参考

程序是另写的，没有复制这些仓库的代码或素材。

- [darrenprx/lolPing](https://github.com/darrenprx/lolPing)：按住选择、松开发送，以及从本机游戏只读提取素材。这里收成一个 C# / WPF 程序。
- [FabianFG/CUE4Parse](https://github.com/FabianFG/CUE4Parse)：Pak、纹理和数据表格式。
- [WorkingRobot/OodleUE](https://github.com/WorkingRobot/OodleUE) `2026-06-04-1357`：解压。
- [vgmstream/vgmstream](https://github.com/vgmstream/vgmstream) `r2117`：把 Wwise 解成 WAV。

查密钥时看过、没有用进最终程序的仓库：[FModel](https://github.com/4sval/FModel)、[ue_aes_finder](https://github.com/clauadv/ue_aes_finder)、[AESKeyFinder](https://github.com/GHFear/AESKeyFinder-By-GHFear)、[AESDumpster](https://github.com/GHFear/AESDumpster)、[UnrealKey](https://github.com/illusorysoftware/UnrealKey)。

## 许可

代码使用 MIT 许可证，见 `LICENSE`。`assets/game` 里的图标、文字和音效仍属于 KRAFTON / PUBG。本程序没有得到官方认可，也没有做反作弊认证。全屏窗口和游戏进程 `TslGame` / `ExecPubg` 在前台时默认不触发。

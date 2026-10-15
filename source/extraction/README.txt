PUBG 本地只读资源提取 / 此次使用的可复现源码

适用性
- 已在本机 Steam buildid 25595147 验证，不保证未来所有游戏版本都使用同一资源结构。
- 不运行游戏、不读运行中进程内存、不注入、不修改 Pak 或游戏可执行文件。
- 只从用户自己的本地安装读取目标资源，写入本项目的素材目录和缓存目录。
- 从零缓存验证静态候选成功；完整提取流水线已在此次会话重跑通过。

流程
1. derive_key.py 只读扫描本机 EXE 的静态 ASCII 字符串；用目标索引完整 SHA-1 验证候选。
   不打印密钥，不发送到网络，不使用账号或认证信息。
2. pubg_pak.py 读取 UE4 v4 Pak、验证索引 SHA-1、读取目标压缩块；Oodle 解压在独立用户态完成。
3. pubg_uasset.py 解析 NameMap、FPropertyTag 和 DataTable。
4. export_pubg_textures.py 将原始 BC7/DXT5 等纹理解码为 PNG，不重绘、不 AI 生成。
5. export_pubg_sounds.py 按 Wwise 事件名定位 HIRC 引用链，导出 DIDX/DATA 媒体并解码成 PCM WAV。
   随机容器的变体分别保存，而不是误当成多层声音混合。
6. export_pubg_catalog.py 解析中文 compact-v1 locres 与字符串表，生成原始 3 页、24 槽的配置。

依赖（只在重新提取时需要；运行桌面 EXE 不需要）
- Windows x64 / Python 3.9+（此次 Python 环境已有依赖）
- Pillow、cryptography。建议在你自己创建的 Python 虚拟环境中安装，脚本不会自动修改系统 Python。
- OodleUE 2026-06-04-1357 的 Windows 解压库。
- vgmstream r2117 的官方 Windows x64 CLI。

重跑
先完全退出桌面程序，再在 PowerShell 中运行：
  & .\source\extraction\extract-assets.ps1 -GameRoot 'D:\SteamLibrary\steamapps\common\PUBG'
如果使用虚拟环境，可以额外传入：
  -Python '虚拟环境路径\Scripts\python.exe'

脚本会在缺少工具 ZIP 时，从固定版本的官方 GitHub 发布地址下载约 10 MB 工具。
它不会启动游戏、修改安装、安装服务、修改系统执行策略或联网发送本地文件。
缓存默认位于 data/extraction-cache；可通过 PUBG_ASSET_CACHE 环境变量指定其他缓存目录。
静态候选、原始中间文件和工具 ZIP 只在缓存中。请勿把缓存上传到公开仓库。
Pak 路径通过 PUBG_PAK_DIR 环境变量在流水线内部传递；外部脚本执行完会还原调用前的环境变量。
重新提取会覆盖 assets/game 下对应导出素材及清单，不会修改 assets/custom 的手动导入文件。

输出
- assets/game/*.png：134 张原始透明纹理（本次构建）。
- assets/game/sounds/*.wav：Normal 3 / Important 3 / Notify 6 / Warning 3。
- assets/game/catalog.json：原始数据表的基本槽位、中文、图标和音效分类。
- texture-manifest.json / sound-manifest.json：来源及校验值。
- original-assets-contact.png：原图素材总览。

参考与归属
资源格式参考 CUE4Parse 的公开实现：https://github.com/FabianFG/CUE4Parse
Oodle 解压库来源：https://github.com/WorkingRobot/OodleUE
Wwise 解码：https://github.com/vgmstream/vgmstream
LoL 资源脚本用于对照相同的 Wwise 格式思路：https://github.com/darrenprx/lolPing
这里只分发独立编写的提取脚本，不把上述第三方工具二进制放入交付包。
游戏图片、文本和声音不属于本项目 MIT 代码许可证；版权仍属于原权利人。

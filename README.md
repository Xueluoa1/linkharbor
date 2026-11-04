# 拾链 v1.0.1

**正式版 · Windows 视频解析与批量下载工具 · 免费开源**

作者 **雪落a** · B站 ID **雪落a77** · [xueluo.online](https://xueluo.online)（我的其他 AI 开发程序）

拾链基于开源项目 [yt-dlp](https://github.com/yt-dlp/yt-dlp)，主要提供 Windows UI 适配、批量任务管理及安装/便携封装，让解析链接、选择画质和下载更方便。音视频处理使用 FFmpeg，JavaScript 支持使用 Node.js。

## 下载与使用

**[直接下载 v1.0.1 安装包 EXE](https://github.com/Xueluoa1/linkharbor/releases/download/v1.0.1/LinkHarbor-v1.0.1-Setup.exe)**

1. 下载 **LinkHarbor-v1.0.1-Setup.exe** 并双击，选择安装位置，按需勾选“创建桌面快捷方式”。
2. 点击“安装”，路径和快捷方式选项会替换为实际安装进度。完成后点击“启动拾链”，以后从桌面或开始菜单打开。已包含 yt-dlp、FFmpeg、Node.js 和许可证，无需另下 ZIP 或联网补组件。
3. 粘贴视频链接，点击“解析链接”，选择画质或音频格式、保存目录，再点击“开始下载”。

适用于 Windows 10/11 x64，需要 .NET Framework 4.x。安装包约 92 MiB，安装后约 300 MiB；为安装和临时文件保留充足空间。默认仅为当前用户安装，无需管理员权限；自选位置须有写入权限。设置保存在 `%LOCALAPPDATA%\LinkHarbor\settings.json`，默认下载到用户“视频”目录下的“拾链”文件夹。可通过 Windows 设置 → 应用 → 拾链卸载，个人设置和下载文件保留。

也可下载 **[ZIP 便携包](https://github.com/Xueluoa1/linkharbor/releases/download/v1.0.1/LinkHarbor-v1.0.1-Windows-x64.zip)**，全部解压后双击其中的 `拾链下载器.exe`，保留 `tools/`。发布页只保留安装包 EXE 和压缩包，旧自展开 EXE、界面单文件 EXE 已移除。Release 自动生成的 `Source code` 是源码，不是可运行程序。文件校验值见发布页。

**[完整操作教程](docs/使用教程.md)** · [离线快速说明](使用说明.txt) · [作者声明](AUTHOR_NOTICE.md) · [更新记录](CHANGELOG.md)

## 功能

- 多链接按换行、英文 `;` 或中文 `；` 分隔，重复链接自动忽略；回车只换行。
- 最多 3 项并行下载，其余排队；单条失败不影响其他任务。
- 视频画质上限、原格式音频、MP3 转换、分离音视频自动合并。
- 独立任务弹窗显示状态、进度、速度、剩余时间、输出路径和日志。
- 可选代理及 Netscape Cookie，每个进程使用独立临时 Cookie 副本。
- 取消全部、同名完整文件保护，以及来源允许时的断点续传。
- 浅色工作台、可缩放布局、侧栏导航、多尺寸图标和内置网络帮助。

默认下载到用户“视频/拾链”（安装版）或程序旁的 `下载/`（便携版），可更改保存位置。总进度包含下载、合并和校验；失败与取消不算完成。分离音视频通常合并为 MKV，单文件来源保留原格式。不处理频道、播放列表或进行中的直播。

## 构建、验证和打包

使用 Windows PowerShell 5.1 或 PowerShell 7，以及系统 .NET Framework 编译器。无需 Visual Studio 或 npm。

```powershell
git clone https://github.com/Xueluoa1/linkharbor.git
cd linkharbor
git checkout master
# 从正式便携版复制完整 tools 文件夹，或按 tools/README.md 准备依赖
.\check-tools.ps1 -VerifyHashes
.\build.ps1
.\test-all.ps1
.\package.ps1
```

构建生成根目录 `拾链下载器.exe`；版本信息统一定义在 `src/AssemblyInfo.cs`。打包生成 `dist/LinkHarbor-v1.0.1-Windows-x64.zip` 和 SHA-256 清单。完整步骤见 [操作教程第 12 节](docs/使用教程.md#12-从源码构建与测试)。

完整 EXE 使用 Windows 自带的 LZMS 解压能力，全部下载与转码组件保持原样；首次启动或修复时才解压，组件完整时直接复用。内存不足时可使用原 ZIP 便携包。

完整单文件 EXE 的构建脚本为 `package-singlefile.ps1`，启动器源码位于 `packaging/SingleFileLauncher.cs`。在仓库 `master` 分支（或 Release 的 `Compact-Source.zip`）运行 `.\package-singlefile.ps1 -SkipBuild`，即可生成含全部离线组件的 EXE 及独立校验文件；原 `v1.0.1` 标签保留应用初始发布源码，单文件打包补充源码单独提供。可用 `.\test-singlefile.ps1` 验证首次展开、重复启动、组件修复及设置保留。

测试按核心、批量、GUI 的顺序执行，生成本地 HTTP 媒体，不依赖视频网站；GUI 测试需要桌面会话，默认超时 300 秒。可单独执行 `test-ui.ps1 -TimeoutSeconds 600` 调整。测试共用目录，不能同时运行。

若脚本被执行策略阻止，可对单次命令使用 `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`，不必修改全局策略。

## 目录

安装包源码位于 `packaging/Setup.cs` 和 `packaging/installer.iss`，使用与主程序共用的 WinForms 绘制控件及 Inno Setup 6.7.3。运行 `package-installer.ps1` 构建，详见 [安装包构建与验证](docs/安装包构建.md)。Release 的 `LinkHarbor-v1.0.1-Setup-Source.zip` 对应安装包补充源码；原 `v1.0.1` 标签及版本记录保持不变。

| 路径 | 内容 |
| --- | --- |
| `src/` | C# 源码、统一版本信息、图标和图标生成脚本 |
| `tests/` | 核心与批量测试、本地 HTTP 测试服务 |
| `docs/使用教程.md` | 启动、下载、音频、批量、代理、Cookie、故障排查和升级 |
| `docs/releases/v1.0.1.md` | 本次发布说明 |
| `tools/` | 依赖说明、许可证、校验清单；EXE/DLL 不提交 Git |
| `build.ps1` / `package.ps1` | 编译与便携版打包 |
| `check-tools.ps1` / `test-all.ps1` | 依赖校验及测试入口 |

界面规范见 [UI设计说明.md](UI设计说明.md)，仓库维护流程见 [GIT_UPLOAD.md](GIT_UPLOAD.md)。个人设置、Cookie、媒体和构建产物由 `.gitignore` 排除。二进制便携包通过 Release 分发。

## 作者、许可与声明

Copyright (C) 2026 **雪落a**。拾链自身源码采用 **GPL-3.0-only**，完整条款见 [LICENSE](LICENSE)。第三方组件仍按各自许可证使用，来源及源码入口见 [THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt) 和 [第三方源码说明](docs/THIRD_PARTY_SOURCES.md)。

**作者倡议：官方免费提供，不希望原版被简单打包、改名或冒充原创后售卖。** 这只是个人愿望，不构成额外授权条件，也不限制 GPL 允许的商业使用或收费分发。详见 [作者声明](AUTHOR_NOTICE.md)。

请只下载自己拥有权利或获授权的内容；网站改版、网络、账号权限和验证会影响下载。本地测试通过不代表所有站点均可用。反馈问题请到 [Issues](https://github.com/Xueluoa1/linkharbor/issues)，不要附带 Cookie、代理密码或私人链接。

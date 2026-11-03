# 拾链 · 视频解析下载

Windows 本地视频下载工具，使用 C# / WinForms 和 yt-dlp。当前源码为 **2026-11-03 多任务版**，包括批量解析、并行下载、任务弹窗及最新界面修复。

## 功能

- 多个链接按换行、英文 `;` 或中文 `；` 分隔，重复链接自动忽略；回车只换行。
- 最多 3 项并行下载，其余排队；单条失败不影响其他任务。
- 视频画质上限、原格式音频、MP3 转换，自动合并分离的音视频流。
- 独立任务弹窗显示状态、进度、速度、剩余时间、文件路径和日志。
- 可选代理和 Netscape Cookie 文件；每个任务使用独立 Cookie 临时副本。
- 支持续传、同名文件保护和取消全部；使用 Windows Job Object 回收下载子进程树。
- 浅色工作台、可缩放布局、侧栏导航、多尺寸链环图标和内置帮助。

完整操作说明见 [使用说明.txt](使用说明.txt)，界面规范见 [UI设计说明.md](UI设计说明.md)。

## 从源码运行

环境：Windows 10/11 x64、.NET Framework 4.x（含 `Framework64/v4.0.30319/csc.exe`）、Windows PowerShell 5.1 或 PowerShell 7。构建脚本直接调用系统编译器，无需 Visual Studio、Python 或 npm。

1. 克隆或解压源码。
2. 按 [tools/README.md](tools/README.md) 准备运行组件。Git 仓库不包含第三方 EXE / DLL。
3. 在项目根目录执行：

```powershell
.\check-tools.ps1 -VerifyHashes
.\build.ps1
.\拾链下载器.exe
```

若系统执行策略阻止脚本，可对单次命令使用 `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`，无需修改全局策略。

默认构建结果为根目录 `拾链下载器.exe`。运行时需保留同目录下的 `tools/`，默认下载目录为 `下载/`。个人配置存入 `settings.json`，不进入 Git。已有便携版的用户可将其中整个 `tools/` 复制过来。

## 验证

```powershell
.\test-all.ps1
```

该命令先检查依赖，再依次运行核心测试、批量测试和 GUI 冒烟测试；GUI 测试会重新构建程序，需要可用的 Windows 桌面会话。测试使用本机 HTTP 服务和生成的媒体文件，不依赖视频网站；不要同时运行多个测试脚本，它们共用测试目录和端口记录文件。

GUI 测试默认超时 300 秒，可使用 `.\test-ui.ps1 -TimeoutSeconds 600` 调整；每次运行前清除旧的界面测试报告。

也可按顺序分别运行：

```powershell
.\test.ps1        # 生成媒体素材，测试参数、下载、合并、音频和取消
.\test-batch.ps1  # 依赖上一步素材，测试并行、排队、失败隔离和 Cookie 隔离
.\test-ui.ps1     # 依赖上一步素材，测试真实窗口中的批量流程
```

网站解析和下载结果取决于该站点、网络、账号权限及验证状态。本地测试通过不代表所有站点均可下载。

## 源码结构

| 路径 | 用途 |
| --- | --- |
| `src/App.cs` | 程序入口、窗口状态、设置与诊断入口 |
| `src/Interface.cs` | 主窗口布局、侧栏、设置与帮助 |
| `src/Theme.cs` | 绘制、主题、按钮和图标 |
| `src/Core.cs` | 参数构造、格式解析和进程执行 |
| `src/Batch.cs` | 并行队列、任务进度、Cookie 和输出隔离 |
| `src/BatchInterface.cs` | 批量交互及任务弹窗 |
| `src/make-icon.ps1` / `src/app.ico` | 图标生成脚本和图标资源 |
| `tests/` | C# 测试及本地 Node.js HTTP 服务 |
| `build.ps1` | 编译主程序，并重新生成图标 |
| `check-tools.ps1` | 检查运行组件，可选 SHA-256 校验 |
| `test-all.ps1` | 按依赖顺序执行全部现有测试 |
| `tools/` | 组件说明、许可证和校验清单；二进制不提交 |

## 提交与发布

上传流程与排除内容见 [GIT_UPLOAD.md](GIT_UPLOAD.md)，本次源码整理记录见 [CHANGELOG.md](CHANGELOG.md)。主程序、历史版本、便携包、构建缓存、下载媒体、测试产物和个人配置已由 `.gitignore` 排除。正式可执行程序可以作为远程仓库的 Release 附件单独发布。

第三方组件来源及许可证记录见 [THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt)。本仓库尚未指定拾链自身源码的开源许可证；第三方许可证仅适用于对应组件。

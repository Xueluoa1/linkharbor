# 运行组件

源码库只保留本说明、许可证文本和 `SHA256SUMS.txt`。EXE / DLL 体积较大，不提交到 Git。

## 准备方法

最直接的方法是从当前拾链便携版复制整个 `tools/`。也可从下列项目来源取得对应 Windows x64 组件，放在本目录：

| 组件 | 当前本地基线（非自动追踪最新版） | 文件 |
| --- | --- | --- |
| yt-dlp | `2026.10.26` | `yt-dlp.exe` |
| FFmpeg / FFprobe | `N-128280-ga88838af48-20261101`，共享构建 | `ffmpeg.exe`、`ffprobe.exe` 和随包 DLL |
| Node.js | `v24.16.0` | `node.exe` |

来源按现有 `THIRD_PARTY_NOTICES.txt` 记录：

- yt-dlp：[对应发行页](https://github.com/yt-dlp/yt-dlp/releases/tag/2026.10.26)。
- FFmpeg：[yt-dlp/FFmpeg-Builds](https://github.com/yt-dlp/FFmpeg-Builds)，使用 `ffmpeg-master-latest-win64-gpl-shared.zip` 类型的 x64 共享构建，将其中 `bin/` 的 EXE / DLL 放入本目录。`latest` 构建会变化，复现本地基线应使用原便携包；不同构建不能混用 DLL。
- Node.js：[v24.16.0 文件目录](https://nodejs.org/dist/v24.16.0/)，使用 Windows x64 发行包中的 `node.exe`。

本地基线的 DLL 为：`avcodec-63.dll`、`avdevice-63.dll`、`avfilter-12.dll`、`avformat-63.dll`、`avutil-61.dll`、`swresample-7.dll`、`swscale-10.dll`。

## 检查

在项目根目录运行：

```powershell
.\check-tools.ps1                # 检查四个入口程序是否存在
.\check-tools.ps1 -VerifyHashes  # 核对清单中全部 EXE / DLL 的 SHA-256
```

`SHA256SUMS.txt` 描述当前本地组件，不能用于校验任意其他版本。升级组件后应重新验证下载来源、运行测试，并一起更新版本记录、许可证与校验清单。不要通过直接修改校验值来掩盖来源不明的文件。

许可证文本保留为 `yt-dlp-LICENSE.txt`、`FFmpeg-LICENSE.txt` 和 `Node-LICENSE.txt`。详细分发说明沿用根目录 `THIRD_PARTY_NOTICES.txt`。

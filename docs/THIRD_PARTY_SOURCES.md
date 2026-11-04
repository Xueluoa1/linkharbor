# 第三方组件与源码入口

拾链 v1.0.1 通过独立进程调用下列组件。组件未作二进制修改；源码库不提交第三方 EXE/DLL，便携包保留许可证。拾链 GPL-3.0-only 不替代这些组件及其依赖的许可证。

| 组件 | 本次二进制基线 | 对应源码与构建入口 |
| --- | --- | --- |
| yt-dlp | 2026.10.26 Windows x64 | [固定版本源码](https://github.com/yt-dlp/yt-dlp/tree/2026.10.26)、[源码归档](https://github.com/yt-dlp/yt-dlp/archive/refs/tags/2026.10.26.tar.gz)、[构建说明](https://github.com/yt-dlp/yt-dlp/tree/2026.10.26#compile)、[第三方许可证](https://github.com/yt-dlp/yt-dlp/blob/2026.10.26/THIRD_PARTY_LICENSES.txt) |
| FFmpeg / FFprobe | N-128280-ga88838af48-20261101，win64 GPL shared | [FFmpeg 源码提交](https://github.com/FFmpeg/FFmpeg/commit/a88838af48)、[源码归档](https://github.com/FFmpeg/FFmpeg/archive/a88838af48.tar.gz)、[上游构建脚本、补丁及依赖](https://github.com/yt-dlp/FFmpeg-Builds) |
| Node.js | v24.16.0 Windows x64 | [版本源码](https://github.com/nodejs/node/tree/v24.16.0)、[源码归档](https://nodejs.org/dist/v24.16.0/node-v24.16.0.tar.gz)、[构建说明](https://github.com/nodejs/node/blob/v24.16.0/BUILDING.md) |

yt-dlp 源码采用 Unlicense，其官方 Windows 打包程序还包含第三方代码，须按其分发说明处理 GPLv3+ 等许可。FFmpeg 本次使用 GPL 构建；Node.js 保留 MIT 及其捆绑依赖声明。许可证文件位于 `tools/`，yt-dlp 打包依赖声明一并保存在 `tools/yt-dlp-THIRD-PARTY-LICENSE.txt`。

FFmpeg 构建的 `latest` 下载标签会变化；上述 FFmpeg 源码提交与 `tools/SHA256SUMS.txt` 用于标识本次基线。项目不声称重新构建能逐字节得到相同二进制。分发者应提供适用许可证要求的对应源码及构建材料，不能仅用拾链源码替代第三方源码。

本次二进制报告的 FFmpeg 编译选项保存在 [FFMPEG_BUILD_CONFIG.txt](FFMPEG_BUILD_CONFIG.txt)，便于识别启用的第三方组件。

拾链自身 v1.0.1 源码、构建脚本和许可随 Git 标签及 Release 源码包提供：[v1.0.1](https://github.com/Xueluoa1/linkharbor/tree/v1.0.1)。

# 仓库维护与发布

仓库：[Xueluoa1/linkharbor](https://github.com/Xueluoa1/linkharbor)。远程名 `origin`，分支 `master`；v1.0.1 起按作者决定公开源码，采用 GPL-3.0-only。

## 日常提交

```powershell
git status --short
git add src tests docs README.md CHANGELOG.md AUTHOR_NOTICE.md LICENSE 使用说明.txt build.ps1 package.ps1 check-tools.ps1 test-all.ps1 test.ps1 test-batch.ps1 test-ui.ps1 THIRD_PARTY_NOTICES.txt tools .gitignore .gitattributes GIT_UPLOAD.md
git diff --cached --stat
git diff --cached
git commit -m "描述本次修改"
git push origin master
```

检查暂存内容，不要加入个人 Cookie、代理凭据、下载文件、配置、日志或测试产物。第三方许可证保留原文，不为了格式检查改写其内容。

## 正式发布

1. 修改 `src/AssemblyInfo.cs` 的统一版本号，同时更新 README、教程、更新日志和发布说明。
2. 执行 `check-tools.ps1 -VerifyHashes`、`test-all.ps1`，确认编译和测试通过。
3. 执行 `package.ps1 -SkipBuild` 生成与当前已测试 EXE 一致的便携包；检查包内文件、EXE 版本、SHA-256 和解压后的启动。
4. 提交源码并推送；为同一提交创建版本标签，例如 `git tag -a v1.0.1 -m "拾链 v1.0.1 正式版"`，再 `git push origin v1.0.1`。不要重复或强制覆盖已经发布的标签。
5. 在 GitHub Releases 选择该标签，发布正式版本，上传便携 ZIP、源码 ZIP 和 SHA-256 清单，并附上 `docs/releases/` 中的对应说明。
6. 核对发布页文件大小、下载链接和可见性。第三方组件源码及构建入口随说明一并提供。

`.gitignore` 排除 EXE、DLL、ZIP、build、dist、test-output、下载目录及个人配置。大文件只作为 Release 附件；不要使用 `git add -f` 强行提交。

## 清理旧版本

先确认新正式版构建、测试和打包通过，再删除本机旧的“更新版/紧凑版/新版/修复版/多任务版”重复程序及旧分发 ZIP。保留当前 `拾链下载器.exe`、当前版本 Release 文件、`tools/`、自己的 `下载/` 和 `settings.json`。Git 历史保留，不需要重写历史。

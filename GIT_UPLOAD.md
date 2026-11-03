# Git 上传准备

## 当前仓库

- GitHub：`https://github.com/Xueluoa1/linkharbor`
- 可见性：Private（私有）。
- 远程名称：`origin`，地址：`https://github.com/Xueluoa1/linkharbor.git`。
- 本地初始分支保留为 `master`。

首次上传后，日常更新使用 `git add .`、`git commit` 和 `git push`。下方首次提交与连接远程步骤供新目录初始化时参考，无需在已配置的目录中重复执行。

## 本次整理范围

以 `src/` 的 2026-11-03 多任务版为准。根目录旧版 EXE 和便携包保留在本机，均不加入源码库。源码包只包含源码、图标、构建与测试脚本、文档、第三方许可证和组件校验清单。

已排除：`build/`、`dist/`、`test-output/`、`下载/`、第三方二进制、所有 EXE / DLL / ZIP、`settings.json`、常见 Cookie 文件和环境密钥文件。不要使用 `git add -f` 将上述本地内容强行加入。

## 首次提交

当前仓库已经初始化；无需再次 `git init`。从源码 ZIP 解压的目录不包含 `.git`，请先执行 `git init`。在项目根目录执行：

```powershell
git status --short
git add .
git diff --cached --stat
git diff --cached --check
git diff --cached
git commit -m "chore: prepare LinkHarbor multitask source for Git"
```

首次提交前检查暂存内容，确保没有自己的 Cookie、代理凭据、下载媒体和测试产物。若 Git 提示缺少身份，配置自己的 `user.name` 和 `user.email` 后重新提交。

## 连接远程仓库

在 GitHub / Gitee / 自建 Git 服务创建空仓库，不预置 README 或 LICENSE。将下方占位地址替换为真实仓库地址：

```powershell
git remote add origin <你的仓库地址>
git push -u origin HEAD
```

如果已有 `origin`，先用 `git remote -v` 确认目标，无需重复添加。这里保留现有分支名；推送不会自动重命名分支。

## 源码与便携版

- 源码库：提交 Git 可见的项目文件；克隆后按 `tools/README.md` 准备运行依赖。
- 便携版：主程序、完整 `tools/`、使用说明和第三方声明单独打包，上传至 Release 附件。不要打包本人的 `settings.json`、Cookie、下载文件和测试输出。
- 仓库保持私有；拾链自身源码尚未选定开源许可证。不要将可见性改为 Public，除非项目所有者明确决定公开。

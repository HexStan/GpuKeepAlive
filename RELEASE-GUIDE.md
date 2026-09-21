# 版本发布指南

> 版本号管理与发布触发的行为约束见 [AGENTS.md](AGENTS.md)。

## 前置条件

- 已安装 .NET 10 SDK、Git、GitHub CLI，且 `gh auth status` 已登录
- 版本号的唯一来源是仓库根目录 `Directory.Build.props` 中的 `<Version>` 属性（格式 `x.y.z`，语义化版本）；三个 csproj 均自动继承该属性，不单独设置版本号
- 发布操作须在默认分支 `master` 的最新代码上进行
- 撰写发布注记前，先查看既有 Release 的行文风格：`gh release view vX.Y.Z`

## 发布步骤

> 以下 `x.y.z` 指本次发布的版本号，即 `Directory.Build.props` 中待提交的现有内容。

### 1. 确认版本号就绪

- 读取根目录 `Directory.Build.props` 中 `<Version>` 的现有内容，作为本次发布的版本号 `x.y.z`（Agent 不决定进位、不修改该属性）
- 确认 `Directory.Build.props` 存在未提交的修改（`git show HEAD:Directory.Build.props` 与工作区内容不一致；脚本亦会校验，若无未提交修改将中止发布）

### 2. 分析变更内容

分析上个版本 tag 至今的变更，作为发布注记素材：

```bash
git log v[上一版本]..HEAD --oneline
git diff v[上一版本]..HEAD --stat
```

同时查看当前未提交改动（`git status`、`git diff`）：它们将随本次发布入库，作为发布注记与提交信息的素材。发布前，Agent 可依据变更类型决定是否将其拆分为多条提交并先行提交；若不拆分，则留在工作区由发布脚本一并提交。无论是否拆分，版本号修改（`Directory.Build.props`）都不得提前提交，须保留至发布时由脚本提交。

按既有 Release 的风格撰写**中文**发布注记，保存为 Markdown 文件（建议 `dist/release-notes-vx.y.z.md`，`dist/` 已被 gitignore）。注记结构：

- 开头一段概述本版本主题
- 分类条目（按需）：`### 新特性` / `### 修复` / `### 破坏性变更` 等，加粗要点 + 冒号 + 说明
- 必要时附 `### 升级提示`（如配置格式变化、用户需手动操作等）
- 结尾：`完整说明见 [README](https://github.com/HexStan/GpuKeepAlive/blob/master/README.md)。`
- 不要罗列 `chore: 版本号升至 …` 之类的纯版本提交

### 3. 编译、打包并发布（推荐脚本一步完成）

```powershell
powershell -ExecutionPolicy Bypass -File scripts/release.ps1 -NotesFile dist/release-notes-vx.y.z.md -CommitMessage "<提交信息>"
```

脚本将依次执行：

1. **校验**：从 `Directory.Build.props` 的 `<Version>` 读取版本号并校验格式、确认其有未提交修改、`gh` 登录状态、本地不落后远端、tag/Release 不重复
2. **编译**：基于最新代码 `dotnet publish -c Release -r win-x64 --self-contained` 自包含单文件压缩发布（GUI 项目须附加 `-p:IncludeNativeLibrariesForSelfExtract=true`，规避 .NET 10 WPF 单文件的 P/Invoke 解析缺陷；与 `编译.ps1` 一致）
3. **打包**：在 `dist/` 下生成两个压缩包（每个二进制一个，zip 内仅含对应 exe）：
   - CLI：`GpuKeepAliveCli.exe` → `GpuKeepAlive-vx.y.z-CLI-win-x64.zip`
   - GUI：`GpuKeepAlive.exe` → `GpuKeepAlive-vx.y.z-GUI-win-x64.zip`
4. **提交版本号并打 tag**：确认后提交工作区剩余未提交改动（若其余改动已提前拆分提交，此处通常仅版本号修改），提交信息由 Agent 依据变更内容拟定、经 `-CommitMessage` 传入；创建 `vx.y.z` 指向该提交，推送分支与 tag 到 origin
5. **发布 Release**：`gh release create vx.y.z --title "vx.y.z" --notes-file <注记> <两个 zip>`，**直接发布**（非 Draft）

发布前脚本会展示摘要并要求确认（`-Yes` 可跳过确认）。

脚本常用参数：

| 参数 | 说明 |
|---|---|
| `-NotesFile <路径>` | 发布注记 Markdown 文件；缺省时自动生成简易变更日志草稿（建议人工改写后重新发布） |
| `-CommitMessage <信息>` | **必填**。发布提交的信息，由 Agent 依据变更内容拟定（该提交包含版本号修改及所有未提前拆分提交的改动） |
| `-SkipBuild` | 跳过编译，复用 `dist/` 下已有产物 |
| `-Yes` | 跳过发布前确认 |

### 4. 核对结果

打开脚本输出的 Release URL，确认：标题为 `vx.y.z`、注记完整、`GpuKeepAlive-vx.y.z-CLI-win-x64.zip` 与 `GpuKeepAlive-vx.y.z-GUI-win-x64.zip` 两个构件已上传、Latest 标记正确。

## 手动发布（脚本不可用时）

按上述第 2 步撰写注记后，依次手动执行：

```powershell
# 编译（GUI 须带 IncludeNativeLibrariesForSelfExtract）
dotnet publish src/GpuKeepAlive.Gui/GpuKeepAlive.Gui.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
dotnet publish src/GpuKeepAlive.Cli/GpuKeepAlive.Cli.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o dist

# 打包（zip 内仅含 exe）
Compress-Archive -Path dist/GpuKeepAliveCli.exe -DestinationPath dist/GpuKeepAlive-vx.y.z-CLI-win-x64.zip -Force
Compress-Archive -Path dist/GpuKeepAlive.exe   -DestinationPath dist/GpuKeepAlive-vx.y.z-GUI-win-x64.zip -Force

# 提交版本号（维护者已修改 Directory.Build.props 但未提交），打 tag 并推送
# 其余改动可在此步之前由 Agent 按类型拆分提交；剩余改动由本步提交，提交信息由 Agent 拟定
git add -A
git commit -m "<提交信息>"
git tag vx.y.z
git push origin HEAD vx.y.z

# 发布 Release
gh release create vx.y.z --title "vx.y.z" --notes-file dist/release-notes-vx.y.z.md dist/GpuKeepAlive-vx.y.z-CLI-win-x64.zip dist/GpuKeepAlive-vx.y.z-GUI-win-x64.zip
```

## 注意事项

- tag 必须指向包含本次版本号提交的 commit（该提交由发布脚本在发布时完成）
- 压缩包内仅含对应 exe，不含 pdb 等其他文件
- 构建产物目录为 `dist/`（已被 gitignore）；`编译.ps1` 用于日常本地构建，`scripts/release.ps1` 用于正式发布，两者编译参数保持一致
- 撤回误发布（须维护者指示）：`gh release delete vx.y.z --yes`，并删除远端 tag `git push origin :refs/tags/vx.y.z`

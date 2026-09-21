# GpuKeepAlive 版本发布脚本
# 用法: powershell -ExecutionPolicy Bypass -File scripts/release.ps1 -CommitMessage <提交信息> [-NotesFile <发布注记.md>] [-SkipBuild] [-Yes]
# 流程: 读取 Directory.Build.props 的版本号(须存在未提交修改) -> 编译 -> 打包 -> 确认 -> 提交 -> 打 tag 推送
#       -> gh release create 直接发布 (标题 vx.y.z, 注记, 构件为两个 zip)
# 仅应在维护者主动下达发布指令时运行; 版本号进位由维护者决定: 维护者修改 Directory.Build.props 但不提交,
# 本脚本在发布时提交剩余未提交改动 (要求版本号存在未提交修改, 否则中止; 与版本号无关的其余改动可由
# 调用者按类型拆分后提前提交); 提交信息由调用者依据变更内容通过 -CommitMessage 传入
param(
    [string]$NotesFile,      # 发布注记 Markdown 文件路径; 缺省时自动生成简易变更日志
    [string]$CommitMessage,  # 发布提交的信息 (涵盖版本号修改及未提前提交的其余改动, 必填, 由调用者依据变更内容拟定)
    [switch]$SkipBuild,      # 跳过编译, 直接使用 dist/ 下已有产物
    [switch]$Yes             # 跳过发布前的人工确认
)

$ErrorActionPreference = "Stop"

function Fail([string]$msg) {
    Write-Host "[ERROR] $msg" -ForegroundColor Red
    exit 1
}

function Run([string]$cmd) {
    Write-Host "> $cmd" -ForegroundColor DarkGray
    Invoke-Expression $cmd
    if ($LASTEXITCODE -ne 0) { Fail "命令执行失败: $cmd" }
}

$root = Split-Path $PSScriptRoot -Parent
$distDir = Join-Path $root "dist"

# ---------- 前置校验 ----------
foreach ($tool in @("dotnet", "gh", "git")) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { Fail "未找到 $tool, 请先安装" }
}
# 探测类命令经 cmd /c 重定向 stderr, 规避 PS5.1 中 EAP=Stop 时 stderr 重定向抛 NativeCommandError 的问题
$null = & cmd /c "gh auth status >nul 2>nul"
if ($LASTEXITCODE -ne 0) { Fail "GitHub CLI 未登录, 请先执行: gh auth login" }

# 版本号来源: 仓库根目录 Directory.Build.props 的 <Version> 属性 (由维护者修改, 本脚本仅在发布时提交)
$propsFile = Join-Path $root "Directory.Build.props"
if (-not (Test-Path $propsFile)) { Fail "未找到版本号文件: $propsFile" }
$propsContent = Get-Content $propsFile -Raw
$Version = if ($propsContent -match '<Version>\s*(\d+\.\d+\.\d+)\s*</Version>') { $Matches[1] } else { "" }
if (-not $Version) { Fail "Directory.Build.props 中缺少 <Version>x.y.z</Version> (语义化版本, 数字)" }
$tag = "v$Version"

if (-not $CommitMessage) { Fail "请通过 -CommitMessage 指定发布提交信息 (由调用者依据变更内容拟定)" }

Write-Host "===================================================" -ForegroundColor Cyan
Write-Host "  GpuKeepAlive Release $tag" -ForegroundColor Cyan
Write-Host "===================================================" -ForegroundColor Cyan

Push-Location $root
try {
    # 发布前提: 版本号须存在未提交的修改 (维护者已写入新版本号等待发布);
    # 该文件在 HEAD 中不存在 (首次引入) 同样视为存在未提交修改
    $headProps = (& cmd /c "git show HEAD:Directory.Build.props 2>nul") -join "`n"
    $headVersion = if ($LASTEXITCODE -eq 0 -and $headProps -match '<Version>\s*(\d+\.\d+\.\d+)\s*</Version>') { $Matches[1] } else { "" }
    if ($headVersion -eq $Version) {
        Fail "Directory.Build.props 无未提交的修改, 不执行发布流程 (发布仅由维护者主动指令发起)"
    }
    git fetch origin --quiet
    if ($LASTEXITCODE -ne 0) { Fail "git fetch 失败, 请检查网络后重试" }
    $upstream = & cmd /c "git rev-parse --abbrev-ref --symbolic-full-name @{u} 2>nul"
    if ($LASTEXITCODE -eq 0 -and $upstream) {
        $behind = git rev-list --count "HEAD..$upstream"
        if ($behind -gt 0) { Fail "当前分支落后 $upstream $behind 个提交, 请先 pull 再发布" }
    }

    # tag / release 重复检查
    $null = & cmd /c "git rev-parse -q --verify refs/tags/$tag >nul 2>nul"
    if ($LASTEXITCODE -eq 0) { Fail "tag $tag 已存在" }
    $null = & cmd /c "gh release view $tag >nul 2>nul"
    if ($LASTEXITCODE -eq 0) { Fail "GitHub 上已存在 Release $tag" }

    # 上一个版本 tag (用于变更分析)
    $prevTag = git tag --list "v*" |
        Where-Object { $_ -match '^v\d+\.\d+\.\d+$' } |
        Sort-Object { [version]$_.TrimStart("v") } |
        Select-Object -Last 1
    $changeRange = if ($prevTag) { "$prevTag..HEAD" } else { "HEAD" }

    # ---------- 1. 编译 ----------
    if ($SkipBuild) {
        Write-Host "`n[1/4] 跳过编译 (-SkipBuild), 使用 dist/ 已有产物" -ForegroundColor Green
    }
    else {
        Write-Host "`n[1/4] 基于最新代码编译 (Release win-x64, 自包含单文件)..." -ForegroundColor Green
        if (Test-Path $distDir) { Remove-Item $distDir -Recurse -Force }
        $targets = @(
            @{ Project = "src/GpuKeepAlive.Gui/GpuKeepAlive.Gui.csproj"; Exe = "GpuKeepAlive.exe";    Extra = "-p:IncludeNativeLibrariesForSelfExtract=true" },
            @{ Project = "src/GpuKeepAlive.Cli/GpuKeepAlive.Cli.csproj"; Exe = "GpuKeepAliveCli.exe"; Extra = "" }
        )
        # GUI (WPF) 在 .NET 10 压缩单文件发布下存在内部 P/Invoke 解析缺陷, 须配合 IncludeNativeLibrariesForSelfExtract
        foreach ($t in $targets) {
            Run "dotnet publish `"$($t.Project)`" -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true $($t.Extra) -o `"$distDir`""
            $exe = Join-Path $distDir $t.Exe
            if (-not (Test-Path $exe)) { Fail "编译产物缺失: $exe" }
            Copy-Item $exe -Destination (Join-Path $root $t.Exe) -Force
        }
    }

    # ---------- 2. 打包 ----------
    Write-Host "`n[2/4] 打包压缩包..." -ForegroundColor Green
    $cliExe = Join-Path $distDir "GpuKeepAliveCli.exe"
    $guiExe = Join-Path $distDir "GpuKeepAlive.exe"
    foreach ($exe in @($cliExe, $guiExe)) {
        if (-not (Test-Path $exe)) { Fail "缺少 $exe, 请先编译 (不要对首次发布使用 -SkipBuild)" }
    }
    $cliZip = Join-Path $distDir "GpuKeepAlive-v$Version-CLI-win-x64.zip"
    $guiZip = Join-Path $distDir "GpuKeepAlive-v$Version-GUI-win-x64.zip"
    Compress-Archive -Path $cliExe -DestinationPath $cliZip -Force
    Compress-Archive -Path $guiExe -DestinationPath $guiZip -Force
    Write-Host "  $cliZip ($([math]::Round((Get-Item $cliZip).Length/1MB,1)) MB)"
    Write-Host "  $guiZip ($([math]::Round((Get-Item $guiZip).Length/1MB,1)) MB)"

    # ---------- 3. 发布注记 ----------
    Write-Host "`n[3/4] 准备发布注记..." -ForegroundColor Green
    $generatedNotes = $false
    if ($NotesFile) {
        if (-not (Test-Path $NotesFile)) { Fail "注记文件不存在: $NotesFile" }
        $notesPath = (Resolve-Path $NotesFile).Path
    }
    else {
        $generatedNotes = $true
        $notesPath = Join-Path $distDir "release-notes-v$Version.md"
        $commits = git log $changeRange --pretty=format:"- %s" |
            Where-Object { $_ -and $_ -notmatch 'chore: 版本号升至' }
        $changeLog = if ($commits) { $commits -join "`n" } else { "- (无提交记录)" }
        $notesContent = @"
## GpuKeepAlive $tag

（此处应为本版本概述）

### 变更

$changeLog

> 本注记由脚本依据 ``git log $changeRange`` 自动生成, 建议按既有 Release 风格改写后重新运行并指定 -NotesFile。
"@
        # 写入 UTF-8 无 BOM, 避免 BOM 出现在 Release 正文开头
        [System.IO.File]::WriteAllText($notesPath, $notesContent, (New-Object System.Text.UTF8Encoding($false)))
        Write-Host "  未指定 -NotesFile, 已生成草稿: $notesPath" -ForegroundColor Yellow
    }
    Write-Host "  注记: $notesPath"

    # ---------- 确认 ----------
    Write-Host ""
    Write-Host "即将发布:" -ForegroundColor Cyan
    Write-Host "  提交:    $CommitMessage"
    Write-Host "  Tag:     $tag  (指向该提交)"
    Write-Host "  Release: 标题 $tag, 直接发布 (非 Draft)"
    Write-Host "  构件:    $(Split-Path $cliZip -Leaf), $(Split-Path $guiZip -Leaf)"
    $pendingChanges = git status --porcelain
    if ($pendingChanges) {
        Write-Host "  发布提交将包含:" -ForegroundColor Yellow
        $pendingChanges | ForEach-Object { Write-Host "    $_" }
        Write-Host "  提示: 如需按类型拆分提交, 可先行提交后再运行本脚本 (版本号修改除外)" -ForegroundColor DarkGray
    }
    if (-not $Yes) {
        $answer = Read-Host "确认发布? (y/N)"
        if ($answer -notmatch '^[Yy]') { Fail "已取消" }
    }

    # ---------- 4. 提交版本号 / tag / 发布 ----------
    Write-Host "`n[4/4] 提交版本号, 打 tag 并发布 Release..." -ForegroundColor Green
    Run "git add -A"
    # 提交信息为调用者传入的自由文本, 绕过 Invoke-Expression 直接调用, 规避引号转义问题
    Write-Host "> git commit -m $CommitMessage" -ForegroundColor DarkGray
    & git commit -m $CommitMessage
    if ($LASTEXITCODE -ne 0) { Fail "命令执行失败: git commit" }
    Run "git tag $tag"
    Run "git push origin HEAD"
    Run "git push origin $tag"

    Run "gh release create $tag --title $tag --notes-file `"$notesPath`" `"$cliZip`" `"$guiZip`""
    if ($generatedNotes) {
        Write-Host "  注意: 本次使用的是脚本自动生成的简易注记, 建议人工润色" -ForegroundColor Yellow
    }

    $releaseUrl = gh release view $tag --json url -q .url
    Write-Host ""
    Write-Host "===================================================" -ForegroundColor Cyan
    Write-Host "[SUCCESS] $tag 发布完成!" -ForegroundColor Green
    Write-Host "  $releaseUrl" -ForegroundColor Green
    Write-Host "  请打开链接核对标题/注记/构件/Latest 标记" -ForegroundColor Cyan
    Write-Host "===================================================" -ForegroundColor Cyan
}
finally {
    Pop-Location
}

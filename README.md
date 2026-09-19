# GPU KeepAlive - GPU 保活工具

## 1. 开发背景

本项目源于作者实际使用中遇到的一个双显卡拓扑问题：

- **拓扑结构**：主板同时存在核显（iGPU，如 Intel UHD 730）与独显（dGPU，如 AMD Radeon RX 6500 XT），显示器物理连接在独显上。
- **卡顿成因**：
  当某些程序强制使用核显渲染时，核显生成的帧需要通过 PCIe 总线和共享内存跨适配器（Cross-Adapter Scan-Out / Blit）拷贝到独显再输出到显示器。
  当画面静止无变动时（0 FPS），核显与 PCIe 链路进入深度节能状态（如 Intel RC6、PCIe ASPM L1/L1.2、显存降频）。一旦画面发生变动，显卡从深度休眠中唤醒、恢复时钟频率、重新建立跨适配器流水线需要 **100~500ms（0.x秒）的硬件响应延迟**，导致用户感知到的严重顿挫或初次变动瞬间卡顿。
- **解决思路**：
  在后台以极低能耗（CPU < 0.1%，GPU 3D 引擎 0.2%~0.5%）持续向目标显卡提交微量 3D 渲染帧，**使显卡 3D 核心维持在活跃就绪状态，阻止其掉入深度休眠**。

上述机理并非核显独有——任何显卡（核显、独显或虚拟 GPU）在深度节能调度下都可能出现“静止画面变动瞬间的唤醒卡顿”，本工具旨在解决这个问题。

---

## 2. 使用方法

### 方式 A：直接运行（最简便）

在项目根目录直接运行 **`GpuKeepAlive.exe`**：
- 不带参数启动：调度目标严格遵循 Windows 图形首选项（配置方法见方式 B）。
- 需要手动指定显卡：先执行 `GpuKeepAlive.exe -l` 查看显卡列表及索引，再以 `-a` 参数指定（支持索引或品牌/系列关键字，详见第 3 节），例如 `GpuKeepAlive.exe -a intel -f 15 -i 1` 表示对 Intel 显卡以 15 FPS 轻量负载持续保活。
- 需要后台无窗口运行：追加 `--hide` 参数。

### 方式 B：通过 Windows 图形首选项指派（系统原生方式）

若您希望交由 Windows 自行调度：
1. 打开 Windows **“设置”** -> **“系统”** -> **“屏幕”** -> **“显示卡”**（或“图形首选项”）。
2. 在“添加应用”中点击“浏览”，选择 `GpuKeepAlive.exe`。
3. 添加后点击该应用，点击 **“选项”**，勾选 **“节能”** 或 **“高性能”**（取决于您希望保活哪块显卡）并保存。
4. 直接无参数启动 `GpuKeepAlive.exe`，程序检测到未指定参数时，将严格遵循 Windows 设置的图形首选项。

---

## 3. 命令行参数详解 (`GpuKeepAlive.exe`)

```text
GpuKeepAlive.exe [参数]

参数选项:
  --list, -l
      列出系统当前所有可用的 GPU 显卡适配器及其索引。

  --adapter <索引或名称>, -a <索引或名称>
      指定要保活的目标显卡：
      - 留空 (默认): 自动遵循 Windows 屏幕图形首选项。
      - 数字 (如 -a 1): 强制绑定编号为 1 的显卡。
      - 文本 (如 -a intel): 模糊匹配名称包含 intel 的显卡。
        支持的主流品牌/系列关键字: intel、arc、nvidia、geforce、rtx、amd、radeon、rx 等。

  --fps <数值>, -f <数值>
      渲染保活频率，默认 15 FPS。推荐 15 ~ 60。
      15 FPS 下 CPU 占用率极低 (< 0.1%)，且足以维持 GPU 唤醒时钟。

  --intensity <1|2|3>, -i <1|2|3>
      负载强度等级 (默认: 1):
      1 = 轻微 (ClearRenderTargetView): GPU 占用约 0.2%~0.5%，纯指令提交，阻止时钟休眠。
      2 = 中等 (全屏 2D 顶点与像素着色光栅化): GPU 占用约 1%~2%，模拟常规 3D 渲染管线。
      3 = 进阶 (Compute Shader 计算着色器并行运算): GPU 占用约 3%~5%，激活完整流处理器单元。

  --hide, -h
      启动后自动隐藏控制台窗口，在后台静默运行。
```

### 常用示例
- 查看所有显卡：`GpuKeepAlive.exe -l`
- 指定 1 号显卡、15 FPS、等级 1：`GpuKeepAlive.exe -a 1 -f 15 -i 1`
- 按品牌关键字模糊匹配（不区分大小写）：`GpuKeepAlive.exe -a intel` 或 `GpuKeepAlive.exe -a nvidia`
- 后台静默运行：`GpuKeepAlive.exe -a 1 -i 1 --hide`

---

## 4. 验证与监控

启动后，可以打开 Windows **任务管理器** -> 点击 **“性能”** 选项卡 -> 选择目标 GPU（即程序当前绑定的那块显卡）：
- 在 3D 引擎曲线中，可以看到有一条持续稳定在 **0.3% ~ 0.5%** 的微小水平线。
- GPU 不再降频到断电级待机，从而彻底消除从静止画面变动时的“卡顿 0.x 秒”。
- CPU 与系统内存占用几乎为零，不影响日常其他任务运行。

---

## 5. 编译指南 (从源码构建)

如果克隆了本代码仓库，由于可执行文件未纳入版本控制，请先参照本章节进行编译构建。

### 5.1 环境准备

- **操作系统**：Windows 10 / Windows 11 (x64)
- **.NET SDK**：[.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 或更高版本
  - 安装完成后可在命令行中通过 `dotnet --version` 验证。
- **开发工具（可选）**：
  - Visual Studio 2026 / Visual Studio 2022 (v17.12+)（勾选“.NET 桌面开发”工作负载）
  - 或 VS Code + C# Dev Kit 扩展

### 5.2 方式一：一键脚本编译（推荐）

在项目根目录下运行 **`.\编译.ps1`**（或右键选择“使用 PowerShell 运行”）：
- 脚本将自动调用 `dotnet publish` 进行单文件独立发布（Self-Contained），包含内置运行时与单文件体积压缩。
- 编译完成后会自动将 `GpuKeepAlive.exe` 输出到项目根目录，随后即可直接运行。

### 5.3 方式二：.NET CLI 命令行手动编译

在项目根目录下打开终端（PowerShell 或 CMD）：

#### 1. 独立单文件发布（Self-Contained，推荐）
生成无需目标机器安装 .NET 运行时的独立单文件可执行程序：
```bash
# 发布至 dist 目录并开启单文件压缩
dotnet publish GpuKeepAlive/GpuKeepAlive.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o dist

# 复制到项目根目录，便于直接运行
copy dist\GpuKeepAlive.exe .
```

#### 2. 框架依赖发布（Framework-Dependent）
若目标机器已预先安装 .NET 10 运行时，可生成超小体积（约 1.3 MB）的单文件：
```bash
dotnet publish GpuKeepAlive/GpuKeepAlive.csproj -c Release -r win-x64 --no-self-contained -p:PublishSingleFile=true -o dist
copy dist\GpuKeepAlive.exe .
```

#### 3. 日常开发与调试构建
直接编译解决方案或工程以进行调试：
```bash
# 编译整个解决方案
dotnet build GpuKeepAlive.sln

# 直接运行项目（可追加测试参数）
dotnet run --project GpuKeepAlive/GpuKeepAlive.csproj -- -l
```

### 5.4 方式三：Visual Studio IDE 构建

1. 双击打开根目录下的 **`GpuKeepAlive.sln`**（或 `GpuKeepAlive.slnx`）。
2. 在顶部工具栏将配置选择为 **`Release`**，平台选择 **`Any CPU`** 或 **`x64`**。
3. 点击菜单 **“生成”** -> **“生成解决方案”**。
4. 如需发布独立单文件可执行程序：
   - 在“解决方案资源管理器”中右键点击 `GpuKeepAlive` 项目，选择 **“发布...” (Publish)**。
   - 目标选择 **“文件夹”**，配置部署模式为 **“独立” (Self-contained)**，目标运行时选择 **`win-x64`**。
   - 在“文件发布选项”中展开勾选 **“生成单个文件”** 和 **“启用压缩”**。
   - 点击 **“发布”** 按钮，将生成的 `GpuKeepAlive.exe` 拷贝至项目根目录即可。

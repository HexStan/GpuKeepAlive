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

## 2. 版本说明

本仓库提供两个共享同一核心逻辑的可执行程序：

| 程序 | 定位 |
| ---- | ---- |
| **`GpuKeepAlive.exe`** | **GUI 托盘版**：常驻系统托盘，图形化配置，支持配置持久化与服务状态自动恢复 |
| **`GpuKeepAliveCli.exe`** | **CLI 命令行版**：参数驱动，适合脚本调用与临时使用，不读取用户配置 |

两者均为单实例运行（各自的锁相互独立，可同时使用），均支持**同时保活多块显卡**。

> 注：显卡枚举已自动过滤不可保活的适配器——基于 CPU 的 "Microsoft Basic Render Driver" 软件渲染器，以及串流/远控软件（ToDesk、向日葵、Parsec 等）创建的**虚拟显示适配器**（它们在 DXGI 中会伪装成真实显卡，但保活负载实际全部转发到真实 GPU 上，为它们保活既无意义又会造成重复负载）。判定口径与任务管理器的 GPU 列表一致。

---

## 3. GUI 版使用方法 (`GpuKeepAlive.exe`)

### 3.1 基本操作

- 启动后不弹窗口，仅在系统托盘驻留图标（Win11 默认收纳在托盘折叠面板中）。
- **左键单击**托盘图标：打开配置窗口。
- **右键**托盘图标：显示菜单，其中的 **"退出"** 是唯一退出入口。
- 点击配置窗口右上角 **X**：仅隐藏窗口回到托盘，保活服务继续运行。
- 托盘图标悬停提示（Tooltip）实时显示服务状态。

### 3.2 配置窗口

- **显卡选择**：
  - `默认`：交由 Windows 图形首选项自动调度（配置方法见 3.3）；
  - `全部`：为检测到的所有硬件显卡同时保活；
  - `自定义`：在列表中自由勾选一块或多块显卡。
  - 列表中每块显卡均标注 **LUID**（适配器唯一标识，同型号多卡也互不相同），状态区同样按 LUID 显示实际保活目标，选了哪块、跑在哪块一目了然。
- **保活帧率**：1–240 FPS（默认 15）。
- **负载强度**：1 轻微 / 2 中等 / 3 进阶（含义同 CLI 版，见 4.2）。
- **服务**：启动/停止按钮；下方状态区实时显示每块目标显卡的运行状态与实际帧率。

### 3.3 配置持久化

- 配置保存在 **exe 同级目录**的 `GpuKeepAlive.config.json`（便携式，不写用户目录）。
- 任何有效配置变更（含服务启停）即时落盘；服务运行中修改配置会**立即生效**（以新配置重启保活线程）。
- **初次运行**（检测不到配置文件）：使用默认配置且**不自动启动服务**。
- **后续运行**：自动恢复上次的配置；若上次退出时服务正在运行，则启动时**自动恢复服务**（含配置中已失效的显卡索引时的处理：自动剔除失效项；若全部失效则不启动并气泡提醒）。

### 3.4 通过 Windows 图形首选项指派（默认模式）

若选择"默认"模式，由 Windows 自行调度保活目标：

1. 打开 Windows **“设置”** -> **“系统”** -> **“屏幕”** -> **“显示卡”**（或“图形首选项”）。
2. 在“添加应用”中点击“浏览”，选择 `GpuKeepAlive.exe`。
3. 添加后点击该应用，点击 **“选项”**，勾选 **“节能”** 或 **“高性能”**（取决于您希望保活哪块显卡）并保存。
4. 保活线程将严格遵循 Windows 设置的图形首选项。

---

## 4. CLI 版使用方法 (`GpuKeepAliveCli.exe`)

### 4.1 参数一览

```text
GpuKeepAliveCli.exe [参数]

参数选项:
  --list, -l
      列出系统当前所有可用的 GPU 显卡适配器及其索引与 LUID。
      (注: 已自动过滤 WARP 软件渲染器及串流/远控软件创建的虚拟显示适配器)

  --adapter <项[,项...]>, -a <项[,项...]>
      指定要保活的目标显卡，支持同时保活多块：
      - 逗号分隔多个目标 (如 -a 0,1)，参数可重复出现 (如 -a 0 -a 1)。
      - 若不指定 (默认): 交由系统 Windows 图形首选项自动调度。
      - 每项若为数字: 绑定指定索引的显卡 (如 -a 1)。
      - 每项若为文本: 模糊匹配显卡名称 (如 -a 4090，不区分大小写)。
        支持的主流品牌/系列关键字: intel、arc、nvidia、geforce、rtx、amd、radeon、rx 等。
      - 部分项匹配失败: 警告并跳过，继续保活其余显卡。
      - 全部项匹配失败: 报错退出，不会回退到系统默认调度。

  --fps <数值>, -f <数值>
      渲染保活帧率 (默认: 15 FPS)。推荐 15 ~ 60。

  --intensity <1|2|3>, -i <1|2|3>
      负载强度等级 (默认: 1)。

  --hide, -h
      启动后自动隐藏控制台窗口，在后台静默运行。
```

### 4.2 负载强度说明

| 等级 | 实现方式 | GPU 占用 |
| ---- | -------- | -------- |
| 1 轻微 | ClearRenderTargetView | 约 0.2%~0.5%，纯指令提交，阻止时钟休眠 |
| 2 中等 | 全屏 2D 顶点与像素着色光栅化 | 约 1%~2%，模拟常规 3D 渲染管线 |
| 3 进阶 | Compute Shader 计算着色器并行运算 | 约 3%~5%，激活完整流处理器单元 |

### 4.3 常用示例

- 查看所有显卡：`GpuKeepAliveCli.exe -l`
- 同时保活 0、1 号两块显卡：`GpuKeepAliveCli.exe -a 0,1 -f 15 -i 1`
- 按品牌关键字模糊匹配（不区分大小写）：`GpuKeepAliveCli.exe -a intel,nvidia`
- 后台静默运行：`GpuKeepAliveCli.exe -a 1 -i 1 --hide`

> CLI 版为单实例运行：同一时间仅允许一个保活进程，重复启动会被拒绝（`-l` / `--help` 不受限制）。CLI 版不读取也不写入 GUI 版的配置文件。

---

## 5. 验证与监控

启动后，可以打开 Windows **任务管理器** -> 点击 **“性能”** 选项卡 -> 选择目标 GPU（即程序当前绑定的那块显卡）：

- 在 3D 引擎曲线中，可以看到有一条持续稳定在 **0.3% ~ 0.5%** 的微小水平线。
- GPU 不再降频到断电级待机，从而彻底消除从静止画面变动时的“卡顿 0.x 秒”。
- CPU 与系统内存占用几乎为零，不影响日常其他任务运行。

### ⚠ 关于任务管理器的进程 GPU 归属显示

任务管理器"详细信息"页中进程的 **GPU 列**并不总是显示保活目标显卡——GUI 程序自身的界面渲染设备由 Windows 指派（可能与保活目标不同块），任务管理器展示的关联可能取自界面渲染设备而非保活设备。**判断保活是否落在正确的显卡上，请以 3D 引擎负载曲线（性能页）或程序状态区的 LUID 标识为准**，它们与 GPU 性能计数器实例名（`luid_0x..._0x...`）一一对应。

---

## 6. 编译指南 (从源码构建)

如果克隆了本代码仓库，由于可执行文件未纳入版本控制，请先参照本章节进行编译构建。

### 6.1 环境准备

- **操作系统**：Windows 10 / Windows 11 (x64)
- **.NET SDK**：[.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 或更高版本
  - 安装完成后可在命令行中通过 `dotnet --version` 验证。
- **开发工具（可选）**：
  - Visual Studio 2026 / Visual Studio 2022 (v17.12+)（勾选“.NET 桌面开发”工作负载）
  - 或 VS Code + C# Dev Kit 扩展

### 6.2 方式一：一键脚本编译（推荐）

在项目根目录下运行 **`.\编译.ps1`**（或右键选择“使用 PowerShell 运行”）：

- 脚本将依次发布 GUI 版（`GpuKeepAlive.exe`）与 CLI 版（`GpuKeepAliveCli.exe`），均为单文件独立发布（Self-Contained），包含内置运行时与单文件体积压缩。
- 编译完成后两个 exe 会自动复制到项目根目录，随后即可直接运行。

### 6.3 方式二：.NET CLI 命令行手动编译

在项目根目录下打开终端（PowerShell 或 CMD），以 GUI 版为例（CLI 版替换项目路径与 exe 名即可）：

#### 1. 独立单文件发布（Self-Contained，推荐）

生成无需目标机器安装 .NET 运行时的独立单文件可执行程序：

```bash
# 发布 GUI 版至 dist 目录并开启单文件压缩
# 注: WPF 在 .NET 10 的压缩单文件发布下须配合 IncludeNativeLibrariesForSelfExtract，
#     否则运行时 WPF 内部 P/Invoke 会抛 DllNotFoundException（一键脚本 编译.ps1 已自动处理）
dotnet publish src/GpuKeepAlive.Gui/GpuKeepAlive.Gui.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist

# 复制到项目根目录，便于直接运行
copy dist\GpuKeepAlive.exe .
```

CLI 版无 WPF，不受此限制：

```bash
dotnet publish src/GpuKeepAlive.Cli/GpuKeepAlive.Cli.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o dist
copy dist\GpuKeepAliveCli.exe .
```

#### 2. 框架依赖发布（Framework-Dependent）

若目标机器已预先安装 .NET 10 运行时，可生成超小体积的单文件：

```bash
dotnet publish src/GpuKeepAlive.Gui/GpuKeepAlive.Gui.csproj -c Release -r win-x64 --no-self-contained -p:PublishSingleFile=true -o dist
copy dist\GpuKeepAlive.exe .
```

#### 3. 日常开发与调试构建

直接编译解决方案或工程以进行调试：

```bash
# 编译整个解决方案
dotnet build GpuKeepAlive.slnx

# 直接运行 CLI 版（可追加测试参数）
dotnet run --project src/GpuKeepAlive.Cli/GpuKeepAlive.Cli.csproj -- -l
```

### 6.4 方式三：Visual Studio IDE 构建

1. 双击打开根目录下的 **`GpuKeepAlive.slnx`**。
2. 在顶部工具栏将配置选择为 **`Release`**，平台选择为 **`Any CPU`** 或 **`x64`**。
3. 点击菜单 **“生成”** -> **“生成解决方案”**。
4. 如需发布独立单文件可执行程序：
   - 在“解决方案资源管理器”中右键点击 `GpuKeepAlive.Gui`（或 `GpuKeepAlive.Cli`）项目，选择 **“发布...”** (Publish)。
   - 目标选择 **“文件夹”**，配置部署模式为 **“独立”** (Self-contained)，目标运行时选择 **`win-x64`**。
   - 在“文件发布选项”中展开勾选 **“生成单个文件”** 和 **“启用压缩”**。
   - 点击 **“发布”** 按钮，将生成的 exe 拷贝至项目根目录即可。

### 6.5 重新生成应用图标（可选）

GUI 版托盘与窗口图标位于 `src/GpuKeepAlive.Gui/Assets/app.ico`，如需调整可修改 `scripts/make-icon.ps1` 后重新运行：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/make-icon.ps1
```

---

## 7. 项目结构

```
src/
  GpuKeepAlive.Core/    共享核心库：显卡枚举、D3D11 保活 worker、多 GPU 管理
  GpuKeepAlive.Gui/     WPF 托盘版（产出 GpuKeepAlive.exe）
  GpuKeepAlive.Cli/     命令行版（产出 GpuKeepAliveCli.exe）
scripts/
  make-icon.ps1         应用图标生成脚本
```

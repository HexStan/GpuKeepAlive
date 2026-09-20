using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace GpuKeepAlive.Core;

/// <summary>
/// 单个 GPU 的保活单元：在专用后台线程上创建 D3D11 设备并持续提交微量渲染负载，
/// 阻止目标显卡因画面静止进入深度节能状态。
/// </summary>
public sealed class KeepAliveWorker : IDisposable
{
    private readonly GpuAdapterId? _adapterId;
    private readonly int _fps;
    private readonly int _intensity;
    private readonly string _displayName;

    private readonly object _gate = new();
    private KeepAliveStats _stats;
    private Thread? _thread;
    private CancellationTokenSource? _cts;

    public KeepAliveWorker(GpuAdapterId? adapterId, int fps, int intensity, string displayName)
    {
        _adapterId = adapterId;
        _fps = Math.Clamp(fps, 1, 240);
        _intensity = Math.Clamp(intensity, 1, 3);
        _displayName = displayName;
        _stats = new KeepAliveStats(displayName, null, KeepAliveWorkerStatus.Stopped, TimeSpan.Zero, 0, 0, null);
    }

    public KeepAliveStats Stats
    {
        get { lock (_gate) return _stats; }
    }

    public void Start()
    {
        Thread thread;
        lock (_gate)
        {
            if (_thread is not null)
                throw new InvalidOperationException("Worker 已启动。");
            _cts = new CancellationTokenSource();
            _thread = new Thread(RunLoop)
            {
                IsBackground = true,
                Name = $"GpuKeepAlive: {_displayName}",
            };
            thread = _thread;
        }
        thread.Start();
    }

    public void Stop(TimeSpan? timeout = null)
    {
        Thread? thread;
        lock (_gate) thread = _thread;
        _cts?.Cancel();
        thread?.Join(timeout ?? TimeSpan.FromSeconds(5));
    }

    public void Dispose()
    {
        Stop(TimeSpan.FromSeconds(2));
        _cts?.Dispose();
    }

    private void SetStats(KeepAliveStats stats)
    {
        lock (_gate) _stats = stats;
    }

    private void RunLoop()
    {
        var cancel = _cts!.Token;
        try
        {
            SetStats(Stats with { Status = KeepAliveWorkerStatus.Starting, ErrorMessage = null });
            Run(cancel);
        }
        catch (Exception ex)
        {
            SetStats(Stats with { Status = KeepAliveWorkerStatus.Failed, ErrorMessage = ex.Message });
            return;
        }
        SetStats(Stats with { Status = KeepAliveWorkerStatus.Stopped });
    }

    private void Run(CancellationToken cancel)
    {
        IDXGIAdapter1? adapter = null;
        if (_adapterId is { } id)
        {
            adapter = GpuAdapterEnumerator.OpenAdapter(id)
                ?? throw new InvalidOperationException($"未找到 [{id.Index}] {id.Name} 对应的显卡（显卡列表可能已变化）。");
        }

        var hr = D3D11.D3D11CreateDevice(
            adapter,
            adapter is null ? DriverType.Hardware : DriverType.Unknown,
            DeviceCreationFlags.BgraSupport,
            [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1],
            out ID3D11Device device,
            out ID3D11DeviceContext context);
        adapter?.Dispose();

        if (hr.Failure)
            throw new InvalidOperationException($"无法创建 Direct3D 11 设备: {hr}");

        try
        {
            string actualAdapterName;
            using (var dxgiDevice = device.QueryInterface<IDXGIDevice>())
            {
                dxgiDevice.GetAdapter(out IDXGIAdapter actualAdapter);
                using (actualAdapter)
                using (var actualAdapter1 = actualAdapter.QueryInterface<IDXGIAdapter1>())
                    actualAdapterName = actualAdapter1.Description1.Description;
            }

            const int texSize = 512;
            var texDesc = new Texture2DDescription
            {
                Width = (uint)texSize,
                Height = (uint)texSize,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.R8G8B8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource
                    | (_intensity == 3 ? BindFlags.UnorderedAccess : BindFlags.None)
            };
            using var renderTargetTexture = device.CreateTexture2D(texDesc);
            using var rtv = device.CreateRenderTargetView(renderTargetTexture);

            ID3D11VertexShader? vs = null;
            ID3D11PixelShader? ps = null;
            ID3D11ComputeShader? cs = null;
            ID3D11UnorderedAccessView? uav = null;
            ID3D11Buffer? constantBuffer = null;
            try
            {
                if (_intensity == 2)
                {
                    var vsBytecode = Compiler.Compile(Shaders.VertexShader, "VSMain", "vs.hlsl", "vs_5_0", ShaderFlags.OptimizationLevel3, EffectFlags.None);
                    vs = device.CreateVertexShader(vsBytecode.Span);

                    var psBytecode = Compiler.Compile(Shaders.PixelShader, "PSMain", "ps.hlsl", "ps_5_0", ShaderFlags.OptimizationLevel3, EffectFlags.None);
                    ps = device.CreatePixelShader(psBytecode.Span);

                    constantBuffer = device.CreateBuffer(new BufferDescription
                    {
                        ByteWidth = 16,
                        Usage = ResourceUsage.Dynamic,
                        BindFlags = BindFlags.ConstantBuffer,
                        CPUAccessFlags = CpuAccessFlags.Write
                    });
                }
                else if (_intensity == 3)
                {
                    var csBytecode = Compiler.Compile(Shaders.ComputeShader, "CSMain", "cs.hlsl", "cs_5_0", ShaderFlags.OptimizationLevel3, EffectFlags.None);
                    cs = device.CreateComputeShader(csBytecode.Span);
                    uav = device.CreateUnorderedAccessView(renderTargetTexture);

                    constantBuffer = device.CreateBuffer(new BufferDescription
                    {
                        ByteWidth = 16,
                        Usage = ResourceUsage.Dynamic,
                        BindFlags = BindFlags.ConstantBuffer,
                        CPUAccessFlags = CpuAccessFlags.Write
                    });
                }

                SetStats(Stats with
                {
                    ActualAdapterName = actualAdapterName,
                    Status = KeepAliveWorkerStatus.Running,
                    Elapsed = TimeSpan.Zero,
                    FramesSubmitted = 0,
                    ActualFps = 0,
                });

                long frameIntervalTicks = Stopwatch.Frequency / _fps;
                var clock = Stopwatch.StartNew();
                long nextFrameTicks = clock.ElapsedTicks;

                long frameCount = 0;
                float simTime = 0.0f;
                float dt = 1.0f / _fps;

                var statsWatch = Stopwatch.StartNew();
                long lastStatFrames = 0;

                while (!cancel.IsCancellationRequested)
                {
                    simTime += dt;
                    frameCount++;

                    if (_intensity == 1)
                    {
                        float r = 0.5f + 0.5f * MathF.Sin(simTime);
                        float g = 0.5f + 0.5f * MathF.Cos(simTime * 0.7f);
                        context.ClearRenderTargetView(rtv, new Color4(r, g, 0.5f, 1.0f));
                    }
                    else if (_intensity == 2 && vs != null && ps != null && constantBuffer != null)
                    {
                        var mapped = context.Map(constantBuffer, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
                        Marshal.StructureToPtr(new Vector4(simTime, 0, 0, 0), mapped.DataPointer, false);
                        context.Unmap(constantBuffer, 0);

                        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
                        context.IASetInputLayout(null);
                        context.VSSetShader(vs);
                        context.PSSetShader(ps);
                        context.PSSetConstantBuffer(0, constantBuffer);
                        context.RSSetViewport(new Viewport(0, 0, texSize, texSize));
                        context.OMSetRenderTargets(rtv);

                        context.Draw(3, 0);
                    }
                    else if (_intensity == 3 && cs != null && uav != null && constantBuffer != null)
                    {
                        var mapped = context.Map(constantBuffer, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
                        Marshal.StructureToPtr(new Vector4(simTime, 0, 0, 0), mapped.DataPointer, false);
                        context.Unmap(constantBuffer, 0);

                        context.CSSetShader(cs);
                        context.CSSetConstantBuffer(0, constantBuffer);
                        context.CSSetUnorderedAccessView(0, uav);

                        context.Dispatch((uint)(texSize / 8), (uint)(texSize / 8), 1);
                    }

                    context.Flush();

                    if (statsWatch.ElapsedMilliseconds >= 1000)
                    {
                        double elapsedSec = statsWatch.Elapsed.TotalSeconds;
                        double actualFps = (frameCount - lastStatFrames) / elapsedSec;
                        lastStatFrames = frameCount;
                        statsWatch.Restart();

                        SetStats(Stats with
                        {
                            Elapsed = clock.Elapsed,
                            FramesSubmitted = frameCount,
                            ActualFps = actualFps,
                        });
                    }

                    nextFrameTicks += frameIntervalTicks;
                    long sleepMs = (nextFrameTicks - clock.ElapsedTicks) * 1000 / Stopwatch.Frequency;
                    if (sleepMs > 1)
                    {
                        Thread.Sleep((int)sleepMs);
                    }
                    else if (sleepMs < -frameIntervalTicks * 2)
                    {
                        nextFrameTicks = clock.ElapsedTicks;
                    }
                }
            }
            finally
            {
                vs?.Dispose();
                ps?.Dispose();
                cs?.Dispose();
                uav?.Dispose();
                constantBuffer?.Dispose();
            }
        }
        finally
        {
            context.Dispose();
            device.Dispose();
        }
    }
}

using System.Reflection;

namespace GpuKeepAlive.Core;

/// <summary>
/// 提供运行时版本号（唯一来源为 Directory.Build.props 的 Version 属性，
/// 经程序集 AssemblyInformationalVersion 透传，CLI / GUI 共用）。
/// </summary>
public static class AppVersion
{
    /// <summary>版本号（x.y.z 形式；去除 SemVer 2.0 附加的 "+build metadata" 部分）。</summary>
    public static string Current { get; } = GetVersion();

    private static string GetVersion()
    {
        string? informational = Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        return string.IsNullOrEmpty(informational) ? "0.0.0" : informational.Split('+')[0];
    }
}

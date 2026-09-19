using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GpuKeepAlive.Gui.Services;

/// <summary>
/// GUI 配置持久化：exe 同级目录下的 GpuKeepAlive.config.json（便携式，不写用户目录）。
/// 写入失败（如目录只读）时返回 false，由调用方提示，不中断运行。
/// </summary>
public sealed class JsonSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Path { get; }

    public JsonSettingsStore(string path) => Path = path;

    /// <summary>加载配置；文件不存在或内容损坏时返回 null（视同初次运行）。</summary>
    public GuiSettings? Load()
    {
        try
        {
            if (!File.Exists(Path))
                return null;
            return JsonSerializer.Deserialize<GuiSettings>(File.ReadAllText(Path), SerializerOptions);
        }
        catch
        {
            return null;
        }
    }

    public bool TrySave(GuiSettings settings)
    {
        try
        {
            File.WriteAllText(Path, JsonSerializer.Serialize(settings, SerializerOptions));
            return true;
        }
        catch
        {
            return false;
        }
    }
}

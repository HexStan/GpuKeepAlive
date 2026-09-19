using System.Globalization;
using System.Windows.Data;

namespace GpuKeepAlive.Gui.Converters;

/// <summary>
/// 枚举/整数值与 RadioButton IsChecked 的双向匹配转换：
/// ConverterParameter 使用字符串（枚举名或数字），选中时把参数解析回源属性类型。
/// </summary>
public sealed class EnumMatchConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null
           && string.Equals(value.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter is not null)
        {
            string text = parameter.ToString()!;
            try
            {
                if (targetType.IsEnum)
                    return Enum.Parse(targetType, text, ignoreCase: true);
                if (targetType == typeof(int))
                    return int.Parse(text, CultureInfo.InvariantCulture);
            }
            catch
            {
                // 解析失败时回退为 DoNothing，不改动源属性。
            }
        }
        return Binding.DoNothing;
    }
}

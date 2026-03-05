using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Collections.ObjectModel;
using ModbusMonitor.ViewModels;

namespace ModbusMonitor.Converters
{
    /// <summary>
    /// 布尔值取反转换器
    /// </summary>
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : value;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : value;
    }

    /// <summary>
    /// 布尔值 → Visibility 转换器
    /// </summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is Visibility v && v == Visibility.Visible;
    }

    /// <summary>
    /// 在线状态 → 颜色转换器（绿色/灰色）
    /// </summary>
    public class OnlineStatusToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isOnline = value is bool b && b;
            return isOnline
                ? new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76))  // 亮绿色
                : new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)); // 灰色
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// 报警状态 → 颜色转换器
    /// 0=绿色（正常），1=黄色（报警），2=红色（超高）
    /// </summary>
    public class AlarmStatusToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int status = value is int i ? i : 0;
            return status switch
            {
                0 => new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76)), // 绿色 —— 正常
                1 => new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07)), // 黄色 —— 温度报警
                2 => new SolidColorBrush(Color.FromRgb(0xFF, 0x35, 0x35)), // 红色 —— 温度超高
                _ => new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55))  // 灰色 —— 未知
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// 故障状态 → 颜色转换器
    /// 0=绿色（正常），1=橙色（温度故障），2=红色（电池电量低）
    /// </summary>
    public class FaultStatusToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int status = value is int i ? i : 0;
            return status switch
            {
                0 => new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76)), // 绿色 —— 正常
                1 => new SolidColorBrush(Color.FromRgb(0xFF, 0x87, 0x00)), // 橙色 —— 温度故障
                2 => new SolidColorBrush(Color.FromRgb(0xFF, 0x35, 0x35)), // 红色 —— 电池电量低
                _ => new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55))  // 灰色 —— 未知
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// 连接状态文字颜色转换器
    /// </summary>
    public class ConnectionStatusColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool connected = value is bool b && b;
            return connected
                ? new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76))
                : new SolidColorBrush(Color.FromRgb(0xFF, 0x55, 0x55));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// 温度值 → 颜色转换器（严格按设备自身的预警/报警阈值显示颜色）
    /// values[0]=Temperature, values[1]=WarningTemperature, values[2]=AlarmTemperature
    /// </summary>
    public class TemperatureToColorConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            double temp    = values.Length > 0 && values[0] is double t ? t : 0;
            double warning = values.Length > 1 && values[1] is int w    ? w : 999;
            double alarm   = values.Length > 2 && values[2] is int a    ? a : 999;

            // 阈值均为 0：设备未连接 / 数据未初始化，显示灰色
            if (warning == 0 && alarm == 0)
                return new SolidColorBrush(Color.FromRgb(0x66, 0x77, 0x88));

            if (temp >= alarm && alarm > 0)
                return new SolidColorBrush(Color.FromRgb(0xFF, 0x35, 0x35));  // 红色 —— 达到报警温度
            if (temp >= warning && warning > 0)
                return new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07));  // 黄色 —— 达到预警温度
            return new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));      // 绿色 —— 正常范围
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// 将所有通道的 Devices 列表平铺为单一 IEnumerable
    /// 供右侧 WrapPanel 统一渲染全部设备面板
    /// </summary>
    public class FlattenDevicesConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values is not { Length: 1 }
                || values[0] is not ObservableCollection<ChannelViewModel> channels)
                return Enumerable.Empty<DeviceViewModel>();

            return channels.SelectMany(ch => ch.Devices).ToList();
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}

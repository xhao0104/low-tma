using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

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
    /// 温度值 → 颜色转换器（根据温度高低显示不同颜色）
    /// </summary>
    public class TemperatureToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double temp = value is double d ? d : 0;
            if (temp < 40)
                return new SolidColorBrush(Color.FromRgb(0x00, 0xA8, 0xFF)); // 蓝色 —— 正常偏低
            else if (temp < 55)
                return new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76)); // 绿色 —— 正常
            else if (temp < 65)
                return new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07)); // 黄色 —— 偏高
            else
                return new SolidColorBrush(Color.FromRgb(0xFF, 0x35, 0x35)); // 红色 —— 过高
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}

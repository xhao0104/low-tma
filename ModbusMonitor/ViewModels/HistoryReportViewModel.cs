using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Win32;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using ModbusMonitor.Services;

namespace ModbusMonitor.ViewModels
{
    /// <summary>
    /// 历史趋势报告窗口的 ViewModel
    /// </summary>
    public class HistoryReportViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // ===== 设备下拉选择 =====
        public ObservableCollection<string> DeviceKeys { get; } = new();

        private string _selectedDeviceKey = "";
        public string SelectedDeviceKey
        {
            get => _selectedDeviceKey;
            set
            {
                _selectedDeviceKey = value;
                OnPropertyChanged();
                LoadData();
            }
        }

        // ===== 图表模型 =====
        private PlotModel _plotModel = new();
        public PlotModel PlotModel
        {
            get => _plotModel;
            private set { _plotModel = value; OnPropertyChanged(); }
        }

        // ===== 汇总统计 =====
        private string _summaryText = "--";
        public string SummaryText
        {
            get => _summaryText;
            private set { _summaryText = value; OnPropertyChanged(); }
        }

        private int _recordCount;
        public int RecordCount
        {
            get => _recordCount;
            private set { _recordCount = value; OnPropertyChanged(); }
        }

        // ===== 命令 =====
        public ICommand RefreshCommand        { get; }
        public ICommand ExportDeviceCommand   { get; }
        public ICommand ExportAllCommand      { get; }
        public ICommand ClearDeviceDataCommand { get; }

        public HistoryReportViewModel()
        {
            RefreshCommand         = new RelayCommand(RefreshDeviceList);
            ExportDeviceCommand    = new RelayCommand(ExportDevice, () => !string.IsNullOrEmpty(_selectedDeviceKey));
            ExportAllCommand       = new RelayCommand(ExportAll);
            ClearDeviceDataCommand = new RelayCommand(ClearDeviceData, () => !string.IsNullOrEmpty(_selectedDeviceKey));

            RefreshDeviceList();
        }

        // ===== 刷新设备列表 =====
        private void RefreshDeviceList()
        {
            DeviceKeys.Clear();
            DeviceKeys.Add("全部设备");
            foreach (var k in DataLoggerService.GetAllDeviceKeys())
                DeviceKeys.Add(k);

            if (DeviceKeys.Count > 1)
                SelectedDeviceKey = DeviceKeys[1];
            else
                SelectedDeviceKey = "";
        }

        // ===== 加载历史数据并绘图 =====
        private void LoadData()
        {
            var pm = new PlotModel { Background = OxyColor.FromRgb(0x0D, 0x11, 0x17) };
            pm.TextColor   = OxyColor.FromRgb(0x99, 0xAA, 0xBB);
            pm.PlotAreaBorderColor = OxyColor.FromRgb(0x2A, 0x3A, 0x6A);

            var timeAxis = new DateTimeAxis
            {
                Position  = AxisPosition.Bottom,
                StringFormat = "MM-dd HH:mm",
                MajorGridlineStyle = LineStyle.Dot,
                MajorGridlineColor = OxyColor.FromRgb(0x1F, 0x2E, 0x4A),
                TextColor = OxyColor.FromRgb(0x80, 0x9A, 0xBB)
            };
            var tempAxis = new LinearAxis
            {
                Position  = AxisPosition.Left,
                Title     = "温度 (°C)",
                MajorGridlineStyle = LineStyle.Dot,
                MajorGridlineColor = OxyColor.FromRgb(0x1F, 0x2E, 0x4A),
                TextColor = OxyColor.FromRgb(0x80, 0x9A, 0xBB)
            };
            pm.Axes.Add(timeAxis);
            pm.Axes.Add(tempAxis);

            // 设备颜色表
            var colors = new[]
            {
                OxyColor.FromRgb(0x00, 0xC8, 0xFF),
                OxyColor.FromRgb(0x00, 0xE6, 0x76),
                OxyColor.FromRgb(0xFF, 0xC1, 0x07),
                OxyColor.FromRgb(0xFF, 0x55, 0x55),
                OxyColor.FromRgb(0xBB, 0x86, 0xFC),
            };
            int colorIdx = 0;

            var keys = _selectedDeviceKey == "全部设备"
                ? DataLoggerService.GetAllDeviceKeys()
                : new List<string> { _selectedDeviceKey };

            int totalCount = 0;
            double maxTemp = double.MinValue;
            double minTemp = double.MaxValue;
            double sumTemp = 0;

            foreach (var key in keys)
            {
                var records = DataLoggerService.ReadRecords(key);
                if (records.Count == 0) continue;

                var series = new LineSeries
                {
                    Title           = key,
                    Color           = colors[colorIdx++ % colors.Length],
                    StrokeThickness = 2,
                    MarkerType      = MarkerType.Circle,
                    MarkerSize      = 3,
                    TrackerFormatString = "{0}\n{2:yyyy/MM/dd HH:mm}\n温度: {4:F1}°C"
                };

                foreach (var r in records)
                {
                    series.Points.Add(DateTimeAxis.CreateDataPoint(r.Timestamp, r.Temperature));
                    maxTemp  = Math.Max(maxTemp, r.Temperature);
                    minTemp  = Math.Min(minTemp, r.Temperature);
                    sumTemp += r.Temperature;
                    totalCount++;
                }

                pm.Series.Add(series);
            }

            if (totalCount > 0)
            {
                double avgTemp = sumTemp / totalCount;
                SummaryText = $"共 {totalCount} 条记录  |  最高 {maxTemp:F1}°C  |  最低 {minTemp:F1}°C  |  平均 {avgTemp:F1}°C";
            }
            else
            {
                SummaryText = "暂无数据";
            }

            RecordCount = totalCount;
            PlotModel = pm;
        }

        // ===== 导出当前设备 CSV =====
        private void ExportDevice()
        {
            var dlg = new SaveFileDialog
            {
                Title      = $"导出 {_selectedDeviceKey} 历史数据",
                FileName   = $"{_selectedDeviceKey}_{DateTime.Now:yyyyMMdd}.csv",
                Filter     = "CSV 文件|*.csv"
            };
            if (dlg.ShowDialog() == true)
                DataLoggerService.ExportDevice(_selectedDeviceKey, dlg.FileName);
        }

        // ===== 一键导出全部设备 =====
        private void ExportAll()
        {
            var dlg = new SaveFileDialog
            {
                Title    = "导出全部设备历史数据",
                FileName = $"全部设备_{DateTime.Now:yyyyMMdd}.csv",
                Filter   = "CSV 文件|*.csv"
            };
            if (dlg.ShowDialog() == true)
                DataLoggerService.ExportAll(dlg.FileName);
        }

        // ===== 清空当前设备数据 =====
        private void ClearDeviceData()
        {
            if (string.IsNullOrEmpty(_selectedDeviceKey)) return;
            var safeKey = string.Concat(_selectedDeviceKey.Split(Path.GetInvalidFileNameChars()));
            var files = Directory.Exists(DataLoggerService.DataDir)
                ? Directory.GetFiles(DataLoggerService.DataDir, $"{safeKey}_*.csv")
                : Array.Empty<string>();
            foreach (var f in files) File.Delete(f);
            LoadData();
        }
    }
}

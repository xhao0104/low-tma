using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using ModbusMonitor.Models;
using ModbusMonitor.Services;
using ModbusMonitor.Views;

namespace ModbusMonitor.ViewModels
{
    /// <summary>
    /// 主界面 ViewModel — 从配置文件加载动态通道列表，管理日志
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private readonly ModbusService _modbusService;
        private AppConfig _appConfig;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // ===== 动态通道列表（从配置文件驱动）=====
        public ObservableCollection<ChannelViewModel> Channels { get; } = new();

        // ===== 扁平化的所有设备列表（供 UI 完全自适应绑定）=====
        public ObservableCollection<DeviceViewModel> AllDevices { get; } = new();

        // ===== 日志 =====
        private ObservableCollection<string> _logEntries = new();
        public ObservableCollection<string> LogEntries
        {
            get => _logEntries;
            set { _logEntries = value; OnPropertyChanged(); }
        }

        // ===== 顶部状态栏：任意通道已连接则为 true =====
        public bool AnyConnected => Channels.Any(c => c.Connected);

        // ===== 视图模式 =====
        private bool _isAdvancedMode = false;
        public bool IsAdvancedMode
        {
            get => _isAdvancedMode;
            set { _isAdvancedMode = value; OnPropertyChanged(); }
        }

        // ===== 命令 =====
        public RelayCommand ConnectAllCommand          { get; }
        public RelayCommand DisconnectAllCommand        { get; }
        public RelayCommand AddNewChannelCommand        { get; }
        public RelayCommand ToggleAdvancedModeCommand   { get; }
        public RelayCommand OpenReportCommand           { get; }

        public MainViewModel()
        {
            _modbusService = new ModbusService();

            // ===== 加载配置文件 =====
            _appConfig = ConfigService.Load();

            // ===== 根据配置构建通道 ViewModel =====
            for (int i = 0; i < _appConfig.Channels.Count; i++)
            {
                var ch = new ChannelViewModel(
                    index:          i,
                    config:         _appConfig.Channels[i],
                    modbusService:  _modbusService,
                    onSaveConfig:   SaveConfig,
                    onLog:          AddLog,
                    onRemove:       RemoveChannel);

                // 监听通道连接状态变化，刷新顶部状态栏
                ch.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(ChannelViewModel.Connected))
                        OnPropertyChanged(nameof(AnyConnected));
                };

                BindChannelDevices(ch);
                Channels.Add(ch);
            }

            // ===== 订阅服务日志 =====
            _modbusService.LogMessage    += msg => AddLog(msg);
            _modbusService.ErrorOccurred += msg => AddLog($"[错误] {msg}");

            // ===== 全局命令 =====
            ConnectAllCommand         = new RelayCommand(async () => await ConnectAllAsync());
            DisconnectAllCommand      = new RelayCommand(() => _modbusService.DisconnectAll());
            AddNewChannelCommand      = new RelayCommand(AddNewChannel);
            ToggleAdvancedModeCommand = new RelayCommand(() => IsAdvancedMode = !IsAdvancedMode);
            OpenReportCommand         = new RelayCommand(() =>
            {
                var win = new HistoryReportWindow();
                win.Owner = Application.Current?.MainWindow;
                win.Show();
            });

            // ===== 启动时自动连接所有通道 =====
            _ = Task.Run(async () =>
            {
                await Task.Delay(500); // 等待 UI 渲染完成后再连接
                await ConnectAllAsync();
            });
        }

        // ===== 一键连接所有通道 =====
        private async Task ConnectAllAsync()
        {
            var tasks = Channels.Select(ch => Task.Run(async () =>
            {
                // 触发各自的 ToggleCommand（若已连接则跳过）
                if (!ch.Connected)
                    await ch.ToggleCommand.ExecuteAsync();
            }));
            await Task.WhenAll(tasks);
        }

        // ===== 监听通道设备变更以维护 AllDevices =====
        private void BindChannelDevices(ChannelViewModel ch)
        {
            foreach (var d in ch.Devices) AllDevices.Add(d);
            
            ch.Devices.CollectionChanged += (s, e) =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (e.NewItems != null)
                        foreach (DeviceViewModel d in e.NewItems) AllDevices.Add(d);
                    if (e.OldItems != null)
                        foreach (DeviceViewModel d in e.OldItems) AllDevices.Remove(d);
                });
            };
        }

        // ===== 动态增删通道 =====
        private void AddNewChannel()
        {
            // 获取当前最大 index，分配新的 ID
            int newIndex = Channels.Count > 0 ? Channels.Max(c => c.ChannelIndex) + 1 : 0;
            
            var newConfig = new ChannelConfig
            {
                Name   = $"COM{newIndex + 1}",
                Ip     = "192.168.1.80",
                Port   = 10000 + newIndex + 1,
                Slaves = new List<int> { 1 }
            };

            var ch = new ChannelViewModel(
                index:          newIndex,
                config:         newConfig,
                modbusService:  _modbusService,
                onSaveConfig:   SaveConfig,
                onLog:          AddLog,
                onRemove:       RemoveChannel);

            ch.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ChannelViewModel.Connected))
                    OnPropertyChanged(nameof(AnyConnected));
            };

            BindChannelDevices(ch);
            Channels.Add(ch);
            SaveConfig();
            AddLog($"➕ 已添加新设备通道: {newConfig.Name}");
        }

        private void RemoveChannel(ChannelViewModel ch)
        {
            if (ch.Connected)
            {
                _modbusService.DisconnectChannel(ch.ChannelIndex);
            }
            foreach (var d in ch.Devices) AllDevices.Remove(d);
            Channels.Remove(ch);
            SaveConfig();
            AddLog($"➖ 已移除设备通道: {ch.Name}");
        }

        // ===== 保存当前所有通道配置到文件 =====
        private void SaveConfig()
        {
            _appConfig.Channels = Channels.Select(c => c.ToChannelConfig()).ToList();
            ConfigService.Save(_appConfig);
            AddLog("✓ 配置已保存到 settings.json");
        }

        // ===== 日志 =====
        private void AddLog(string message)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                LogEntries.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}");
                while (LogEntries.Count > 200)
                    LogEntries.RemoveAt(LogEntries.Count - 1);
            });
        }

        /// <summary>释放资源（窗口关闭时调用）</summary>
        public void Cleanup() => _modbusService.Dispose();
    }
}

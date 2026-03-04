using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using ModbusMonitor.Models;
using ModbusMonitor.Services;

namespace ModbusMonitor.ViewModels
{
    /// <summary>
    /// 单个通道（一路 TCP 连接）的 ViewModel
    /// 包含连接配置、连接状态、该通道下所有设备的 ViewModel
    /// </summary>
    public class ChannelViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private readonly ModbusService _modbusService;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value; OnPropertyChanged(name); return true;
        }

        // ===== 通道序号（用于标识通道，从 0 开始）=====
        public int ChannelIndex { get; }

        // ===== 连接配置（绑定到 UI，可实时修改后保存）=====
        private string _name;
        public string Name { get => _name; set => SetField(ref _name, value); }

        private string _ip;
        public string Ip { get => _ip; set => SetField(ref _ip, value); }

        private string _portText;
        public string PortText { get => _portText; set => SetField(ref _portText, value); }

        /// <summary>从站地址列表文本（逗号分隔，如 "1,2,3"）供 UI 输入</summary>
        private string _slavesText;
        public string SlavesText { get => _slavesText; set => SetField(ref _slavesText, value); }

        // ===== 连接状态 =====
        private bool _connected;
        public bool Connected
        {
            get => _connected;
            set
            {
                if (SetField(ref _connected, value))
                {
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(ButtonText));
                }
            }
        }

        public string StatusText => _connected ? "● 已连接" : "○ 未连接";
        public string ButtonText => _connected ? $"断开 {_name}" : $"连接 {_name}";

        // ===== 该通道下的设备列表 =====
        public ObservableCollection<DeviceViewModel> Devices { get; } = new();

        // ===== 命令 =====
        public RelayCommand ToggleCommand { get; }
        public RelayCommand SaveCommand   { get; }
        public RelayCommand RemoveCommand { get; }

        // ===== 外部注入的保存回调 =====
        private readonly Action _onSaveConfig;
        private readonly Action<string> _onLog;

        public ChannelViewModel(
            int index,
            ChannelConfig config,
            ModbusService modbusService,
            Action onSaveConfig,
            Action<string> onLog,
            Action<ChannelViewModel> onRemove)
        {
            ChannelIndex   = index;
            _modbusService = modbusService;
            _onSaveConfig  = onSaveConfig;
            _onLog         = onLog;

            // 从配置初始化 UI 绑定值
            _name       = config.Name;
            _ip         = config.Ip;
            _portText   = config.Port.ToString();
            _slavesText = string.Join(",", config.Slaves);

            // 根据配置构建设备 ViewModel
            RefreshDeviceList(config.Slaves);

            // 订阅服务事件
            _modbusService.ChannelConnectionChanged += OnChannelConnectionChanged;
            _modbusService.DataReceived             += OnDataReceived;

            ToggleCommand = new RelayCommand(async () => await ToggleAsync());
            SaveCommand   = new RelayCommand(SaveConfig);
            RemoveCommand = new RelayCommand(() => onRemove(this));
        }

        // ===== 连接 / 断开 =====

        private async Task ToggleAsync()
        {
            if (_connected)
            {
                _modbusService.DisconnectChannel(ChannelIndex);
            }
            else
            {
                if (!int.TryParse(_portText, out int port))
                {
                    _onLog($"[CH{ChannelIndex}] 端口号格式无效");
                    return;
                }

                var slaves = ParseSlaves(_slavesText);
                if (slaves.Count == 0)
                {
                    _onLog($"[CH{ChannelIndex}] 从站地址列表为空");
                    return;
                }

                // 连接前先同步设备列表
                RefreshDeviceList(slaves);
                await _modbusService.ConnectChannelAsync(ChannelIndex, _ip, port, slaves);
            }
        }

        // ===== 配置保存 =====

        private void SaveConfig()
        {
            _onSaveConfig();
        }

        /// <summary>将当前 UI 配置导出为 ChannelConfig（供 ConfigService 保存）</summary>
        public ChannelConfig ToChannelConfig()
        {
            int.TryParse(_portText, out int port);
            return new ChannelConfig
            {
                Name   = _name,
                Ip     = _ip,
                Port   = port,
                Slaves = ParseSlaves(_slavesText)
            };
        }

        // ===== 辅助方法 =====

        /// <summary>解析逗号分隔的从站地址文本，返回有效整数列表</summary>
        private static List<int> ParseSlaves(string text)
        {
            var result = new List<int>();
            if (string.IsNullOrWhiteSpace(text)) return result;
            foreach (var s in text.Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(s.Trim(), out int v)) result.Add(v);
            return result;
        }

        /// <summary>根据从站地址列表同步 Devices 集合（保留已有、新增缺失、移除多余）</summary>
        private void RefreshDeviceList(List<int> slaves)
        {
            // 移除不在新列表中的设备
            for (int i = Devices.Count - 1; i >= 0; i--)
                if (!slaves.Contains(Devices[i].SlaveAddress))
                    Devices.RemoveAt(i);

            // 添加新从站对应的设备
            var existing = Devices.Select(d => d.SlaveAddress).ToHashSet();
            foreach (int s in slaves)
                if (!existing.Contains(s))
                    Devices.Add(new DeviceViewModel(_modbusService, ChannelIndex, s, $"设备 #{s} ({_name})"));
        }

        // ===== 事件处理 =====

        private void OnChannelConnectionChanged(int channel, bool connected)
        {
            if (channel != ChannelIndex) return;
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Connected = connected;
                if (!connected)
                    foreach (var d in Devices) d.Data.IsOnline = false;
            });
        }

        private void OnDataReceived(int slaveAddr, DeviceData data)
        {
            var device = Devices.FirstOrDefault(d => d.SlaveAddress == slaveAddr);
            device?.UpdateData(data);
        }
    }
}

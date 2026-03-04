using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using ModbusMonitor.Models;
using ModbusMonitor.Services;

namespace ModbusMonitor.ViewModels
{
    /// <summary>
    /// 主界面 ViewModel —— 管理双路连接配置和所有设备
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private readonly ModbusService _modbusService;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        // ===== 通道 1 连接配置（COM1 / 设备 #1）=====
        private string _ip1 = "192.168.1.80";
        public string Ip1 { get => _ip1; set => SetField(ref _ip1, value); }

        private string _port1 = "10001";
        public string Port1 { get => _port1; set => SetField(ref _port1, value); }

        private string _slaveAddr1 = "2";  // COM1 接的设备从站地址=2
        public string SlaveAddr1 { get => _slaveAddr1; set => SetField(ref _slaveAddr1, value); }

        // ===== 通道 2 连接配置（COM2 / 设备 #2）=====
        private string _ip2 = "192.168.1.80";
        public string Ip2 { get => _ip2; set => SetField(ref _ip2, value); }

        private string _port2 = "10002";
        public string Port2 { get => _port2; set => SetField(ref _port2, value); }

        private string _slaveAddr2 = "1";  // COM2 接的设备从站地址=1
        public string SlaveAddr2 { get => _slaveAddr2; set => SetField(ref _slaveAddr2, value); }

        // ===== 通道连接状态 =====
        private bool _ch1Connected;
        public bool Ch1Connected
        {
            get => _ch1Connected;
            set
            {
                if (SetField(ref _ch1Connected, value))
                {
                    OnPropertyChanged(nameof(Ch1StatusText));
                    OnPropertyChanged(nameof(Ch1ButtonText));
                    OnPropertyChanged(nameof(AnyConnected));
                }
            }
        }

        private bool _ch2Connected;
        public bool Ch2Connected
        {
            get => _ch2Connected;
            set
            {
                if (SetField(ref _ch2Connected, value))
                {
                    OnPropertyChanged(nameof(Ch2StatusText));
                    OnPropertyChanged(nameof(Ch2ButtonText));
                    OnPropertyChanged(nameof(AnyConnected));
                }
            }
        }

        // 兼容顶部状态栏
        public bool AnyConnected => _ch1Connected || _ch2Connected;

        public string Ch1StatusText => _ch1Connected ? "● 已连接" : "○ 未连接";
        public string Ch2StatusText => _ch2Connected ? "● 已连接" : "○ 未连接";
        public string Ch1ButtonText => _ch1Connected ? "断开 CH1" : "连接 CH1";
        public string Ch2ButtonText => _ch2Connected ? "断开 CH2" : "连接 CH2";

        // ===== 设备 ViewModel =====
        public DeviceViewModel Device1 { get; }
        public DeviceViewModel Device2 { get; }

        // ===== 日志 =====
        private ObservableCollection<string> _logEntries = new();
        public ObservableCollection<string> LogEntries
        {
            get => _logEntries;
            set => SetField(ref _logEntries, value);
        }

        // ===== 命令 =====
        public RelayCommand ConnectCh1Command { get; }
        public RelayCommand ConnectCh2Command { get; }
        public RelayCommand ConnectAllCommand { get; }

        public MainViewModel()
        {
            _modbusService = new ModbusService();

            Device1 = new DeviceViewModel(_modbusService, 1, 1, "设备 #1 (COM1)");
            Device2 = new DeviceViewModel(_modbusService, 2, 2, "设备 #2 (COM2)");

            ConnectCh1Command  = new RelayCommand(async () => await ToggleChannel1Async());
            ConnectCh2Command  = new RelayCommand(async () => await ToggleChannel2Async());
            ConnectAllCommand  = new RelayCommand(async () => await ConnectAllAsync());

            // 订阅服务事件
            _modbusService.ChannelConnectionChanged += OnChannelConnectionChanged;
            _modbusService.DataReceived             += OnDataReceived;
            _modbusService.LogMessage               += msg => AddLog(msg);
            _modbusService.ErrorOccurred            += msg => AddLog($"[错误] {msg}");
        }

        // ===== 连接逻辑 =====

        /// <summary>切换通道 1 连接/断开</summary>
        private async Task ToggleChannel1Async()
        {
            if (Ch1Connected)
            {
                _modbusService.DisconnectChannel1();
            }
            else
            {
                if (!ValidateInputs(Port1, SlaveAddr1, 1, out int port, out int addr)) return;
                Device1.SlaveAddress = addr;
                Device1.DeviceName   = $"设备 #{addr} (COM1)";
                await _modbusService.ConnectChannel1Async(Ip1, port, addr);
            }
        }

        /// <summary>切换通道 2 连接/断开</summary>
        private async Task ToggleChannel2Async()
        {
            if (Ch2Connected)
            {
                _modbusService.DisconnectChannel2();
            }
            else
            {
                if (!ValidateInputs(Port2, SlaveAddr2, 2, out int port, out int addr)) return;
                Device2.SlaveAddress = addr;
                Device2.DeviceName   = $"设备 #{addr} (COM2)";
                await _modbusService.ConnectChannel2Async(Ip2, port, addr);
            }
        }

        /// <summary>一键连接两个通道</summary>
        private async Task ConnectAllAsync()
        {
            await Task.WhenAll(
                ToggleChannel1Async(),
                ToggleChannel2Async()
            );
        }

        /// <summary>验证端口号和从站地址输入</summary>
        private bool ValidateInputs(string portStr, string addrStr, int channel,
                                    out int port, out int addr)
        {
            port = 0; addr = 0;
            if (!int.TryParse(portStr, out port))
            {
                AddLog($"[错误] 通道 {channel} 端口号格式无效");
                return false;
            }
            if (!int.TryParse(addrStr, out addr))
            {
                AddLog($"[错误] 通道 {channel} 从站地址格式无效");
                return false;
            }
            return true;
        }

        // ===== 事件处理 =====

        private void OnChannelConnectionChanged(int channel, bool connected)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (channel == 1)
                {
                    Ch1Connected = connected;
                    if (!connected) Device1.Data.IsOnline = false;
                }
                else
                {
                    Ch2Connected = connected;
                    if (!connected) Device2.Data.IsOnline = false;
                }
            });
        }

        private void OnDataReceived(int slaveAddress, DeviceData data)
        {
            if (int.TryParse(SlaveAddr1, out int addr1) && slaveAddress == addr1)
                Device1.UpdateData(data);
            else if (int.TryParse(SlaveAddr2, out int addr2) && slaveAddress == addr2)
                Device2.UpdateData(data);
        }

        private void AddLog(string message)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                LogEntries.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}");
                while (LogEntries.Count > 200)
                    LogEntries.RemoveAt(LogEntries.Count - 1);
            });
        }

        /// <summary>释放资源</summary>
        public void Cleanup() => _modbusService.Dispose();
    }
}

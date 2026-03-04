using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using ModbusMonitor.Models;
using ModbusMonitor.Services;

namespace ModbusMonitor.ViewModels
{
    /// <summary>
    /// 单台设备的 ViewModel —— 管理一台设备的数据显示和参数写入
    /// </summary>
    public class DeviceViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private readonly ModbusService _modbusService;

        // 该设备对应的通道编号（1 或 2）
        private readonly int _channel;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        // ===== 设备标识 =====
        private string _deviceName = "设备";
        public string DeviceName
        {
            get => _deviceName;
            set => SetField(ref _deviceName, value);
        }

        private int _slaveAddress = 1;
        public int SlaveAddress
        {
            get => _slaveAddress;
            set => SetField(ref _slaveAddress, value);
        }

        // ===== 设备数据模型 =====
        private DeviceData _data = new();
        public DeviceData Data
        {
            get => _data;
            set => SetField(ref _data, value);
        }

        // ===== 写入参数 —— 用户输入值 =====
        private string _inputWarningTemp = "";
        public string InputWarningTemp
        {
            get => _inputWarningTemp;
            set => SetField(ref _inputWarningTemp, value);
        }

        private string _inputAlarmTemp = "";
        public string InputAlarmTemp
        {
            get => _inputAlarmTemp;
            set => SetField(ref _inputAlarmTemp, value);
        }

        private string _inputLightLevel = "";
        public string InputLightLevel
        {
            get => _inputLightLevel;
            set => SetField(ref _inputLightLevel, value);
        }

        // ===== 写入命令 =====
        public RelayCommand WriteWarningTempCommand { get; }
        public RelayCommand WriteAlarmTempCommand   { get; }
        public RelayCommand WriteLightLevelCommand  { get; }
        public RelayCommand WriteAllParamsCommand   { get; }

        // ===== 轮询统计 =====
        private int _pollSuccessCount;
        /// <summary>累计成功帧数</summary>
        public int PollSuccessCount { get => _pollSuccessCount; private set => SetField(ref _pollSuccessCount, value); }

        private int _pollTimeoutCount;
        /// <summary>累计超时次数（设备未唤醒）</summary>
        public int PollTimeoutCount { get => _pollTimeoutCount; private set => SetField(ref _pollTimeoutCount, value); }

        private int _pollOtherErrorCount;
        /// <summary>其他通信异常次数</summary>
        public int PollOtherErrorCount { get => _pollOtherErrorCount; private set => SetField(ref _pollOtherErrorCount, value); }

        private DateTime? _lastSuccessTime;
        /// <summary>最近一次成功读取时间</summary>
        public DateTime? LastSuccessTime
        {
            get => _lastSuccessTime;
            private set
            {
                if (SetField(ref _lastSuccessTime, value))
                    OnPropertyChanged(nameof(LastSuccessTimeText));
            }
        }

        /// <summary>最近成功时间文本，未有则显示 --</summary>
        public string LastSuccessTimeText
            => _lastSuccessTime.HasValue ? _lastSuccessTime.Value.ToString("HH:mm:ss") : "--";

        /// <summary>合并摘要：成功 N / 超时 N / 异常 N</summary>
        public string PollSummaryText
            => $"成功 {_pollSuccessCount} / 超时 {_pollTimeoutCount} / 异常 {_pollOtherErrorCount}";

        /// <summary>最近 5 秒内是否有成功帧（用于指示灯颜色）</summary>
        public bool HasRecentSuccess
            => _lastSuccessTime.HasValue && (DateTime.Now - _lastSuccessTime.Value).TotalSeconds <= 5;

        // ===== 重置统计命令 =====
        public RelayCommand ResetStatsCommand { get; }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="modbusService">Modbus 通信服务</param>
        /// <param name="channel">通道编号（1 或 2）</param>
        /// <param name="slaveAddress">从站地址</param>
        /// <param name="name">设备显示名称</param>
        public DeviceViewModel(ModbusService modbusService, int channel, int slaveAddress, string name)
        {
            _modbusService = modbusService;
            _channel       = channel;
            _slaveAddress  = slaveAddress;
            _deviceName    = name;

            WriteWarningTempCommand = new RelayCommand(async () => await WriteWarningTempAsync());
            WriteAlarmTempCommand   = new RelayCommand(async () => await WriteAlarmTempAsync());
            WriteLightLevelCommand  = new RelayCommand(async () => await WriteLightLevelAsync());
            WriteAllParamsCommand   = new RelayCommand(async () => await WriteAllParamsAsync());
            ResetStatsCommand       = new RelayCommand(ResetPollStats);

            // 订阅轮询结果事件，按通道+从站地址过滤
            _modbusService.PollResult += OnPollResult;
        }

        /// <summary>
        /// 接收 ModbusService 的每次轮询结果，更新统计数据
        /// </summary>
        private void OnPollResult(int channel, int slaveAddr, bool success, bool isTimeout)
        {
            // 只处理属于本设备通道 + 从站地址的结果
            if (channel != _channel || slaveAddr != _slaveAddress) return;

            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (success)
                {
                    PollSuccessCount++;
                    LastSuccessTime = DateTime.Now;
                    OnPropertyChanged(nameof(HasRecentSuccess));
                }
                else if (isTimeout)
                {
                    PollTimeoutCount++;
                }
                else
                {
                    PollOtherErrorCount++;
                }
                // 每次都刷新摘要文本
                OnPropertyChanged(nameof(PollSummaryText));
                OnPropertyChanged(nameof(HasRecentSuccess));
            });
        }

        /// <summary>重置轮询统计计数</summary>
        public void ResetPollStats()
        {
            PollSuccessCount    = 0;
            PollTimeoutCount    = 0;
            PollOtherErrorCount = 0;
            LastSuccessTime     = null;
            OnPropertyChanged(nameof(PollSummaryText));
            OnPropertyChanged(nameof(HasRecentSuccess));
            OnPropertyChanged(nameof(LastSuccessTimeText));
        }

        /// <summary>
        /// 更新设备数据（由轮询回调在非 UI 线程调用）
        /// </summary>
        public void UpdateData(DeviceData newData)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Data.BaudRate           = newData.BaudRate;
                Data.SlaveAddress       = newData.SlaveAddress;
                Data.DeviceNumber       = newData.DeviceNumber;
                Data.McuId              = newData.McuId;
                Data.RunTimeSeconds     = newData.RunTimeSeconds;
                Data.Temperature        = newData.Temperature;
                Data.WarningTemperature = newData.WarningTemperature;
                Data.AlarmTemperature   = newData.AlarmTemperature;
                Data.LightSensorLevel   = newData.LightSensorLevel;
                Data.AlarmStatus        = newData.AlarmStatus;
                Data.FaultStatus        = newData.FaultStatus;
                Data.BatteryVoltage     = newData.BatteryVoltage;
                Data.IsOnline           = newData.IsOnline;
                Data.LastUpdate         = newData.LastUpdate;
            });
        }

        // ===== 写入操作（均通过对应通道发送）=====

        /// <summary>写入预警温度 —— 寄存器 40008（地址 0x0007）</summary>
        private async Task WriteWarningTempAsync()
        {
            if (ushort.TryParse(InputWarningTemp, out ushort value))
                await _modbusService.WriteSingleRegisterAsync(_channel, (byte)SlaveAddress, 0x0007, value);
        }

        /// <summary>写入报警温度 —— 寄存器 40009（地址 0x0008）</summary>
        private async Task WriteAlarmTempAsync()
        {
            if (ushort.TryParse(InputAlarmTemp, out ushort value))
                await _modbusService.WriteSingleRegisterAsync(_channel, (byte)SlaveAddress, 0x0008, value);
        }

        /// <summary>写入光感档位 —— 寄存器 40010（地址 0x0009），有效值 1~3</summary>
        private async Task WriteLightLevelAsync()
        {
            if (ushort.TryParse(InputLightLevel, out ushort value) && value >= 1 && value <= 3)
                await _modbusService.WriteSingleRegisterAsync(_channel, (byte)SlaveAddress, 0x0009, value);
        }

        /// <summary>一键写入所有参数</summary>
        private async Task WriteAllParamsAsync()
        {
            await WriteWarningTempAsync();
            await Task.Delay(100);
            await WriteAlarmTempAsync();
            await Task.Delay(100);
            await WriteLightLevelAsync();
        }
    }
}

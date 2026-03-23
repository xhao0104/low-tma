using System.ComponentModel;
using System.Media;
using System.Runtime.CompilerServices;
using System.Timers;
using SysTimer = System.Timers.Timer;
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

        /// <summary>用户自定义别名（显示在简易模式卡片标题，为空时回退显示 DeviceName）</summary>
        private string _alias = "";
        public string Alias
        {
            get => _alias;
            set
            {
                SetField(ref _alias, value);
                OnPropertyChanged(nameof(DisplayName));
            }
        }

        /// <summary>简易模式下的显示名称：有别名显示别名，没有显示原始名称</summary>
        public string DisplayName => string.IsNullOrWhiteSpace(_alias) ? _deviceName : _alias;

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

        private string _inputSlaveAddress = "";
        public string InputSlaveAddress
        {
            get => _inputSlaveAddress;
            set => SetField(ref _inputSlaveAddress, value);
        }

        // ===== 写入命令 =====
        public RelayCommand WriteWarningTempCommand { get; }
        public RelayCommand WriteAlarmTempCommand   { get; }
        public RelayCommand WriteLightLevelCommand  { get; }
        public RelayCommand WriteSlaveAddressCommand{ get; }
        public RelayCommand WriteAllParamsCommand   { get; }
        /// <summary>保存别名到配置文件</summary>
        public RelayCommand SaveAliasCommand        { get; }

        /// <summary>别名变更时由外部（ChannelViewModel）传入的持久化回调</summary>
        public Action<int, string>? OnAliasSaved { get; set; }

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

        // ===== 定时采样 =====
        private readonly SysTimer _sampleTimer;

        /// <summary>采样间隔（秒），默认 300 秒（5分钟）</summary>
        private int _sampleIntervalSeconds = 300;
        public int SampleIntervalSeconds
        {
            get => _sampleIntervalSeconds;
            set
            {
                if (SetField(ref _sampleIntervalSeconds, value) && value > 0)
                {
                    _sampleTimer.Interval = value * 1000.0;
                }
            }
        }

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
            WriteSlaveAddressCommand= new RelayCommand(async () => await WriteSlaveAddressAsync());
            WriteAllParamsCommand   = new RelayCommand(async () => await WriteAllParamsAsync());
            ResetStatsCommand       = new RelayCommand(ResetPollStats);
            SaveAliasCommand        = new RelayCommand(() =>
            {
                // 通知外部（ChannelViewModel）将别名写入配置文件
                OnAliasSaved?.Invoke(_slaveAddress, _alias ?? "");
            });

            // 订阅轮询结果事件，按通道+从站地址过滤
            _modbusService.PollResult += OnPollResult;

            // 初始化定时采样计时器（默认 5 分钟，到期自动记录一次温度）
            _sampleTimer = new SysTimer(_sampleIntervalSeconds * 1000.0);
            _sampleTimer.Elapsed  += OnSampleTimer;
            _sampleTimer.AutoReset = true;
            _sampleTimer.Start();
        }

        /// <summary>
        /// 接收 ModbusService 的每次轮询结果，更新统计数据
        /// </summary>
        private void OnPollResult(int channel, int slaveAddr, bool success, bool isTimeout)
        {
            // 只处理属于本设备通道 + 从站地址的结果
            if (channel != _channel || slaveAddr != _slaveAddress) return;

            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
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
            }));
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

        /// <summary>定时器触发：写入一条温度快照（只有设备在线才记录）</summary>
        private void OnSampleTimer(object? sender, ElapsedEventArgs e)
        {
            if (!Data.IsOnline) return;

            DataLoggerService.LogSample(
                deviceKey:   _deviceName,
                slaveAddress: _slaveAddress,
                temperature:  Data.Temperature,
                alarmStatus:  Data.AlarmStatusText,
                faultStatus:  Data.FaultStatusText);
        }

        /// <summary>
        /// 更新设备数据（由轮询回调在非 UI 线程调用）
        /// </summary>
        public void UpdateData(DeviceData newData)
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
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
                var prevAlarm = Data.AlarmStatus;
                Data.AlarmStatus        = newData.AlarmStatus;
                Data.FaultStatus        = newData.FaultStatus;
                Data.BatteryVoltage     = newData.BatteryVoltage;
                Data.IsOnline           = newData.IsOnline;
                Data.LastUpdate         = newData.LastUpdate;

                // 报警状态发生变化时播放系统音效
                if (newData.AlarmStatus != prevAlarm && newData.AlarmStatus > 0)
                {
                    if (newData.AlarmStatus == 2)
                        SystemSounds.Hand.Play();        // 报警超高——警告音
                    else
                        SystemSounds.Exclamation.Play(); // 预警——提示音
                }
            }));
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

        /// <summary>从站地址修改成功触发，参数为 (旧地址, 新地址)</summary>
        public Action<int, int>? OnSlaveAddressChanged { get; set; }

        /// <summary>写入从站地址 —— 寄存器 40002（地址 0x0001），有效值 1~247</summary>
        private async Task WriteSlaveAddressAsync()
        {
            if (ushort.TryParse(InputSlaveAddress, out ushort value) && value >= 1 && value <= 247 && value != SlaveAddress)
            {
                int oldAddress = SlaveAddress;
                bool success = await _modbusService.WriteSingleRegisterAsync(_channel, (byte)oldAddress, 0x0001, value);
                if (success)
                {
                    Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var result = MessageBox.Show($"修改成功！设备的从站地址已从 {oldAddress} 变更为 {value}。\n\n是否允许上位机自动更新本通道的配置并重新连接？\n\n（如果选择“否”，则您需要在接下来的通信前手动对左侧列表的配置项做出修改。）", 
                            "地址修改成功", MessageBoxButton.YesNo, MessageBoxImage.Question);
                        
                        if (result == MessageBoxResult.Yes)
                        {
                            OnSlaveAddressChanged?.Invoke(oldAddress, value);
                        }
                    }));
                }
            }
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

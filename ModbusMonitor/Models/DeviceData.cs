using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ModbusMonitor.Models
{
    /// <summary>
    /// 设备数据模型 —— 映射 Modbus 寄存器 40001~40013
    /// </summary>
    public class DeviceData : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        // 40001 - 波特率
        private int _baudRate = 9600;
        public int BaudRate
        {
            get => _baudRate;
            set => SetField(ref _baudRate, value);
        }

        // 40002 - 从站地址
        private int _slaveAddress = 1;
        public int SlaveAddress
        {
            get => _slaveAddress;
            set => SetField(ref _slaveAddress, value);
        }

        // 40003 - 编号（固定）
        private int _deviceNumber = 1;
        public int DeviceNumber
        {
            get => _deviceNumber;
            set => SetField(ref _deviceNumber, value);
        }

        // 40004-40005 - 单片机 ID（两个寄存器组合）
        private uint _mcuId;
        public uint McuId
        {
            get => _mcuId;
            set => SetField(ref _mcuId, value);
        }

        // 40006 - 运行计时（秒）
        private int _runTimeSeconds;
        public int RunTimeSeconds
        {
            get => _runTimeSeconds;
            set
            {
                if (SetField(ref _runTimeSeconds, value))
                {
                    OnPropertyChanged(nameof(RunTimeFormatted));
                }
            }
        }

        /// <summary>
        /// 格式化运行时间显示
        /// </summary>
        public string RunTimeFormatted
        {
            get
            {
                var ts = System.TimeSpan.FromSeconds(_runTimeSeconds);
                if (ts.Days > 0)
                    return $"{ts.Days}天 {ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
                return $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            }
        }

        // 40007 - 实际温度
        private double _temperature;
        public double Temperature
        {
            get => _temperature;
            set => SetField(ref _temperature, value);
        }

        // 40008 - 预警温度（可写）
        private int _warningTemperature;
        public int WarningTemperature
        {
            get => _warningTemperature;
            set => SetField(ref _warningTemperature, value);
        }

        // 40009 - 报警温度（可写）
        private int _alarmTemperature;
        public int AlarmTemperature
        {
            get => _alarmTemperature;
            set => SetField(ref _alarmTemperature, value);
        }

        // 40010 - 光感档位（1/2/3）（可写）
        private int _lightSensorLevel = 1;
        public int LightSensorLevel
        {
            get => _lightSensorLevel;
            set => SetField(ref _lightSensorLevel, value);
        }

        // 40011 - 报警指示：0正常，1温度报警，2温度超高
        private int _alarmStatus;
        public int AlarmStatus
        {
            get => _alarmStatus;
            set
            {
                if (SetField(ref _alarmStatus, value))
                {
                    OnPropertyChanged(nameof(AlarmStatusText));
                }
            }
        }

        public string AlarmStatusText => _alarmStatus switch
        {
            0 => "正常",
            1 => "温度报警",
            2 => "温度超高",
            _ => "未知"
        };

        // 40012 - 故障指示：0正常，1温度故障，2电池电量低
        private int _faultStatus;
        public int FaultStatus
        {
            get => _faultStatus;
            set
            {
                if (SetField(ref _faultStatus, value))
                {
                    OnPropertyChanged(nameof(FaultStatusText));
                }
            }
        }

        public string FaultStatusText => _faultStatus switch
        {
            0 => "正常",
            1 => "温度故障",
            2 => "电池电量低",
            _ => "未知"
        };

        // 40013 - 电池电压
        private double _batteryVoltage;
        public double BatteryVoltage
        {
            get => _batteryVoltage;
            set => SetField(ref _batteryVoltage, value);
        }

        // 连接状态标识
        private bool _isOnline;
        public bool IsOnline
        {
            get => _isOnline;
            set => SetField(ref _isOnline, value);
        }

        // 最后更新时间
        private DateTime _lastUpdate;
        public DateTime LastUpdate
        {
            get => _lastUpdate;
            set => SetField(ref _lastUpdate, value);
        }
    }
}

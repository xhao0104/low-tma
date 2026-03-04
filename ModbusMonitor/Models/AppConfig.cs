namespace ModbusMonitor.Models
{
    /// <summary>
    /// 单个通道的配置（一路 TCP 对应一个串口服务器端口）
    /// </summary>
    public class ChannelConfig
    {
        /// <summary>通道显示名称，如 "COM1"</summary>
        public string Name { get; set; } = "通道";

        /// <summary>TCP 服务器 IP 地址</summary>
        public string Ip { get; set; } = "192.168.1.80";

        /// <summary>TCP 端口号</summary>
        public int Port { get; set; } = 10001;

        /// <summary>该通道下挂载的 Modbus 从站地址列表（支持多台设备）</summary>
        public List<int> Slaves { get; set; } = new();
    }

    /// <summary>
    /// 应用全局配置
    /// </summary>
    public class AppConfig
    {
        /// <summary>所有通道配置</summary>
        public List<ChannelConfig> Channels { get; set; } = new();
    }
}

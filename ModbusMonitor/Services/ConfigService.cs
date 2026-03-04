using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModbusMonitor.Models;

namespace ModbusMonitor.Services
{
    /// <summary>
    /// 配置文件读写服务，使用 settings.json（存放于 exe 同目录）
    /// </summary>
    public static class ConfigService
    {
        private static readonly string ConfigPath =
            Path.Combine(AppContext.BaseDirectory, "settings.json");

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented            = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition   = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// 读取配置文件；文件不存在则写入默认配置并返回
        /// </summary>
        public static AppConfig Load()
        {
            if (File.Exists(ConfigPath))
            {
                try
                {
                    string json = File.ReadAllText(ConfigPath);
                    var cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOpts);
                    if (cfg?.Channels?.Count > 0) return cfg;
                }
                catch { /* 解析失败则使用默认值 */ }
            }

            // 没有配置文件或解析失败：生成默认配置并写入
            var defaultCfg = BuildDefaultConfig();
            Save(defaultCfg);
            return defaultCfg;
        }

        /// <summary>
        /// 将配置序列化写回 settings.json
        /// </summary>
        public static void Save(AppConfig config)
        {
            try
            {
                string json = JsonSerializer.Serialize(config, JsonOpts);
                File.WriteAllText(ConfigPath, json);
            }
            catch { /* 写入失败静默处理，不影响主程序 */ }
        }

        // ===== 默认配置（与原来硬编码一致）=====
        private static AppConfig BuildDefaultConfig() => new()
        {
            Channels = new List<ChannelConfig>
            {
                new() { Name = "COM1", Ip = "192.168.1.80", Port = 10001, Slaves = new List<int> { 2 } },
                new() { Name = "COM2", Ip = "192.168.1.80", Port = 10002, Slaves = new List<int> { 1 } }
            }
        };
    }
}

using System.IO;
using System.Globalization;

namespace ModbusMonitor.Services
{
    /// <summary>
    /// 温度历史数据定时记录服务（写入 CSV，无数据库依赖）
    /// 文件路径：{exe目录}/data/{设备文件名key}_{年份}.csv
    /// </summary>
    public static class DataLoggerService
    {
        // 数据存储目录（与 exe 同目录下的 data 子目录）
        public static readonly string DataDir =
            Path.Combine(AppContext.BaseDirectory, "data");

        /// <summary>
        /// 写入一条温度快照记录
        /// </summary>
        public static void LogSample(
            string deviceKey,
            int slaveAddress,
            double temperature,
            string alarmStatus,
            string faultStatus)
        {
            try
            {
                Directory.CreateDirectory(DataDir);

                // 每个设备按年份一个文件
                var safeKey = string.Concat(deviceKey.Split(Path.GetInvalidFileNameChars()));
                var filePath = Path.Combine(DataDir, $"{safeKey}_{DateTime.Now.Year}.csv");

                bool isNew = !File.Exists(filePath);
                using var writer = File.AppendText(filePath);

                // 新文件写表头
                if (isNew)
                    writer.WriteLine("时间戳,从站地址,实际温度(°C),报警状态,故障状态");

                writer.WriteLine(
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}," +
                    $"{slaveAddress}," +
                    $"{temperature.ToString("F1", CultureInfo.InvariantCulture)}," +
                    $"{alarmStatus}," +
                    $"{faultStatus}");
            }
            catch
            {
                // 静默失败，不影响主流程
            }
        }

        /// <summary>
        /// 读取指定设备所有历史记录（可选筛选年份，传 0 = 全部年份）
        /// </summary>
        public static List<HistoryRecord> ReadRecords(string deviceKey, int year = 0)
        {
            var records = new List<HistoryRecord>();

            try
            {
                var safeKey = string.Concat(deviceKey.Split(Path.GetInvalidFileNameChars()));
                var pattern = year > 0
                    ? $"{safeKey}_{year}.csv"
                    : $"{safeKey}_*.csv";

                var files = Directory.Exists(DataDir)
                    ? Directory.GetFiles(DataDir, pattern)
                    : Array.Empty<string>();

                foreach (var file in files.OrderBy(f => f))
                {
                    var lines = File.ReadAllLines(file);
                    foreach (var line in lines.Skip(1)) // 跳过表头
                    {
                        var cols = line.Split(',');
                        if (cols.Length < 5) continue;
                        if (!DateTime.TryParse(cols[0], out var ts)) continue;
                        if (!double.TryParse(cols[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var temp)) continue;

                        records.Add(new HistoryRecord
                        {
                            Timestamp   = ts,
                            SlaveAddr   = cols[1],
                            Temperature = temp,
                            AlarmStatus = cols[3],
                            FaultStatus = cols[4]
                        });
                    }
                }
            }
            catch { }

            return records;
        }

        /// <summary>
        /// 获取所有已存在数据的设备键名列表
        /// </summary>
        public static List<string> GetAllDeviceKeys()
        {
            if (!Directory.Exists(DataDir)) return new();

            return Directory.GetFiles(DataDir, "*.csv")
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .Select(n => n.Length >= 5 ? n[..^5] : n)   // 去掉 "_2026" 后缀
                .Distinct()
                .OrderBy(x => x)
                .ToList();
        }

        /// <summary>
        /// 导出指定设备的所有 CSV 文件到目标路径（合并为一个文件）
        /// </summary>
        public static void ExportDevice(string deviceKey, string destPath)
        {
            var records = ReadRecords(deviceKey);
            WriteExportFile(destPath, records);
        }

        /// <summary>
        /// 一键导出所有设备（合并为一个文件，增加设备名列）
        /// </summary>
        public static void ExportAll(string destPath)
        {
            if (!Directory.Exists(DataDir)) return;

            using var writer = File.CreateText(destPath);
            writer.WriteLine("时间戳,设备,从站地址,实际温度(°C),报警状态,故障状态");

            foreach (var key in GetAllDeviceKeys())
            {
                var records = ReadRecords(key);
                foreach (var r in records)
                    writer.WriteLine(
                        $"{r.Timestamp:yyyy-MM-dd HH:mm:ss},{key},{r.SlaveAddr}," +
                        $"{r.Temperature.ToString("F1", CultureInfo.InvariantCulture)}," +
                        $"{r.AlarmStatus},{r.FaultStatus}");
            }
        }

        private static void WriteExportFile(string destPath, List<HistoryRecord> records)
        {
            using var writer = File.CreateText(destPath);
            writer.WriteLine("时间戳,从站地址,实际温度(°C),报警状态,故障状态");
            foreach (var r in records)
                writer.WriteLine(
                    $"{r.Timestamp:yyyy-MM-dd HH:mm:ss},{r.SlaveAddr}," +
                    $"{r.Temperature.ToString("F1", CultureInfo.InvariantCulture)}," +
                    $"{r.AlarmStatus},{r.FaultStatus}");
        }
    }

    /// <summary>
    /// 一条历史采样记录
    /// </summary>
    public class HistoryRecord
    {
        public DateTime Timestamp   { get; init; }
        public string   SlaveAddr   { get; init; } = "";
        public double   Temperature { get; init; }
        public string   AlarmStatus { get; init; } = "";
        public string   FaultStatus { get; init; } = "";
    }
}

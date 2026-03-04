namespace ModbusMonitor.Services
{
    /// <summary>
    /// Modbus CRC16 校验工具类
    /// </summary>
    public static class CrcHelper
    {
        /// <summary>
        /// 计算 Modbus CRC16 校验值
        /// </summary>
        /// <param name="data">待计算的数据字节数组</param>
        /// <param name="offset">起始偏移量</param>
        /// <param name="length">计算长度</param>
        /// <returns>CRC16 校验值（低字节在前）</returns>
        public static ushort CalculateCrc16(byte[] data, int offset, int length)
        {
            ushort crc = 0xFFFF;

            for (int i = offset; i < offset + length; i++)
            {
                crc ^= data[i];
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 0x0001) != 0)
                    {
                        crc >>= 1;
                        crc ^= 0xA001;
                    }
                    else
                    {
                        crc >>= 1;
                    }
                }
            }

            return crc;
        }

        /// <summary>
        /// 将 CRC16 校验值追加到字节数组末尾
        /// </summary>
        public static byte[] AppendCrc(byte[] data)
        {
            ushort crc = CalculateCrc16(data, 0, data.Length);
            byte[] result = new byte[data.Length + 2];
            Array.Copy(data, result, data.Length);
            // 低字节在前，高字节在后
            result[data.Length] = (byte)(crc & 0xFF);
            result[data.Length + 1] = (byte)(crc >> 8);
            return result;
        }

        /// <summary>
        /// 验证带 CRC 的数据帧是否正确
        /// </summary>
        public static bool VerifyCrc(byte[] data, int length)
        {
            if (length < 4) return false;
            ushort calculated = CalculateCrc16(data, 0, length - 2);
            ushort received = (ushort)(data[length - 2] | (data[length - 1] << 8));
            return calculated == received;
        }
    }
}

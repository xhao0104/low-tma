using System.Net.Sockets;
using ModbusMonitor.Models;

namespace ModbusMonitor.Services
{
    /// <summary>
    /// Modbus RTU over TCP 通信服务（双路独立连接版）
    /// 每台设备对应一个独立的 TCP 连接，互不干扰
    /// </summary>
    public class ModbusService : IDisposable
    {
        // ===== 通信参数常量 =====
        private const int TIMEOUT_MS = 2000;        // 通信超时（毫秒）
        private const int POLL_INTERVAL_MS = 1000;  // 轮询间隔（毫秒）

        // ===== 通道 1（设备 #1 / COM1）=====
        private TcpClient? _client1;
        private NetworkStream? _stream1;
        private readonly object _lock1 = new();
        private bool _connected1;
        private CancellationTokenSource? _cts1;
        // 记录通道1连接参数，用于自动重连
        private string _ip1 = "";
        private int _port1;
        private int _slave1;

        // ===== 通道 2（设备 #2 / COM2）=====
        private TcpClient? _client2;
        private NetworkStream? _stream2;
        private readonly object _lock2 = new();
        private bool _connected2;
        private CancellationTokenSource? _cts2;
        // 记录通道2连接参数，用于自动重连
        private string _ip2 = "";
        private int _port2;
        private int _slave2;

        // ===== 对外事件 =====

        /// <summary>连接状态变更事件（通道编号 1/2，是否已连接）</summary>
        public event Action<int, bool>? ChannelConnectionChanged;

        /// <summary>数据更新事件（从站地址，设备数据）</summary>
        public event Action<int, DeviceData>? DataReceived;

        /// <summary>日志消息事件</summary>
        public event Action<string>? LogMessage;

        /// <summary>错误事件</summary>
        public event Action<string>? ErrorOccurred;

        /// <summary>
        /// 每次轮询结果上报事件
        /// 参数：通道编号, 从站地址, 是否成功, 是否超时（true=设备未唤醒超时，false=其他异常）
        /// </summary>
        public event Action<int, int, bool, bool>? PollResult;

        // ===== 状态属性 =====
        public bool IsChannel1Connected => _connected1;
        public bool IsChannel2Connected => _connected2;

        // ===== 连接 / 断开 =====

        /// <summary>
        /// 连接通道 1（对应 COM1 / 设备 #1）
        /// </summary>
        public async Task<bool> ConnectChannel1Async(string ip, int port, int slaveAddr)
        {
            return await ConnectChannelAsync(1, ip, port, slaveAddr);
        }

        /// <summary>
        /// 连接通道 2（对应 COM2 / 设备 #2）
        /// </summary>
        public async Task<bool> ConnectChannel2Async(string ip, int port, int slaveAddr)
        {
            return await ConnectChannelAsync(2, ip, port, slaveAddr);
        }

        /// <summary>
        /// 断开通道 1
        /// </summary>
        public void DisconnectChannel1()
        {
            DisconnectChannel(1);
        }

        /// <summary>
        /// 断开通道 2
        /// </summary>
        public void DisconnectChannel2()
        {
            DisconnectChannel(2);
        }

        /// <summary>
        /// 断开所有通道
        /// </summary>
        public void DisconnectAll()
        {
            DisconnectChannel(1);
            DisconnectChannel(2);
        }

        // ===== 读写操作（指定通道）=====

        /// <summary>
        /// 读取保持寄存器（功能码 0x03）— Modbus TCP 格式
        /// </summary>
        public async Task<ushort[]?> ReadRegistersAsync(int channel, byte slaveAddr, ushort startAddr, ushort count)
        {
            // 构建 Modbus TCP 请求帧（MBAP头6字节 + PDU6字节，共12字节，无CRC）
            ushort transId = (ushort)(Environment.TickCount & 0xFFFF); // 事务ID
            byte[] frame = new byte[12];
            frame[0] = (byte)(transId >> 8);    // 事务标识符高
            frame[1] = (byte)(transId & 0xFF);  // 事务标识符低
            frame[2] = 0x00;                    // 协议标识符（Modbus = 0x0000）
            frame[3] = 0x00;
            frame[4] = 0x00;                    // 后续字节数（Unit ID + FC + 数据 = 6）
            frame[5] = 0x06;
            frame[6] = slaveAddr;               // Unit ID（从站地址）
            frame[7] = 0x03;                    // 功能码：读保持寄存器
            frame[8]  = (byte)(startAddr >> 8);
            frame[9]  = (byte)(startAddr & 0xFF);
            frame[10] = (byte)(count >> 8);
            frame[11] = (byte)(count & 0xFF);

            // TCP响应长度：MBAP头(6) + Unit ID(1) + FC(1) + 字节数(1) + 数据(count×2)
            int expectedLen = 9 + count * 2;

            byte[]? response = await Task.Run(() => SendAndReceive(channel, frame, expectedLen));
            if (response == null) return null;

            // 验证响应（MBAP头占前6字节，PDU从偏移6开始）
            if (response[6] != slaveAddr || response[7] != 0x03)
            {
                LogMessage?.Invoke($"[CH{channel}] 设备 #{slaveAddr} 响应功能码异常");
                return null;
            }

            int byteCount = response[8];
            if (byteCount != count * 2)
            {
                LogMessage?.Invoke($"[CH{channel}] 设备 #{slaveAddr} 数据长度异常: 期望 {count * 2}，实际 {byteCount}");
                return null;
            }

            // 解析寄存器值（从偏移9开始，大端字节序）
            ushort[] values = new ushort[count];
            for (int i = 0; i < count; i++)
                values[i] = (ushort)((response[9 + i * 2] << 8) | response[9 + i * 2 + 1]);

            return values;
        }

        /// <summary>
        /// 写入单个寄存器（功能码 0x06）— Modbus TCP 格式
        /// </summary>
        public async Task<bool> WriteSingleRegisterAsync(int channel, byte slaveAddr, ushort regAddr, ushort value)
        {
            ushort transId = (ushort)(Environment.TickCount & 0xFFFF);
            byte[] frame = new byte[12];
            frame[0] = (byte)(transId >> 8);
            frame[1] = (byte)(transId & 0xFF);
            frame[2] = 0x00;
            frame[3] = 0x00;
            frame[4] = 0x00;
            frame[5] = 0x06;        // 后续6字节
            frame[6] = slaveAddr;   // Unit ID
            frame[7] = 0x06;        // 功能码：写单寄存器
            frame[8]  = (byte)(regAddr >> 8);
            frame[9]  = (byte)(regAddr & 0xFF);
            frame[10] = (byte)(value >> 8);
            frame[11] = (byte)(value & 0xFF);

            // 写响应：MBAP头(6) + Unit ID(1) + FC(1) + 寄存器地址(2) + 值(2) = 12字节
            byte[]? response = await Task.Run(() => SendAndReceive(channel, frame, 12));

            if (response == null)
            {
                LogMessage?.Invoke($"[CH{channel}] 设备 #{slaveAddr} 写寄存器 {regAddr} 无响应");
                return false;
            }

            if (response[6] == slaveAddr && response[7] == 0x06)
            {
                LogMessage?.Invoke($"[CH{channel}] ✓ 设备 #{slaveAddr} 写寄存器 0x{regAddr:X4} = {value}");
                return true;
            }

            LogMessage?.Invoke($"[CH{channel}] ✗ 设备 #{slaveAddr} 写寄存器失败");
            return false;
        }

        // ===== 私有辅助方法 =====

        /// <summary>
        /// 连接指定通道，并启动该通道的轮询任务
        /// </summary>
        private async Task<bool> ConnectChannelAsync(int channel, string ip, int port, int slaveAddr)
        {
            // 先断开旧连接
            DisconnectChannel(channel);

            var client = new TcpClient();
            client.ReceiveTimeout = TIMEOUT_MS;
            client.SendTimeout = TIMEOUT_MS;

            try
            {
                LogMessage?.Invoke($"[CH{channel}] 正在连接 {ip}:{port}...");
                await client.ConnectAsync(ip, port);
                var stream = client.GetStream();

                if (channel == 1)
                {
                    _client1 = client;
                    _stream1 = stream;
                    _connected1 = true;
                }
                else
                {
                    _client2 = client;
                    _stream2 = stream;
                    _connected2 = true;
                }

                LogMessage?.Invoke($"[CH{channel}] ✓ 连接成功 {ip}:{port}");
                ChannelConnectionChanged?.Invoke(channel, true);

                // 保存连接参数（供自动重连使用）
                if (channel == 1) { _ip1 = ip; _port1 = port; _slave1 = slaveAddr; }
                else              { _ip2 = ip; _port2 = port; _slave2 = slaveAddr; }

                // 启动独立轮询任务
                StartChannelPolling(channel, slaveAddr);
                return true;
            }
            catch (Exception ex)
            {
                client.Close();
                LogMessage?.Invoke($"[CH{channel}] ✗ 连接失败: {ex.Message}");
                ErrorOccurred?.Invoke($"通道 {channel} 连接失败: {ex.Message}");
                ChannelConnectionChanged?.Invoke(channel, false);
                return false;
            }
        }

        /// <summary>
        /// 断开指定通道并停止其轮询
        /// </summary>
        private void DisconnectChannel(int channel)
        {
            if (channel == 1)
            {
                try { _cts1?.Cancel(); _cts1?.Dispose(); } catch { }
                _cts1 = null;
                try { _stream1?.Close(); _client1?.Close(); } catch { }
                _stream1 = null;
                _client1 = null;
                if (_connected1)
                {
                    _connected1 = false;
                    LogMessage?.Invoke("[CH1] 已断开连接");
                    ChannelConnectionChanged?.Invoke(1, false);
                }
            }
            else
            {
                try { _cts2?.Cancel(); _cts2?.Dispose(); } catch { }
                _cts2 = null;
                try { _stream2?.Close(); _client2?.Close(); } catch { }
                _stream2 = null;
                _client2 = null;
                if (_connected2)
                {
                    _connected2 = false;
                    LogMessage?.Invoke("[CH2] 已断开连接");
                    ChannelConnectionChanged?.Invoke(2, false);
                }
            }
        }

        /// <summary>
        /// 启动指定通道的定时轮询任务（含断线自动重连）
        /// </summary>
        private void StartChannelPolling(int channel, int slaveAddr)
        {
            var cts = new CancellationTokenSource();
            if (channel == 1) _cts1 = cts;
            else _cts2 = cts;

            var token = cts.Token;

            Task.Run(async () =>
            {
                const int RECONNECT_DELAY_MS = 3000; // 断线后 3 秒重连

                while (!token.IsCancellationRequested)
                {
                    bool isConn = channel == 1 ? _connected1 : _connected2;

                    if (!isConn)
                    {
                        // 断线了，等待后自动重连
                        LogMessage?.Invoke($"[CH{channel}] 断线，{RECONNECT_DELAY_MS / 1000} 秒后自动重连...");
                        try { await Task.Delay(RECONNECT_DELAY_MS, token); } catch { break; }

                        if (token.IsCancellationRequested) break;

                        // 取出保存的连接参数
                        string ip   = channel == 1 ? _ip1 : _ip2;
                        int    port = channel == 1 ? _port1 : _port2;
                        if (string.IsNullOrEmpty(ip)) break; // 没有参数，不重连

                        LogMessage?.Invoke($"[CH{channel}] 正在重连 {ip}:{port}...");
                        try
                        {
                            var newClient = new TcpClient();
                            newClient.ReceiveTimeout = TIMEOUT_MS;
                            newClient.SendTimeout    = TIMEOUT_MS;
                            await newClient.ConnectAsync(ip, port);
                            var newStream = newClient.GetStream();

                            if (channel == 1)
                            {
                                try { _stream1?.Close(); _client1?.Close(); } catch { }
                                _client1 = newClient; _stream1 = newStream; _connected1 = true;
                            }
                            else
                            {
                                try { _stream2?.Close(); _client2?.Close(); } catch { }
                                _client2 = newClient; _stream2 = newStream; _connected2 = true;
                            }
                            LogMessage?.Invoke($"[CH{channel}] ✓ 重连成功");
                            ChannelConnectionChanged?.Invoke(channel, true);
                        }
                        catch (Exception ex)
                        {
                            LogMessage?.Invoke($"[CH{channel}] 重连失败: {ex.Message}");
                        }
                        continue;
                    }

                    try
                    {
                        // 读取寄存器 40001~40013（起始地址 0x0000，数量 13）
                        var regs = await ReadRegistersAsync(channel, (byte)slaveAddr, 0x0000, 13);
                        if (regs != null)
                        {
                            // 读取成功：上报统计事件
                            PollResult?.Invoke(channel, slaveAddr, true, false);
                            var deviceData = ParseDeviceData(regs, slaveAddr);
                            DataReceived?.Invoke(slaveAddr, deviceData);
                        }
                        else
                        {
                            // 返回 null 说明超时（设备未唤醒）或帧校验失败
                            PollResult?.Invoke(channel, slaveAddr, false, true);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogMessage?.Invoke($"[CH{channel}] 轮询异常: {ex.Message}");
                        // 其他异常（非超时）
                        PollResult?.Invoke(channel, slaveAddr, false, false);
                    }

                    try { await Task.Delay(POLL_INTERVAL_MS, token); }
                    catch (OperationCanceledException) { break; }
                }
            }, token);
        }

        /// <summary>
        /// 发送帧并接收响应（线程安全，按通道分锁）
        /// </summary>
        private byte[]? SendAndReceive(int channel, byte[] sendData, int expectedLength)
        {
            NetworkStream? stream = channel == 1 ? _stream1 : _stream2;
            object lockObj = channel == 1 ? _lock1 : _lock2;

            if (stream == null) return null;

            lock (lockObj)
            {
                try
                {
                    // 清空残留数据
                    if (stream.DataAvailable)
                    {
                        byte[] discard = new byte[1024];
                        stream.Read(discard, 0, discard.Length);
                    }

                    // 发送请求
                    stream.Write(sendData, 0, sendData.Length);
                    stream.Flush();

                    // 接收响应（等待超时）
                    byte[] buffer = new byte[256];
                    int totalRead = 0;
                    DateTime startTime = DateTime.Now;

                    while (totalRead < expectedLength)
                    {
                        if ((DateTime.Now - startTime).TotalMilliseconds > TIMEOUT_MS)
                        {
                            // 超时属于设备未唤醒的正常现象，静默返回 null，由轮询统计展示
                            return null;
                        }

                        if (stream.DataAvailable)
                        {
                            int bytesRead = stream.Read(buffer, totalRead, buffer.Length - totalRead);
                            totalRead += bytesRead;
                        }
                        else
                        {
                            Thread.Sleep(10);
                        }
                    }

                    // Modbus TCP 不需要 CRC 校验，直接返回收到的字节
                    byte[] result = new byte[totalRead];
                    Array.Copy(buffer, result, totalRead);
                    return result;
                }
                catch (Exception ex)
                {
                    LogMessage?.Invoke($"[CH{channel}] 通信异常: {ex.Message}");
                    // 标记该通道断线
                    if (channel == 1) { _connected1 = false; ChannelConnectionChanged?.Invoke(1, false); }
                    else { _connected2 = false; ChannelConnectionChanged?.Invoke(2, false); }
                    return null;
                }
            }
        }

        /// <summary>
        /// 解析寄存器数据为 DeviceData 对象
        /// </summary>
        private DeviceData ParseDeviceData(ushort[] registers, int slaveAddress)
        {
            return new DeviceData
            {
                BaudRate           = registers[0],                            // 40001
                SlaveAddress       = registers[1],                            // 40002
                DeviceNumber       = registers[2],                            // 40003
                McuId              = ((uint)registers[3] << 16) | registers[4], // 40004-40005
                RunTimeSeconds     = registers[5],                            // 40006
                Temperature        = registers[6] / 10.0,                    // 40007 ÷10
                WarningTemperature = registers[7],                            // 40008
                AlarmTemperature   = registers[8],                            // 40009
                LightSensorLevel   = registers[9],                            // 40010
                AlarmStatus        = registers[10],                           // 40011
                FaultStatus        = registers[11],                           // 40012
                BatteryVoltage     = registers[12] / 100.0,                  // 40013 ÷100
                IsOnline           = true,
                LastUpdate         = DateTime.Now
            };
        }

        public void Dispose() => DisconnectAll();
    }
}

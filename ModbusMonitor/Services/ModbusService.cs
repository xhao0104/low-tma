using System.Net.Sockets;
using ModbusMonitor.Models;

namespace ModbusMonitor.Services
{
    /// <summary>
    /// Modbus RTU over TCP 通信服务（动态 N 通道版）
    /// 每个通道对应一路 TCP 连接，每个通道支持多个 Modbus 从站
    /// </summary>
    public class ModbusService : IDisposable
    {
        // ===== 通信参数 =====
        private const int TIMEOUT_MS      = 2000;   // 通信超时（毫秒）
        private const int POLL_INTERVAL_MS = 1000;  // 每轮轮询间隔（毫秒）

        // ===== 通道状态（动态 N 个）=====
        private class ChannelState
        {
            public TcpClient?      Client;
            public NetworkStream?  Stream;
            public readonly object Lock = new();
            public bool            Connected;
            public CancellationTokenSource? Cts;
            // 保存连接参数供自动重连使用
            public string          Ip     = "";
            public int             Port;
            public List<int>       Slaves = new();
        }

        private readonly Dictionary<int, ChannelState> _channels = new();
        private readonly object _channelsDictLock = new();

        // ===== 对外事件 =====

        /// <summary>连接状态变更（通道编号，是否已连接）</summary>
        public event Action<int, bool>? ChannelConnectionChanged;

        /// <summary>数据更新（从站地址，设备数据）</summary>
        public event Action<int, DeviceData>? DataReceived;

        /// <summary>日志消息</summary>
        public event Action<string>? LogMessage;

        /// <summary>错误消息</summary>
        public event Action<string>? ErrorOccurred;

        /// <summary>
        /// 每次轮询结果上报（通道编号, 从站地址, 是否成功, 是否超时=设备未唤醒）
        /// </summary>
        public event Action<int, int, bool, bool>? PollResult;

        // ===== 状态查询 =====

        public bool IsChannelConnected(int channel)
        {
            lock (_channelsDictLock)
                return _channels.TryGetValue(channel, out var s) && s.Connected;
        }

        // ===== 连接 / 断开 =====

        /// <summary>连接指定通道（支持多从站）</summary>
        public async Task<bool> ConnectChannelAsync(int channel, string ip, int port, List<int> slaves)
        {
            // 先断开旧连接
            DisconnectChannel(channel);

            var state = new ChannelState { Ip = ip, Port = port, Slaves = slaves };
            lock (_channelsDictLock) _channels[channel] = state;

            var client = new TcpClient();
            client.ReceiveTimeout = TIMEOUT_MS;
            client.SendTimeout    = TIMEOUT_MS;

            try
            {
                LogMessage?.Invoke($"[CH{channel}] 正在连接 {ip}:{port}...");
                await client.ConnectAsync(ip, port);

                state.Client    = client;
                state.Stream    = client.GetStream();
                state.Connected = true;

                LogMessage?.Invoke($"[CH{channel}] ✓ 连接成功 {ip}:{port}，从站: [{string.Join(",", slaves)}]");
                ChannelConnectionChanged?.Invoke(channel, true);

                // 启动轮询
                StartChannelPolling(channel, state);
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

        /// <summary>断开指定通道</summary>
        public void DisconnectChannel(int channel)
        {
            ChannelState? state;
            lock (_channelsDictLock) _channels.TryGetValue(channel, out state);
            if (state == null) return;

            try { state.Cts?.Cancel(); state.Cts?.Dispose(); } catch { }
            state.Cts = null;

            try { state.Stream?.Close(); state.Client?.Close(); } catch { }
            state.Stream    = null;
            state.Client    = null;

            if (state.Connected)
            {
                state.Connected = false;
                LogMessage?.Invoke($"[CH{channel}] 已断开连接");
                ChannelConnectionChanged?.Invoke(channel, false);
            }
        }

        /// <summary>断开所有通道</summary>
        public void DisconnectAll()
        {
            List<int> keys;
            lock (_channelsDictLock) keys = new List<int>(_channels.Keys);
            foreach (var ch in keys) DisconnectChannel(ch);
        }

        // ===== 读写操作 =====

        /// <summary>读取保持寄存器（功能码 0x03）— Modbus TCP 格式</summary>
        public async Task<ushort[]?> ReadRegistersAsync(int channel, byte slaveAddr, ushort startAddr, ushort count)
        {
            ushort transId = (ushort)(Environment.TickCount & 0xFFFF);
            byte[] frame = new byte[12];
            frame[0] = (byte)(transId >> 8);
            frame[1] = (byte)(transId & 0xFF);
            frame[2] = 0x00;
            frame[3] = 0x00;
            frame[4] = 0x00;
            frame[5] = 0x06;
            frame[6] = slaveAddr;
            frame[7] = 0x03;
            frame[8]  = (byte)(startAddr >> 8);
            frame[9]  = (byte)(startAddr & 0xFF);
            frame[10] = (byte)(count >> 8);
            frame[11] = (byte)(count & 0xFF);

            int expectedLen = 9 + count * 2;
            byte[]? response = await Task.Run(() => SendAndReceive(channel, frame, expectedLen));
            if (response == null) return null;

            if (response[6] != slaveAddr || response[7] != 0x03) return null;

            int byteCount = response[8];
            if (byteCount != count * 2) return null;

            ushort[] values = new ushort[count];
            for (int i = 0; i < count; i++)
                values[i] = (ushort)((response[9 + i * 2] << 8) | response[9 + i * 2 + 1]);

            return values;
        }

        /// <summary>写入单个寄存器（功能码 0x06）— Modbus TCP 格式</summary>
        public async Task<bool> WriteSingleRegisterAsync(int channel, byte slaveAddr, ushort regAddr, ushort value)
        {
            ushort transId = (ushort)(Environment.TickCount & 0xFFFF);
            byte[] frame = new byte[12];
            frame[0] = (byte)(transId >> 8);
            frame[1] = (byte)(transId & 0xFF);
            frame[2] = 0x00; frame[3] = 0x00;
            frame[4] = 0x00; frame[5] = 0x06;
            frame[6] = slaveAddr;
            frame[7] = 0x06;
            frame[8]  = (byte)(regAddr >> 8);
            frame[9]  = (byte)(regAddr & 0xFF);
            frame[10] = (byte)(value >> 8);
            frame[11] = (byte)(value & 0xFF);

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

        // ===== 私有辅助 =====

        /// <summary>启动指定通道的定时轮询（含断线自动重连）</summary>
        private void StartChannelPolling(int channel, ChannelState state)
        {
            var cts = new CancellationTokenSource();
            state.Cts = cts;
            var token = cts.Token;

            Task.Run(async () =>
            {
                const int RECONNECT_DELAY_MS = 3000;

                while (!token.IsCancellationRequested)
                {
                    if (!state.Connected)
                    {
                        // 断线重连
                        LogMessage?.Invoke($"[CH{channel}] 断线，{RECONNECT_DELAY_MS / 1000} 秒后自动重连...");
                        try { await Task.Delay(RECONNECT_DELAY_MS, token); } catch { break; }
                        if (token.IsCancellationRequested || string.IsNullOrEmpty(state.Ip)) break;

                        LogMessage?.Invoke($"[CH{channel}] 正在重连 {state.Ip}:{state.Port}...");
                        try
                        {
                            var newClient = new TcpClient();
                            newClient.ReceiveTimeout = TIMEOUT_MS;
                            newClient.SendTimeout    = TIMEOUT_MS;
                            await newClient.ConnectAsync(state.Ip, state.Port);

                            lock (state.Lock)
                            {
                                try { state.Stream?.Close(); state.Client?.Close(); } catch { }
                                state.Client    = newClient;
                                state.Stream    = newClient.GetStream();
                                state.Connected = true;
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

                    // ===== 对每个从站依次轮询 =====
                    foreach (int slave in state.Slaves)
                    {
                        if (token.IsCancellationRequested) break;
                        try
                        {
                            var regs = await ReadRegistersAsync(channel, (byte)slave, 0x0000, 13);
                            if (regs != null)
                            {
                                PollResult?.Invoke(channel, slave, true, false);
                                DataReceived?.Invoke(slave, ParseDeviceData(regs, slave));
                            }
                            else
                            {
                                // null = 超时（设备未唤醒）
                                PollResult?.Invoke(channel, slave, false, true);
                            }
                        }
                        catch (Exception ex)
                        {
                            LogMessage?.Invoke($"[CH{channel}] 从站 #{slave} 轮询异常: {ex.Message}");
                            PollResult?.Invoke(channel, slave, false, false);
                        }
                    }

                    try { await Task.Delay(POLL_INTERVAL_MS, token); }
                    catch (OperationCanceledException) { break; }
                }
            }, token);
        }

        /// <summary>发送帧并接收响应（线程安全，按通道分锁）</summary>
        private byte[]? SendAndReceive(int channel, byte[] sendData, int expectedLength)
        {
            ChannelState? state;
            lock (_channelsDictLock) _channels.TryGetValue(channel, out state);
            if (state?.Stream == null) return null;

            lock (state.Lock)
            {
                try
                {
                    var stream = state.Stream;
                    if (stream == null) return null;

                    // 清空残留数据
                    if (stream.DataAvailable)
                    {
                        byte[] discard = new byte[1024];
                        stream.Read(discard, 0, discard.Length);
                    }

                    stream.Write(sendData, 0, sendData.Length);
                    stream.Flush();

                    byte[] buffer = new byte[256];
                    int totalRead  = 0;
                    DateTime start = DateTime.Now;

                    while (totalRead < expectedLength)
                    {
                        if ((DateTime.Now - start).TotalMilliseconds > TIMEOUT_MS)
                        {
                            // 超时属于设备未唤醒的正常现象，静默返回 null
                            return null;
                        }
                        if (stream.DataAvailable)
                        {
                            int n = stream.Read(buffer, totalRead, buffer.Length - totalRead);
                            totalRead += n;
                        }
                        else
                        {
                            Thread.Sleep(10);
                        }
                    }

                    byte[] result = new byte[totalRead];
                    Array.Copy(buffer, result, totalRead);
                    return result;
                }
                catch (Exception ex)
                {
                    LogMessage?.Invoke($"[CH{channel}] 通信异常: {ex.Message}");
                    state.Connected = false;
                    ChannelConnectionChanged?.Invoke(channel, false);
                    return null;
                }
            }
        }

        /// <summary>解析寄存器数据为 DeviceData</summary>
        private static DeviceData ParseDeviceData(ushort[] registers, int slaveAddress)
        {
            return new DeviceData
            {
                BaudRate           = registers[0],
                SlaveAddress       = registers[1],
                DeviceNumber       = registers[2],
                McuId              = ((uint)registers[3] << 16) | registers[4],
                RunTimeSeconds     = registers[5],
                Temperature        = registers[6] / 10.0,
                WarningTemperature = registers[7],
                AlarmTemperature   = registers[8],
                LightSensorLevel   = registers[9],
                AlarmStatus        = registers[10],
                FaultStatus        = registers[11],
                BatteryVoltage     = registers[12] / 100.0,
                IsOnline           = true,
                LastUpdate         = DateTime.Now
            };
        }

        public void Dispose() => DisconnectAll();
    }
}

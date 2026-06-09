using Microsoft.Extensions.Configuration;
using MpsMes.Core.Enums;
using MpsMes.Core.Interfaces;
using System.Collections.Concurrent;

namespace MpsMes.Infrastructure.Plc;

/// <summary>
/// Melsec-Q PLC 통신 서비스 (MX Component 5.0)
/// - STA 전용 스레드에서만 COM 호출 → 스레드 충돌 방지
/// - SemaphoreSlim으로 직렬화 → 명령 큐잉/지연 방지
/// </summary>
public class PlcService : IPlcService
{
    private dynamic?  _plc;

    // STA 전용 스레드
    private Thread?   _staThread;
    private readonly BlockingCollection<Action> _queue = new();

    // 직렬 접근 보호
    private readonly SemaphoreSlim _sem = new(1, 1);

    private PlcConnectionStatus _status = PlcConnectionStatus.Disconnected;
    private CancellationTokenSource? _pollCts;
    private readonly TimeSpan _pollInterval;
    private readonly int _logicalStationNumber;

    public PlcConnectionStatus ConnectionStatus
    {
        get => _status;
        private set
        {
            if (_status == value) return;
            _status = value;
            ConnectionStatusChanged?.Invoke(value);
        }
    }

    public event Action<PlcConnectionStatus>? ConnectionStatusChanged;
    public event Action<PlcData>?             DataRefreshed;

    public PlcService(IConfiguration config)
    {
        var pollIntervalMs = int.TryParse(config["Plc:PollIntervalMs"], out var pollMs) ? pollMs : 200;
        _pollInterval = TimeSpan.FromMilliseconds(pollIntervalMs);
        _logicalStationNumber = int.TryParse(config["Plc:LogicalStationNumber"], out var stationNo)
            ? stationNo
            : 1;

        // STA 전용 스레드 시작 (COM 객체는 항상 이 스레드에서만 호출)
        _staThread = new Thread(() =>
        {
            foreach (var action in _queue.GetConsumingEnumerable())
            {
                try { action(); }
                catch { /* 개별 오류 무시 */ }
            }
        });
        _staThread.SetApartmentState(ApartmentState.STA);
        _staThread.IsBackground = true;
        _staThread.Name = "PLC-STA";
        _staThread.Start();
    }

    // STA 스레드에서 실행하고 결과 반환
    private Task<T> RunOnSta<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>();
        _queue.Add(() =>
        {
            try   { tcs.SetResult(func()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    private Task RunOnSta(Action action)
    {
        var tcs = new TaskCompletionSource<bool>();
        _queue.Add(() =>
        {
            try   { action(); tcs.SetResult(true); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    // ── 연결 ──────────────────────────────────────────────────────────
    public async Task<bool> ConnectAsync(string ipAddress, int port)
    {
        try
        {
            ConnectionStatus = PlcConnectionStatus.Connecting;

            await RunOnSta(() =>
            {
                var type = Type.GetTypeFromProgID("ActUtlType.ActUtlType");
                if (type == null)
                    throw new PlcException("MX Component COM 객체를 찾을 수 없습니다.");

                _plc = Activator.CreateInstance(type);
                _plc!.ActLogicalStationNumber = _logicalStationNumber;

                int ret = (int)_plc.Open();
                if (ret != 0)
                    throw new PlcException($"PLC Open 실패: 에러코드 0x{ret:X8}");
            });

            ConnectionStatus = PlcConnectionStatus.Connected;
            StartPolling();
            return true;
        }
        catch (Exception ex)
        {
            ConnectionStatus = PlcConnectionStatus.Error;
            throw new PlcException($"PLC 연결 실패: {ex.Message}", ex);
        }
    }

    // ── 해제 ──────────────────────────────────────────────────────────
    public async Task DisconnectAsync()
    {
        StopPolling();
        await RunOnSta(() => { try { _plc?.Close(); } catch { } });
        ConnectionStatus = PlcConnectionStatus.Disconnected;
    }

    // ── 워드 단일 읽기 ────────────────────────────────────────────────
    public Task<int> ReadWordAsync(string address)
        => RunOnSta(() => GetDeviceValue(address));

    public Task<int[]> ReadWordsAsync(string startAddress, int count)
        => RunOnSta(() =>
        {
            int baseAddr = ParseDAddress(startAddress);
            return Enumerable.Range(0, count)
                .Select(i => GetDeviceValue($"D{baseAddr + i}"))
                .ToArray();
        });

    // ── 비트 읽기 ─────────────────────────────────────────────────────
    public Task<bool> ReadBitAsync(string address)
        => RunOnSta(() => GetDeviceValue(address) != 0);

    // ── 비트 쓰기 ─────────────────────────────────────────────────────
    public Task<bool> WriteBitAsync(string address, bool value)
        => RunOnSta(() =>
        {
            try
            {
                short val = (short)(value ? 1 : 0);
                int ret = (int)_plc!.SetDevice2(address, val);
                return ret == 0;
            }
            catch { return false; }
        });

    // ── 워드 쓰기 ─────────────────────────────────────────────────────
    public Task<bool> WriteWordAsync(string address, int value)
        => RunOnSta(() =>
        {
            try
            {
                short val = (short)value;
                int ret = (int)_plc!.SetDevice2(address, val);
                return ret == 0;
            }
            catch { return false; }
        });

    // ── 일괄 읽기 (폴링) ──────────────────────────────────────────────
    public Task<PlcData> ReadAllAsync()
        => RunOnSta(() =>
        {
            try
            {
                return new PlcData
                {
                    SnapAt            = DateTime.Now,
                    D0_Mode           = GetDeviceValue("D0"),
                    D1_Signal         = GetDeviceValue("D1"),
                    D10_NonMetal      = GetDeviceValue("D10"),
                    D11_Metal         = GetDeviceValue("D11"),
                    D2000_ServoPos    = GetDeviceValue("D2000"),
                    D2005_ServoStatus = GetDeviceValue("D2005"),
                    T116_TimerVal     = GetDeviceValue("TN116"), // T116 현재값
                    X00_SupplyFwd     = GetDeviceValue("X00") != 0,
                    X01_SupplyBwd     = GetDeviceValue("X01") != 0,
                    X02_DistribFwd    = GetDeviceValue("X02") != 0,
                    X03_DistribBwd    = GetDeviceValue("X03") != 0,
                    X04_MachFwd       = GetDeviceValue("X04") != 0,
                    X05_MachBwd       = GetDeviceValue("X05") != 0,
                    X06_EjectFwd      = GetDeviceValue("X06") != 0,
                    X07_EjectBwd      = GetDeviceValue("X07") != 0,
                    X08_StopFwd       = GetDeviceValue("X08") != 0,
                    X09_StopBwd       = GetDeviceValue("X09") != 0,
                    X0A_SuctionFwd    = GetDeviceValue("X0A") != 0,
                    X0B_SuctionBwd    = GetDeviceValue("X0B") != 0,
                    X0C_StoreFwd      = GetDeviceValue("X0C") != 0,
                    X0D_StoreBwd      = GetDeviceValue("X0D") != 0,
                    X0E_Vacuum        = GetDeviceValue("X0E") != 0,
                    X0F_SupplyMag     = GetDeviceValue("X0F") != 0,
                    X11_Capacitive    = GetDeviceValue("X11") != 0,
                    X12_Inductive     = GetDeviceValue("X12") != 0,
                    X13_StopperFiber  = GetDeviceValue("X13") != 0,
                    Y20_ProcessMotor  = GetDeviceValue("Y20") != 0,
                    Y21_Conveyor      = GetDeviceValue("Y21") != 0,
                    Y22_SupplyCylFwd  = GetDeviceValue("Y22") != 0,
                    Y23_SupplyCylBwd  = GetDeviceValue("Y23") != 0,
                    Y24_DistribCylFwd = GetDeviceValue("Y24") != 0,
                    Y25_DistribCylBwd = GetDeviceValue("Y25") != 0,
                    Y26_MachCylDown   = GetDeviceValue("Y26") != 0,
                    Y27_EjectCylFwd   = GetDeviceValue("Y27") != 0,
                    Y28_StopperDown   = GetDeviceValue("Y28") != 0,
                    Y29_StopperUp     = GetDeviceValue("Y29") != 0,
                    Y2A_SuctionCylFwd = GetDeviceValue("Y2A") != 0,
                    Y2B_SuctionCylBwd = GetDeviceValue("Y2B") != 0,
                    Y2C_StoreCylFwd   = GetDeviceValue("Y2C") != 0,
                    Y2D_StoreCylBwd   = GetDeviceValue("Y2D") != 0,
                    Y2E_SuctionOn     = GetDeviceValue("Y2E") != 0,
                };
            }
            catch
            {
                return new PlcData { SnapAt = DateTime.Now };
            }
        });

    // ── GetDevice2 헬퍼 (반드시 STA 스레드에서 호출) ─────────────────
    private int GetDeviceValue(string address)
    {
        try
        {
            short val = 0;
            _plc!.GetDevice2(address, out val);
            return (int)val;
        }
        catch { return 0; }
    }

    private static int ParseDAddress(string address)
    {
        if (address.StartsWith("D", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(address[1..], out int num))
            return num;
        return 0;
    }

    // ── 폴링 루프 ─────────────────────────────────────────────────────
    private void StartPolling()
    {
        _pollCts = new CancellationTokenSource();
        var token = _pollCts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (ConnectionStatus == PlcConnectionStatus.Connected)
                    {
                        var data = await ReadAllAsync();
                        DataRefreshed?.Invoke(data);
                    }
                }
                catch
                {
                    ConnectionStatus = PlcConnectionStatus.Error;
                }
                await Task.Delay(_pollInterval, token);
            }
        }, token);
    }

    private void StopPolling() => _pollCts?.Cancel();

    public void Dispose()
    {
        StopPolling();
        _queue.CompleteAdding();
        try { _plc?.Close(); } catch { }
        _sem.Dispose();
    }
}

public class PlcException : Exception
{
    public PlcException(string message, Exception? inner = null) : base(message, inner) { }
}

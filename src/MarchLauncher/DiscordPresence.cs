using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MarchLauncher;

/// <summary>
/// Discord's local RPC IPC transport. Requires a Discord Developer Portal application ID;
/// no account credentials, bot token, network calls or extra native packages are needed.
/// </summary>
internal sealed class DiscordPresence : IDisposable
{
    private CancellationTokenSource? _source;
    private readonly long _startedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private readonly SemaphoreSlim _onlineChanged = new(0, 1);
    private string _serverOnline = "Онлайн сервера: проверка...";
    private string _gameNickname = "";

    /// <summary>
    /// Only a fresh, server-confirmed authenticated local MTA session can set
    /// the nickname. An empty value restores the ordinary launcher activity.
    /// </summary>
    public void UpdateGameSession(string? nickname)
    {
        var clean = nickname?.Trim() ?? "";
        if (clean.Length > 32) clean = clean[..32];
        if (string.Equals(Volatile.Read(ref _gameNickname), clean, StringComparison.Ordinal))
            return;

        Volatile.Write(ref _gameNickname, clean);
        if (_onlineChanged.CurrentCount == 0)
            _onlineChanged.Release();
    }

    /// <summary>Receives the same verified online counts as the MTA monitor.</summary>
    public void UpdateServerOnline(int? online, int? maximum)
    {
        var next = online.HasValue && maximum.HasValue
            ? $"Онлайн сервера: {online.Value} / {maximum.Value}"
            : "Сервер недоступен";
        if (string.Equals(Volatile.Read(ref _serverOnline), next, StringComparison.Ordinal))
            return;

        Volatile.Write(ref _serverOnline, next);
        // Wake the Discord IPC sender immediately when real online counts change.
        if (_onlineChanged.CurrentCount == 0)
            _onlineChanged.Release();
    }
    public event Action<string>? StatusChanged;

    public void Configure(string? applicationId)
    {
        Stop();
        applicationId = applicationId?.Trim();
        if (string.IsNullOrWhiteSpace(applicationId))
        {
            StatusChanged?.Invoke("Discord: укажи Application ID");
            return;
        }
        if (!Regex.IsMatch(applicationId, @"^\d{17,20}$"))
        {
            StatusChanged?.Invoke("Discord: неверный Application ID");
            return;
        }
        var source = new CancellationTokenSource();
        _source = source;
        _ = RunAsync(applicationId, source);
    }

    private async Task RunAsync(string applicationId, CancellationTokenSource source)
    {
        var ct = source.Token;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var pipe = await ConnectAsync(ct);
                    if (pipe == null)
                    {
                        StatusChanged?.Invoke("Discord: запусти приложение Discord");
                    }
                    else
                    {
                        await WriteFrameAsync(pipe, 0, new { v = 1, client_id = applicationId }, ct);
                        using (var ready = await ReadFrameAsync(pipe, ct))
                        {
                            if (!ready.RootElement.TryGetProperty("evt", out var evt) ||
                                evt.GetString() != "READY")
                                throw new InvalidDataException("Discord отклонил Application ID");
                        }
                        StatusChanged?.Invoke("Discord: подключён");
                        while (!ct.IsCancellationRequested)
                        {
                            var command = new
                            {
                                cmd = "SET_ACTIVITY",
                                args = new
                                {
                                    pid = Environment.ProcessId,
                                    activity = new
                                    {
                                        details = string.IsNullOrEmpty(Volatile.Read(ref _gameNickname))
                                            ? "В лаунчере MARCH ROLE PLAY"
                                            : "На сервере MARCH ROLE PLAY",
                                        state = string.IsNullOrEmpty(Volatile.Read(ref _gameNickname))
                                            ? Volatile.Read(ref _serverOnline)
                                            : "Ник: " + Volatile.Read(ref _gameNickname),
                                        timestamps = new { start = _startedAt }
                                    }
                                },
                                nonce = Guid.NewGuid().ToString("N")
                            };
                            await WriteFrameAsync(pipe, 1, command, ct);
                            using (var response = await ReadFrameAsync(pipe, ct))
                            {
                                if (response.RootElement.TryGetProperty("evt", out var error) &&
                                    error.ValueKind == JsonValueKind.String && error.GetString() == "ERROR")
                                    throw new InvalidDataException("Discord не принял активность");
                            }
                            StatusChanged?.Invoke("Discord: активность включена");
                            await _onlineChanged.WaitAsync(TimeSpan.FromSeconds(45), ct);
                        }
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke("Discord: " + ex.Message);
                }
                await Task.Delay(TimeSpan.FromSeconds(15), ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally { source.Dispose(); }
    }

    private static async Task<NamedPipeClientStream?> ConnectAsync(CancellationToken ct)
    {
        for (var index = 0; index < 10; index++)
        {
            var pipe = new NamedPipeClientStream(".", "discord-ipc-" + index,
                PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(400, ct);
                return pipe;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                pipe.Dispose();
                throw;
            }
            catch (IOException) { pipe.Dispose(); }
            catch (TimeoutException) { pipe.Dispose(); }
        }
        return null;
    }

    private static async Task WriteFrameAsync(Stream stream, int opcode, object body, CancellationToken ct)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(body);
        var header = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0, 4), opcode);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4, 4), json.Length);
        await stream.WriteAsync(header, ct);
        await stream.WriteAsync(json, ct);
        await stream.FlushAsync(ct);
    }

    private static async Task<JsonDocument> ReadFrameAsync(Stream stream, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));
        var header = new byte[8];
        await stream.ReadExactlyAsync(header, timeout.Token);
        var opcode = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0, 4));
        var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4, 4));
        if ((opcode != 1 && opcode != 2) || length < 2 || length > 1024 * 1024)
            throw new InvalidDataException("Некорректный ответ Discord IPC");
        var json = new byte[length];
        await stream.ReadExactlyAsync(json, timeout.Token);
        return JsonDocument.Parse(json);
    }

    public void Stop()
    {
        var previous = _source;
        _source = null;
        previous?.Cancel();
    }

    public void Dispose() => Stop();
}

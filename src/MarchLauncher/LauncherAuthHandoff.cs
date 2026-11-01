using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace MarchLauncher;

/// <summary>
/// Temporary local-only handoff to the MTA server running on the SAME Windows PC.
/// The login password is kept in RAM and given out only once. No listener is opened
/// to a LAN or public IP address. Never use this protocol for remote deployments.
/// </summary>
internal sealed class LauncherAuthHandoff : IDisposable
{
    public const int Port = 28745;

    private readonly object _gate = new();
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private Pending? _pending;

    public event Action<bool, string>? Completed;

    private sealed record Pending(
        string Id, string Mode, string Username, string Password,
        DateTime ExpiresAt, bool Delivered);

    public LauncherAuthHandoff()
    {
        _listener = new TcpListener(IPAddress.Loopback, Port);
        _listener.Start(4);
        _ = AcceptLoopAsync(_stop.Token);
    }

    public void Prepare(string mode, string username, string password)
    {
        // The server independently checks these values before logging anyone in.
        if (mode != "login" && mode != "register")
            throw new ArgumentException("Unsupported authentication mode.", nameof(mode));
        lock (_gate)
        {
            _pending = new Pending(
                Guid.NewGuid().ToString("N"), mode, username, password,
                DateTime.UtcNow.AddMinutes(5), false);
        }
    }

    public void CancelPending()
    {
        lock (_gate) _pending = null;
    }

    private object TakePending()
    {
        lock (_gate)
        {
            var item = _pending;
            if (item == null || item.ExpiresAt <= DateTime.UtcNow || item.Delivered)
                return new { ok = false };

            // Replace the in-memory secret with empty text after a single handoff.
            _pending = item with { Password = "", Delivered = true };
            return new
            {
                ok = true,
                id = item.Id,
                mode = item.Mode,
                username = item.Username,
                password = item.Password
            };
        }
    }

    private void ReportResult(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var id = root.GetProperty("id").GetString();
            var ok = root.GetProperty("ok").GetBoolean();
            var message = root.GetProperty("message").GetString() ?? "";

            lock (_gate)
            {
                if (_pending == null || !_pending.Delivered ||
                    _pending.ExpiresAt <= DateTime.UtcNow ||
                    !string.Equals(_pending.Id, id, StringComparison.Ordinal))
                    return;
                _pending = null;
            }
            Completed?.Invoke(ok, message.Length > 240 ? message[..240] : message);
        }
        catch (JsonException) { }
        catch (KeyNotFoundException) { }
        catch (InvalidOperationException) { }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(ct);
                _ = HandleClientAsync(client, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) when (ct.IsCancellationRequested) { break; }
            catch (SocketException) when (ct.IsCancellationRequested) { break; }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken stop)
    {
        using (client)
        {
            if (client.Client.RemoteEndPoint is not IPEndPoint remote ||
                !IPAddress.IsLoopback(remote.Address))
                return;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var ct = timeout.Token;

            try
            {
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, new UTF8Encoding(false),
                    detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
                var requestLine = await reader.ReadLineAsync(ct) ?? "";
                if (requestLine.Length > 300)
                    return;

                var contentLength = 0;
                for (int index = 0; index < 32; index++)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (line == null) return;
                    if (line.Length == 0) break;
                    if (line.Length > 2048) return;
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase) &&
                        !int.TryParse(line["Content-Length:".Length..].Trim(), out contentLength))
                        return;
                }
                if (contentLength < 0 || contentLength > 4096) return;

                object response;
                if (requestLine.StartsWith("GET /pending ", StringComparison.Ordinal))
                {
                    response = TakePending();
                }
                else if (requestLine.StartsWith("POST /result ", StringComparison.Ordinal) &&
                         contentLength > 0)
                {
                    var content = new char[contentLength];
                    var offset = 0;
                    while (offset < content.Length)
                    {
                        var count = await reader.ReadAsync(content.AsMemory(offset), ct);
                        if (count <= 0) return;
                        offset += count;
                    }
                    ReportResult(new string(content));
                    response = new { ok = true };
                }
                else
                {
                    await SendAsync(stream, 404, new { ok = false, error = "not found" }, ct);
                    return;
                }

                await SendAsync(stream, 200, response, ct);
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (SocketException) { }
        }
    }

    private static async Task SendAsync(NetworkStream stream, int status, object body, CancellationToken ct)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(body);
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Not Found")}\r\n" +
            "Content-Type: application/json; charset=utf-8\r\n" +
            "Cache-Control: no-store\r\n" +
            "X-Content-Type-Options: nosniff\r\n" +
            "Connection: close\r\n" +
            $"Content-Length: {json.Length}\r\n\r\n");
        await stream.WriteAsync(header, ct);
        await stream.WriteAsync(json, ct);
        await stream.FlushAsync(ct);
    }

    public void Dispose()
    {
        CancelPending();
        _stop.Cancel();
        _listener.Stop();
        _stop.Dispose();
    }
}

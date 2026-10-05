using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ChiliMusic;

internal sealed record LanRequest(string Method, string Target, Dictionary<string, string> Headers, byte[] Body, IPAddress Peer);
internal sealed record LanResponse(int Status, string ContentType, byte[] Body, Dictionary<string, string>? Headers = null, long? Length = null, Func<Stream, CancellationToken, Task>? StreamBody = null, IDisposable? Resource = null) : IDisposable
{
    public static LanResponse Text(int status, string text) => new(status, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(text));
    public void Dispose() => Resource?.Dispose();
}

// A bounded, connection-close HTTP transport for the embedded LAN remote. No file serving or proxying.
internal sealed class LanHttpServer(Func<LanRequest, CancellationToken, Task<LanResponse>> handler) : IDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _lifetime;
    private readonly SemaphoreSlim _clients = new(24, 24);
    public int Port { get; private set; }
    public void Start(int port)
    {
        if (_listener != null) return;
        var listener = new TcpListener(IPAddress.Any, port); listener.Start(24);
        Port = ((IPEndPoint)listener.LocalEndpoint).Port; _listener = listener; _lifetime = new();
        _ = AcceptAsync(listener, _lifetime.Token);
    }
    private async Task AcceptAsync(TcpListener listener, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(ct);
                if (!_clients.Wait(0)) { client.Dispose(); continue; }
                _ = ServeAsync(client, ct);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }
    internal static bool IsLocalAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = address.GetAddressBytes(); return b[0] == 10 || b[0] == 192 && b[1] == 168 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 169 && b[1] == 254;
    }
    private async Task ServeAsync(TcpClient client, CancellationToken stop)
    {
        using (client)
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop))
        {
            deadline.CancelAfter(TimeSpan.FromSeconds(30)); var ct = deadline.Token;
            try
            {
                var peer = ((IPEndPoint)client.Client.RemoteEndPoint!).Address;
                if (!IsLocalAddress(peer)) return;
                client.NoDelay = true; var stream = client.GetStream(); LanRequest request;
                using (var readDeadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    readDeadline.CancelAfter(TimeSpan.FromSeconds(5));
                    request = await ReadAsync(stream, peer, readDeadline.Token);
                }
                LanResponse response;
                if (!ValidHost(request)) response = LanResponse.Text(403, "不允许的访问地址");
                else response = await handler(request, ct);
                using (response) { if (response.StreamBody != null) deadline.CancelAfter(Timeout.InfiniteTimeSpan); await WriteAsync(stream, response, ct); }
            }
            catch (InvalidDataException) { try { await WriteAsync(client.GetStream(), LanResponse.Text(400, "请求格式无效"), ct); } catch { } }
            catch (Exception e) when (e is OperationCanceledException or IOException or SocketException or ObjectDisposedException) { }
            catch (Exception e) { Store.Log("WARNING", "手机遥控请求失败 " + e.GetType().Name); try { await WriteAsync(client.GetStream(), LanResponse.Text(500, "操作未完成，请重试"), ct); } catch { } }
            finally { _clients.Release(); }
        }
    }
    private bool ValidHost(LanRequest request)
    {
        if (!request.Headers.TryGetValue("Host", out var host) || !Uri.TryCreate("http://" + host, UriKind.Absolute, out var uri) || uri.Port != Port || uri.PathAndQuery != "/" || uri.UserInfo.Length > 0) return false;
        if (uri.Host != "localhost" && (!IPAddress.TryParse(uri.Host, out var address) || !IsLocalAddress(address))) return false;
        // Do not allow foreign browser origins (including sandboxed null origins).
        return !request.Headers.TryGetValue("Origin", out var origin) || origin == "http://" + host;
    }
    private static async Task<LanRequest> ReadAsync(NetworkStream stream, IPAddress peer, CancellationToken ct)
    {
        var header = new MemoryStream(); byte[] one = new byte[1]; int matched = 0;
        while (header.Length < 8192)
        {
            if (await stream.ReadAsync(one, ct) != 1) throw new InvalidDataException();
            byte b = one[0]; if (b > 127 || b == 0) throw new InvalidDataException(); header.WriteByte(b);
            matched = b == (matched is 0 or 2 ? 13 : 10) ? matched + 1 : b == 13 ? 1 : 0;
            if (matched == 4) break;
        }
        if (matched != 4) throw new InvalidDataException();
        string[] lines = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n"); var start = lines[0].Split(' ');
        if (start.Length != 3 || start[0] is not ("GET" or "POST") || start[2] is not ("HTTP/1.1" or "HTTP/1.0") || !start[1].StartsWith('/') || start[1].StartsWith("//") || start[1].Length > 2048) throw new InvalidDataException();
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1).Where(l => l.Length > 0))
        {
            int colon = line.IndexOf(':'); if (colon <= 0 || char.IsWhiteSpace(line[0]) || !line[..colon].All(c => char.IsAsciiLetterOrDigit(c) || c == '-')) throw new InvalidDataException();
            if (!headers.TryAdd(line[..colon], line[(colon + 1)..].Trim())) throw new InvalidDataException();
        }
        if (headers.ContainsKey("Transfer-Encoding") || headers.ContainsKey("Expect")) throw new InvalidDataException();
        int size = 0;
        if (headers.TryGetValue("Content-Length", out var length) && (!int.TryParse(length, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out size) || size is < 0 or > 8192)) throw new InvalidDataException();
        if (start[0] == "GET" && size > 0) throw new InvalidDataException();
        var body = new byte[size]; await stream.ReadExactlyAsync(body, ct);
        return new(start[0], start[1], headers, body, peer);
    }
    private static async Task WriteAsync(NetworkStream stream, LanResponse response, CancellationToken ct)
    {
        string phrase = response.Status switch { 200 => "OK", 206 => "Partial Content", 400 => "Bad Request", 401 => "Unauthorized", 403 => "Forbidden", 404 => "Not Found", 409 => "Conflict", 416 => "Range Not Satisfiable", 429 => "Too Many Requests", _ => "Internal Server Error" };
        bool chunked = response.StreamBody != null && response.Length == null;
        string framing = chunked ? "Transfer-Encoding: chunked" : "Content-Length: " + (response.Length ?? response.Body.Length);
        string extra = response.Headers == null ? "" : string.Concat(response.Headers.Select(h => h.Key + ": " + h.Value + "\r\n"));
        string header = $"HTTP/1.1 {response.Status} {phrase}\r\nContent-Type: {response.ContentType}\r\n{framing}\r\n{extra}Connection: close\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\nCross-Origin-Resource-Policy: same-origin\r\nContent-Security-Policy: default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' blob: data:; media-src 'self'; connect-src 'self'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), ct);
        if (response.StreamBody != null) await response.StreamBody(stream, ct); else await stream.WriteAsync(response.Body, ct);
    }
    internal static async Task CopyAudioAsync(Stream input, Stream output, long? length, bool chunked, CancellationToken ct)
    {
        byte[] buffer = new byte[65536]; long remaining = length ?? long.MaxValue;
        using var stalled = CancellationTokenSource.CreateLinkedTokenSource(ct);
        while (remaining > 0)
        {
            stalled.CancelAfter(TimeSpan.FromSeconds(30)); int count = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), stalled.Token);
            if (count == 0) { if (length.HasValue && remaining > 0) throw new IOException("音频传输未完成"); break; }
            if (chunked) await output.WriteAsync(Encoding.ASCII.GetBytes(count.ToString("X") + "\r\n"), stalled.Token);
            await output.WriteAsync(buffer.AsMemory(0, count), stalled.Token); if (chunked) await output.WriteAsync("\r\n"u8.ToArray(), stalled.Token); remaining -= count;
        }
        if (chunked) await output.WriteAsync("0\r\n\r\n"u8.ToArray(), stalled.Token);
    }
    public void Dispose() { _lifetime?.Cancel(); _listener?.Stop(); _listener = null; _lifetime?.Dispose(); _lifetime = null; }
}

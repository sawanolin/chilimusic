using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace ChiliMusic;

internal static class DownloadVerification
{
    internal static async Task RunAsync()
    {
        using var fixture = new AudioFixture();
        var settings = new AppSettings { Server = fixture.Url, Username = "fixture", OfflineCacheMb = 64 };
        using var api = new NavidromeApiClient(settings, "fixture"); using var offline = new OfflineCacheService(api, settings);
        var one = new Track { Id = "one", Title = "断点续传", Suffix = "wav" }; var two = new Track { Id = "two", Title = "追加任务", Suffix = "wav" }; var bad = new Track { Id = "bad", Title = "无权限文件", Suffix = "wav" };
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        var download = offline.DownloadAsync([one]);
        for (int i = 0; i < 100 && offline.Tasks[0].Bytes == 0; i++) await Task.Delay(10);
        offline.Pause(offline.Tasks[0]); await download; Check(offline.Tasks[0].State == "已暂停", "Download did not pause");
        long partial = offline.Tasks[0].Bytes; Check(partial > 0 && partial < fixture.Data.Length, "Partial download missing");
        offline.Resume(offline.Tasks[0]); var appended = offline.DownloadAsync([bad, two]); await appended;
        Check(fixture.RangeCount > 0, "HTTP resume did not send Range"); Check(offline.Tasks.Count(t => t.State == "完成") == 2 && offline.Tasks.Single(t => t.Track.Id == "bad").State == "失败", "Individual failure blocked batch");
        string path = offline.GetPath(one) ?? throw new InvalidOperationException("Downloaded file missing"); Check(SHA256.HashData(await File.ReadAllBytesAsync(path)).SequenceEqual(SHA256.HashData(fixture.Data)), "Resumed content corrupted");
        await offline.ClearAsync(); Check(offline.GetPath(one) != null, "Pinned download was evicted");
        offline.SetPinned(one, false); offline.SetPinned(two, false); await offline.ClearAsync(); Check(offline.Entries.Count == 0, "Unpinned cache was not cleared");
        var cancelled = new Track { Id = "cancelled", Title = "取消任务", Suffix = "wav" }; var cancelRun = offline.DownloadAsync([cancelled]);
        var cancelJob = offline.Tasks.Single(t => t.Track.Id == cancelled.Id);
        for (int i = 0; i < 100 && cancelJob.Bytes == 0; i++) await Task.Delay(10);
        offline.Cancel(cancelJob); await cancelRun; Check(cancelJob.State == "已取消" && !Directory.EnumerateFiles(Path.Combine(Store.Root, "cache", "audio"), "*.part").Any(), "Cancel left partial data");
        fixture.RejectBad = false; offline.RetryFailed();
        for (int i = 0; i < 500 && offline.Downloading; i++) await Task.Delay(10);
        Check(offline.Tasks.Single(t => t.Track.Id == "bad").State == "完成", "Failed task did not retry");
        offline.Resume(cancelJob); offline.Pause(cancelJob); offline.Resume(cancelJob);
        for (int i = 0; i < 500 && offline.Downloading; i++) await Task.Delay(10);
        Check(cancelJob.State == "完成", "Rapid pause/resume left task stranded");
        Store.Write("download-verification.json", new { Result = "PASS", PartialBytes = partial, fixture.RangeCount, Checks = new[] { "HTTP Range resume and SHA256", "append while downloading", "failed item isolation and retry", "pin survives clear", "unpin and clear", "cancel removes partial data", "rapid pause/resume" } });
    }
    private sealed class AudioFixture : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0); private readonly CancellationTokenSource _stop = new();
        public byte[] Data { get; } = RandomNumberGenerator.GetBytes(2 * 1024 * 1024);
        public int RangeCount;
        public volatile bool RejectBad = true;
        public string Url => "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port;
        internal AudioFixture() { _listener.Start(); _ = AcceptAsync(); }
        private async Task AcceptAsync() { while (!_stop.IsCancellationRequested) try { var client = await _listener.AcceptTcpClientAsync(_stop.Token); _ = ServeAsync(client); } catch (OperationCanceledException) { break; } }
        private async Task ServeAsync(TcpClient client)
        {
            using (client) try
            {
                var stream = client.GetStream(); using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, true); string request = await reader.ReadLineAsync(_stop.Token) ?? ""; long offset = 0;
                while (await reader.ReadLineAsync(_stop.Token) is string header && header.Length > 0) if (header.StartsWith("Range:", StringComparison.OrdinalIgnoreCase)) { string number = header[(header.IndexOf('=') + 1)..].TrimEnd('-'); if (long.TryParse(number, out offset)) Interlocked.Increment(ref RangeCount); }
                if (RejectBad && request.Contains("id=bad")) { await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), _stop.Token); return; }
                string response = $"HTTP/1.1 {(offset > 0 ? "206 Partial Content" : "200 OK")}\r\nContent-Type: audio/wav\r\nContent-Length: {Data.Length - offset}\r\nAccept-Ranges: bytes\r\n" + (offset > 0 ? $"Content-Range: bytes {offset}-{Data.Length - 1}/{Data.Length}\r\n" : "") + "Connection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(response), _stop.Token);
                for (int i = (int)offset; i < Data.Length; i += 8192) { await stream.WriteAsync(Data.AsMemory(i, Math.Min(8192, Data.Length - i)), _stop.Token); await Task.Delay(4, _stop.Token); }
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or SocketException) { }
        }
        public void Dispose() { _stop.Cancel(); _listener.Stop(); }
    }
}

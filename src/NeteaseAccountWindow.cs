using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using QRCoder;

namespace ChiliMusic;

internal sealed class NeteaseAccountWindow : DialogShell
{
    private readonly PlayerViewModel _vm;
    private readonly NeteaseClient _client;
    private readonly CancellationTokenSource _life = new();
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly TextBlock _account = UiFactory.Text("", 16), _status = UiFactory.Text("", 12);
    private readonly System.Windows.Controls.Image _qr = new() { Width = 208, Height = 208, Stretch = Stretch.Uniform };
    private readonly Button _login, _remove;
    private readonly Border _qrCard;
    private string _key = "";
    private bool _checking;
    internal NeteaseAccountWindow(PlayerViewModel vm, bool generateLogin = true) : base("网易云账号", 520, 650)
    {
        _vm = vm; _client = vm.Api.Netease; MinWidth = 460; MinHeight = 460; Height = Math.Max(MinHeight, Math.Min(650, SystemParameters.WorkArea.Height - 24));
        var panel = new StackPanel(); Body.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        _account.FontWeight = FontWeights.SemiBold;
        _login = UiFactory.Button("扫码登录", () => _ = GenerateAsync(), true); _remove = UiFactory.Button("移除本机账号", Remove);
        panel.Children.Add(UiFactory.Card("账号", _account, UiFactory.Buttons(_login, _remove)));
        var qrBox = new Border { Padding = new Thickness(10), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 8), Child = _qr };
        _status.TextAlignment = TextAlignment.Center;
        _qrCard = UiFactory.Card("用网易云音乐 App 扫码", qrBox, _status); panel.Children.Add(_qrCard);
        var quality = UiFactory.Combo(["标准", "较高", "极高", "无损", "Hi-Res"], Array.IndexOf(new[] { "standard", "higher", "exhigh", "lossless", "hires" }, vm.Settings.NeteaseQuality));
        if (quality.SelectedIndex < 0) quality.SelectedIndex = 2;
        quality.SelectionChanged += (_, _) => { if (quality.SelectedIndex >= 0) { vm.Settings.NeteaseQuality = new[] { "standard", "higher", "exhigh", "lossless", "hires" }[quality.SelectedIndex]; vm.SaveSettings(); } };
        panel.Children.Add(UiFactory.Card("播放音质", quality, UiFactory.Text("按账号权益获取音质，更改后下次播放生效。", 11)));
        var lyrics = UiFactory.Check("自动补全本地与 Navidrome 歌词", vm.Settings.NeteaseLyrics);
        lyrics.Checked += (_, _) => { vm.Settings.NeteaseLyrics = true; vm.SaveSettings(); }; lyrics.Unchecked += (_, _) => { vm.Settings.NeteaseLyrics = false; vm.SaveSettings(); };
        panel.Children.Add(UiFactory.Card("歌词", lyrics, UiFactory.Text("已有歌词优先使用；缺少歌词时按歌名、歌手和时长匹配网易云。", 11)));
        _poll.Tick += async (_, _) => await PollAsync(); _client.AccountChanged += Refresh;
        Loaded += async (_, _) => { Refresh(); if (!_client.LoggedIn && generateLogin) await GenerateAsync(); else _status.Text = _client.LoggedIn ? "已登录，可关闭窗口开始听歌" : "点击扫码登录"; };
        Closed += (_, _) => { _poll.Stop(); _life.Cancel(); _client.AccountChanged -= Refresh; };
    }
    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        _account.Text = _client.LoggedIn ? (_client.Nickname.Length > 0 ? _client.Nickname : "已登录") : "未登录"; _remove.IsEnabled = _client.LoggedIn; _login.Content = _client.LoggedIn ? "重新扫码" : "扫码登录";
        _qrCard.Visibility = _client.LoggedIn && _key.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
    private async Task GenerateAsync()
    {
        if (!_login.IsEnabled) return; _poll.Stop(); _key = ""; _login.IsEnabled = false; _qr.Source = null; _qrCard.Visibility = Visibility.Visible; _status.Text = "正在生成二维码…";
        try
        {
            string key = await _client.CreateLoginKeyAsync(_life.Token); if (_life.IsCancellationRequested) return;
            using var generator = new QRCodeGenerator(); using var data = generator.CreateQrCode("https://music.163.com/login?codekey=" + Uri.EscapeDataString(key), QRCodeGenerator.ECCLevel.M); using var png = new PngByteQRCode(data);
            _qr.Source = CoverCacheService.Decode(png.GetGraphic(8), 420); _key = key; _status.Text = "请扫码，并在网易云音乐 App 中确认登录"; _poll.Start();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { _status.Text = error is ApiException ? error.Message : "二维码生成失败，请重试。"; }
        finally { _login.IsEnabled = true; }
    }
    private async Task PollAsync()
    {
        if (_checking || _key.Length == 0 || _life.IsCancellationRequested) return; _checking = true; string key = _key;
        try
        {
            int code = await _client.CheckLoginAsync(key, _life.Token); if (_life.IsCancellationRequested || key != _key) return;
            _status.Text = code switch { 800 => "二维码已过期，请点击扫码登录重试", 802 => "已扫码，请在网易云音乐 App 中确认", 803 => "登录成功，可以开始听歌", _ => "等待扫码…" };
            if (code is 800 or 803) _poll.Stop(); if (code == 803) { _key = ""; _qr.Source = null; Refresh(); _vm.Changed(nameof(PlayerViewModel.ServerConfigured)); }
        }
        catch (OperationCanceledException) { }
        catch (ApiException error) { _poll.Stop(); _status.Text = error.Message; }
        finally { _checking = false; }
    }
    private void Remove()
    {
        _poll.Stop(); _key = ""; if (_vm.Current?.IsNetease == true) _vm.Stop(); _client.RemoveAccount(); _qr.Source = null; _status.Text = "已移除本机登录，可重新扫码"; Refresh();
    }
}

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using QRCoder;

namespace ChiliMusic;

internal sealed class RemoteControlWindow : DialogShell
{
    private readonly RemoteControlService _remote;
    private readonly TextBlock _status = UiFactory.Text("", 14), _code = UiFactory.Text("", 28), _hint = UiFactory.Text("", 11), _address = UiFactory.Text("", 12);
    private readonly System.Windows.Controls.Image _qr = new() { Width = 176, Height = 176, Stretch = Stretch.Uniform };
    private readonly ComboBox _network = UiFactory.Combo([]);
    private readonly CheckBox _publicNetwork = UiFactory.Check("也允许公用网络中的局域网连接", false);
    private readonly Button _toggle, _reset, _copy;
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(1) };
    private string _shown = "";
    internal RemoteControlWindow(RemoteControlService remote) : base("手机遥控", 600, 760)
    {
        _remote = remote; MinWidth = 520; MinHeight = 540; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Height = Math.Max(MinHeight, Math.Min(760, SystemParameters.WorkArea.Height - 24));
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var panel = new StackPanel(); Body.Children.Add(scroll); scroll.Content = panel;
        _status.FontWeight = FontWeights.SemiBold;
        _toggle = UiFactory.Button("开启手机遥控", Toggle, true); _reset = UiFactory.Button("重新配对", () => { _remote.ResetPairing(); Refresh(); }); _copy = UiFactory.Button("复制地址", () => { if (_address.Text.StartsWith("http://")) System.Windows.Clipboard.SetText(_address.Text); });
        panel.Children.Add(UiFactory.Card("局域网连接", _status, UiFactory.Text("手机与电脑连接同一 Wi-Fi 或局域网。", 11), UiFactory.Buttons(_toggle, _reset)));
        _network.ItemsSource = RemoteControlService.Addresses(); _network.SelectedIndex = 0; _network.SelectionChanged += (_, _) => Refresh();
        var qrBox = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(8), Padding = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Center, Child = _qr, Margin = new Thickness(0, 12, 0, 12) };
        _code.HorizontalAlignment = HorizontalAlignment.Center; _code.FontWeight = FontWeights.SemiBold;
        _address.TextWrapping = TextWrapping.Wrap; _address.HorizontalAlignment = HorizontalAlignment.Center;
        panel.Children.Add(UiFactory.Card("扫码连接", _network, qrBox, _address, UiFactory.Text("在手机网页输入下方配对码", 11), _code, UiFactory.Buttons(_copy)));
        var firewall = UiFactory.Button("允许局域网连接", AllowFirewall);
        panel.Children.Add(UiFactory.Card("连接帮助", UiFactory.Text("网页打不开时，确认手机未使用访客网络。连接规则仅限此播放器和本地子网。", 11), _publicNetwork, firewall, _hint));
        _refresh.Tick += (_, _) => Refresh(); Loaded += (_, _) => { Refresh(); _refresh.Start(); }; Closed += (_, _) => _refresh.Stop();
    }
    private void Toggle()
    {
        try { if (_remote.Running) _remote.Stop(); else _remote.Start(); _hint.Text = ""; }
        catch (System.Net.Sockets.SocketException) { _hint.Text = "启动失败，端口 47831 已被占用。请关闭占用程序后重试。"; }
        Refresh();
    }
    private void Refresh()
    {
        _toggle.Content = _remote.Running ? "关闭手机遥控" : "开启手机遥控"; _reset.IsEnabled = _copy.IsEnabled = _remote.Running;
        _status.Text = _remote.Running ? $"已开启 · {_remote.Devices} 台设备已配对" : "未开启";
        _code.Text = _remote.Running ? string.Join(" ", _remote.PairCode.ToCharArray()) : "— — — — — —";
        string address = _network.SelectedItem as string ?? "";
        _address.Text = !_remote.Running ? "开启后显示连接地址" : address.Length == 0 ? "未找到局域网，请检查网络连接" : $"http://{address}:{_remote.Port}/";
        string shown = _remote.Running && address.Length > 0 ? _address.Text : "";
        if (_shown == shown) return; _shown = shown; _qr.Source = null;
        if (shown.Length == 0) return;
        using var data = QRCodeGenerator.GenerateQrCode(shown, QRCodeGenerator.ECCLevel.M); using var renderer = new PngByteQRCode(data);
        _qr.Source = CoverCacheService.Decode(renderer.GetGraphic(6), 200);
    }
    private void AllowFirewall()
    {
        try { var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" }; start.ArgumentList.Add(_publicNetwork.IsChecked == true ? "--allow-remote-public" : "--allow-remote"); Process.Start(start); _hint.Text = "请在 Windows 提示中允许修改，随后用手机重新连接。"; }
        catch (System.ComponentModel.Win32Exception) { _hint.Text = "未修改防火墙，可在 Windows 中手动允许此播放器通过专用网络。"; }
    }
}

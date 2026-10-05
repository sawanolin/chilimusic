using System.IO.Pipes;
using System.Security.Principal;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using Windows.Media;
namespace ChiliMusic;
public partial class App : Application
{
    private Mutex? _mutex; private bool _ownsMutex; private readonly CancellationTokenSource _exitCts = new();
    private System.Windows.Forms.NotifyIcon? _tray; private System.Drawing.Icon? _icon; private readonly System.Windows.Threading.DispatcherTimer _trayClick = new();
    private TaskbarService? _taskbar; private System.Windows.Forms.ContextMenuStrip? _trayMenu;
    private FunctionKeyService? _functionKeys;
    private MiniPlayerWindow? _mini; private MainWindow? _main; private SettingsWindow? _settingsWindow;
    private MusicToolsWindow? _tools; private DesktopLyricsWindow? _desktopLyrics;
    public PlayerViewModel Vm { get; private set; } = null!;
    public bool Exiting { get; private set; }
    private string InstanceName => "ChiliMusic-" + WindowsIdentity.GetCurrent().User!.Value;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--self-test")) { try { await SelfTest.RunAsync(e.Args); Shutdown(0); } catch (Exception error) { Store.Write("selftest-failure.json", new { Error = error.GetType().Name, Message = error is ApiException ? error.Message : "验收失败" }); Shutdown(1); } return; }
        _mutex = new Mutex(true, @"Local\" + InstanceName, out _ownsMutex);
        if (!_ownsMutex) { try { using var pipe = new NamedPipeClientStream(".", InstanceName, PipeDirection.Out); await pipe.ConnectAsync(2000); await pipe.WriteAsync(new byte[] { 1 }); } catch (IOException) { } catch (TimeoutException) { } Shutdown(); return; }
        DispatcherUnhandledException += (_, args) => { Store.Log("ERROR", $"UI 异常 {args.Exception.GetType().Name}"); if (Vm != null) Vm.Status = "操作发生异常，请重试。"; args.Handled = true; };
        try
        {
            var settings = Store.Read("config.json", new AppSettings()); string password = "";
            try { password = CredentialService.Unprotect(settings.ProtectedPassword); } catch { Store.Log("WARNING", "无法解密已有账户，请重新登录。"); }
            NativeFonts.Initialize(); Theme.Apply(settings.Theme); Vm = new(settings, password); _mini = new(Vm); new WindowInteropHelper(_mini).EnsureHandle();
            try { Vm.Media = new(new WindowInteropHelper(_mini).Handle); Vm.Media.ButtonPressed += button => Dispatcher.BeginInvoke(() => OnMedia(button)); } catch (Exception ex) { Vm.Status = "系统媒体控制初始化失败"; Store.Log("ERROR", $"SMTC 初始化失败 {ex.GetType().Name}"); }
            CreateTray(); _taskbar = new(Vm, this); Vm.SettingsChanged += OnSettingsChanged; Store.Write("taskbar-diagnostics.json", _taskbar.Diagnostics()); _functionKeys = new(Vm); SystemEvents.UserPreferenceChanged += OnPreferences; _ = ListenAsync();
            if (e.Args.Contains("--qa-features")) Vm.Run(() => FeatureUiVerification.RunAsync(this, _taskbar, e.Args.Contains("--qa-quick")));
            else if (e.Args.Contains("--qa-layout")) Vm.Run(() => UiVerification.RunLayoutAsync(this, _taskbar, e.Args.Skip(1).ToArray()));
            else if (e.Args.Contains("--qa-local")) Vm.Run(() => UiVerification.RunLocalAsync(this, e.Args.Skip(1).ToArray()));
            else if (Vm.Api.Configured) { if (e.Args.Contains("--qa-ui")) Vm.Run(() => UiVerification.RunAsync(this, _taskbar)); else { Vm.Run(Vm.ConnectAsync); if (!settings.StartInTray) ShowMain(); } } else ShowMain();
        }
        catch (Exception ex) { Store.Log("ERROR", $"启动失败 {ex.GetType().Name}"); MessageBox.Show("播放器无法启动。请确认 mpv-2.dll 与 EXE 在同一目录，以及已安装 .NET 8 Desktop Runtime。\n" + ex.GetType().Name, "chilimusic"); Exit(); }
    }
    private void OnPreferences(object sender, UserPreferenceChangedEventArgs e) { if (!Exiting && Vm != null) Dispatcher.BeginInvoke(() => { Theme.Apply(Vm.Settings.Theme); _taskbar?.Refresh(); }); }
    private void CreateTray()
    {
        using var stream = GetResourceStream(new Uri("pack://application:,,,/Resources/player.ico")).Stream; _icon = new System.Drawing.Icon(stream);
        _tray = new() { Icon = _icon, Text = "chilimusic", Visible = true }; var menu = new System.Windows.Forms.ContextMenuStrip { Renderer = new PlayerMenuRenderer(), ShowImageMargin = false, Padding = new System.Windows.Forms.Padding(4, 6, 4, 6), Font = new System.Drawing.Font("Microsoft YaHei UI", 9) }; _trayMenu = menu;
        var now = menu.Items.Add("尚未播放"); now.Enabled = false; menu.Opening += (_, _) => now.Text = Vm.Current is { } t ? $"正在播放：{t.Artist} - {t.Title}" : "尚未播放";
        void Item(string title, Action action) { menu.Items.Add(title, null, (_, _) => Dispatcher.Invoke(action)); }
        Item("播放 / 暂停", () => Vm.Run(Vm.ToggleAsync)); Item("上一首", () => Vm.Run(Vm.PreviousAsync)); Item("下一首", () => Vm.Run(() => Vm.NextAsync(false))); menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        Item("随机播放全部", () => Vm.Run(() => Vm.RandomAsync(false))); Item("随机播放收藏", () => Vm.Run(() => Vm.RandomAsync(true))); menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        Item("显示播放器", ShowMini); Item("搜索", () => ShowMain(false, true)); Item("打开本地音乐…", () => { ShowMain(); _main!.OpenLocalFiles(); }); Item("歌词与歌单", () => ShowTools("歌词")); Item("桌面歌词", ToggleDesktopLyrics); Item("解锁桌面歌词", () => _desktopLyrics?.Unlock()); Item("离线下载", () => ShowTools("离线")); Item("定时停止", () => ShowTools("定时")); Item("设置", ShowSettings); Item("退出", Exit); _tray.ContextMenuStrip = menu;
        _trayClick.Interval = TimeSpan.FromMilliseconds(System.Windows.Forms.SystemInformation.DoubleClickTime); _trayClick.Tick += (_, _) => { _trayClick.Stop(); if (_mini!.IsVisible) _mini.Hide(); else ShowMini(); };
        _tray.MouseClick += (_, args) => { if (args.Button == System.Windows.Forms.MouseButtons.Left) _trayClick.Start(); };
        _tray.MouseDoubleClick += (_, args) => { if (args.Button == System.Windows.Forms.MouseButtons.Left) { _trayClick.Stop(); ShowMain(); } };
    }
    private void OnMedia(SystemMediaTransportControlsButton button)
    {
        switch (button) { case SystemMediaTransportControlsButton.Play: Vm.Run(() => Vm.Player.IsIdle ? Vm.ToggleAsync() : ResumeAsync()); break; case SystemMediaTransportControlsButton.Pause: Vm.Player.Pause(); Vm.Changed(nameof(Vm.PlayGlyph)); break; case SystemMediaTransportControlsButton.Next: Vm.Run(() => Vm.NextAsync(false)); break; case SystemMediaTransportControlsButton.Previous: Vm.Run(Vm.PreviousAsync); break; case SystemMediaTransportControlsButton.Stop: Vm.Stop(); break; }
    }
    private Task ResumeAsync() { Vm.Player.Resume(); Vm.Changed(nameof(Vm.PlayGlyph)); return Task.CompletedTask; }
    private async Task ListenAsync()
    {
        while (!_exitCts.IsCancellationRequested) try
            {
                using var pipe = new NamedPipeServerStream(InstanceName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_exitCts.Token); var bytes = new byte[1]; if (await pipe.ReadAsync(bytes, _exitCts.Token) > 0) _ = Dispatcher.BeginInvoke(ShowMini);
            }
            catch (OperationCanceledException) { break; }
            catch (IOException) { if (!_exitCts.IsCancellationRequested) await Task.Delay(250); }
    }
    public void ShowMini() { if (_mini != null) _mini.Popup(); }
    public void ShowTrayMenu() => _trayMenu?.Show(System.Windows.Forms.Cursor.Position);
    internal void CaptureTrayMenu(string path) { if (_trayMenu == null) return; _trayMenu.Show(new System.Drawing.Point(50, 50)); using var bitmap = new System.Drawing.Bitmap(_trayMenu.Width, _trayMenu.Height); _trayMenu.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height)); bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png); _trayMenu.Hide(); }
    public void ShowMain(bool queue = false, bool search = false)
    {
        _mini?.Hide(); bool created = _main == null; _main ??= new(Vm); _main.Show(); _main.WindowState = WindowState.Normal; _main.Activate(); if (created) Vm.Run(() => Vm.BrowseAsync(Vm.Api.Configured ? "最近添加" : "本地音乐")); if (queue) _main.FocusQueue(); if (search) _main.FocusSearch();
    }
    public void ShowSettings() { _mini?.Hide(); if (_settingsWindow != null) { _settingsWindow.Activate(); return; } _settingsWindow = new(Vm); _settingsWindow.Closed += (_, _) => _settingsWindow = null; _settingsWindow.Show(); }
    private void OnSettingsChanged() => _taskbar?.Refresh();
    public void ShowTools(string tab, IEnumerable<Track>? selection = null)
    {
        if (selection != null && _tools != null) _tools.Close();
        if (_tools == null) { _tools = new(Vm, tab, selection); _tools.Closed += (_, _) => _tools = null; }
        _tools.SelectTab(tab); _tools.Show(); _tools.Activate();
    }
    public void ToggleDesktopLyrics()
    {
        if (_desktopLyrics != null) { _desktopLyrics.Close(); return; }
        _desktopLyrics = new(Vm); _desktopLyrics.Closed += (_, _) => _desktopLyrics = null; _desktopLyrics.Show();
    }
    public void NotifyTrack(Track track) { if (Vm.Settings.NotifyTrack) _tray?.ShowBalloonTip(3000, track.Title, track.Artist, System.Windows.Forms.ToolTipIcon.None); }
    public new void Exit()
    {
        if (Exiting) return; Exiting = true; _exitCts.Cancel(); _trayClick.Stop(); SystemEvents.UserPreferenceChanged -= OnPreferences;
        if (Vm != null) Vm.SettingsChanged -= OnSettingsChanged;
        _functionKeys?.Dispose(); _taskbar?.Dispose(); _tray?.Dispose(); _icon?.Dispose(); NativeFonts.Dispose(); try { Vm?.Dispose(); } catch (Exception e) { Store.Log("ERROR", $"退出保存失败 {e.GetType().Name}"); }
        Shutdown();
    }
    protected override void OnExit(ExitEventArgs e) { if (_ownsMutex) _mutex?.ReleaseMutex(); _mutex?.Dispose(); _exitCts.Dispose(); base.OnExit(e); }
}

using System.Text.Json;
using System.Windows;
namespace ChiliMusic;
public partial class SettingsWindow : Window
{
    private readonly PlayerViewModel _vm;
    private readonly SettingsOptionsPanel _options;
    public SettingsWindow(PlayerViewModel vm)
    {
        InitializeComponent(); WindowAppearance.Attach(this); _vm = vm;
        _options = new(vm); AudioOptions.Content = _options.Page("音频输出"); TaskbarOptions.Content = _options.Page("任务栏"); HotkeyOptions.Content = _options.Page("快捷键"); FolderOptions.Content = _options.Page("本地音乐目录"); CacheOptions.Content = _options.Page("离线缓存"); AppearanceOptions.Content = _options.AccentOption;
        ServerBox.Text = vm.Settings.Server; UsernameBox.Text = vm.Settings.Username; RememberCheck.IsChecked = vm.Settings.Remember; UntrustedCheck.IsChecked = vm.Settings.AllowUntrustedCertificate;
        StartupCheck.IsChecked = vm.Settings.StartWithWindows; TrayCheck.IsChecked = vm.Settings.StartInTray; CloseCheck.IsChecked = vm.Settings.CloseToTray; NotifyCheck.IsChecked = vm.Settings.NotifyTrack; ExclusiveCheck.IsChecked = vm.Settings.Exclusive; ThemeBox.SelectedIndex = vm.Settings.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
    }
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
    internal void SelectPage(int index) => SettingsTabs.SelectedIndex = index;
    private async void TestClick(object sender, RoutedEventArgs e) => await TestOrSave(false);
    private async void SaveClick(object sender, RoutedEventArgs e) => await TestOrSave(true);
    private async Task TestOrSave(bool save)
    {
        TestButton.IsEnabled = SaveButton.IsEnabled = false; SaveStatus.Text = "";
        try
        {
            var copy = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(_vm.Settings))!;
            copy.Server = string.IsNullOrWhiteSpace(ServerBox.Text) && save ? "" : NavidromeApiClient.ValidateServer(ServerBox.Text); copy.Username = UsernameBox.Text.Trim(); copy.AllowUntrustedCertificate = UntrustedCheck.IsChecked == true;
            string password = string.IsNullOrEmpty(PasswordInput.Password) ? _vm.Password : PasswordInput.Password;
            if (copy.Server.Length > 0 && (copy.Username.Length == 0 || password.Length == 0)) throw new ApiException("请填写用户名和密码。");
            if (!save) { ConnectionStatus.Text = "正在连接…"; using var api = new NavidromeApiClient(copy, password); await api.PingAsync(); ConnectionStatus.Text = "连接成功"; }
            if (save)
            {
                copy.Remember = RememberCheck.IsChecked == true; copy.ProtectedPassword = copy.Remember ? CredentialService.Protect(password) : null;
                copy.StartWithWindows = StartupCheck.IsChecked == true; copy.StartInTray = TrayCheck.IsChecked == true; copy.CloseToTray = CloseCheck.IsChecked == true; copy.NotifyTrack = NotifyCheck.IsChecked == true; copy.Exclusive = ExclusiveCheck.IsChecked == true; copy.Theme = ThemeBox.SelectedIndex switch { 1 => "Light", 2 => "Dark", _ => "System" };
                _options.Apply(copy);
                if (copy.StartWithWindows != _vm.Settings.StartWithWindows) Store.SetStartup(copy.StartWithWindows);
                _vm.UpdateSettings(copy, password); Close(); ((App)Application.Current).ShowMini();
            }
        }
        catch (Exception e) { var message = e is ApiException ? e.Message : "无法保存或连接，请重试。"; ConnectionStatus.Text = message; if (save) SaveStatus.Text = message; Store.Log("ERROR", $"设置操作失败 {e.GetType().Name}"); }
        finally { TestButton.IsEnabled = SaveButton.IsEnabled = true; }
    }
}

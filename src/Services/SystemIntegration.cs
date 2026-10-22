using Microsoft.Win32;

namespace ChiliMusic;

internal static class SystemIntegration
{
    internal static void Register()
    {
        string executable = Environment.ProcessPath ?? throw new IOException("无法定位播放器。");
        void Set(string path, string name, string value) { using var key = Registry.CurrentUser.CreateSubKey(path); key.SetValue(name, value); }
        const string cls = @"Software\Classes\";
        Set(cls + @"ChiliMusic.Audio", "", "chilimusic 音乐文件"); Set(cls + @"ChiliMusic.Audio\DefaultIcon", "", "\"" + executable + "\",0"); Set(cls + @"ChiliMusic.Audio\shell\open\command", "", "\"" + executable + "\" \"%1\"");
        foreach (string ext in PlayerViewModel.LocalExtensions.Concat(new[] { ".m3u", ".m3u8" }))
        {
            Set(cls + ext + @"\OpenWithProgids", "ChiliMusic.Audio", ""); Set(@"Software\ChiliMusic\Capabilities\FileAssociations", ext, "ChiliMusic.Audio");
            Set(cls + @"SystemFileAssociations\" + ext + @"\shell\ChiliMusic", "", "使用 chilimusic 播放"); Set(cls + @"SystemFileAssociations\" + ext + @"\shell\ChiliMusic\command", "", "\"" + executable + "\" \"%1\"");
        }
        Set(@"Software\ChiliMusic\Capabilities", "ApplicationName", "chilimusic"); Set(@"Software\ChiliMusic\Capabilities", "ApplicationDescription", "本地音乐、Navidrome 与网易云播放器"); Set(@"Software\RegisteredApplications", "chilimusic", @"Software\ChiliMusic\Capabilities");
        Set(cls + @"Directory\shell\ChiliMusic", "", "使用 chilimusic 播放文件夹"); Set(cls + @"Directory\shell\ChiliMusic\command", "", "\"" + executable + "\" \"%1\"");
    }
}

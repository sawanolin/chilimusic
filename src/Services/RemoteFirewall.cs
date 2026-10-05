using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace ChiliMusic;

internal static class RemoteFirewall
{
    internal static bool Allow(bool publicNetwork = false)
    {
        string executable = Environment.ProcessPath!;
        string name = "chilimusic-lan-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(executable))).Substring(0, 12);
        string profile = publicNetwork ? "Private,Public" : "Private";
        string script = "$ErrorActionPreference='Stop'; $rule=Get-NetFirewallRule -Name '" + name + "' -ErrorAction SilentlyContinue; if (!$rule) { New-NetFirewallRule -Name '" + name + "' -DisplayName 'chilimusic 手机遥控' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 47831 -RemoteAddress LocalSubnet -Profile " + profile + " -Program '" + executable.Replace("'", "''") + "' | Out-Null } else { Set-NetFirewallRule -Name '" + name + "' -Enabled True -Action Allow -Profile " + profile + " -RemoteAddress LocalSubnet | Out-Null }";
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!; process.WaitForExit(20000); return process.HasExited && process.ExitCode == 0;
    }
}

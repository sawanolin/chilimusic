const
  ChiliUninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{65F4E2E7-51E4-46CF-A8A7-10D1406357F4}_is1';

function InstalledDirectoryAt(RootKey: HKEY): String;
var
  Directory: String;
begin
  Result := '';
  Directory := '';
  RegQueryStringValue(RootKey, ChiliUninstallKey, 'Inno Setup: App Path', Directory);
  if (Directory = '') or not DirExists(Directory) then
    RegQueryStringValue(RootKey, ChiliUninstallKey, 'InstallLocation', Directory);
  if (Directory <> '') and DirExists(Directory) and
     (FileExists(AddBackslash(Directory) + 'chilimusic.exe') or
      FileExists(AddBackslash(Directory) + 'unins000.exe')) then
    Result := Directory;
end;

function PreviousInstallDirectory: String;
begin
  Result := InstalledDirectoryAt(HKCU64);
  if Result = '' then Result := InstalledDirectoryAt(HKCU32);
end;

function DefaultInstallDirectory(Param: String): String;
begin
  Result := PreviousInstallDirectory;
  if Result = '' then Result := ExpandConstant('{localappdata}\Programs\chilimusic');
end;

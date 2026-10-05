function IsDesktopRuntimeVersion(const Version: String): Boolean;
var
  I: Integer;
begin
  Result := False;
  if (Length(Version) < 5) or (Copy(Version, 1, 4) <> '8.0.') then Exit;
  for I := 5 to Length(Version) do
    if (Version[I] < '0') or (Version[I] > '9') then Exit;
  Result := True;
end;

function HasFramework(const Root, Framework, RequiredFile: String): Boolean;
var
  Entry: TFindRec;
  Base: String;
begin
  Result := False;
  Base := AddBackslash(Root) + 'shared\' + Framework + '\';
  if FindFirst(Base + '8.0.*', Entry) then
  begin
    try
      repeat
        if ((Entry.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0) and
           IsDesktopRuntimeVersion(Entry.Name) and
           FileExists(Base + Entry.Name + '\' + RequiredFile) then
        begin
          Result := True;
          Exit;
        end;
      until not FindNext(Entry);
    finally
      FindClose(Entry);
    end;
  end;
end;

function HasDesktopRuntimeAt(const Root: String): Boolean;
begin
  Result := FileExists(AddBackslash(Root) + 'dotnet.exe') and
    HasFramework(Root, 'Microsoft.NETCore.App', 'coreclr.dll') and
    HasFramework(Root, 'Microsoft.WindowsDesktop.App', 'PresentationFramework.dll');
end;

function DesktopRuntimeInstalled: Boolean;
var
  Location: String;
begin
  Result := False;
  if RegQueryStringValue(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64',
    'InstallLocation', Location) then
    Result := HasDesktopRuntimeAt(Location);
  if not Result then Result := HasDesktopRuntimeAt(ExpandConstant('{pf64}\dotnet'));
end;

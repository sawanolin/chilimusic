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

function SelectDesktopRuntimeRoot(const X64Root, CommonRoot, RegisteredRoot, DefaultRoot: String): String;
begin
  { Match the .NET 8 apphost's search order; a chosen root is not combined
    with frameworks from a different installation. }
  if (X64Root <> '') and DirExists(X64Root) then Result := X64Root
  else if (CommonRoot <> '') and DirExists(CommonRoot) then Result := CommonRoot
  else if RegisteredRoot <> '' then Result := RegisteredRoot
  else Result := DefaultRoot;
end;

function DesktopRuntimeInstalled: Boolean;
var
  Location: String;
begin
  Location := '';
  { .NET registers x64 InstallLocation in the 32-bit registry view. }
  RegQueryStringValue(HKLM32, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64',
    'InstallLocation', Location);
  Location := SelectDesktopRuntimeRoot(GetEnv('DOTNET_ROOT_X64'), GetEnv('DOTNET_ROOT'),
    Location, ExpandConstant('{pf64}\dotnet'));
  Result := HasDesktopRuntimeAt(Location);
end;

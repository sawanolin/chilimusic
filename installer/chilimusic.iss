#ifndef AppVersion
  #define AppVersion "1.3.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\dist"
#endif
#ifndef OutputDir
  #define OutputDir "..\release"
#endif
#define Payload AddBackslash(SourceDir)

[Setup]
AppId={{65F4E2E7-51E4-46CF-A8A7-10D1406357F4}
AppName=chilimusic
AppVersion={#AppVersion}
AppPublisher=Chili
AppPublisherURL=https://github.com/sawanolin/chilimusic
AppSupportURL=https://github.com/sawanolin/chilimusic/issues
AppUpdatesURL=https://github.com/sawanolin/chilimusic/releases/latest
DefaultDirName={localappdata}\Programs\chilimusic
DefaultGroupName=chilimusic
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.19041
DisableProgramGroupPage=yes
DisableWelcomePage=no
UsePreviousAppDir=yes
UsePreviousTasks=yes
CloseApplications=yes
RestartApplications=no
SetupIconFile=..\src\Resources\player.ico
UninstallDisplayIcon={app}\chilimusic.exe
OutputDir={#OutputDir}
OutputBaseFilename=chilimusic-{#AppVersion}-setup-x64
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern dynamic windows11
WizardSizePercent=110
WizardResizable=yes
VersionInfoDescription=chilimusic 安装程序
VersionInfoProductName=chilimusic
VersionInfoProductVersion={#AppVersion}
UninstallDisplayName=chilimusic
LicenseFile=..\LICENSE.txt

[Languages]
Name: "zhcn"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："

[Files]
Source: "{#Payload}chilimusic.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}chilimusic.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}chilimusic.deps.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}chilimusic.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}Microsoft.Windows.SDK.NET.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}WinRT.Runtime.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}TagLibSharp.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}QRCoder.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}mpv-2.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}vulkan-1.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}CHANGELOG.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}licenses\*.txt"; DestDir: "{app}\licenses"; Flags: ignoreversion
Source: "{#Payload}docs\images\main-server.png"; DestDir: "{app}\docs\images"; Flags: ignoreversion
Source: "{#Payload}docs\images\taskbar-playing.png"; DestDir: "{app}\docs\images"; Flags: ignoreversion

[Icons]
Name: "{group}\chilimusic"; Filename: "{app}\chilimusic.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\chilimusic"; Filename: "{app}\chilimusic.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\chilimusic.exe"; Description: "启动 chilimusic"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
#include "runtime.iss"

var
  EnvironmentPage: TWizardPage;
  EnvironmentStatus, EnvironmentHint: TNewStaticText;
  DownloadButton, DetectButton: TNewButton;

procedure RefreshEnvironment;
begin
  if DesktopRuntimeInstalled then
  begin
    EnvironmentStatus.Caption := '运行环境已就绪';
    EnvironmentHint.Caption := '已检测到 .NET 8 Desktop Runtime（x64），可以继续安装。';
    DownloadButton.Enabled := False;
  end
  else
  begin
    EnvironmentStatus.Caption := '需要安装 .NET 8 Desktop Runtime（x64）';
    EnvironmentHint.Caption := '点击下方按钮前往微软官网，在 .NET Desktop Runtime 中选择 Windows x64。安装完成后，回到这里点击“重新检测”。';
    DownloadButton.Enabled := True;
  end;
end;

procedure DownloadClick(Sender: TObject);
var
  Code: Integer;
begin
  if not ShellExecAsOriginalUser('open', 'https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0',
    '', '', SW_SHOWNORMAL, ewNoWait, Code) then
    MsgBox('未能打开浏览器，请访问 dotnet.microsoft.com 下载 .NET 8 Desktop Runtime（x64）。', mbError, MB_OK);
end;

procedure DetectClick(Sender: TObject);
begin
  RefreshEnvironment;
end;

procedure InitializeWizard;
begin
  EnvironmentPage := CreateCustomPage(wpWelcome, '运行环境', '检查电脑是否可以运行 chilimusic');
  EnvironmentStatus := TNewStaticText.Create(EnvironmentPage);
  EnvironmentStatus.Parent := EnvironmentPage.Surface;
  EnvironmentStatus.AutoSize := False;
  EnvironmentStatus.Font.Size := 14;
  EnvironmentStatus.Font.Style := [fsBold];
  EnvironmentStatus.WordWrap := True;
  EnvironmentStatus.SetBounds(0, ScaleY(18), EnvironmentPage.SurfaceWidth, ScaleY(52));
  EnvironmentHint := TNewStaticText.Create(EnvironmentPage);
  EnvironmentHint.Parent := EnvironmentPage.Surface;
  EnvironmentHint.AutoSize := False;
  EnvironmentHint.WordWrap := True;
  EnvironmentHint.SetBounds(0, ScaleY(80), EnvironmentPage.SurfaceWidth, ScaleY(92));
  DownloadButton := TNewButton.Create(EnvironmentPage);
  DownloadButton.Parent := EnvironmentPage.Surface;
  DownloadButton.SetBounds(0, ScaleY(188), ScaleX(190), ScaleY(34));
  DownloadButton.Caption := '前往微软官网下载';
  DownloadButton.OnClick := @DownloadClick;
  DetectButton := TNewButton.Create(EnvironmentPage);
  DetectButton.Parent := EnvironmentPage.Surface;
  DetectButton.SetBounds(ScaleX(202), ScaleY(188), ScaleX(112), ScaleY(34));
  DetectButton.Caption := '重新检测';
  DetectButton.OnClick := @DetectClick;
  RefreshEnvironment;
end;

procedure CurPageChanged(PageID: Integer);
begin
  if PageID = EnvironmentPage.ID then RefreshEnvironment;
end;

function NextButtonClick(PageID: Integer): Boolean;
begin
  Result := True;
  if PageID = EnvironmentPage.ID then
  begin
    RefreshEnvironment;
    Result := DesktopRuntimeInstalled;
    if not Result and not WizardSilent then
      MsgBox('请先安装 .NET 8 Desktop Runtime（x64），然后点击“重新检测”。', mbInformation, MB_OK);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not DesktopRuntimeInstalled then
    Result := '未检测到 .NET 8 Desktop Runtime（x64）。请从微软官网下载并安装后，再运行安装程序。';
end;

function InitializeUninstall: Boolean;
var
  Command: String;
begin
  Result := True;
  if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'ChiliMusic', Command) then
    if Pos(Lowercase(AddBackslash(ExpandConstant('{app}'))), Lowercase(Command)) > 0 then
      RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'ChiliMusic');
end;

; PhotoWidget Inno Setup Script
#define MyAppName "桌面照片组件"
#define MyAppEnglishName "PhotoWidget"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "sakuyaxiaoye"
#define MyAppURL "https://github.com/sakuyaxiaoye/PhotoWidget"
#define MyAppExeName "PhotoWidget.exe"
#define MyAppId "D37E88A1-805B-4C9D-A536-121A8961D8E7"
#define SourceDir "..\dist\PhotoWidget-v1.0.0-win-x64"

[Setup]
AppId={{{#MyAppId}}
AppName={#MyAppName} ({#MyAppEnglishName})
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppEnglishName}
DefaultGroupName={#MyAppName}
DisableDirPage=no
UsePreviousAppDir=yes
AlwaysShowDirOnReadyPage=yes
DirExistsWarning=no
AllowNoIcons=yes
LicenseFile=..\LICENSE
OutputDir=..\dist
OutputBaseFilename=PhotoWidget-v1.0.0-Setup
SetupIconFile=..\src\DesktopPicture.App\Resources\app.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
CloseApplicationsFilter=PhotoWidget.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "开机自动启动桌面照片组件"; GroupDescription: "启动设置:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "PhotoWidget"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[Code]
// Helper function to detect previous installed version
function GetInstalledVersion(): String;
var
  ver: String;
begin
  if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{{#MyAppId}}_is1', 'DisplayVersion', ver) then
    Result := ver
  else if RegQueryStringValue(HKLM, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{{#MyAppId}}_is1', 'DisplayVersion', ver) then
    Result := ver
  else
    Result := '';
end;

// Terminate running PhotoWidget process cleanly before copying files
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/F /IM PhotoWidget.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;

procedure InitializeWizard();
var
  oldVer: String;
begin
  oldVer := GetInstalledVersion();
  if oldVer <> '' then
  begin
    WizardForm.WelcomeLabel2.Caption := 
      '检测到您的计算机上已安装 PhotoWidget (已安装版本: ' + oldVer + ')。' + #13#10#13#10 +
      '安装程序将自动执行平滑升级覆盖。您的用户配置、相框布局及所有本地设置将完整保留。' + #13#10#13#10 +
      '点击“下一步”继续。';
  end;
end;

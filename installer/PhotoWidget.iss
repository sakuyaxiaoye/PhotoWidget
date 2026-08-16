; PhotoWidget Inno Setup Script
#define MyAppName "桌面照片组件"
#define MyAppEnglishName "PhotoWidget"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "sakuyaxiaoye"
#define MyAppURL "https://github.com/sakuyaxiaoye/PhotoWidget"
#define MyAppExeName "PhotoWidget.exe"
#define SourceDir "..\dist\PhotoWidget-v1.0.0-win-x64"

[Setup]
AppId={{D37E88A1-805B-4C9D-A536-121A8961D8E7}
AppName={#MyAppName} ({#MyAppEnglishName})
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppEnglishName}
DefaultGroupName={#MyAppName}
DisableDirPage=no
UsePreviousAppDir=no
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

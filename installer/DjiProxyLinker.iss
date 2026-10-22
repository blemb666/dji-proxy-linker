; Установщик DJI Proxy Linker (Inno Setup 6).
; Собирается через build.ps1 -Installer, версия приходит параметром /DAppVersion=x.y.z.
; Ставится без прав администратора в %LOCALAPPDATA%\Programs, реестр — только HKCU.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppName "DJI Proxy Linker"
#define AppExe "DjiProxyLinker.exe"
#define RepoUrl "https://github.com/blemb666/dji-proxy-linker"

[Setup]
AppId={{5F48F4F8-CB08-4D7F-AF0F-8F0111AA156E}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=blemb666
AppPublisherURL={#RepoUrl}
AppSupportURL=https://t.me/adam_editing_page
AppUpdatesURL={#RepoUrl}/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
MinVersion=10.0
OutputBaseFilename=DjiProxyLinker-Setup-{#AppVersion}
SetupIconFile=..\build\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
russian.ContextMenu=Создать прокси для DaVinci
english.ContextMenu=Create proxies for DaVinci
russian.ContextMenuTask=Пункт «Создать прокси для DaVinci» в контекстном меню папок и дисков
english.ContextMenuTask="Create proxies for DaVinci" in the folder and drive context menu
russian.SendToTask=Ярлык в меню «Отправить»
english.SendToTask=Shortcut in the "Send to" menu
russian.Integration=Интеграция с Проводником:
english.Integration=Explorer integration:

[Tasks]
Name: "contextmenu"; Description: "{cm:ContextMenuTask}"; GroupDescription: "{cm:Integration}"
Name: "sendto"; Description: "{cm:SendToTask}"; GroupDescription: "{cm:Integration}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\dist\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon
Name: "{usersendto}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: sendto

; ПКМ по папке, по пустому месту внутри папки и по диску (SD-карте).
; "--run" — сразу обрабатывает папку; откатить можно в самой программе.
[Registry]
Root: HKA; Subkey: "Software\Classes\Directory\shell\DjiProxyLinker"; ValueType: string; ValueName: ""; ValueData: "{cm:ContextMenu}"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\DjiProxyLinker"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#AppExe}"",0"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\DjiProxyLinker\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" --run ""%1"""; Tasks: contextmenu

Root: HKA; Subkey: "Software\Classes\Directory\Background\shell\DjiProxyLinker"; ValueType: string; ValueName: ""; ValueData: "{cm:ContextMenu}"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\Background\shell\DjiProxyLinker"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#AppExe}"",0"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\Background\shell\DjiProxyLinker\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" --run ""%V"""; Tasks: contextmenu

Root: HKA; Subkey: "Software\Classes\Drive\shell\DjiProxyLinker"; ValueType: string; ValueName: ""; ValueData: "{cm:ContextMenu}"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Drive\shell\DjiProxyLinker"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#AppExe}"",0"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Drive\shell\DjiProxyLinker\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" --run ""%1"""; Tasks: contextmenu

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

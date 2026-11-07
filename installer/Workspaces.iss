; Установщик Workspaces Free (Inno Setup 6). Сборка: build-release.bat (нужен установленный Inno Setup).
; Данные пользователя (%APPDATA%\Workspaces) при обновлении и переустановке не затрагиваются.
#ifndef AppVersion
  #define AppVersion "1.3.0"
#endif
#define AppName "Workspaces Free"

[Setup]
AppId={{D354A37C-393A-4C86-8BB4-C76F5AD60D5B}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
VersionInfoVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\Workspaces
DefaultGroupName=Workspaces
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
OutputDir=..\dist-installer
OutputBaseFilename=Workspaces-Free-Setup-{#AppVersion}
SetupIconFile=..\Workspaces\Assets\app.ico
UninstallDisplayIcon={app}\Workspaces.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; не даём установить поверх запущенной копии
AppMutex=Workspaces.SingleInstance.Mutex
CloseApplications=yes

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
russian.DeleteUserData=Удалить также ваши данные (рабочие пространства, настройки, резервные копии и журналы)?%n%nВыберите «Нет», чтобы сохранить их для будущей установки.
english.DeleteUserData=Also delete your data (workspaces, settings, backups and logs)?%n%nChoose No to keep them for a future installation.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\publish\Workspaces.exe"; DestDir: "{app}"; Flags: ignoreversion
; конфигурация источников обновлений и лицензий не перезаписывается при обновлении
Source: "..\publish\appconfig.json"; DestDir: "{app}"; Flags: onlyifdoesntexist uninsneveruninstall

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\Workspaces.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\Workspaces.exe"; Tasks: desktopicon

[Registry]
; убрать автозапуск, если он был включён в настройках приложения
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "Workspaces"; Flags: uninsdeletevalue dontcreatekey

[Run]
Filename: "{app}\Workspaces.exe"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and (not UninstallSilent) then
  begin
    if MsgBox(ExpandConstant('{cm:DeleteUserData}'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(ExpandConstant('{userappdata}\Workspaces'), True, True, True);
  end;
end;

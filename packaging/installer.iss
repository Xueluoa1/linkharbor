#ifndef StageDir
  #error StageDir is required
#endif
#define AppVersion "1.0.1"

[Setup]
AppId={{B275D277-6BCF-4786-BD58-F9696BDD7F05}
AppName=拾链
AppVersion={#AppVersion}
AppPublisher=雪落a
AppPublisherURL=https://xueluo.online
AppSupportURL=https://github.com/Xueluoa1/linkharbor/issues
DefaultDirName={localappdata}\Programs\LinkHarbor
DefaultGroupName=拾链
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
OutputDir={#OutputDir}
OutputBaseFilename=LinkHarbor-Backend
SetupIconFile={#SourcePath}\..\src\app.ico
UninstallDisplayIcon={app}\拾链下载器.exe
UninstallDisplayName=拾链 v{#AppVersion}
VersionInfoVersion={#AppVersion}
VersionInfoCompany=雪落a
WizardStyle=modern
DisableProgramGroupPage=yes
DisableDirPage=yes
DisableReadyPage=yes
UsePreviousAppDir=no
UsePreviousTasks=no
CloseApplications=yes
RestartApplications=no
SetupMutex=LinkHarborSetupBackend
Uninstallable=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Messages]
SetupAppTitle=拾链安装
SetupWindowTitle=拾链安装
UninstallAppTitle=拾链卸载
UninstallAppFullTitle=卸载拾链
ButtonOK=确定
ButtonCancel=取消
ButtonYes=是
ButtonNo=否
ConfirmUninstall=确定要卸载 %1 吗？%n%n个人设置和下载文件将保留。
UninstallStatusLabel=正在卸载 %1，请稍候…
UninstalledAll=%1 已卸载。%n%n个人设置和下载文件已保留。
UninstalledMost=%1 已卸载。%n%n部分文件仍在使用，可稍后处理。个人设置和下载文件已保留。

[Files]
Source: "{#StageDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\拾链"; Filename: "{app}\拾链下载器.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\拾链"; Filename: "{app}\拾链下载器.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Code]
var
  ProgressFile, CancelFile: String;
  CancelRequested: Boolean;

function InitializeSetup(): Boolean;
begin
  ProgressFile := ExpandConstant('{param:PROGRESSFILE|}');
  CancelFile := ExpandConstant('{param:CANCELFILE|}');
  Result := True;
end;

procedure CurInstallProgressChanged(CurProgress, MaxProgress: Integer);
begin
  if ProgressFile <> '' then
    SaveStringToFile(ProgressFile, IntToStr(CurProgress) + '/' + IntToStr(MaxProgress), False);
  if (CancelFile <> '') and FileExists(CancelFile) and not CancelRequested then begin
    CancelRequested := True;
    { Inno intentionally ignores Close while a very-silent wizard is hidden.
      Show it off-screen, then post WM_CLOSE after this callback returns so the
      engine's normal cancellation/rollback path handles the request. }
    WizardForm.Left := -32000;
    WizardForm.Top := -32000;
    WizardForm.Show;
    PostMessage(WizardForm.Handle, $0010, 0, 0);
  end;
end;

procedure CancelButtonClick(CurPageID: Integer; var Cancel, Confirm: Boolean);
begin
  Cancel := True;
  Confirm := False;
end;

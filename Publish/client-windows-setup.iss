;==========================================================
; Alife.Client Inno Setup 脚本
; 用法：iscc Alife.Client.Setup.iss /dMyAppVersion=4.6.1 /dMyOutputDir="..."
;==========================================================

#ifndef MyAppVersion
#define MyAppVersion "0.0.0"
#endif
#ifndef MyOutputDir
#define MyOutputDir "C:\Users\13309\Desktop\Alife.OfficialPluginStorage"
#endif
#ifndef MySourceDir
#define MySourceDir "C:\Users\13309\Desktop\Shared\Alife\Outputs\Alife.Client"
#endif

[Setup]
AppName=Alife.Client
AppVersion={#MyAppVersion}
AppPublisher=Alife官方
AppSupportURL=https://github.com/BDFFZI/Alife
DefaultDirName={commonpf}\Alife\Alife.Client
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DefaultGroupName=Alife
AllowNoIcons=yes
LicenseFile=
OutputDir={#MyOutputDir}
OutputBaseFilename=Alife.Client.Windows
Compression=lzma
SolidCompression=yes
PrivilegesRequired=admin
CloseApplications=yes
RestartApplications=no
Uninstallable=yes
CreateUninstallRegKey=yes
ChangesAssociations=yes

[Languages]
Name: "chinese"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务:"; Flags: unchecked

[Code]
const
  DotnetChannel = '10.0';
  DotnetInstallDir = 'C:\Program Files\dotnet';
  DotnetInstallScriptUrl = 'https://dot.net/v1/dotnet-install.ps1';

function DotnetSdkInstalled: Boolean;
var
  ResultCode: Integer;
begin
  Result := ShellExec('', 'cmd.exe', '/c dotnet --list-sdks 2>nul | findstr /c:"10.0" >nul', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function InstallDotnet: Boolean;
var
  Cmd: String;
  ResultCode: Integer;
begin
  Result := False;
  Cmd := '-NoProfile -ExecutionPolicy Bypass -Command "' +
    '$ErrorActionPreference=''Stop''; ' +
    '[Net.ServicePointManager]::SecurityProtocol=''Tls12''; ' +
    '$p=Join-Path $env:TEMP ''dotnet-install.ps1''; ' +
    'Invoke-WebRequest ''' + DotnetInstallScriptUrl + ''' -UseBasicParsing -OutFile $p; ' +
    '& $p -Channel ' + DotnetChannel + ' -InstallDir ''' + DotnetInstallDir + '''"';
  if ShellExec('', 'powershell.exe', Cmd, '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
  begin
    Result := (ResultCode = 0);
  end;

  if not Result then
  begin
    MsgBox('.NET SDK 自动安装失败，请手动安装 .NET SDK 10.0', mbError, MB_OK);
  end;
end;

function InitializeSetup: Boolean;
begin
  Result := True;

  if not DotnetSdkInstalled then
  begin
    if MsgBox('检测到未安装 .NET SDK 10.0。' + #13#10 +
              'Alife.Client 需要 .NET SDK 才能运行。' + #13#10 +
              '是否现在自动下载并安装？（约 100MB，需联网）',
              mbConfirmation, MB_YESNO) = IDYES then
    begin
      if not InstallDotnet then
      begin
        MsgBox('请手动安装 .NET SDK 10.0 后再运行 Alife.Client。', mbInformation, MB_OK);
      end
      else if not DotnetSdkInstalled then
      begin
        MsgBox('.NET SDK 安装完成，但检测仍未通过，请重启安装程序。', mbInformation, MB_OK);
        Result := False;
      end;
    end
    else
    begin
      MsgBox('客户端需要 .NET SDK 10.0 才能运行，请手动安装。', mbInformation, MB_OK);
      Result := False;
    end;
  end;
end;

[Files]
Source: "{#MySourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Alife"; Filename: "{app}\Alife.Client.Windows.exe"
Name: "{group}\卸载 Alife"; Filename: "{uninstallexe}"
Name: "{commondesktop}\Alife"; Filename: "{app}\Alife.Client.Windows.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Alife.Client.Windows.exe"; Description: "启动 Alife.Client"; Flags: nowait postinstall


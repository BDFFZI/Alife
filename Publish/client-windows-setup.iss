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

[Files]
Source: "{#MySourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Alife"; Filename: "{app}\Alife.Client.Windows.exe"
Name: "{group}\卸载 Alife"; Filename: "{uninstallexe}"
Name: "{commondesktop}\Alife"; Filename: "{app}\Alife.Client.Windows.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Alife.Client.Windows.exe"; Description: "启动 Alife.Client"; Flags: nowait postinstall


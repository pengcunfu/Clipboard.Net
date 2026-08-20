; 熔岩超级剪贴板 - Inno Setup 安装脚本
;
; 需要 Inno Setup 6.3+（choco install innosetup）
; 用法：
;   1. 先发布单文件绿色版：
;      dotnet publish Clipboard/Clipboard.csproj -c Release -r win-x64 --self-contained true `
;        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
;        -p:EnableCompressionInSingleFile=true -o publish/win-x64
;   2. 编译安装程序：
;      iscc installer\setup.iss /DMyAppVersion=1.0.0

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppName "熔岩超级剪贴板"
#define MyAppPublisher "pengcunfu"
#define MyAppExeName "熔岩超级剪贴板.exe"
#define MyAppIcon "..\Clipboard\Assets\icon.ico"

[Setup]
AppId={{B2E7C0B6-4F3A-4C9E-8A7B-1D2F3A4B5C6D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
; 按用户目录安装（无需管理员权限），程序可在安装目录下正常读写 data/
DefaultDirName={localappdata}\Programs\{#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename={#MyAppName}-{#MyAppVersion}-安装版
SetupIconFile={#MyAppIcon}
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加图标："; Flags: unchecked

[Files]
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "立即运行 {#MyAppName}"; Flags: nowait postinstall skipifsilent

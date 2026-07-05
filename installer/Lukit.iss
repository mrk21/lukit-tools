; Lukit Tools 配布用インストーラ (Inno Setup 6)
;
; ビルド（推奨）: publish から setup.exe まで一括で作る
;   pwsh scripts\build-installer.ps1 -Version 1.2.3
;
; 手動でやる場合:
;   1) self-contained 単一 exe を artifacts\publish\Lukit.exe に用意する
;        dotnet publish src\Lukit\Lukit.csproj -c Release -r win-x64 `
;          --self-contained true -p:PublishSingleFile=true `
;          -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts\publish
;   2) iscc /DMyAppVersion=1.2.3 installer\Lukit.iss
;
; 出力:  dist\Lukit-Setup-<version>-x64.exe
;
; ユーザー単位インストール（管理者権限なし・UAC なし）。常駐版の慣習に合わせ
; %LOCALAPPDATA%\Programs\Lukit へ入れる。

#define MyAppName "Lukit Tools"
#define MyAppPublisher "mrk21"
#define MyAppExeName "Lukit.exe"
#define MyAppUrl "https://github.com/mrk21/lukit-tools"

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0-dev"
#endif

; 既定は install.ps1 / release ワークフローの publish 先。/DSourceExe=... で上書き可。
#ifndef SourceExe
  #define SourceExe "..\artifacts\publish\Lukit.exe"
#endif

[Setup]
; この AppId は Lukit Tools 固有。バージョンを上げても変えないこと（アップグレード判定に使われる）。
AppId={{B2E7C1A4-9D3F-4E56-8A0B-6C1D2E3F4A5B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}
DefaultDirName={localappdata}\Programs\Lukit
DisableProgramGroupPage=yes
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
; 管理者権限なしのユーザー単位インストール
PrivilegesRequired=lowest
; 常駐中でも上書きできるよう、実行中インスタンスの終了を促す（Program.cs の Mutex 名と一致）
AppMutex=Lukit_SingleInstance_9C6B4D2E7A834F1B9E5A1D2C3B4A5F60
CloseApplications=yes
RestartApplications=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
OutputDir=..\dist
OutputBaseFilename=Lukit-Setup-{#MyAppVersion}-x64

[Languages]
Name: "ja"; MessagesFile: "compiler:Languages\Japanese.isl"

[Tasks]
Name: "startup"; Description: "Windows へのログオン時に Lukit Tools を自動起動する"; GroupDescription: "スタートアップ:"
Name: "desktopicon"; Description: "デスクトップにショートカットを作成する"; GroupDescription: "追加のショートカット:"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; スタートアップ登録（install.ps1 と同じ HKCU Run キー）。アンインストール時に自動削除。
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Lukit"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Lukit Tools を起動する"; Flags: nowait postinstall skipifsilent

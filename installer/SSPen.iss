; SS Pen 설치 프로그램 (WI-19, Inno Setup 6)
; AC-25 시작 메뉴 등록 / AC-26 로그인 시 자동 시작(옵션) / AC-27 깨끗한 제거 (R9)

#define MyAppName "SS Pen"
; src/SSPen/SSPen.csproj 의 <Version> 과 반드시 같아야 한다.
#define MyAppVersion "1.3.2"
#define MyAppExeName "SSPen.exe"

[Setup]
AppId={{6E9A2C41-8B7D-4A53-9F2E-D10C5A7B3F64}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=SS Pen (개인용)
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; 개인용 앱: 관리자 권한 불필요 (사용자 영역 설치).
PrivilegesRequired=lowest
OutputDir=..\publish\installer
OutputBaseFilename=SSPen-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
UninstallDisplayName={#MyAppName}
; 설치 마법사·제어판 프로그램 목록 아이콘 (exe와 동일 자산).
SetupIconFile=..\src\SSPen\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
; AC-26: 로그인 시 자동 시작 — 설치 시 기본 켜짐, 앱 설정에서도 끌 수 있다.
Name: "runatlogin"; Description: "윈도우 로그인 시 시작"

[Files]
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; AC-25: 시작 메뉴 등록.
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

[Registry]
; 앱의 RunAtLogin 설정과 동일한 값 (동일 소유 지점): HKCU Run.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
  ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; \
  Flags: uninsdeletevalue; Tasks: runatlogin
; AC-27/R9 잔재 제거: 설치 시 태스크를 껐어도 앱 설정으로 켠 Run 값이 남지 않도록 언인스톨 시 무조건 삭제.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
  ValueName: "{#MyAppName}"; ValueType: none; Flags: uninsdeletevalue; \
  Check: not WizardIsTaskSelected('runatlogin')

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{#MyAppName} 실행"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; AC-27 / R9: 앱 데이터(설정·로그)까지 깨끗이 제거.
Type: filesandordirs; Name: "{userappdata}\{#MyAppName}"

[UninstallRun]
Filename: "taskkill"; Parameters: "/IM {#MyAppExeName} /F"; Flags: runhidden skipifdoesntexist; RunOnceId: "KillApp"

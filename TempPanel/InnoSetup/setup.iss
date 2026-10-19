; TempPanel � Instalador Inno Setup
; Requiere: publish self-contained en ..\publish\win-x64

#define MyAppName "TempPanel"
#define MyAppVersion "3.0.0"
#define MyAppPublisher "TempPanel"
#define MyAppURL "https://github.com/temppanel/temppanel"
#define MyAppExeName "TempPanel.exe"
#ifndef PublishDir
#define PublishDir "C:\tempanel\publish\win-x64"
#endif

[Setup]
AppId={{8A3F2B1C-7D4E-4A5F-9C6B-1E2F3A4B5C6D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppMutex=TempPanelAppMutex
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=C:\tempanel\dist
OutputBaseFilename=TempPanel-Setup-x64
SetupIconFile=C:\tempanel\TempPanel\app.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName={#MyAppName}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=Monitor de hardware en tiempo real
LicenseFile=C:\tempanel\LICENSE.txt
InfoBeforeFile=C:\tempanel\README_Installer.txt

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; NOTA: no se borran archivos personales; solo los propios (ver [UninstallDelete])

[InstallDelete]
; Restos del frontend antiguo al actualizar desde v2.5.0 o anterior: ahora todo
; el bundle vive en Frontend\assets\ y estos archivos ya no se usan.
Type: files; Name: "{app}\Frontend\app.js"
Type: files; Name: "{app}\Frontend\styles.css"
Type: files; Name: "{app}\TempPanel.pdb"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent shellexec

[UninstallDelete]
; Limpia solo cach� WebView2 local de TempPanel (los ajustes del usuario se conservan a prop�sito
; salvo que el usuario los borre; no se toca ning�n archivo personal).
Type: filesandordirs; Name: "{localappdata}\TempPanel\WebView2"

[Code]
function IsWebView2Installed(): Boolean;
var
  Ver: String;
begin
  Result := RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\ClientState\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Ver)
    or RegQueryStringValue(HKCU, 'SOFTWARE\Microsoft\EdgeUpdate\ClientState\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Ver);
end;

function InitializeSetup(): Boolean;
var
  Res: Integer;
begin
  Result := True;
  if not IsWebView2Installed() then
  begin
    Res := MsgBox('TempPanel necesita el runtime Microsoft Edge WebView2 para mostrar su interfaz.' + #13#10 + #13#10 +
      'Si contin�as sin �l, la aplicaci�n mostrar� un mensaje con el enlace de descarga oficial.' + #13#10 + #13#10 +
      '�Deseas abrir la p�gina de descarga ahora?',
      mbConfirmation, MB_YESNO);
    if Res = IDYES then
      ShellExec('open', 'https://go.microsoft.com/fwlink/p/?LinkId=2124703', '', '', SW_SHOW, ewNoWait, Res);
  end;
end;

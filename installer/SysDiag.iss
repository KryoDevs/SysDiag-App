#ifndef AppVersion
  #error La version se obtiene de SysDiag.csproj: usar Tools/build_installer.ps1
#endif

[Setup]
AppName=SysDiag
AppVersion={#AppVersion}
AppPublisher=SysDiag
AppPublisherURL=https://github.com/KryoDevs/SysDiag-App
AppSupportURL=https://github.com/KryoDevs/SysDiag-App/issues
AppUpdatesURL=https://github.com/KryoDevs/SysDiag-App/releases
DefaultDirName={autopf}\SysDiag
DefaultGroupName=SysDiag
; Ruta de salida explicita, en ASCII a proposito (este archivo lo lee el
; compilador de Inno Setup, no PowerShell). El valor por defecto ("Output",
; relativo a este script) dejaba el setup en installer\Output y la puerta del
; CI lo buscaba en installer\: el job "instalador" fallaba con "Se esperaba un
; instalador y hay 0" despues de haber compilado bien. Lo que se declara aca es
; lo que busca Tools\validate_installer.ps1, y Tools\validate_docs.ps1
; comprueba que los dos sigan coincidiendo.
OutputDir={#SourcePath}\Output
Compression=lzma
SolidCompression=yes
OutputBaseFilename=SysDiag-Setup-{#AppVersion}
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs

[Icons]
Name: "{group}\SysDiag"; Filename: "{app}\SysDiag.exe"
Name: "{group}\Desinstalar SysDiag"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\SysDiag.exe"; Description: "Abrir SysDiag"; Flags: nowait postinstall skipifsilent

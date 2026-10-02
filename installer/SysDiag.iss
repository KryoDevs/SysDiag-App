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

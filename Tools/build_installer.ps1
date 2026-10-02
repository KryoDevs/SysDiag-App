param([string]$Compiler = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe")
$ErrorActionPreference = 'Stop'
if (-not (Test-Path $Compiler)) { throw 'Instala Inno Setup 6 o indica -Compiler con la ruta a ISCC.exe' }
[xml]$project = Get-Content (Join-Path $PSScriptRoot '..\SysDiag.csproj') -Raw
$version = @($project.Project.PropertyGroup.Version | Where-Object { $_ })[0]
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Versión inválida' }
$publish = Join-Path $PSScriptRoot '..\publish'
& (Join-Path $PSScriptRoot 'validate_release.ps1') -Root $publish
& (Join-Path $PSScriptRoot 'validate_version.ps1') -Root $publish
& $Compiler "/DAppVersion=$version" (Join-Path $PSScriptRoot '..\installer\SysDiag.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup falló' }

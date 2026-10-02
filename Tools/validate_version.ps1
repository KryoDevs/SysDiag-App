param([string]$Root = (Join-Path $PSScriptRoot '..\publish'), [string]$Tag = '')
$ErrorActionPreference = 'Stop'
[xml]$project = Get-Content (Join-Path $PSScriptRoot '..\SysDiag.csproj') -Raw
$version = @($project.Project.PropertyGroup.Version | Where-Object { $_ })[0]
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'La versión del proyecto no es válida' }
if ($Tag -and $Tag -cne "v$version") { throw "Etiqueta $Tag distinta de la versión de proyecto v$version" }
$file = (Get-Item (Join-Path $Root 'SysDiag.exe')).VersionInfo
if ($file.ProductVersion -notlike "$version*") { throw "ProductVersion incorrecta: $($file.ProductVersion), se esperaba $version" }
Write-Host "Versión consistente: $version"

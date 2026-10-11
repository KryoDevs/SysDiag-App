$path = Join-Path $PSScriptRoot "fixtures\diagnostico_fixture.json"
if (-not (Test-Path $path)) { Write-Error "Fixture not found: $path"; exit 2 }
$json = Get-Content $path -Raw | ConvertFrom-Json
$bad = $json.Hallazgos | Where-Object { $_.Severity -eq 'Bad' }
Write-Host "Total hallazgos: $($json.Hallazgos.Count)"
Write-Host "Criticos (Bad): $($bad.Count)"
if ($bad.Count -gt 0) { Write-Host "Fixture OK: contiene hallazgos críticos"; exit 0 }
else { Write-Error "Fixture inválido: no contiene hallazgos críticos"; exit 1 }
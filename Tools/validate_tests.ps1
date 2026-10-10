param([string]$Root = (Join-Path $PSScriptRoot '..\TestResults'))
$ErrorActionPreference = 'Stop'
$files = @(Get-ChildItem $Root -Filter '*.trx')
if ($files.Count -eq 0) { throw 'No se generó ningún resultado TRX' }
$total = 0; $passed = 0; $failed = 0
foreach ($file in $files) {
    [xml]$document = Get-Content $file.FullName -Raw
    foreach ($result in $document.TestRun.Results.UnitTestResult) {
        if ($result.outcome -eq 'Passed') { continue }
        $detail = "$($result.testName): $($result.Output.ErrorInfo.Message) $($result.Output.ErrorInfo.StackTrace)"
        $detail = $detail.Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A')
        Write-Output "::error title=Regresión $($result.outcome)::$detail"
    }
    $counters = $document.TestRun.ResultSummary.Counters
    $total += [int]$counters.total; $passed += [int]$counters.passed; $failed += [int]$counters.failed
}
# El mínimo es una barrera contra la pérdida silenciosa de cobertura: si una
# prueba desaparece, el número baja y la compilación falla. Sube con cada lote
# que agrega pruebas; no se baja nunca.
if ($total -lt 232 -or $passed -ne $total -or $failed -gt 0) {
    throw "Suite incompleta o fallida: $passed/$total aprobadas, $failed fallidas; mínimo 232 (114 auditadas + 34 del sistema visual + 4 de licencia + 11 del recorte de ventanas + 40 de funciones puras del lote 5.9 + 15 del lote «Confiar» + 14 del diff entre diagnósticos) — no eliminar pruebas sin reemplazarlas"
}
Write-Output "::notice title=Pruebas de regresión::$passed/$total pruebas aprobadas, sin omisiones ni fallos"

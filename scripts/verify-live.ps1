param(
    [string]$BaseUrl = "http://localhost:5014",
    [string]$CasesPath = "tests/CampusGrid.Tests/TestData/sample_cases.json",
    [int]$DelaySeconds = 15
)

$ErrorActionPreference = "Stop"
$base = $BaseUrl.TrimEnd("/")
Write-Host "Checking $base/health"
$health = Invoke-RestMethod -Uri "$base/health" -Method Get
if ($health.status -ne "ok") {
    throw "Health endpoint did not return status=ok."
}

$document = Get-Content -LiteralPath $CasesPath -Raw | ConvertFrom-Json
$failures = [System.Collections.Generic.List[string]]::new()
$latencies = [System.Collections.Generic.List[double]]::new()

foreach ($case in $document.cases) {
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $body = $case.input | ConvertTo-Json -Depth 20 -Compress
        $actual = Invoke-RestMethod `
            -Uri "$base/optimize-energy" `
            -Method Post `
            -ContentType "application/json" `
            -Body $body
        $timer.Stop()
        $latencies.Add($timer.Elapsed.TotalSeconds)

        if ($actual.scenario_id -ne $case.id) {
            $failures.Add("$($case.id): scenario_id mismatch")
        }
        if ($actual.directive_interpretation.Count -ne $case.expected_output.directive_interpretation.Count) {
            $failures.Add("$($case.id): directive count mismatch")
        }
        for ($i = 0; $i -lt $case.expected_output.directive_interpretation.Count; $i++) {
            $expectedDirective = $case.expected_output.directive_interpretation[$i]
            $actualDirective = $actual.directive_interpretation[$i]
            if ($actualDirective.note_index -ne $i -or
                $actualDirective.directive_type -ne $expectedDirective.directive_type -or
                $actualDirective.applies -ne $expectedDirective.applies) {
                $failures.Add("$($case.id): directive #$i meaning mismatch")
            }
            if ($expectedDirective.applies) {
                $expectedAdjustment = $expectedDirective.structured_adjustment
                $actualAdjustment = $actualDirective.structured_adjustment
                if ($null -eq $actualAdjustment) {
                    $failures.Add("$($case.id): directive #$i adjustment is missing")
                }
                else {
                    $expectedHours = @($expectedAdjustment.hours) -join ","
                    $actualHours = @($actualAdjustment.hours) -join ","
                    if ($expectedHours -ne $actualHours) {
                        $failures.Add("$($case.id): directive #$i hours mismatch")
                    }
                    foreach ($property in @("factor", "minimum_energy_kwh", "max_grid_kwh")) {
                        $expectedProperty = $expectedAdjustment.PSObject.Properties[$property]
                        if ($null -ne $expectedProperty -and $null -ne $expectedProperty.Value) {
                            $actualProperty = $actualAdjustment.PSObject.Properties[$property]
                            if ($null -eq $actualProperty -or $null -eq $actualProperty.Value -or
                                [Math]::Abs([double]$actualProperty.Value - [double]$expectedProperty.Value) -gt 0.01) {
                                $failures.Add("$($case.id): directive #$i $property mismatch")
                            }
                        }
                    }
                }
            }
        }
        if ($actual.hourly_plan.Count -ne 24) {
            $failures.Add("$($case.id): hourly_plan does not contain 24 entries")
        }
        $costDifference = [Math]::Abs(
            [double]$actual.total_cost_bdt - [double]$case.expected_output.total_cost_bdt)
        if ($costDifference -gt 0.01) {
            $failures.Add("$($case.id): cost differs by $costDifference BDT")
        }

        Write-Host ("PASS {0}  {1:N2}s  {2:N2} BDT" -f `
            $case.id, $timer.Elapsed.TotalSeconds, $actual.total_cost_bdt)
    }
    catch {
        $timer.Stop()
        $failures.Add("$($case.id): $($_.Exception.Message)")
        Write-Host "FAIL $($case.id)  $($_.Exception.Message)" -ForegroundColor Red
    }

    if ($DelaySeconds -gt 0) {
        Write-Host "Waiting $DelaySeconds seconds for provider rate limit..."
        Start-Sleep -Seconds $DelaySeconds
    }
}

if ($latencies.Count -gt 0) {
    $ordered = $latencies | Sort-Object
    $p95Index = [Math]::Max(0, [Math]::Ceiling($ordered.Count * 0.95) - 1)
    Write-Host ("Observed p95 latency: {0:N2}s" -f $ordered[$p95Index])
}

if ($failures.Count -gt 0) {
    Write-Host "Live verification failures:" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "- $_" -ForegroundColor Red }
    exit 1
}

Write-Host "All public cases passed through the running model-backed API." -ForegroundColor Green

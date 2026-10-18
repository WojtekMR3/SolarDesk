param(
    [string]$Executable = (Join-Path $PSScriptRoot 'dist\SolarDesk\SolarDesk.exe'),
    [ValidateRange(15, 600)][int]$Seconds = 60,
    [string]$Output = (Join-Path $PSScriptRoot 'artifacts\performance.json')
)
$ErrorActionPreference = 'Stop'
$executablePath = (Resolve-Path -LiteralPath $Executable).Path
$outputPath = [IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($outputPath)) -Force | Out-Null
$arguments = @('--performance-check', '--seconds', $Seconds, '--output', ('"' + $outputPath + '"'))
$run = Start-Process -FilePath $executablePath -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (-not $run.WaitForExit(($Seconds * 3 + 60) * 1000)) {
    $run.Kill()
    throw 'The performance check timed out.'
}
$run.WaitForExit()
if ($run.ExitCode -ne 0) { throw 'The performance check failed.' }
Get-Content -LiteralPath $outputPath
Write-Output "Performance report: $outputPath"

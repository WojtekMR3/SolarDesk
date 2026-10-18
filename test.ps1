param([string]$Executable)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\cli-home'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools\packages'
$localSdk = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$sdk = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
& $sdk run --project (Join-Path $projectRoot 'tests\SolarDesk.Tests\SolarDesk.Tests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
if (-not $Executable) {
    & $sdk build (Join-Path $projectRoot 'src\SolarDesk.App\SolarDesk.App.csproj') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'App build failed.' }
    $Executable = Join-Path $projectRoot 'src\SolarDesk.App\bin\Release\net8.0-windows10.0.19041.0\SolarDesk.exe'
}
$artifacts = Join-Path $projectRoot 'artifacts'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$executablePath = (Resolve-Path -LiteralPath $Executable).Path
$report = Join-Path $artifacts 'ui-smoke.txt'
$preview = Join-Path $artifacts 'preview.png'
$arguments = @('--ui-smoke', '--output', ('"' + $report + '"'), '--render', ('"' + $preview + '"'))
$run = Start-Process -FilePath $executablePath -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (-not $run.WaitForExit(180000)) {
    $run.Kill()
    throw 'UI smoke tests timed out after three minutes.'
}
$run.WaitForExit()
if ($run.ExitCode -ne 0) { throw 'UI smoke tests failed.' }
Get-Content -LiteralPath $report
Write-Output "UI preview: $preview"

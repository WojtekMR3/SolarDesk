param([switch]$FrameworkDependent, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\cli-home'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools\packages'
$localSdk = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$sdk = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
$appProject = Join-Path $projectRoot 'src\SolarDesk.App\SolarDesk.App.csproj'
$output = if ($OutputDirectory) { $OutputDirectory } else { Join-Path $projectRoot 'dist\SolarDesk' }
if ($FrameworkDependent) {
    & $sdk publish $appProject -c Release -o $output --self-contained false
} else {
    # The download ZIP supplies compression. Keep assemblies directly loadable in
    # the executable instead of decompressing them into private memory at startup.
    & $sdk publish $appProject -c Release -r win-x64 -o $output --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=false -p:DebugType=None -p:DebugSymbols=false
}
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $output
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $output
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Destination $output
Copy-Item -LiteralPath (Join-Path $projectRoot 'CHANGELOG.md') -Destination $output
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $output -Recurse -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'licenses') -Destination $output -Recurse -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'src\SolarDesk.Core\Data\NOTICE.txt') -Destination (Join-Path $output 'GeoNames-NOTICE.txt')
if (-not $FrameworkDependent) {
    # Use the runtime versions resolved for this publish, not whichever cached package is newest.
    $assets = Get-Content -Raw -LiteralPath (Join-Path $projectRoot 'src\SolarDesk.App\obj\project.assets.json') | ConvertFrom-Json
    $downloads = @($assets.project.frameworks.PSObject.Properties | ForEach-Object { $_.Value.downloadDependencies })
    $licenseDirectory = Join-Path $output 'licenses'
    New-Item -ItemType Directory -Path $licenseDirectory -Force | Out-Null
    $runtimeNotices = @(
        @{ Package = 'Microsoft.NETCore.App.Runtime.win-x64'; Source = 'LICENSE.TXT'; Target = 'dotnet-LICENSE.txt' },
        @{ Package = 'Microsoft.NETCore.App.Runtime.win-x64'; Source = 'THIRD-PARTY-NOTICES.TXT'; Target = 'dotnet-THIRD-PARTY-NOTICES.txt' },
        @{ Package = 'Microsoft.WindowsDesktop.App.Runtime.win-x64'; Source = 'LICENSE'; Target = 'windowsdesktop-LICENSE.txt' }
    )
    foreach ($notice in $runtimeNotices) {
        $dependency = @($downloads | Where-Object { $_.name -eq $notice.Package })
        if ($dependency.Count -ne 1) { throw "Cannot resolve runtime license for $($notice.Package)." }
        $version = ($dependency[0].version -replace '[\[\]]', '').Split(',')[0].Trim()
        $packageDirectory = Join-Path $env:NUGET_PACKAGES ($notice.Package.ToLowerInvariant() + '\' + $version)
        Copy-Item -LiteralPath (Join-Path $packageDirectory $notice.Source) -Destination (Join-Path $licenseDirectory $notice.Target)
    }
}
Write-Output "Built: $output\SolarDesk.exe"

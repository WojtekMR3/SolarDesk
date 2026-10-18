param([string]$OutputDirectory, [switch]$IncludeUi)
$ErrorActionPreference = 'Stop'
if (-not $IncludeUi -and $env:GITHUB_ACTIONS -ne 'true') {
    throw 'Release packaging includes visible UI tests. Run it in GitHub Actions, or use -IncludeUi on a dedicated test desktop.'
}
$projectRoot = $PSScriptRoot
[xml]$project = Get-Content -Raw -LiteralPath (Join-Path $projectRoot 'src\SolarDesk.App\SolarDesk.App.csproj')
$version = [string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected a three-part release version.' }
$packageName = "SunsetMonitor-$version-win-x64"
$releaseDirectory = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $projectRoot 'artifacts\releases' }
New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
$archivePath = Join-Path $releaseDirectory "$packageName.zip"
if (Test-Path -LiteralPath $archivePath) { throw "Package already exists: $archivePath. Choose a fresh output directory or version." }

# A fresh staging folder prevents local settings or old binaries from entering a release.
$stagingRoot = Join-Path $projectRoot ('artifacts\package-' + [Guid]::NewGuid().ToString('N'))
$applicationDirectory = Join-Path $stagingRoot 'SunsetMonitor'
& (Join-Path $projectRoot 'build.ps1') -OutputDirectory $applicationDirectory
& (Join-Path $projectRoot 'test.ps1') -Executable (Join-Path $applicationDirectory 'SunsetMonitor.exe') -IncludeUi

# Bundle screenshots rendered by the tested build, so documentation uses the current branding.
foreach ($theme in @('day', 'night')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot "artifacts\theme-$theme.png") -Destination (Join-Path $applicationDirectory "docs\screenshots\$theme.png") -Force
}

# The public README points to the last published release until the new upload is ready.
# Documentation inside the new ZIP must point to its own version.
$bundledReadme = Join-Path $applicationDirectory 'README.md'
$readme = [IO.File]::ReadAllText($bundledReadme)
$readme = $readme.Replace('github.com/WojtekMR3/SolarDesk/', 'github.com/WojtekMR3/SunsetMonitor/')
$readme = [regex]::Replace($readme, '/releases/download/v\d+\.\d+\.\d+/(?:SolarDesk|SunsetMonitor)-\d+\.\d+\.\d+-win-x64\.zip', "/releases/download/v$version/SunsetMonitor-$version-win-x64.zip")
$readme = [regex]::Replace($readme, 'Version \d+\.\d+\.\d+', "Version $version")
[IO.File]::WriteAllText($bundledReadme, $readme, (New-Object Text.UTF8Encoding($false)))

$expected = @('SunsetMonitor.exe', 'README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'CHANGELOG.md', 'GeoNames-NOTICE.txt', 'docs', 'licenses')
$unexpected = @(Get-ChildItem -LiteralPath $applicationDirectory -Force | Where-Object { $_.Name -notin $expected })
if ($unexpected.Count -gt 0) { throw "Unexpected release contents: $($unexpected.Name -join ', ')" }
foreach ($name in $expected) {
    if (-not (Test-Path -LiteralPath (Join-Path $applicationDirectory $name))) { throw "Missing release file: $name" }
}
Compress-Archive -LiteralPath $applicationDirectory -DestinationPath $archivePath -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$archivePath.sha256" -Value "$hash  $packageName.zip" -Encoding ascii
Write-Output "Release package: $archivePath"
Write-Output "SHA-256: $hash"

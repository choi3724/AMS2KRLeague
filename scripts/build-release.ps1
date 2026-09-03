param(
    [string]$DotnetExecutable = 'dotnet',
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:\.[0-9A-Za-z]+)*)?$')]
    [string]$Version = '0.2.3-beta.3',
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:\.[0-9A-Za-z]+)*)?$')]
    [string]$DisplayVersion = '0.2.3-beta.3',
    [string]$IsccExecutable = ''
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$artifactsRoot = Join-Path $projectRoot 'artifacts'
$publishRoot = Join-Path $artifactsRoot "AMS2-League-Overlay-$DisplayVersion-win-x64"
$zipPath = "$publishRoot.zip"
$installerPath = Join-Path $artifactsRoot "AMS2-League-Overlay-$DisplayVersion-Setup.exe"
$checksumPath = Join-Path $artifactsRoot "SHA256SUMS-$DisplayVersion.txt"
$manifestPath = Join-Path $artifactsRoot "release-manifest-$DisplayVersion.json"
$solutionPath = Join-Path $projectRoot 'AMS2KRLeague.sln'
$clientProject = Join-Path $projectRoot 'src\AMS2LeagueClient\AMS2LeagueClient.csproj'
$telemetryTests = Join-Path $projectRoot 'tests\AMS2LeagueClient.Tests\AMS2LeagueClient.Tests.csproj'
$activityTests = Join-Path $projectRoot 'tests\AMS2LeagueActivity.Tests\AMS2LeagueActivity.Tests.csproj'
$auditScript = Join-Path $PSScriptRoot 'Test-PublicPackage.ps1'
$versionProps = [xml](Get-Content -Raw -LiteralPath (Join-Path $projectRoot 'Directory.Build.props'))
$declaredVersion = [string]$versionProps.Project.PropertyGroup.Version
$declaredDisplayVersion = [string]$versionProps.Project.PropertyGroup.InformationalVersion
$numericVersion = ($Version -split '-', 2)[0]

if ($declaredVersion -ne $Version) {
    throw "Requested release version $Version does not match Directory.Build.props version $declaredVersion."
}
if ($declaredDisplayVersion -ne $DisplayVersion) {
    throw "Requested display version $DisplayVersion does not match InformationalVersion $declaredDisplayVersion."
}

foreach ($path in @($publishRoot, $zipPath, $installerPath, $checksumPath, $manifestPath)) {
    if (Test-Path -LiteralPath $path) {
        throw "Release output already exists. Use a clean artifact path: $path"
    }
}

New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null
& $DotnetExecutable restore $solutionPath
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $DotnetExecutable build $solutionPath -c Release --no-restore -p:Version=$Version
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $DotnetExecutable run --project $telemetryTests -c Release --no-build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $DotnetExecutable run --project $activityTests -c Release --no-build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $DotnetExecutable restore $clientProject -r win-x64 --ignore-failed-sources
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $DotnetExecutable publish $clientProject `
    -c Release `
    -r win-x64 `
    --self-contained true `
    --no-restore `
    -t:Rebuild `
    -p:Version=$Version `
    -p:DebugSymbols=false `
    -p:DebugType=None `
    -o $publishRoot
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Copy-Item -LiteralPath (Join-Path $projectRoot 'release\README_KO.txt') -Destination (Join-Path $publishRoot 'README_KO.txt')
Copy-Item -LiteralPath (Join-Path $projectRoot 'release\RELEASE_NOTES_KO.md') -Destination (Join-Path $publishRoot 'RELEASE_NOTES_KO.md')
Get-ChildItem -LiteralPath $publishRoot -Recurse -File -Filter '*.pdb' | Remove-Item -Force

& $auditScript -PackagePath $publishRoot
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Compress-Archive -Path (Join-Path $publishRoot '*') -DestinationPath $zipPath -CompressionLevel Optimal
& $auditScript -PackagePath $zipPath
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if ([string]::IsNullOrWhiteSpace($IsccExecutable)) {
    $knownIscc = @(
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe',
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if ($knownIscc) { $IsccExecutable = $knownIscc }
}

if (-not [string]::IsNullOrWhiteSpace($IsccExecutable) -and (Test-Path -LiteralPath $IsccExecutable)) {
    & $IsccExecutable `
        "/DSourceDir=$publishRoot" `
        "/DOutputDir=$artifactsRoot" `
        "/DAppVersion=$DisplayVersion" `
        (Join-Path $projectRoot 'installer\AMS2LeagueOverlay.iss')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $auditScript -PackagePath $installerPath
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$assets = @($zipPath)
if (Test-Path -LiteralPath $installerPath) { $assets += $installerPath }
$checksumLines = foreach ($asset in $assets) {
    $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $asset
    "$($hash.Hash.ToLowerInvariant()) *$(Split-Path -Leaf $asset)"
}
$utf8NoBom = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllLines($checksumPath, [string[]]$checksumLines, $utf8NoBom)

$manifestAssets = foreach ($asset in $assets) {
    $item = Get-Item -LiteralPath $asset
    [ordered]@{
        filename = $item.Name
        size = $item.Length
        sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $asset).Hash.ToLowerInvariant()
    }
}
$manifest = [ordered]@{
    product = 'AMS2 League Overlay'
    version = $DisplayVersion
    fileVersion = "$numericVersion.0"
    releaseChannel = $(if ($DisplayVersion.Contains('-')) { 'closed-beta' } else { 'stable' })
    prerelease = $DisplayVersion.Contains('-')
    platform = 'windows'
    architecture = 'win-x64'
    selfContained = $true
    minimumOs = 'Windows 10'
    serverApiCompatibility = '>=1.6.0/schema15 for compact telemetry; >=1.4.0 for legacy activity and distributed witness'
    assets = $manifestAssets
}
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 5), $utf8NoBom)

Write-Output "Portable: $zipPath"
Write-Output "Installer: $(if (Test-Path -LiteralPath $installerPath) { $installerPath } else { 'NOT BUILT - Inno Setup 6 not found' })"
Write-Output "Checksums: $checksumPath"
Write-Output "Manifest: $manifestPath"

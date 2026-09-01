param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath
)

$ErrorActionPreference = 'Stop'
$resolved = (Resolve-Path -LiteralPath $PackagePath).Path
$temporaryRoot = $null
$scanRoot = $resolved

if ((Get-Item -LiteralPath $resolved).PSIsContainer -eq $false -and [IO.Path]::GetExtension($resolved) -ieq '.zip') {
    $temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("ams2-public-audit-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
    Expand-Archive -LiteralPath $resolved -DestinationPath $temporaryRoot
    $scanRoot = $temporaryRoot
}

try {
    $rootIsDirectory = (Get-Item -LiteralPath $scanRoot).PSIsContainer
    $files = if ($rootIsDirectory) {
        @(Get-ChildItem -LiteralPath $scanRoot -Recurse -File)
    } else {
        @(Get-Item -LiteralPath $scanRoot)
    }

    $forbiddenNames = @(
        'activity-connection.json',
        'pairing-token.dat',
        'config.local.php',
        '.env'
    )
    $forbiddenStrings = @(
        'ENG-IceBlasT',
        'EVT-CANARY',
        'SEASON-CANARY',
        'TIMEATTACK-CANARY',
        'GENERAL-CANARY',
        '--host-recorder',
        'v1/recorder/results',
        'C:\Users\User\',
        'Documents\Codex',
        'files-pasted-by-the-user',
        'work\dotnet8'
    )

    $issues = New-Object System.Collections.Generic.List[string]
    foreach ($file in $files) {
        $relative = if ($rootIsDirectory) {
            $file.FullName.Substring($scanRoot.Length).TrimStart('\')
        } else {
            $file.Name
        }
        if ($file.Extension -ieq '.pdb') { $issues.Add("DEBUG_SYMBOL:$relative") }
        if ($forbiddenNames -contains $file.Name) { $issues.Add("PRIVATE_FILE:$relative") }
        if ($relative -match '(^|\\)(tests?|fixtures?|captures?|logs?|activity|server|host|private)(\\|$)') {
            $issues.Add("FORBIDDEN_PATH:$relative")
        }

        [byte[]]$bytes = [IO.File]::ReadAllBytes($file.FullName)
        [string]$hex = [Convert]::ToHexString($bytes)
        foreach ($needle in $forbiddenStrings) {
            $asciiHex = [Convert]::ToHexString([Text.Encoding]::UTF8.GetBytes($needle))
            $unicodeHex = [Convert]::ToHexString([Text.Encoding]::Unicode.GetBytes($needle))
            if ($hex.IndexOf($asciiHex, [StringComparison]::OrdinalIgnoreCase) -ge 0 `
                -or $hex.IndexOf($unicodeHex, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                $issues.Add("FORBIDDEN_STRING:${needle}:$relative")
            }
        }
    }

    if ($issues.Count -gt 0) {
        $issues | Sort-Object -Unique | ForEach-Object { Write-Error $_ }
        exit 1
    }

    Write-Output "PUBLIC_PACKAGE_AUDIT PASS files=$($files.Count) forbidden=0 path=$resolved"
}
finally {
    if ($temporaryRoot -and (Test-Path -LiteralPath $temporaryRoot)) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}

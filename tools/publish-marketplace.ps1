param(
    [Parameter(Mandatory)][string[]]$Vsix,     # the packed VSIX files, named cantoria-<target>-<version>.vsix
    [int]$TimeoutMinutes = 30,                 # how long one target may take to appear on the Marketplace
    [switch]$DryRun                            # say what would be published, and publish nothing
)
# Publishes VSIX files to the Marketplace ONE AT A TIME, each only after the one before it is listed.
#
# ⚠️ `vsce publish` answering "Published" means the upload was accepted, not that the version is
# listed. Cantoria 0.4.0 sent three targets ten seconds apart: all three said "Published", the first
# was listed ten minutes later, and the other two never were (Lily#'s legs, two minutes apart, all
# were). So the listing is what is waited for, and a target already listed is left alone, which
# also makes this safe to run again after a partial release. VSCE_PAT comes from the environment.
$ErrorActionPreference = 'Stop'

function Test-Listed([string]$Version, [string]$Target) {
    $body = '{"filters":[{"criteria":[{"filterType":7,"value":"yotsuda.cantoria"}],"pageNumber":1,"pageSize":1}],"assetTypes":[],"flags":1}'
    try {
        $r = Invoke-RestMethod -Uri 'https://marketplace.visualstudio.com/_apis/public/gallery/extensionquery' -Method Post `
            -ContentType 'application/json' -Headers @{ Accept = 'application/json;api-version=3.0-preview.1' } -Body $body -TimeoutSec 30
        foreach ($v in @($r.results[0].extensions[0].versions)) {
            if ($v.version -eq $Version -and $v.targetPlatform -eq $Target) { return $true }
        }
    } catch {
        Write-Host "  (the Marketplace did not answer: $($_.Exception.Message))"
    }
    return $false
}

$failed = @()
foreach ($file in $Vsix) {
    $item = Get-Item $file
    if ($item.Name -notmatch '^cantoria-(.+)-(\d+\.\d+\.\d+)\.vsix$') { throw "not a Cantoria VSIX name: $($item.Name)" }
    $target = $Matches[1]
    $version = $Matches[2]
    if (Test-Listed $version $target) { "$version $target is already listed: left alone"; continue }
    if ($DryRun) { "would publish $version $target"; continue }

    "publishing $version $target"
    npx --yes @vscode/vsce publish --packagePath $item.FullName
    if ($LASTEXITCODE -ne 0) { $failed += $target; continue }

    $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
    while (-not (Test-Listed $version $target)) {
        if ((Get-Date) -gt $deadline) { break }
        Start-Sleep -Seconds 30
    }
    if (Test-Listed $version $target) { "$version $target is listed" }
    else { Write-Host "$version $target was accepted but is not listed after $TimeoutMinutes minutes"; $failed += $target }
}
if ($failed) { throw "not listed: $($failed -join ', ')" }
$global:LASTEXITCODE = 0

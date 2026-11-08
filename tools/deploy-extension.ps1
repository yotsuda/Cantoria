param(
    [string]$Code,          # the VS Code CLI (code.cmd); found on the PATH or in the user install when not given
    [switch]$NoPack,        # install the .vsix already in artifacts\extension instead of packing it again
    [switch]$SkipPublish,   # pack, but reuse editors\vscode\bin\win-x64 from the last run (the extension's code changed, not the program)
    [string]$Out = "$PSScriptRoot\..\artifacts\extension"
)
# Deploys Cantilena to THIS PC's VS Code: packs the extension (pack-extension.ps1 — the program
# published self-contained, the extension compiled, `vsce package`) and installs the .vsix over
# any earlier one. Never publishes (that is the owner's `vsce publish`). Lily# finds the engine
# through the extension unless lilysharp.playback.singer names a program, which wins.
$ErrorActionPreference = 'Stop'

if (-not $NoPack) {
    & "$PSScriptRoot\pack-extension.ps1" -Out $Out -SkipPublish:$SkipPublish
}
$vsix = Get-ChildItem $Out -Filter *.vsix -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1
if (-not $vsix) { throw "no .vsix in ${Out}: run without -NoPack" }

if (-not $Code) {
    $Code = (Get-Command code.cmd -ErrorAction SilentlyContinue).Source
    if (-not $Code) { $Code = "$env:LOCALAPPDATA\Programs\Microsoft VS Code\bin\code.cmd" }
}
if (-not (Test-Path $Code)) { throw "no VS Code CLI at ${Code}: pass -Code <path to code.cmd>" }

# An engine still running from the installed extension holds its folder (cantilena.exe, and
# NEUTRINO's server bound to it): the old install cannot be replaced under it.
$running = Get-Process cantilena -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*yotsuda.cantilena*' }
if ($running) { throw "cantilena.exe is running from the installed extension (pid $($running.Id -join ', ')): close the VS Code windows that sing, or run Cantilena: Forget Rendered Voices and wait, then try again" }

"installing {0} into {1}" -f $vsix.Name, $Code
cmd /d /s /c "`"$Code`" --install-extension `"$($vsix.FullName)`" --force < NUL"
if ($LASTEXITCODE -ne 0) { throw "code --install-extension failed" }
cmd /d /s /c "`"$Code`" --list-extensions --show-versions < NUL" | Select-String '^yotsuda\.' | ForEach-Object { "  $_" }

# What Lily# will use instead of the extension, if anything.
$settings = "$env:APPDATA\Code\User\settings.json"
if (Test-Path $settings) {
    $text = Get-Content $settings -Raw
    if ($text -match '"lilysharp\.playback\.singer"\s*:\s*"([^"]+)"') { Write-Warning "lilysharp.playback.singer = $($Matches[1]): Lily# runs that program instead of the installed extension" }
    if ($text -notmatch '"cantilena\.neutrinoFolder"\s*:\s*"[^"]+"') { "next: Cantilena: Choose NEUTRINO Folder… (the setting cantilena.neutrinoFolder is not set yet), then Cantilena: Check Setup" }
}
foreach ($scope in 'Process', 'User', 'Machine') {
    $v = [Environment]::GetEnvironmentVariable('CANTILENA_NEUTRINO_DIR', $scope)
    if ($v) { "note: CANTILENA_NEUTRINO_DIR ($scope) = ${v} — the extension passes its own cantilena.neutrinoFolder to the engine, which wins over this" }
}
"done: reload the VS Code windows (Developer: Reload Window) to use the new engine"

param(
    [string]$Code,          # the VS Code CLI (code.cmd); found on the PATH or in the user install when not given
    [switch]$NoPack,        # install the win32-x64 .vsix already in artifacts\extension instead of packing it again
    [switch]$SkipPublish,   # pack, but reuse editors\vscode\bin\win-x64 from the last run (the extension's code changed, not the program)
    [string]$Out = "$PSScriptRoot\..\artifacts\extension"
)
# Deploys Cantoria to THIS PC's VS Code, Windows x64, without closing VS Code: packs the win-x64
# target (pack-extension.ps1 -Targets win-x64 — the program published self-contained on the
# DirectML package, the extension compiled, `vsce package --target win32-x64`), stops the engine a
# running VS Code may have started from the installed extension (cantoria.exe and the NEUTRINO
# server bound to it — the extension starts a new one on the next sing; VS Code itself stays up),
# uninstalls the earlier Cantoria and installs the new .vsix. Then one "Developer: Reload Window"
# loads the new extension code. Never publishes (that is the owner's `vsce publish`).
#
# ⚠️ The .vsix is chosen BY NAME — cantoria-win32-x64-<version>.vsix, the version from
# editors\vscode\package.json — never "the newest file": pack-extension.ps1 writes three
# platforms in a row, and on 2026-11-08 the newest was cantoria-darwin-arm64-0.4.0.vsix, which
# this script installed onto Windows (bin\osx-arm64 only, no cantoria.exe, nothing sang).
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\.."

if (-not $NoPack) {
    & "$PSScriptRoot\pack-extension.ps1" -Out $Out -Targets win-x64 -SkipPublish:$SkipPublish
}
$version = (Get-Content "$root\editors\vscode\package.json" -Raw | ConvertFrom-Json).version
$vsix = Get-Item (Join-Path $Out "cantoria-win32-x64-$version.vsix") -ErrorAction SilentlyContinue
if (-not $vsix) { throw "no cantoria-win32-x64-$version.vsix in ${Out}: run without -NoPack" }

if (-not $Code) {
    $Code = (Get-Command code.cmd -ErrorAction SilentlyContinue).Source
    if (-not $Code) { $Code = "$env:LOCALAPPDATA\Programs\Microsoft VS Code\bin\code.cmd" }
}
if (-not (Test-Path $Code)) { throw "no VS Code CLI at ${Code}: pass -Code <path to code.cmd>" }

# The engine a running VS Code started from the installed extension holds its folder
# (cantoria.exe; the ONNX Runtime and DirectML libraries it loaded; NEUTRINO's server, bound to
# its process id, holds nothing of ours but would be orphaned). Stopping them is enough: the
# extension's Engine starts a fresh one on the next render, and VS Code is not touched.
$extensions = "$env:USERPROFILE\.vscode\extensions"
function Stop-InstalledEngine {
    $stopped = @()
    $engines = @(Get-Process cantoria -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith("$extensions\yotsuda.cantoria", [StringComparison]::OrdinalIgnoreCase) })
    foreach ($engine in $engines) {
        foreach ($child in Get-CimInstance Win32_Process -Filter "Name = 'neutrino_server.exe' OR Name = 'neutrino_client.exe' OR Name = 'musicXMLtoLabel.exe'" | Where-Object { $_.ParentProcessId -eq $engine.Id }) {
            Stop-Process -Id $child.ProcessId -Force -ErrorAction SilentlyContinue
            $stopped += "$($child.Name)($($child.ProcessId))"
        }
        Stop-Process -Id $engine.Id -Force -ErrorAction SilentlyContinue
        $stopped += "cantoria.exe($($engine.Id))"
    }
    return $stopped
}
$stopped = Stop-InstalledEngine
if ($stopped) { "stopped the running engine: $($stopped -join ', ') (the extension starts a new one on the next sing)" }

# A stopped process lets go of its files a moment later; the old install must be removable before
# the CLI tries (a locked folder leaves a half-removed extension behind).
$installed = Get-ChildItem $extensions -Directory -Filter 'yotsuda.cantoria-*' -ErrorAction SilentlyContinue
foreach ($dir in $installed) {
    $exe = Join-Path $dir.FullName 'bin\win-x64\cantoria.exe'
    if (-not (Test-Path $exe)) { continue }
    for ($round = 0; $round -lt 20; $round++) {
        try { $s = [IO.File]::Open($exe, 'Open', 'ReadWrite', 'None'); $s.Dispose(); break }
        catch { if ($round -eq 19) { throw "$exe is still held by a process after 10 s: $($_.Exception.Message)" }; Start-Sleep -Milliseconds 500 }
    }
}

# Windows PowerShell turns the CLI's stderr lines (a Node DeprecationWarning on every install) into
# errors under 'Stop'; the exit code is the verdict, so the CLI runs through cmd with stderr merged.
"installing {0} with {1}" -f $vsix.Name, $Code
if ($installed) {
    cmd /d /s /c "`"$Code`" --uninstall-extension yotsuda.cantoria < NUL 2>&1" | Where-Object { $_ -notmatch 'DeprecationWarning|--trace-deprecation' }
}
cmd /d /s /c "`"$Code`" --install-extension `"$($vsix.FullName)`" --force < NUL 2>&1" | Where-Object { $_ -notmatch 'DeprecationWarning|--trace-deprecation' }
if ($LASTEXITCODE -ne 0) { throw "code --install-extension failed ($LASTEXITCODE)" }

# What landed: the folder, its platform, its program.
$dir = Get-ChildItem $extensions -Directory -Filter "yotsuda.cantoria-$version*" | Sort-Object LastWriteTime | Select-Object -Last 1
if (-not $dir) { throw "no yotsuda.cantoria-$version folder under $extensions after the install" }
$manifest = Get-Content (Join-Path $dir.FullName '.vsixmanifest') -Raw
$platform = if ($manifest -match 'TargetPlatform="([^"]+)"') { $Matches[1] } else { '(none)' }
$exe = Join-Path $dir.FullName 'bin\win-x64\cantoria.exe'
if ($platform -ne 'win32-x64' -or -not (Test-Path $exe)) { throw "the installed extension is $($dir.Name) for $platform and $(if (Test-Path $exe) { 'has' } else { 'has no' }) bin\win-x64\cantoria.exe — not the Windows package" }
"installed {0} ({1}): {2}" -f $dir.Name, $platform, (& $exe --version)
if (Test-Path (Join-Path $dir.FullName 'bin\win-x64\DirectML.dll')) { "  DirectML.dll ships: DiffSinger voices run on a DirectX 12 GPU where there is one" }
cmd /d /s /c "`"$Code`" --list-extensions --show-versions < NUL 2>&1" | Select-String '^yotsuda\.' | ForEach-Object { "  $_" }

# What Lily# will use instead of the extension, if anything.
$settings = "$env:APPDATA\Code\User\settings.json"
if (Test-Path $settings) {
    $text = Get-Content $settings -Raw
    if ($text -match '"lilysharp\.playback\.singer"\s*:\s*"([^"]+)"') { Write-Warning "lilysharp.playback.singer = $($Matches[1]): Lily# runs that program instead of the installed extension" }
    if ($text -notmatch '"cantoria\.(neutrinoFolder|voicesFolder)"\s*:\s*"[^"]+"') { "next: Cantoria: Choose NEUTRINO Folder… or Choose Voices Folder… (neither is set yet), then Cantoria: Check Setup" }
}
foreach ($scope in 'Process', 'User', 'Machine') {
    $v = [Environment]::GetEnvironmentVariable('CANTORIA_NEUTRINO_DIR', $scope)
    if ($v) { "note: CANTORIA_NEUTRINO_DIR ($scope) = ${v} — the extension passes its own cantoria.neutrinoFolder to the engine, which wins over this" }
}
"done: in each VS Code window, Developer: Reload Window loads the new extension (VS Code itself need not be closed)"

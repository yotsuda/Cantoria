param(
    [string]$Out = "$PSScriptRoot\..\artifacts\extension",   # where the .vsix goes
    [switch]$SkipPublish                                     # reuse editors\vscode\bin from the last run
)
# Cantilena as a VS Code extension (editors\vscode): the `cantilena` program published
# self-contained for win-x64 into editors\vscode\bin\win-x64, the extension compiled, and
# `vsce package --target win32-x64`. Packages only; publishing to the Marketplace is the owner's
# (`vsce publish --packagePath <vsix>` with the publisher's PAT).
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\.."
$ext = "$root\editors\vscode"
$bin = "$ext\bin\win-x64"

if (-not $SkipPublish) {
    if (Test-Path $bin) { Remove-Item -Recurse -Force $bin }
    cmd /d /s /c "dotnet publish `"$root\src\Cantilena.Cli`" -c Release -r win-x64 --self-contained -o `"$bin`" -v q -nologo < NUL"
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
    Get-ChildItem $bin -Filter *.pdb | Remove-Item
}
if (-not (Test-Path "$bin\cantilena.exe")) { throw "$bin\cantilena.exe is missing" }

# The extension's version is the program's (0.1.0 of 0.1.0-alpha): one number for one release.
$programVersion = ((& "$bin\cantilena.exe" --version) -replace '^cantilena\s+', '').Trim()
$pkg = Get-Content "$ext\package.json" -Raw | ConvertFrom-Json
if (-not $programVersion.StartsWith($pkg.version)) { throw "editors/vscode/package.json is $($pkg.version), the program ${programVersion}: make them one" }
$size = (Get-ChildItem $bin -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
"program {0}, bin {1:F1} MB" -f $programVersion, $size

Push-Location $ext
try {
    if (-not (Test-Path "$ext\node_modules")) {
        cmd /d /s /c "npm install < NUL"
        if ($LASTEXITCODE -ne 0) { throw "npm install failed" }
    }
    cmd /d /s /c "npm run check-types < NUL"
    if ($LASTEXITCODE -ne 0) { throw "check-types failed" }
    cmd /d /s /c "node esbuild.js --production < NUL"
    if ($LASTEXITCODE -ne 0) { throw "esbuild failed" }
    New-Item -ItemType Directory -Force $Out | Out-Null
    cmd /d /s /c "npx --yes @vscode/vsce package --target win32-x64 --out `"$Out`" < NUL"
    if ($LASTEXITCODE -ne 0) { throw "vsce package failed" }
} finally { Pop-Location }
Get-ChildItem $Out -Filter *.vsix | ForEach-Object { "{0} {1:F1} MB" -f $_.Name, ($_.Length / 1MB) }

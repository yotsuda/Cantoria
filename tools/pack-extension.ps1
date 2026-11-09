param(
    [string]$Out = "$PSScriptRoot\..\artifacts\extension",   # where the .vsix files go
    [string[]]$Targets = @('win-x64', 'linux-x64', 'osx-arm64'),  # the programs to publish, one VSIX each
    [switch]$SkipPublish                                     # reuse editors\vscode\bin from the last run (one target)
)
# Cantoria as a VS Code extension (editors\vscode): the `cantoria` program published
# self-contained for each target into editors\vscode\bin\<rid>, the extension compiled, and one
# `vsce package --target <platform>` per target, each VSIX carrying its own program only (the
# bin folder is remade per target). Packages only; publishing to the Marketplace is the owner's
# (`vsce publish --packagePath <vsix>` per file, with the publisher's PAT).
#
# win-x64 is built on the DirectML package of ONNX Runtime (roadmap B3): its native library runs
# the CPU provider too, so one program sings on DirectML where there is a DirectX 12 device and
# on the CPU elsewhere (about 21 MB more than the CPU package). linux-x64 and osx-arm64 take the
# CPU package (CoreML is inside it on macOS). Cross-publishing from Windows is enough: no
# trimming, no native AOT. ⚠️ A VSIX packed here carries no file modes, so the extension gives the
# program its execute bit before the first start on Linux and macOS (engine.ts).
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\.."
$ext = "$root\editors\vscode"
$vsceTarget = @{ 'win-x64' = 'win32-x64'; 'linux-x64' = 'linux-x64'; 'osx-arm64' = 'darwin-arm64'; 'linux-arm64' = 'linux-arm64'; 'osx-x64' = 'darwin-x64'; 'win-arm64' = 'win32-arm64' }

# The extension's version is the program's: one number for one release.
$programVersion = ([xml](Get-Content "$root\src\Cantoria.Cli\Cantoria.Cli.csproj" -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$pkg = Get-Content "$ext\package.json" -Raw | ConvertFrom-Json
if ("$programVersion" -ne $pkg.version) { throw "editors/vscode/package.json is $($pkg.version), the program ${programVersion}: make them one" }

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

    foreach ($rid in $Targets) {
        if (-not $vsceTarget.ContainsKey($rid)) { throw "no VS Code platform for $rid (known: $($vsceTarget.Keys -join ', '))" }
        $bin = "$ext\bin\$rid"
        if (-not $SkipPublish) {
            if (Test-Path "$ext\bin") { Remove-Item -Recurse -Force "$ext\bin" }
            $dml = if ($rid -eq 'win-x64') { '-p:UseDirectML=true' } else { '' }
            cmd /d /s /c "dotnet publish `"$root\src\Cantoria.Cli`" -c Release -r $rid --self-contained $dml -o `"$bin`" -v q -nologo < NUL"
            if ($LASTEXITCODE -ne 0) { throw "dotnet publish $rid failed" }
            Get-ChildItem $bin -Filter *.pdb | Remove-Item
            # The licenses of what is bundled: the VSIX is all a user receives, and the packages' copies stay on this machine.
            $packages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { "$HOME\.nuget\packages" }
            $libraries = (Get-Content "$bin\cantoria.deps.json" -Raw | ConvertFrom-Json).libraries.PSObject.Properties.Name
            function Script:PackageDir([string]$id) {
                $found = $libraries | Where-Object { $_ -like "$id/*" } | Select-Object -First 1
                if (-not $found) { throw "$id is not among the program's libraries" }
                "$packages\$($id.ToLowerInvariant())\$($found.Split('/')[1])"
            }
            $runtime = (Get-Content "$bin\cantoria.runtimeconfig.json" -Raw | ConvertFrom-Json).runtimeOptions.includedFrameworks[0].version
            $onnxId = if ($rid -eq 'win-x64') { 'Microsoft.ML.OnnxRuntime.DirectML' } else { 'Microsoft.ML.OnnxRuntime' }
            $copies = @{
                'dotnet-LICENSE.txt'                 = "$packages\microsoft.netcore.app.runtime.$rid\$runtime\LICENSE.TXT"
                'dotnet-THIRD-PARTY-NOTICES.txt'     = "$packages\microsoft.netcore.app.runtime.$rid\$runtime\THIRD-PARTY-NOTICES.TXT"
                'onnxruntime-LICENSE.txt'            = "$(PackageDir $onnxId)\LICENSE"
                'onnxruntime-ThirdPartyNotices.txt'  = "$(PackageDir $onnxId)\ThirdPartyNotices.txt"
                'YamlDotNet-LICENSE.txt'             = "$(PackageDir 'YamlDotNet')\LICENSE.txt"
            }
            if ($rid -eq 'win-x64') {
                # (DirectML reaches the program as a native file, so the program's libraries do not name it: ONNX Runtime's package says which)
                $directMl = [regex]::Match((Get-Content "$(PackageDir $onnxId)\$($onnxId.ToLowerInvariant()).nuspec" -Raw), '<dependency id="Microsoft\.AI\.DirectML" version="\[?([^",\]]+)').Groups[1].Value
                if (-not $directMl) { throw "no DirectML version in the $onnxId package" }
                $copies['DirectML-LICENSE.txt'] = "$packages\microsoft.ai.directml\$directMl\LICENSE.txt"
                $copies['DirectML-ThirdPartyNotices.txt'] = "$packages\microsoft.ai.directml\$directMl\ThirdPartyNotices.txt"
            }
            New-Item -ItemType Directory -Force "$bin\licenses" | Out-Null
            foreach ($name in $copies.Keys) { Copy-Item -LiteralPath $copies[$name] "$bin\licenses\$name" }
        }
        $exe = if ($rid.StartsWith('win')) { 'cantoria.exe' } else { 'cantoria' }
        if (-not (Test-Path "$bin\$exe")) { throw "$bin\$exe is missing" }
        if ($rid -eq 'win-x64' -and -not (Test-Path "$bin\DirectML.dll")) { throw "$bin\DirectML.dll is missing: the DirectML package did not copy it (PlatformTarget?)" }
        $size = (Get-ChildItem $bin -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
        "{0}: program {1}, bin {2:F1} MB" -f $rid, $programVersion, $size
        cmd /d /s /c "npx --yes @vscode/vsce package --target $($vsceTarget[$rid]) --out `"$Out`" < NUL"
        if ($LASTEXITCODE -ne 0) { throw "vsce package $rid failed" }
    }
} finally { Pop-Location }
Get-ChildItem $Out -Filter "*-$programVersion.vsix" | ForEach-Object { "{0} {1:F1} MB" -f $_.Name, ($_.Length / 1MB) }

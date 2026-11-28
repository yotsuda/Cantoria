param(
    [string]$Build = "$PSScriptRoot/../artifacts/native-build",  # CMake's build folder
    [string]$Out                                                 # default native/out/<this machine's rid>
)
# Cantoria's native libraries (native/) built for this machine into native/out/<rid>/, where
# Cantoria.Core's project copies them from into every build and publish for that rid: the Sinsy
# engine (cantoria_sinsy.dll / libcantoria_sinsy.so / libcantoria_sinsy.dylib and Sinsy's Japanese
# dictionary, sinsy-dic/) and WORLD for the UTAU engine (cantoria_world, COPYING.world).
# Needs CMake and a C++ compiler: on Windows, Visual Studio's (found through vswhere and its
# vcvars64.bat, with the CMake and Ninja that come with it). Paths are written with / for cmake
# on every system.
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot/..").Path -replace '\\', '/'
$arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
$os = if ($IsWindows) { 'win' } elseif ($IsMacOS) { 'osx' } else { 'linux' }
$rid = "$os-$arch"
if (-not $Out) { $Out = "$root/native/out/$rid" }
$Out = $Out -replace '\\', '/'
$Build = $Build -replace '\\', '/'

# vcvars64's environment is put back as it was at the end: its Platform=x64 left in a console sends
# every later `dotnet build` there to bin\x64\ (seen 2026-11-28).
$before = [Environment]::GetEnvironmentVariables()
try {
    if ($IsWindows -and -not (Get-Command cl -ErrorAction SilentlyContinue)) {
        $vs = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
        if (-not $vs) { throw "no Visual Studio with the C++ tools (vswhere found none)" }
        foreach ($line in (cmd /d /c "`"$vs\VC\Auxiliary\Build\vcvars64.bat`" >nul && set")) {
            if ($line -match '^([^=]+)=(.*)$') { [Environment]::SetEnvironmentVariable($Matches[1], $Matches[2]) }
        }
    }
    # Ninja where there is one (Visual Studio's comes with vcvars64), else CMake's own choice.
    $generator = if (Get-Command ninja -ErrorAction SilentlyContinue) { @('-G', 'Ninja') } else { @() }
    & cmake -S "$root/native" -B $Build @generator -DCMAKE_BUILD_TYPE=Release
    if ($LASTEXITCODE -ne 0) { throw "cmake configure failed" }
    & cmake --build $Build --config Release
    if ($LASTEXITCODE -ne 0) { throw "cmake build failed" }
    & cmake --install $Build --config Release --prefix $Out
    if ($LASTEXITCODE -ne 0) { throw "cmake install failed" }
} finally {
    foreach ($name in @([Environment]::GetEnvironmentVariables().Keys)) { if (-not $before.Contains($name)) { [Environment]::SetEnvironmentVariable($name, $null) } }
    foreach ($name in $before.Keys) { [Environment]::SetEnvironmentVariable($name, $before[$name]) }
}
Get-ChildItem $Out -Recurse -File | ForEach-Object { "{0} {1:N0}" -f $_.FullName.Substring((Resolve-Path $Out).Path.Length + 1), $_.Length }

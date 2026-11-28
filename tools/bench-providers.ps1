# Copyright 2026 Yoshifumi Tsuda
# Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

<#
.SYNOPSIS
Sings one score on a DiffSinger voice through each provider asked for, and says how long each took:
the first time (the models loaded, and for CoreML compiled) and warm (the same engine, every phrase
sung again). Writes the WAVs and a Markdown table.

.DESCRIPTION
Made for the macOS runner (.github/workflows/mac.yml), where nobody here can listen or measure; it
runs anywhere the program does (on Windows: -Runs 'cpu','directml').

Each run is a name, a provider and, for CoreML, the CANTORIA_COREML pairs: 'cpu', 'directml',
'coreml' (the defaults in OnnxProviders.CoreMLDefaults), or 'coreml:MLComputeUnits=CPUAndGPU;…'.
Each run is its own `cantoria serve`, sent the score and then the score a semitone up, so the second
answer is every phrase sung warm. The `seconds` of each answer is what is reported.

.EXAMPLE
./tools/bench-providers.ps1 -Program out/osx-arm64/cantoria -VoicesDir ~/ds-voices -Voice diffsinger/Qixuan -Out bench -Runs cpu,coreml,'coreml:MLComputeUnits=CPUAndGPU'
#>
param(
    [Parameter(Mandatory)] [string] $Program,
    [Parameter(Mandatory)] [string] $VoicesDir,
    [string] $Voice = 'diffsinger/Qixuan',
    [string] $Score = (Join-Path $PSScriptRoot '..' 'examples' 'yukiyakonko.json'),
    [string[]] $Runs = @('cpu', 'coreml'),
    [int] $Steps = 20,
    [string] $Out = 'bench'
)

$ErrorActionPreference = 'Stop'
$Program = (Resolve-Path $Program).Path
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$Out = (Resolve-Path $Out).Path
$base = Get-Content -Raw $Score | ConvertFrom-Json
$up = Get-Content -Raw $Score | ConvertFrom-Json
foreach ($n in $up.notes) { $n.pitch = [int]$n.pitch + 1 }

function Invoke-Run([string] $run) {
    $provider, $pairs = $run -split ':', 2
    $safe = ($run -replace '[^A-Za-z0-9]+', '-').Trim('-')
    $psi = [System.Diagnostics.ProcessStartInfo]::new($Program)
    foreach ($a in @('serve', '--voices-dir', $VoicesDir, '--provider', $provider, '--steps', "$Steps", '--out-dir', (Join-Path $Out "tmp-$safe"))) {
        $psi.ArgumentList.Add($a)
    }
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $psi.Environment['CANTORIA_COREML'] = if ($pairs) { $pairs } else { '' }
    $psi.Environment['CANTORIA_TRACE'] = '1'          # each model's seconds on stderr
    $p = [System.Diagnostics.Process]::Start($psi)
    $errTask = $p.StandardError.ReadToEndAsync()
    $row = [ordered]@{ run = $run; provider = ''; first = ''; warm = ''; dur = ''; acoustic = ''; vocoder = ''; error = '' }
    try {
        $ready = $p.StandardOutput.ReadLine()
        if (-not $ready) { throw "serve said nothing" }
        $i = 0
        foreach ($s in @($base, $up)) {
            $i++
            $wav = Join-Path $Out "$safe-$i.wav"
            $req = @{ id = "$i"; score = $s; voice = $Voice; output = $wav } | ConvertTo-Json -Depth 10 -Compress
            $p.StandardInput.WriteLine($req)
            $p.StandardInput.Flush()
            # Progress lines ({"progress":…}) come first; the answer is the line with the id.
            do {
                $line = $p.StandardOutput.ReadLine()
                if ($null -eq $line) { throw "serve ended without answering" }
                $answer = $line | ConvertFrom-Json
            } while ($answer.id -ne "$i")
            if ($answer.error) { throw $answer.error }
            if ($answer.provider) { $row.provider = $answer.provider }
            $row[$(if ($i -eq 1) { 'first' } else { 'warm' })] = '{0:0.00}' -f [double]$answer.seconds
        }
        $p.StandardInput.WriteLine('{"command":"shutdown"}')
        $p.StandardInput.Close()
    }
    catch {
        $row.error = "$_"
        try { $p.StandardInput.Close() } catch {}
    }
    if (-not $p.WaitForExit(120000)) { $p.Kill($true) }
    $stderr = $errTask.Result
    Set-Content -LiteralPath (Join-Path $Out "$safe.stderr.txt") -Value $stderr
    # The warm request's seconds per model, summed over its phrases. Each phrase's log gives the
    # seconds since the phrase began: "timing: … in 0.10 s" (duration), "acoustic: … in 17.86 s"
    # (pitch, variance and acoustic since), "vocoder …: … in 19.44 s" (the vocoder since).
    $sums = @{ dur = 0.0; acoustic = 0.0; vocoder = 0.0 }; $last = 0.0; $seen = $false
    foreach ($m in [regex]::Matches($stderr, "trace 2: (timing|acoustic|vocoder)\b[^\n]* in ([0-9.]+) s")) {
        $at = [double]::Parse($m.Groups[2].Value, [cultureinfo]::InvariantCulture)
        switch ($m.Groups[1].Value) {
            'timing' { $sums.dur += $at }
            'acoustic' { $sums.acoustic += $at - $last }
            'vocoder' { $sums.vocoder += $at - $last }
        }
        $last = $at; $seen = $true
    }
    if ($seen) { foreach ($k in 'dur', 'acoustic', 'vocoder') { $row[$k] = '{0:0.00}' -f $sums[$k] } }
    [pscustomobject]$row
}

$rows = foreach ($run in $Runs) { Invoke-Run $run }

$md = @(
    "| run | first (s) | warm (s) | warm: dur | pitch+acoustic | vocoder | ran on | error |",
    "|---|---|---|---|---|---|---|---|"
) + ($rows | ForEach-Object { "| ``$($_.run)`` | $($_.first) | $($_.warm) | $($_.dur) | $($_.acoustic) | $($_.vocoder) | $($_.provider -replace '\|', '/') | $($_.error -replace '\|', '/') |" })
$table = $md -join "`n"
Set-Content -LiteralPath (Join-Path $Out 'bench.md') -Value $table
$table

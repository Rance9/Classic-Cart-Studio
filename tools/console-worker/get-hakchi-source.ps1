<#
Classic Cart Studio console transport worker: the pinned Hakchi2 CE 3.9.3 source tree it compiles against (SOURCE.md). Copyright (C) 2026
Rance9. GPL-3.0-or-later.

  get-hakchi-source.ps1 -Dest DIR                  clones Hakchi2 CE at tag v3.9.3 (commit 25bfcfd) with every submodule at the commit the
                                                   tag records, then verifies the tree
  get-hakchi-source.ps1 -Dest DIR -UseExisting     verifies an existing tree instead (nothing is fetched or changed)
  get-hakchi-source.ps1 -Dest DIR -WriteInputs     after verifying the commits, writes hakchi-inputs.sha256 from the tree (maintainers only)

Verified: the superproject's HEAD, every submodule's gitlink and checked-out commit (exactly the eight below, none nested), no change to a
tracked file, and every file in hakchi-inputs.sha256 (what the worker compiles, links or derives from, their sources and licenses) byte for
byte. Checkouts use core.autocrlf=true, as Git for Windows does by default: the recorded hashes are those of a Windows checkout. Any
difference stops with an error and a non-zero exit.
#>
param([Parameter(Mandatory)][string]$Dest, [switch]$UseExisting, [switch]$WriteInputs)
$ErrorActionPreference = 'Stop'
$Repo = 'https://github.com/TeamShinkansen/Hakchi2-CE.git'; $Commit = '25bfcfd04b8ed12f7d513dc87c2a0f7096d38ebc'
$Subs = [ordered]@{
    'Libraries/CueSharp' = '95c8effb03c959b1ad1382886d03dce984f461f7'; 'Libraries/FelLib' = 'e28ddfed0e2dea29279fb37fa379329329fa6cca'
    'Libraries/LibUsbDotNet' = '91e84390acd83ded475798488740961da9e3307c'; 'Libraries/ProgressODoom' = '834cdfc7e0bb312a5e3e45b78e4a6a1be59d254e'
    'Libraries/SSH.NET' = '32181aeb4ce69e8f07e5e1d585fd592a44017bc1'; 'Libraries/SpineGen' = '6860f74ba3169b20e03fcbba4a446bc7d8edd2c1'
    'Libraries/tiny7z' = 'b529c11a2a0e7be3e0dd7adab359770d1648fb5e'; 'driver/source' = '6d379a32e93e8a8f27e6205cf2c45d26ab98a68d'
}
$Inputs = Join-Path $PSScriptRoot 'hakchi-inputs.sha256'
function G { $o = & git -c core.autocrlf=true -c advice.detachedHead=false @args; if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') failed ($LASTEXITCODE)" }; $o }
function Fail($why) { Write-Host "STOP  $why" -ForegroundColor Red; exit 1 }

if (-not $UseExisting) {
    if ((Test-Path $Dest) -and (Get-ChildItem -Force $Dest | Select-Object -First 1)) { Fail "$Dest exists and is not empty (give a new folder, or -UseExisting to verify it)" }
    Write-Host "clone $Repo at $Commit"
    G clone --quiet --no-checkout -c core.autocrlf=true $Repo $Dest | Out-Null
    G -C $Dest checkout --quiet --detach $Commit | Out-Null
    G -C $Dest submodule update --quiet --init --recursive | Out-Null
}
if (-not (Test-Path (Join-Path $Dest '.git'))) { Fail "$Dest is not a git checkout of Hakchi2 CE" }
$Dest = (Resolve-Path $Dest).Path
# the commits
$head = (G -C $Dest rev-parse HEAD).Trim(); if ($head -ne $Commit) { Fail "HEAD is $head, not $Commit (v3.9.3)" }
$links = @{}; foreach ($l in (G -C $Dest ls-tree -r HEAD)) { if ($l -match '^160000 commit ([0-9a-f]{40})\t(.+)$') { $links[$Matches[2]] = $Matches[1] } }
if (@($links.Keys | Where-Object { -not $Subs.Contains($_) }).Count -or $links.Count -ne $Subs.Count) { Fail "the tag records other submodules: $($links.Keys -join ', ')" }
foreach ($p in $Subs.Keys) {
    if ($links[$p] -ne $Subs[$p]) { Fail "the tag records $p at $($links[$p]), not $($Subs[$p])" }
    $at = (G -C (Join-Path $Dest $p) rev-parse HEAD).Trim(); if ($at -ne $Subs[$p]) { Fail "$p is checked out at $at, not $($Subs[$p])" }
    if ((G -C (Join-Path $Dest $p) submodule status --recursive | Measure-Object).Count) { Fail "$p has nested submodules (none expected)" }
    Write-Host "PASS  $p $($Subs[$p])"
}
$dirty = G -C $Dest status --porcelain --untracked-files=no --ignore-submodules=none
if ($dirty) { Fail "tracked files differ from the commits: $(($dirty | Select-Object -First 5) -join '; ')" }
Write-Host "PASS  Hakchi2 CE $Commit (v3.9.3), eight submodules at the recorded commits, no tracked file changed"

function Sha($rel) { (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $Dest ($rel -replace '/', '\'))).Hash.ToLowerInvariant() }
if ($WriteInputs) {
    $proj = Get-Content -Raw (Join-Path $PSScriptRoot 'Renci.SshNet\Renci.SshNet.csproj')
    $compiled = @([regex]::Matches($proj, 'Include="\$\(SshNetSource\)\\([^"]+)"') | ForEach-Object { 'Libraries/SSH.NET/src/Renci.SshNet/' + ($_.Groups[1].Value -replace '\\', '/') }) + 'Libraries/SSH.NET/src/Renci.SshNet.snk'
    $derived = 'ISystemShell.cs', 'Clovershell/ClovershellConnection.cs', 'Clovershell/ExecConnection.cs', 'SshClient/SshClientWrapper.cs', 'hakchi.cs', 'Libraries/SSH.NET/src/Renci.SshNet/Renci.SshNet.csproj'
    $libusb = @(G -C (Join-Path $Dest 'Libraries/LibUsbDotNet') ls-files stage/LibUsbDotNet | ForEach-Object { "Libraries/LibUsbDotNet/$_" })
    $licenses = 'LICENSE', 'Licenses/SSH.NET.txt', 'Licenses/LibUsbDotNet.txt', 'Libraries/SSH.NET/LICENSE', 'Libraries/LibUsbDotNet/LICENSE', 'Libraries/LibUsbDotNet/README.md'
    $out = @('# Hakchi2 CE 3.9.3 (commit 25bfcfd04b8ed12f7d513dc87c2a0f7096d38ebc) files the console worker compiles, links or derives from, with',
             '# their sources and licenses; verified byte for byte by get-hakchi-source.ps1 (a Windows checkout, core.autocrlf=true) and copied',
             '# into console-worker-source.zip by build-worker.ps1. Sections: compiled, derived, libusbdotnet, licenses.')
    foreach ($sec in @(@('compiled', $compiled), @('derived', $derived), @('libusbdotnet', $libusb), @('licenses', $licenses))) {
        $out += "# $($sec[0])"; foreach ($rel in ($sec[1] | Sort-Object -Unique)) { $out += "$(Sha $rel)  $rel" }
    }
    [IO.File]::WriteAllText($Inputs, (($out -join "`n") + "`n"), (New-Object Text.UTF8Encoding $false))
    Write-Host "wrote $Inputs ($(@($out | Where-Object { $_ -notmatch '^#' }).Count) files)"
}
if (-not (Test-Path $Inputs)) { Fail "hakchi-inputs.sha256 is missing beside this script" }
$n = 0; $bad = @()
foreach ($l in (Get-Content $Inputs)) {
    if ($l -match '^#' -or -not $l.Trim()) { continue }
    if ($l -notmatch '^([0-9a-f]{64})  ([A-Za-z0-9_./ +-]+)$') { Fail "hakchi-inputs.sha256: unreadable line: $l" }
    $want = $Matches[1]; $rel = $Matches[2]; $n++
    if ($rel -match '(^|/)\.\.?(/|$)') { Fail "hakchi-inputs.sha256: not a plain path: $rel" }
    $f = Join-Path $Dest ($rel -replace '/', '\')
    if (-not (Test-Path -LiteralPath $f -PathType Leaf)) { $bad += "missing $rel"; continue }
    if ((Sha $rel) -ne $want) { $bad += "differs $rel" }
}
if ($bad) { Fail "$($bad.Count) of $n input files are not the recorded bytes: $(($bad | Select-Object -First 5) -join '; ')" }
Write-Host "PASS  $n input files byte for byte (hakchi-inputs.sha256)"
Write-Host "HAKCHI SOURCE OK: $Dest"
exit 0

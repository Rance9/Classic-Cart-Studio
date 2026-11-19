# CCS Console Workbench: the managed program the single-file host carries (its payload), built the ONE way both release workflows build it
# (.github/workflows/release.yml for Classic Cart Studio's installer, tools/console-backup/branch-root/workbench-release.yml for the standalone
# release) and the local release preflight mirrors, so the program Studio installs and the one released on its own are the same bytes.
# Copyright (C) 2026 Rance9. GPL-3.0-or-later.
#   pwsh tools/console-backup/build_workbench_payload.ps1 -RamImages <the console programs folder, with the CCS HOME package in ccs-home>
# The engine libraries carry the repository's VERSION (Classic Cart Studio's) as their assembly version, as they always have; the Workbench's own
# version and channel are tools/console-backup/WORKBENCH-VERSION and WORKBENCH-CHANNEL, passed explicitly (-p:CcsWorkbenchVersion,
# -p:CcsWorkbenchChannel: this program's own version information; build_host.sh --version/--channel for the native host). The build runs in
# tools/console-backup so that folder's global.json pins the .NET SDK. Prints the program's path, size and SHA-256 at the end.
param([Parameter(Mandatory)][string]$RamImages)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$ram = (Resolve-Path $RamImages).Path
# the five console programs and the CCS HOME package are all embedded in the engine: a payload without one of them is a different program
foreach ($d in 'ccs-backup', 'ccs-restore', 'ccs-prepare', 'ccs-rescue', 'ccs-files', 'ccs-home') {
  if (-not (Test-Path (Join-Path $ram $d) -PathType Container)) { throw "the console programs folder has no $d (tools/console-backup/ram/build_images.py, then tools/console-nucleus/build_home_package.py)" }
}
$v = (Get-Content (Join-Path $root 'VERSION') -Raw).Trim()
if ($v -notmatch '^\d+\.\d+\.\d+$') { throw "VERSION is not N.N.N: $v" }
$wv = (Get-Content (Join-Path $root 'tools\console-backup\WORKBENCH-VERSION') -Raw).Trim(); $wc = (Get-Content (Join-Path $root 'tools\console-backup\WORKBENCH-CHANNEL') -Raw).Trim()
if ($wv -notmatch '^\d+\.\d+\.\d+$' -or $wc -notin 'preview', 'production') { throw "WORKBENCH-VERSION ($wv) or WORKBENCH-CHANNEL ($wc) is not usable" }
Push-Location (Join-Path $root 'tools\console-backup')
try {
  dotnet build ccs-backup-utility/ccs-backup-utility.csproj -c Release -nologo "-p:Version=$v" "-p:CcsWorkbenchVersion=$wv" "-p:CcsWorkbenchChannel=$wc" "-p:CcsRamImages=$ram"
  if ($LASTEXITCODE -ne 0) { throw 'the managed Workbench did not build' }
} finally { Pop-Location }
$exe = Join-Path $root 'build\console-backup\bin\ccs-backup-utility\Release\net48\CCS-Console-Workbench.exe'
if (-not (Test-Path $exe)) { throw "no managed Workbench at $exe" }
"payload: $exe $((Get-Item $exe).Length) bytes sha256 $((Get-FileHash -Algorithm SHA256 $exe).Hash.ToLower()) (engine version $v; Workbench $wv $wc)"

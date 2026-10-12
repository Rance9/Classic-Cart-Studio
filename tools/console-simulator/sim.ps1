<#
.SYNOPSIS
  Classic Cart Studio Console Simulator: the single developer launcher (Windows PowerShell 7+).
.DESCRIPTION
  Commands
    setup           audit and install the no-root toolchain inside WSL (zig, qemu-arm, armhf libc); reports owner actions if WSL is missing
    doctor [--json] non-destructive environment validation (Windows + WSL sides)
    build-host      build the Windows and the x86-64 Linux cores
    build-arm       cross-compile the ARM cores (static musl, dynamic glibc)
    build           everything
    run-simulator   interactive diagnostic viewer (keyboard / gamepad); add --script <file> for a headless scripted run
    run-arm-smoke   run the real ARM binary under qemu-arm with a scripted session; logs + exit status + summary.json
    test [suite]    run the suites (core | arm | env | supervisor | all); --path windows|linux|arm for the core suites
    test-faults     deterministic failure injection (fault + supervisor scenarios)
    supervise       one supervisor scenario (--scenario normal|startup-fail|startup-hang|crash-mid|crash-loop|power-loss|malformed|game-fail)
    fixtures        regenerate the synthetic fixtures
    project         developer-only: Classic Cart Studio manifest -> simulator fixture (sim.ps1 project <manifest.json> <out.json>)
    clean           remove generated artifacts (--artifacts | --builds | --toolchain | --all); never touches app data, ROMs or Hakchi
  Everything generated lives under C:\NES\work\console-simulator (builds, artifacts) and ~/.cache/ccs-sim/toolchain inside WSL.
#>
param([Parameter(Position = 0)][string]$Command = "help", [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest)
$ErrorActionPreference = "Stop"
$Sim = $PSScriptRoot; $Distro = if ($env:CCS_SIM_DISTRO) { $env:CCS_SIM_DISTRO } else { "Ubuntu" }
$Work = if ($env:CCS_SIM_WORK) { $env:CCS_SIM_WORK } else { "C:\NES\work\console-simulator" }
$Build = Join-Path $Work "build"; $Artifacts = Join-Path $Work "artifacts"
$SimWsl = "/mnt/" + $Sim.Substring(0, 1).ToLower() + $Sim.Substring(2).Replace("\", "/")
function Wsl([string]$script, [string[]]$args = @()) {
  $out = & wsl.exe -d $Distro -- bash "$SimWsl/scripts/$script" @args 2>&1 | ForEach-Object { "$_" -replace "`0", "" } | Where-Object { $_ -notmatch "kernelCommandLine" }
  $code = $LASTEXITCODE; $out | ForEach-Object { Write-Output $_ }; return $code
}
function WslOk { try { $o = & wsl.exe -l -q 2>$null | ForEach-Object { "$_" -replace "`0", "" }; return ($o -join "`n") -match $Distro } catch { return $false } }
function Has([string]$a) { return $Rest -contains $a }
function ArgAfter([string]$a, [string]$default) { $i = [array]::IndexOf($Rest, $a); if ($i -ge 0 -and $i + 1 -lt $Rest.Count) { return $Rest[$i + 1] } return $default }
New-Item -ItemType Directory -Force -Path $Artifacts | Out-Null

switch ($Command) {
  "help" { Get-Help $PSCommandPath -Detailed | Out-String | Write-Output }
  "doctor" {
    $win = [ordered]@{ os = (Get-CimInstance Win32_OperatingSystem).Caption; build = [Environment]::OSVersion.Version.Build; python = (& python --version 2>&1 | Out-String).Trim(); tkinter = $null; pillow = $null; elevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator); hypervisor = (Get-CimInstance Win32_ComputerSystem).HypervisorPresent; wsl_exe = [bool](Get-Command wsl.exe -ErrorAction SilentlyContinue); builds = [ordered]@{}; problems = @() }
    try { $win.tkinter = (& python -c "import tkinter;print(tkinter.TkVersion)" 2>&1 | Out-String).Trim() } catch { $win.problems += "tkinter missing: install Python with tcl/tk" }
    try { $win.pillow = (& python -c "import PIL;print(PIL.__version__)" 2>&1 | Out-String).Trim() } catch { $win.problems += "Pillow missing: pip install --user pillow" }
    foreach ($t in "windows-x86_64", "linux-x86_64", "arm-musl", "arm-glibc") { $exe = if ($t -like "windows*") { "ccs-sim-core.exe" } else { "ccs-sim-core" }; $win.builds[$t] = Test-Path (Join-Path $Build "$t\$exe") }
    $wsl = [ordered]@{ available = (WslOk); distro = $Distro; report = $null }
    if ($wsl.available) { $raw = & wsl.exe -d $Distro -- bash "$SimWsl/scripts/wsl_doctor.sh" 2>&1 | ForEach-Object { "$_" -replace "`0", "" } | Where-Object { $_ -notmatch "kernelCommandLine" }; try { $wsl.report = ($raw -join "`n") | ConvertFrom-Json } catch { $wsl.report = @{ raw = ($raw -join "`n") } } }
    else { $win.problems += "WSL distribution '$Distro' not found. Owner action (elevated, may need a restart): wsl --install -d Ubuntu. The Windows harness and the Windows core still run without it; the ARM track needs it." }
    $doc = [ordered]@{ schema = "ccs-sim-doctor-win/1"; windows = $win; wsl = $wsl; work = $Work }
    if (Has "--json") { $doc | ConvertTo-Json -Depth 6 } else { $doc | ConvertTo-Json -Depth 6 | Write-Output; if ($win.problems.Count) { Write-Output "PROBLEMS:"; $win.problems | ForEach-Object { Write-Output "  - $_" } } else { Write-Output "doctor: no problems on the Windows side" } }
  }
  "setup" {
    if (-not (WslOk)) { Write-Output "WSL distribution '$Distro' is not installed. This step needs an elevated PowerShell and possibly a restart:`n   wsl --install -d Ubuntu`nNothing was changed. The Windows-side harness works without it; the ARM track needs it."; exit 2 }
    $code = Wsl "wsl_setup.sh"; if ($code -ne 0) { Write-Output "setup failed ($code)"; exit $code }
  }
  "build-host" { exit (Wsl "wsl_build.sh" @("linux-x86_64 windows-x86_64")) }
  "build-arm" { exit (Wsl "wsl_build.sh" @("arm-musl arm-glibc")) }
  "build" { exit (Wsl "wsl_build.sh" @("all")) }
  "run-simulator" { & python (Join-Path $Sim "host\viewer.py") @Rest; exit $LASTEXITCODE }
  "run-arm-smoke" { exit (Wsl "wsl_arm_smoke.sh") }
  "test" { $suite = if ($Rest.Count -and -not $Rest[0].StartsWith("--")) { $Rest[0] } else { "all" }; $p = ArgAfter "--path" "windows"; & python (Join-Path $Sim "tests\run_tests.py") $suite --path $p; exit $LASTEXITCODE }
  "test-faults" { $p = ArgAfter "--path" "windows"; & python (Join-Path $Sim "tests\run_tests.py") faults --path $p; exit $LASTEXITCODE }
  "supervise" { $p = ArgAfter "--path" "windows"; $s = ArgAfter "--scenario" "normal"; & python (Join-Path $Sim "host\supervisor.py") --path $p --scenario $s; exit $LASTEXITCODE }
  "fixtures" { & python (Join-Path $Sim "fixtures\make_fixtures.py"); exit $LASTEXITCODE }
  "project" { & python (Join-Path $Sim "adapter\ccs_projection.py") @Rest; exit $LASTEXITCODE }
  "clean" {
    $all = Has "--all"; $did = @()
    if ($all -or (Has "--artifacts")) { if (Test-Path $Artifacts) { Remove-Item -Recurse -Force $Artifacts }; New-Item -ItemType Directory -Force -Path $Artifacts | Out-Null; $did += "artifacts ($Artifacts)" }
    if ($all -or (Has "--builds")) { if (Test-Path $Build) { Remove-Item -Recurse -Force $Build }; $did += "builds ($Build)" }
    if ($all -or (Has "--toolchain")) { if (WslOk) { & wsl.exe -d $Distro -- bash -lc 'rm -rf "$HOME/.cache/ccs-sim/toolchain"' | Out-Null; $did += "toolchain (~/.cache/ccs-sim/toolchain in WSL)" } }
    if (-not $did) { Write-Output "clean: choose --artifacts, --builds, --toolchain or --all (nothing removed)" } else { Write-Output ("clean removed: " + ($did -join ", ") + ". App data, collections, ROMs and the Hakchi tree were not touched.") }
  }
  default { Write-Output "unknown command '$Command' (sim.ps1 help)"; exit 64 }
}

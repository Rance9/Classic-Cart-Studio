<#
Classic Cart Studio console transport worker: the release build (README.md, SOURCE.md). Copyright (C) 2026 Rance9. GPL-3.0-or-later.

  build-worker.ps1 -HakchiSource DIR -Out DIR [-BuildRoot DIR] [-SourceRevision TEXT]

1. Verifies the pinned Hakchi2 CE 3.9.3 tree (get-hakchi-source.ps1 -UseExisting: commits, submodules, hakchi-inputs.sha256).
2. Builds the worker and its tests (Release; the .NET SDK named in global.json) in BuildRoot\a, runs the offline tests (gate, protocol,
   clovershell, ssh, ops; the BusyBox fidelity tests need WSL and the console's extracted files and are not part of this build), builds the
   worker again in BuildRoot\b and requires the same bytes.
3. Writes Out (a new folder): the worker's five files, NOTICE, SOURCE.md, licenses\, BUILD-INFO.json (schema ccs-console-worker-build/1:
   every shipped file with its size and SHA-256, the upstream commits, the toolchain) and console-worker-source.zip (this folder's source
   and the exact Hakchi2 CE files it compiles, links or derives from, with their licenses: the corresponding source).
Nothing in Out may name a build machine's folders. Any failure stops with a non-zero exit. BuildRoot defaults to the repository's ignored
build folder (build\console-worker-release).
#>
param([Parameter(Mandatory)][string]$HakchiSource, [Parameter(Mandatory)][string]$Out, [string]$BuildRoot, [string]$SourceRevision)
$ErrorActionPreference = 'Stop'
$Src = $PSScriptRoot; $Repo = (Resolve-Path (Join-Path $Src '..\..')).Path
function Fail($why) { Write-Host "STOP  $why" -ForegroundColor Red; exit 1 }
function Sha($p) { (Get-FileHash -Algorithm SHA256 -LiteralPath $p).Hash.ToLowerInvariant() }
$Files = 'ccs-console-worker.exe', 'ccs-console-worker.exe.config', 'CcsWorker.dll', 'Renci.SshNet.dll', 'LibUsbDotNet.LibUsbDotNet.dll'
$LibUsbSha = 'b6de87ff6fdde6d0a245ef37fe81fac49523c35115c61bc002e4935d99875508'   # NuGet LibUsbDotNet 2.2.29 = the copy Hakchi2 CE 3.9.3 embeds

# 1. the pinned upstream tree
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Src 'get-hakchi-source.ps1') -Dest $HakchiSource -UseExisting
if ($LASTEXITCODE -ne 0) { Fail "the Hakchi2 CE source tree did not verify" }
$HakchiSource = (Resolve-Path $HakchiSource).Path
if ((Test-Path $Out) -and (Get-ChildItem -Force $Out | Select-Object -First 1)) { Fail "$Out exists and is not empty" }
if (-not $BuildRoot) { $BuildRoot = Join-Path $Repo 'build\console-worker-release' }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'; $A = Join-Path $BuildRoot "$stamp-a\"; $B = Join-Path $BuildRoot "$stamp-b\"
New-Item -ItemType Directory -Force $A, $B, (Join-Path $BuildRoot "$stamp-tmp") | Out-Null
Push-Location $Src   # global.json here pins the SDK
try {
    $sdk = (& dotnet --version).Trim(); if ($LASTEXITCODE -ne 0) { Fail "the .NET SDK named in global.json is not installed" }
    $msb = ((& dotnet msbuild -version -nologo) | Select-Object -Last 1).Trim()
    Write-Host "SDK $sdk, MSBuild $msb"
    function Build($root, $proj) {
        & dotnet build (Join-Path $Src $proj) -c Release -nologo -v q "-p:CcsBuildRoot=$root" "-p:HakchiSource=$HakchiSource"
        if ($LASTEXITCODE -ne 0) { Fail "dotnet build $proj failed" }
    }
    # 2. build, test, build again elsewhere
    Build $A 'ccs-console-worker\ccs-console-worker.csproj'; Build $A 'CcsWorker.Tests\CcsWorker.Tests.csproj'
    $oldTemp = $env:TEMP; $oldTmp = $env:TMP; $env:TEMP = $env:TMP = (Join-Path $BuildRoot "$stamp-tmp")   # the tests' own temporary folders
    try { $t = & (Join-Path $A 'bin\CcsWorker.Tests\Release\net48\CcsWorker.Tests.exe') --only gate,protocol,clovershell,ssh,ops; $trc = $LASTEXITCODE }
    finally { $env:TEMP = $oldTemp; $env:TMP = $oldTmp }
    $t | ForEach-Object { Write-Host $_ }
    $result = ($t | Where-Object { $_ -match '^RESULT ' } | Select-Object -Last 1)
    if ($trc -ne 0 -or $result -notmatch '^RESULT (\d+)/(\d+)$' -or $Matches[1] -ne $Matches[2]) { Fail "the worker's offline tests did not all pass ($result, exit $trc)" }
    Build $B 'ccs-console-worker\ccs-console-worker.csproj'
} finally { Pop-Location }
$binA = Join-Path $A 'bin\ccs-console-worker\Release\net48'; $binB = Join-Path $B 'bin\ccs-console-worker\Release\net48'
$extra = @(Get-ChildItem -File $binA | Where-Object { $Files -notcontains $_.Name } | ForEach-Object Name)
if ($extra) { Fail "the build made files this script does not ship: $($extra -join ', ')" }
foreach ($f in $Files) {
    if (-not (Test-Path (Join-Path $binA $f))) { Fail "the build did not make $f" }
    if ((Sha (Join-Path $binA $f)) -ne (Sha (Join-Path $binB $f))) { Fail "$f differs between two build folders (not reproducible)" }
}
if ((Sha (Join-Path $binA 'LibUsbDotNet.LibUsbDotNet.dll')) -ne $LibUsbSha) { Fail "LibUsbDotNet.LibUsbDotNet.dll is not NuGet LibUsbDotNet 2.2.29 as recorded" }
Write-Host "PASS  the five files are byte-identical from two build folders"

# 3. the release folder
New-Item -ItemType Directory -Force $Out | Out-Null; $Out = (Resolve-Path $Out).Path
foreach ($f in $Files) { Copy-Item (Join-Path $binA $f) $Out }
Copy-Item (Join-Path $Src 'NOTICE'), (Join-Path $Src 'SOURCE.md') $Out; Copy-Item -Recurse (Join-Path $Src 'licenses') (Join-Path $Out 'licenses')
# the corresponding source: this folder (without build output) and the recorded upstream files, entries sorted, one fixed time
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$zipPath = Join-Path $Out 'console-worker-source.zip'; $when = [DateTimeOffset]::new(2026, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
$entries = New-Object 'System.Collections.Generic.SortedDictionary[string,string]' ([StringComparer]::Ordinal)
Get-ChildItem -Recurse -File -Force $Src | Where-Object { $_.FullName.Substring($Src.Length) -notmatch '^\\(bin|obj)\\|\\(bin|obj)\\' } |
    ForEach-Object { $entries['console-worker/' + ($_.FullName.Substring($Src.Length + 1) -replace '\\', '/')] = $_.FullName }
$inputs = @(Get-Content (Join-Path $Src 'hakchi-inputs.sha256') | Where-Object { $_ -and $_ -notmatch '^#' } | ForEach-Object { ($_ -split '  ', 2)[1] })
foreach ($rel in $inputs) { $entries["upstream/Hakchi2-CE-3.9.3/$rel"] = Join-Path $HakchiSource ($rel -replace '/', '\') }
$readme = @"
Classic Cart Studio console transport worker (ccs-console-worker): corresponding source
Copyright (C) 2026 Rance9. GPL-3.0-or-later (console-worker/NOTICE, console-worker/licenses/).

console-worker/              the worker's own source (tools/console-worker in the Classic Cart Studio repository)
upstream/Hakchi2-CE-3.9.3/   the files of Hakchi2 CE 3.9.3 (https://github.com/TeamShinkansen/Hakchi2-CE, tag v3.9.3, commit
                             25bfcfd04b8ed12f7d513dc87c2a0f7096d38ebc) the worker compiles (SSH.NET, submodule Libraries/SSH.NET at
                             32181aeb4ce69e8f07e5e1d585fd592a44017bc1, compiled unchanged), links (LibUsbDotNet 2.2.29, whose source is
                             submodule Libraries/LibUsbDotNet at 91e84390acd83ded475798488740961da9e3307c) or derives from, and their
                             licenses; console-worker/hakchi-inputs.sha256 lists each with its SHA-256

Build (Windows, the .NET SDK in console-worker/global.json): build-worker.ps1 -HakchiSource <the full pinned tree> -Out <folder>
(console-worker/get-hakchi-source.ps1 fetches and verifies the full tree). The upstream files here are enough to compile:
dotnet build console-worker/ccs-console-worker/ccs-console-worker.csproj -c Release -p:HakchiSource=<this folder>/upstream/Hakchi2-CE-3.9.3
"@
$zs = [IO.File]::Open($zipPath, 'CreateNew'); $zip = New-Object IO.Compression.ZipArchive($zs, [IO.Compression.ZipArchiveMode]::Create)
try {
    function Add-Entry($name, [byte[]]$bytes) { $e = $zip.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal); $e.LastWriteTime = $when; $s = $e.Open(); $s.Write($bytes, 0, $bytes.Length); $s.Dispose() }
    Add-Entry 'README-SOURCE.txt' ([Text.Encoding]::UTF8.GetBytes(($readme -replace "`r`n", "`n")))
    foreach ($kv in $entries.GetEnumerator()) { Add-Entry $kv.Key ([IO.File]::ReadAllBytes($kv.Value)) }
} finally { $zip.Dispose(); $zs.Dispose() }
# BUILD-INFO.json
$subs = [ordered]@{}; foreach ($l in (& git -C $HakchiSource ls-tree -r HEAD)) { if ($l -match '^160000 commit ([0-9a-f]{40})\t(.+)$') { $subs[$Matches[2]] = $Matches[1] } }
$ver = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $Out 'CcsWorker.dll')).Version.ToString()
$shipped = [ordered]@{}
foreach ($f in (Get-ChildItem -Recurse -File $Out | Where-Object { $_.Name -ne 'console-worker-source.zip' } | Sort-Object { $_.FullName.Substring($Out.Length + 1) -replace '\\', '/' } -CaseSensitive)) {
    $shipped[($f.FullName.Substring($Out.Length + 1) -replace '\\', '/')] = [ordered]@{ size = $f.Length; sha256 = (Sha $f.FullName) }
}
$zipInfo = Get-Item $zipPath
$info = [ordered]@{
    schema = 'ccs-console-worker-build/1'; worker = 'ccs-console-worker'; version = $ver; protocol = 1
    hakchi = [ordered]@{ repository = 'https://github.com/TeamShinkansen/Hakchi2-CE'; tag = 'v3.9.3'; commit = (& git -C $HakchiSource rev-parse HEAD).Trim(); submodules = $subs }
    sshnet = [ordered]@{ source = 'Hakchi2 CE submodule Libraries/SSH.NET (TeamShinkansen fork)'; commit = $subs['Libraries/SSH.NET']; compiled = 'from source, unchanged (Renci.SshNet/Renci.SshNet.csproj)' }
    libusbdotnet = [ordered]@{ nuget = 'LibUsbDotNet'; version = '2.2.29'; file = 'LibUsbDotNet.LibUsbDotNet.dll'; sha256 = $LibUsbSha; source = "Hakchi2 CE submodule Libraries/LibUsbDotNet at $($subs['Libraries/LibUsbDotNet'])" }
    inputs = [ordered]@{ list = 'hakchi-inputs.sha256'; sha256 = (Sha (Join-Path $Src 'hakchi-inputs.sha256')); files = $inputs.Count }
    toolchain = [ordered]@{ dotnet_sdk = $sdk; msbuild = $msb; target = 'net48'; configuration = 'Release'; deterministic = $true }
    reproducible = 'the five program files byte-identical from two build folders'
    tests = [ordered]@{ run = 'gate,protocol,clovershell,ssh,ops'; result = ($result -replace '^RESULT ', '') }
    source_revision = $(if ($SourceRevision) { $SourceRevision } else { $null })
    files = $shipped
    source_zip = [ordered]@{ name = 'console-worker-source.zip'; size = $zipInfo.Length; sha256 = (Sha $zipPath); entries = $entries.Count + 1 }
}
$json = ($info | ConvertTo-Json -Depth 6) -replace "`r`n", "`n"
[IO.File]::WriteAllText((Join-Path $Out 'BUILD-INFO.json'), $json + "`n", (New-Object Text.UTF8Encoding $false))
# nothing may name this build machine's folders or user: BUILD-INFO, every entry name and every text entry of the source archive are
# searched for the folders this build used, the user's name and profile, and any Windows user-profile path (as written and as JSON writes it)
$marks = @($Repo, $HakchiSource, $BuildRoot, $Out, $env:USERPROFILE, $env:LOCALAPPDATA, $env:TEMP) | Where-Object { $_ -and $_.Length -ge 4 } | ForEach-Object { $_.TrimEnd('\') }
$Private = @($marks | ForEach-Object { [regex]::Escape($_); [regex]::Escape(($_ -replace '\\', '\\')) }) + '[A-Za-z]:(\\|/)+Users(\\|/)+'
if ($env:USERNAME -and $env:USERNAME.Length -ge 4 -and $env:USERNAME -notmatch '^(runneradmin|runner|Administrator|user)$') { $Private += [regex]::Escape($env:USERNAME) }
$problems = @()
foreach ($p in $Private) { if ($json -match $p) { $problems += "BUILD-INFO.json names a build machine's folder or user" } }
$zr = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    foreach ($e in $zr.Entries) {
        foreach ($p in $Private) { if ($e.FullName -match $p) { $problems += "zip entry name $($e.FullName)" } }
        if ($e.FullName -match '\.(cs|csproj|props|ps1|sh|md|txt|json|sha256|config)$|NOTICE$') {
            $r = New-Object IO.StreamReader($e.Open()); $text = $r.ReadToEnd(); $r.Dispose()
            foreach ($p in $Private) { if ($text -match $p) { $problems += "$($e.FullName) names a build machine's folder or user" } }
        }
    }
} finally { $zr.Dispose() }
if ($problems) { Fail "the output names this build machine: $(($problems | Select-Object -Unique -First 5) -join '; ')" }
Write-Host "PASS  no build machine folder or user name in BUILD-INFO.json or the source archive ($($Private.Count) patterns)"
Get-ChildItem -File $Out | ForEach-Object { "{0,-34} {1,10} {2}" -f $_.Name, $_.Length, (Sha $_.FullName) }
Write-Host "WORKER BUILD OK: $Out ($($result -replace '^RESULT ', '') offline tests; reproducible; source archive $($entries.Count + 1) entries)"
exit 0

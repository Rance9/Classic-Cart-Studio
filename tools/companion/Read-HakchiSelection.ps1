param([int]$ProcessId = 0, [int]$WaitSeconds = 90, [switch]$Foreground)
# Read-only: finds the hakchi main window (by PID when given) and reads its game list through UI Automation.
# Prints one JSON line: {ok, window, listed:[names], checked:[names], rows:[{name, group, checked}], note}. Never clicks anything.
# Game rows are the list items that live inside a ListView GROUP ("Original Games", "Custom Games", "New Games"); list items without a
# group ancestor (combo-box entries such as players/genre/region) are ignored. Output is UTF-8 so names like FINAL FANTASY® survive.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Add-Type -AssemblyName UIAutomationClient; Add-Type -AssemblyName UIAutomationTypes
Add-Type -Name U32r -Namespace N -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr a,int x,int y,int cx,int cy,uint f); [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);'
[N.U32r]::SetProcessDPIAware() | Out-Null
$root = [System.Windows.Automation.AutomationElement]::RootElement
$deadline = (Get-Date).AddSeconds($WaitSeconds); $win = $null
while ((Get-Date) -lt $deadline -and -not $win) {
  $cands = @()
  if ($ProcessId -gt 0) { $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $ProcessId); $cands = $root.FindAll([System.Windows.Automation.TreeScope]::Children, $cond) }
  else { $cands = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.Name -match 'hakchi' } }
  foreach ($c in $cands) { if ($c.Current.Name -match 'hakchi' -and $c.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window) { $win = $c } }
  if (-not $win) { Start-Sleep 2 }
}
if (-not $win) { Write-Output (@{ok=$false; error="hakchi main window not found within $WaitSeconds s"} | ConvertTo-Json -Compress); exit 0 }
# Bring the window onto the primary monitor and to the foreground, then re-acquire the element FROM ITS HANDLE:
# the list rows are exposed to UI Automation only through the handle-based element once the control is realised on screen.
# By default the window is moved onto the primary monitor WITHOUT activating it (SWP_NOACTIVATE), so the reader never steals the owner's keyboard focus
# (a stolen Enter key would press hakchi's default 'Add more games' button). -Foreground activates it (only for screenshots that need it).
try { [IntPtr]$hwnd = [IntPtr]$win.Current.NativeWindowHandle; [N.U32r]::SetWindowPos($hwnd, [IntPtr]::Zero, 40, 40, 2600, 1700, 0x0040 -bor 0x0010 -bor 0x0004) | Out-Null; if ($Foreground) { [N.U32r]::SetForegroundWindow($hwnd) | Out-Null }; Start-Sleep 3; $win = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd) } catch {}
$walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
function Get-GroupName($el) {
  $p = $el
  for ($i = 0; $i -lt 4; $i++) {
    try { $p = $walker.GetParent($p) } catch { return $null }
    if (-not $p) { return $null }
    if ($p.Current.ControlType -eq [System.Windows.Automation.ControlType]::Group) { return $p.Current.Name }
  }
  return $null
}
$rows = @()
# the list is filled asynchronously after the window appears (LoadGamesTask); poll until grouped rows exist or the deadline passes
while ($true) {
  $rows = @()
  $all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
  foreach ($it in $all) {
    if ($it.Current.ControlType.ProgrammaticName -notin @('ControlType.DataItem', 'ControlType.ListItem')) { continue }
    $n = $it.Current.Name; if (-not $n) { continue }
    $g = Get-GroupName $it; if (-not $g -or $g -notin @('Original Games', 'Custom Games', 'New Games')) { continue }   # GroupBox controls (Game Info, Current Games Collection) are Groups too
    $isOn = $false
    $tp = $null; try { $tp = $it.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern) } catch {}
    if ($tp) { $isOn = ($tp.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On) }
    else {
      $cb = $it.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::CheckBox)))
      if ($cb) { try { $t2 = $cb.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern); $isOn = ($t2.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On) } catch {} }
    }
    $rows += @{name=$n; group=$g; checked=$isOn}
  }
  if ($rows.Count -gt 0 -or (Get-Date) -ge $deadline) { break }
  Start-Sleep 3; try { $win = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd) } catch {}
}
$listed = @($rows | ForEach-Object { $_.name }); $checked = @($rows | Where-Object { $_.checked } | ForEach-Object { $_.name })
Write-Output (@{ok=$true; window=$win.Current.Name; listed=$listed; checked=$checked; rows=$rows; note="game rows inside ListView groups; names as shown; toggle state via UIA"} | ConvertTo-Json -Compress -Depth 4)

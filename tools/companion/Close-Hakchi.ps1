param([int]$ProcessId, [int]$WaitSeconds = 60)
# Graceful close of a hakchi instance the integration started: first dismiss any dialog the process owns (WM_CLOSE = cancel, e.g. an
# accidentally opened "Select games" file dialog, which keeps the main window disabled and makes CloseMainWindow a no-op), then ask the
# main window to close (CloseMainWindow, twice if needed) so hakchi saves its configuration normally. Only as a last resort the process is
# stopped, and the result says so. Prints one JSON line {result, dialogs_dismissed, exited}.
Add-Type -Name C -Namespace N -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
public delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint procId);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
'@
$p = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
if (-not $p) { Write-Output (@{result='gone'; dialogs_dismissed=@(); exited=$true} | ConvertTo-Json -Compress); exit 0 }
$dismissed = New-Object System.Collections.ArrayList
$main = [int64]$p.MainWindowHandle
$cb = [N.C+EnumWindowsProc]{
  param($h, $l)
  [uint32]$owner = 0; [N.C]::GetWindowThreadProcessId($h, [ref]$owner) | Out-Null
  if ($owner -eq $ProcessId -and [N.C]::IsWindowVisible($h) -and [int64]$h -ne $main) {
    $sb = New-Object System.Text.StringBuilder 512; [N.C]::GetWindowText($h, $sb, 512) | Out-Null
    $cn = New-Object System.Text.StringBuilder 256; [N.C]::GetClassName($h, $cn, 256) | Out-Null
    if ($cn.ToString() -eq '#32770' -or $sb.ToString() -ne '') { [N.C]::PostMessage($h, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null; [void]$dismissed.Add(($sb.ToString() + ' [' + $cn.ToString() + ']')) }
  }
  return $true
}
[N.C]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
if ($dismissed.Count -gt 0) { Start-Sleep 2 }
$result = 'forced'
$p.CloseMainWindow() | Out-Null
if ($p.WaitForExit($WaitSeconds * 1000)) { $result = 'closed' }
else {
  $p.Refresh(); $p.CloseMainWindow() | Out-Null
  if ($p.WaitForExit($WaitSeconds * 1000)) { $result = 'closed (second request)' }
  else { Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue; Start-Sleep 1 }
}
Write-Output (@{result=$result; dialogs_dismissed=@($dismissed); exited=(-not (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue))} | ConvertTo-Json -Compress)

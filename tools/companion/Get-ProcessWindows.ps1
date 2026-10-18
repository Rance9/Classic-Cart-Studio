param([int]$ProcessId)
# Read-only diagnostic: every visible top-level window of a process (title, class, rectangle), including dialogs on other monitors.
Add-Type -Name W -Namespace N -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
public delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint procId);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr h);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
'@
$found = New-Object System.Collections.ArrayList
$cb = [N.W+EnumWindowsProc]{
  param($h, $l)
  [uint32]$owner = 0; [N.W]::GetWindowThreadProcessId($h, [ref]$owner) | Out-Null
  if ($owner -eq $ProcessId -and [N.W]::IsWindowVisible($h)) {
    $sb = New-Object System.Text.StringBuilder 512; [N.W]::GetWindowText($h, $sb, 512) | Out-Null
    $cn = New-Object System.Text.StringBuilder 256; [N.W]::GetClassName($h, $cn, 256) | Out-Null
    $r = New-Object N.W+RECT; [N.W]::GetWindowRect($h, [ref]$r) | Out-Null
    [void]$found.Add(@{hwnd = [int64]$h; title = $sb.ToString(); class = $cn.ToString(); enabled = [N.W]::IsWindowEnabled($h); rect = @($r.L, $r.T, $r.R, $r.B)})
  }
  return $true
}
[N.W]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
Write-Output ($found | ConvertTo-Json -Compress -Depth 3)

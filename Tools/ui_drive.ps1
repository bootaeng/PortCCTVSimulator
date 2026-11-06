# Window-targeted UI driver for the PortCCTVSimulator player (dev QA only).
# Input is POSTED to the simulator's own window (WM_* messages) and screenshots use PrintWindow on that window,
# so other desktop windows are never clicked or captured, and the user's cursor is not moved.
# Dot-source:  . .\Tools\ui_drive.ps1      Coordinates = client coordinates of the game window (top-left).
Add-Type -AssemblyName System.Drawing
if (-not ("UiMsg" -as [type])) {
Add-Type -ReferencedAssemblies System.Drawing @"
using System;
using System.Drawing;
using System.Runtime.InteropServices;
public static class UiMsg {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  public static IntPtr L(int x, int y) { return (IntPtr)((y << 16) | (x & 0xFFFF)); }
  public static Bitmap Capture(IntPtr h) {
    RECT r; GetClientRect(h, out r);
    var bmp = new Bitmap(r.R - r.L, r.B - r.T);
    using (var g = Graphics.FromImage(bmp)) { var hdc = g.GetHdc(); PrintWindow(h, hdc, 3); g.ReleaseHdc(hdc); }
    return bmp;
  }
}
"@
}
[void][UiMsg]::SetProcessDPIAware()
$WM_MOUSEMOVE=0x200; $WM_LBUTTONDOWN=0x201; $WM_LBUTTONUP=0x202; $WM_RBUTTONDOWN=0x204; $WM_RBUTTONUP=0x205
$WM_KEYDOWN=0x100; $WM_KEYUP=0x101; $WM_CHAR=0x102

function Get-SimWindow {
  $p = Get-Process PortCCTVSimulator -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
  if (-not $p) { throw "PortCCTVSimulator window not found" }
  return $p.MainWindowHandle
}
function Post([uint32]$m, [int]$w, [int]$x, [int]$y) { [void][UiMsg]::PostMessage((Get-SimWindow), $m, [IntPtr]$w, [UiMsg]::L($x, $y)) }
function Click([int]$x, [int]$y, [int]$waitMs = 500) {
  Post $WM_MOUSEMOVE 0 $x $y; Start-Sleep -Milliseconds 120
  Post $WM_LBUTTONDOWN 1 $x $y; Start-Sleep -Milliseconds 120
  Post $WM_LBUTTONUP 0 $x $y; Start-Sleep -Milliseconds $waitMs
}
function Drag([int]$x0, [int]$y0, [int]$x1, [int]$y1, [switch]$Right) {
  $down = $WM_LBUTTONDOWN; $up = $WM_LBUTTONUP; $btn = 1; if ($Right) { $down = $WM_RBUTTONDOWN; $up = $WM_RBUTTONUP; $btn = 2 }
  Post $WM_MOUSEMOVE 0 $x0 $y0; Start-Sleep -Milliseconds 100
  Post $down $btn $x0 $y0; Start-Sleep -Milliseconds 100
  for ($i = 1; $i -le 20; $i++) { Post $WM_MOUSEMOVE $btn ([int]($x0 + ($x1 - $x0) * $i / 20)) ([int]($y0 + ($y1 - $y0) * $i / 20)); Start-Sleep -Milliseconds 40 }
  Post $up 0 $x1 $y1; Start-Sleep -Milliseconds 500
}
function HoldKey([int]$vk, [int]$ms) {
  $h = Get-SimWindow; [void][UiMsg]::PostMessage($h, $WM_KEYDOWN, [IntPtr]$vk, [IntPtr]0); Start-Sleep -Milliseconds $ms
  [void][UiMsg]::PostMessage($h, $WM_KEYUP, [IntPtr]$vk, [IntPtr]0xC0000001); Start-Sleep -Milliseconds 200
}
function TypeText([string]$t) {
  $h = Get-SimWindow
  foreach ($c in $t.ToCharArray()) { [void][UiMsg]::PostMessage($h, $WM_CHAR, [IntPtr][int]$c, [IntPtr]1); Start-Sleep -Milliseconds 40 }
  Start-Sleep -Milliseconds 300
}
function Key([int]$vk) { HoldKey $vk 60 }
function Shot([string]$path) {
  $bmp = [UiMsg]::Capture((Get-SimWindow)); $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $w = $bmp.Width; $h = $bmp.Height; $bmp.Dispose(); "shot $path ${w}x$h"
}

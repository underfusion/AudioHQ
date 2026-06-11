# Captures the running AudioHQ window to docs/screenshot.png.
# Brings the window to the foreground, reads its bounds, copies that screen region.
param(
    [string]$OutPath = "$PSScriptRoot\..\docs\screenshot.png"
)

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32 {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

$p = Get-Process AudioHQ -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Output "ERROR: AudioHQ not running"; exit 1 }

$h = $p.MainWindowHandle
Write-Output "MainWindowHandle = $h"
if ($h -eq 0) { Write-Output "ERROR: window has no handle (hidden to tray?). Restore it first."; exit 2 }

[void][Win32]::ShowWindow($h, 9)   # SW_RESTORE
# Force topmost and move to a clear spot so nothing overlaps the capture region.
# HWND_TOPMOST = -1, SWP_NOSIZE = 0x0001
[void][Win32]::SetWindowPos($h, [IntPtr](-1), 120, 120, 0, 0, 0x0001)
[void][Win32]::SetForegroundWindow($h)
Start-Sleep -Milliseconds 800

$r = New-Object Win32+RECT
[void][Win32]::GetWindowRect($h, [ref]$r)
$w = $r.Right - $r.Left
$ht = $r.Bottom - $r.Top
Write-Output "Window rect: ${w}x${ht} at ($($r.Left),$($r.Top))"
if ($w -le 0 -or $ht -le 0) { Write-Output "ERROR: bad window rect"; exit 3 }

Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap $w, $ht
$g = [System.Drawing.Graphics]::FromImage($bmp)
# PrintWindow renders the window content directly into our DC, so overlapping
# windows do not bleed in. PW_RENDERFULLCONTENT = 2 (required for WPF/DComp).
$hdc = $g.GetHdc()
$ok = [Win32]::PrintWindow($h, $hdc, 2)
$g.ReleaseHdc($hdc)
$g.Dispose()
if (-not $ok) { Write-Output "WARN: PrintWindow returned false" }

$full = [System.IO.Path]::GetFullPath($OutPath)
$bmp.Save($full, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output "Saved: $full"

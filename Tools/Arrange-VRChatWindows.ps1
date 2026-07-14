[CmdletBinding()]
param(
    # 正方形グリッドの一辺の数。0 の場合は検出したウィンドウ数から自動計算する。
    [ValidateRange(0, 16)]
    [int]$GridSize = 0,

    # 各ウィンドウと画面端の余白（ピクセル）。
    [ValidateRange(0, 100)]
    [int]$Margin = 8,

    # ウィンドウ間の間隔（ピクセル）。
    [ValidateRange(0, 100)]
    [int]$Gap = 8
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class VrcWindowLayout
{
    public const int SW_RESTORE = 9;
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public class MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    public class MonitorWorkArea
    {
        public int Left;
        public int Top;
        public int Width;
        public int Height;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, [In, Out] MONITORINFO lpmi);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

    public static MonitorWorkArea GetMonitorWorkArea(IntPtr hWnd)
    {
        IntPtr hMonitor = MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);
        if (hMonitor == IntPtr.Zero)
        {
            throw new InvalidOperationException("The monitor for the VRChat window could not be found.");
        }

        var info = new MONITORINFO();
        info.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
        if (!GetMonitorInfo(hMonitor, info))
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        return new MonitorWorkArea
        {
            Left = info.rcWork.Left,
            Top = info.rcWork.Top,
            Width = info.rcWork.Right - info.rcWork.Left,
            Height = info.rcWork.Bottom - info.rcWork.Top
        };
    }
}
'@

$windows = @(
    Get-Process -Name 'VRChat' -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } |
        Sort-Object Id
)

if ($windows.Count -eq 0) {
    throw '配置できる VRChat ウィンドウがありません。VRChat の起動と画面表示が完了してから実行してください。'
}

$GridSize = if ($GridSize -eq 0) { [int][Math]::Ceiling([Math]::Sqrt($windows.Count)) } else { $GridSize }
$capacity = $GridSize * $GridSize
if ($windows.Count -gt $capacity) {
    Write-Warning "VRChat ウィンドウを $($windows.Count) 枚検出しました。$GridSize x $GridSize のため、先頭 $capacity 枚だけを配置します。"
    $windows = @($windows | Select-Object -First $capacity)
}

$monitor = [VrcWindowLayout]::GetMonitorWorkArea($windows[0].MainWindowHandle)
$windowWidth = [Math]::Floor(($monitor.Width - ($Margin * 2) - ($Gap * ($GridSize - 1))) / $GridSize)
$windowHeight = [Math]::Floor(($monitor.Height - ($Margin * 2) - ($Gap * ($GridSize - 1))) / $GridSize)

if ($windowWidth -lt 1 -or $windowHeight -lt 1) {
    throw '指定したグリッド数・余白ではウィンドウサイズを計算できません。引数を見直してください。'
}

for ($index = 0; $index -lt $windows.Count; $index++) {
    $column = $index % $GridSize
    $row = [Math]::Floor($index / $GridSize)
    $x = $monitor.Left + $Margin + ($column * ($windowWidth + $Gap))
    $y = $monitor.Top + $Margin + ($row * ($windowHeight + $Gap))
    $handle = $windows[$index].MainWindowHandle

    # 最大化されているウィンドウを通常サイズへ戻してから配置する。
    [VrcWindowLayout]::ShowWindowAsync($handle, [VrcWindowLayout]::SW_RESTORE) | Out-Null
    if (-not [VrcWindowLayout]::SetWindowPos(
        $handle,
        [IntPtr]::Zero,
        $x,
        $y,
        $windowWidth,
        $windowHeight,
        [VrcWindowLayout]::SWP_NOZORDER -bor [VrcWindowLayout]::SWP_NOACTIVATE -bor [VrcWindowLayout]::SWP_SHOWWINDOW)) {
        throw "VRChat（PID $($windows[$index].Id)）の配置に失敗しました。"
    }
}

Write-Host ("VRChat ウィンドウを {0} 枚、最初に検出したウィンドウのディスプレイ内で {1} x {1} に配置しました。" -f $windows.Count, $GridSize)

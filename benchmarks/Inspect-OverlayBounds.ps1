<# Read-only HWND geometry check. This does not modify Z-order or inspect a game. #>
param([Parameter(Mandatory=$true)][int]$ProcessId)
$ErrorActionPreference='Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class OverlayBoundsNative {
    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr param);
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Auto)] public struct MonitorInfo {
        public int Size; public Rect Monitor; public Rect Work; public uint Flags;
    }
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet=CharSet.Auto)]
    public static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
'@
$process=Get-Process -Id $ProcessId -ErrorAction Stop
$found=[Collections.Generic.List[object]]::new()
try {
    $callback=[OverlayBoundsNative+EnumWindowsProc]{param($hwnd,$param)
        [uint32]$windowPid=0
        [OverlayBoundsNative]::GetWindowThreadProcessId($hwnd,[ref]$windowPid)|Out-Null
        if($windowPid -ne $ProcessId -or -not [OverlayBoundsNative]::IsWindowVisible($hwnd)){return $true}
        $style=[OverlayBoundsNative]::GetWindowLongPtr($hwnd,-20).ToInt64()
        if(($style -band 0x80020) -ne 0x80020){return $true}
        $rect=[OverlayBoundsNative+Rect]::new()
        if(-not [OverlayBoundsNative]::GetWindowRect($hwnd,[ref]$rect)){return $true}
        $monitor=[OverlayBoundsNative]::MonitorFromWindow($hwnd,2)
        $info=[OverlayBoundsNative+MonitorInfo]::new()
        $info.Size=[Runtime.InteropServices.Marshal]::SizeOf([type][OverlayBoundsNative+MonitorInfo])
        if(-not [OverlayBoundsNative]::GetMonitorInfo($monitor,[ref]$info)){return $true}
        $w=$rect.Right-$rect.Left; $h=$rect.Bottom-$rect.Top
        $mw=$info.Monitor.Right-$info.Monitor.Left; $mh=$info.Monitor.Bottom-$info.Monitor.Top
        $found.Add([pscustomobject]@{
            TimestampUtc=[DateTimeOffset]::UtcNow.ToString('o');Pid=$ProcessId
            Hwnd=$hwnd.ToString();Left=$rect.Left;Top=$rect.Top;Width=$w;Height=$h
            MonitorWidth=$mw;MonitorHeight=$mh
            AreaPercent=[math]::Round(100.0*$w*$h/($mw*$mh),5)
            Layered=(($style -band 0x80000) -ne 0)
            ClickThrough=(($style -band 0x20) -ne 0)
        })
        return $true
    }
    [OverlayBoundsNative]::EnumWindows($callback,[IntPtr]::Zero)|Out-Null
    if(-not $found.Count){throw 'No visible layered/click-through overlay HWND found.'}
    $stamp=[DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ')
    $output=Join-Path $PSScriptRoot "results\overlay-bounds-$stamp.csv"
    $found|Export-Csv $output -NoTypeInformation -Encoding UTF8
    $found|Format-Table -AutoSize
    Write-Host "[Profiler] Saved $output"
} finally {$process.Dispose()}

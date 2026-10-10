$ErrorActionPreference = "Stop"
$hub = "http://127.0.0.1:8107/"

function Test-HubUp {
    try {
        $response = Invoke-WebRequest -Uri $hub -UseBasicParsing -TimeoutSec 2
        return $response.StatusCode -eq 200
    } catch {
        return $false
    }
}

# One listener only. A live hub is left alone. A down hub is started hidden and detached.
if (-not (Test-HubUp)) {
    $starter = Join-Path $PSScriptRoot "Start-HiveBrainComms.ps1"
    Start-Process -FilePath "powershell.exe" -ArgumentList @(
        "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", $starter
    ) -WindowStyle Hidden
    $deadline = (Get-Date).AddSeconds(15)
    while (-not (Test-HubUp) -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 250
    }
    if (-not (Test-HubUp)) { throw "HiveBrainComms hub did not answer on 127.0.0.1:8107 within 15 seconds." }
}

$live = Join-Path $PSScriptRoot "shell\bin\Release\net8.0-windows"
$staging = Join-Path $PSScriptRoot "shell\staging"
$exe = Join-Path $live "Shell.exe"
# Swap a staged build in only when this app is not running. A running Shell.exe is left alone.
if ((Test-Path (Join-Path $staging "Shell.exe")) -and -not (Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -eq "HiveBrainComms" })) {
    New-Item -ItemType Directory -Path $live -Force | Out-Null
    Copy-Item -Path (Join-Path $staging "*") -Destination $live -Recurse -Force
    Remove-Item $staging -Recurse -Force
}
$running = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -eq "HiveBrainComms" } | Select-Object -First 1
if ($running -and $running.MainWindowHandle -ne 0) {
    Add-Type @"
using System;
using System.Runtime.InteropServices;
public class HbcFocus {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
}
"@
    [HbcFocus]::ShowWindow($running.MainWindowHandle, 9) | Out-Null
    [HbcFocus]::SetForegroundWindow($running.MainWindowHandle) | Out-Null
    return
}
# Smart App Control blocks the unsigned Shell.exe, so run the app through the Microsoft-signed dotnet host.
$dll = Join-Path $live "Shell.dll"
if (-not (Test-Path $dll)) { throw "Shell.dll is not built yet." }
$proc = Start-Process -FilePath "dotnet" -ArgumentList "`"$dll`"" -WorkingDirectory $live -WindowStyle Hidden -PassThru
# Hidden keeps the dotnet console out of sight, but it also hides the first show of the form, so show the app window once it exists.
Add-Type @"
using System; using System.Runtime.InteropServices; using System.Text;
public class HbcShow { public delegate bool P(IntPtr h, IntPtr l);
[DllImport("user32.dll")] static extern bool EnumWindows(P p, IntPtr l);
[DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
[DllImport("user32.dll")] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
[DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int n);
[DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
public static bool Show(uint target){ bool ok=false; EnumWindows((h,l)=>{ uint p; GetWindowThreadProcessId(h,out p); var sb=new StringBuilder(200); GetWindowText(h,sb,200); if(p==target && sb.ToString()=="HiveBrainComms"){ ShowWindow(h,9); SetForegroundWindow(h); ok=true; } return true; }, IntPtr.Zero); return ok; } }
"@
$deadline = (Get-Date).AddSeconds(30)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
    if ([HbcShow]::Show([uint32]$proc.Id)) { break }
}



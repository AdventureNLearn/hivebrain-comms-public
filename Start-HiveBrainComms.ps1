# HiveBrainComms — 127.0.0.1:8107 only
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Url = "http://127.0.0.1:8107/"
function Start-DetachedPython {
  param([string]$Python, [string]$WorkDir)
  $code = @'
using System;
using System.Runtime.InteropServices;
public class HbcDetached {
  [DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)]
  static extern bool CreateProcess(string app, string cmd, IntPtr pa, IntPtr ta, bool inherit,
    uint flags, IntPtr env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
  struct STARTUPINFO {
    public int cb; public string reserved, desktop, title;
    public int x, y, xSize, ySize, xCountChars, yCountChars, fillAttribute, flags;
    public short showWindow, reserved2; public IntPtr reserved3, stdIn, stdOut, stdErr;
  }
  [StructLayout(LayoutKind.Sequential)]
  struct PROCESS_INFORMATION { public IntPtr process, thread; public int processId, threadId; }
  public static bool Run(string python, string dir) {
    var si = new STARTUPINFO();
    si.cb = Marshal.SizeOf(si);
    PROCESS_INFORMATION pi;
    string cmd = "\"" + python + "\" server.py";
    // DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP. Breakaway is omitted; this process cannot use it.
    return CreateProcess(null, cmd, IntPtr.Zero, IntPtr.Zero, false, 0x00000208, IntPtr.Zero, dir, ref si, out pi);
  }
}
'@
  if (-not ("HbcDetached" -as [type])) { Add-Type -TypeDefinition $code }
  return [HbcDetached]::Run($Python, $WorkDir)
}
function Test-Up {
  try {
    $r = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 2
    return $r.StatusCode -eq 200
  } catch { return $false }
}
if (-not (Test-Up)) {
  $py = (Get-Command python -ErrorAction SilentlyContinue).Source
  if (-not $py) { throw "python not on PATH" }
  # Break away from the launcher's job so the server stays up after this script exits.
  $ok = Start-DetachedPython -Python $py -WorkDir $Root
  if (-not $ok) { throw "python server.py did not start" }
  $n = 0
  while (-not (Test-Up) -and $n -lt 20) { Start-Sleep -Milliseconds 250; $n++ }
}
if (-not (Test-Up)) { throw "HiveBrainComms did not start" }

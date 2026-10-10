# Sign or verify one bus job. The secret never leaves this process and is never printed.
param(
    [Parameter(Mandatory = $true)][ValidateSet("GrokBot", "Lead")][string]$Owner,
    [Parameter(Mandatory = $true)][string]$File,
    [Parameter(Mandatory = $true)][string]$Id,
    [switch]$Verify
)
$ErrorActionPreference = "Stop"
$fields = @("id", "from", "to", "lane", "priority", "time", "routed", "job", "done-when")
$keyName = @{ GrokBot = "bus-grokbot.bin"; Lead = "bus-lead.bin" }[$Owner]
$keyPath = Join-Path $env:USERPROFILE ".grok\keys\$keyName"
if (-not (Test-Path $keyPath)) { throw "signing key is missing" }

function Get-Secret {
    Add-Type -AssemblyName System.Security
    $protected = [System.IO.File]::ReadAllBytes($keyPath)
    return [System.Security.Cryptography.ProtectedData]::Unprotect(
        $protected, $null, [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
}

function Get-Canonical([hashtable]$map) {
    $lines = foreach ($name in $fields) {
        $value = ""
        if ($map.ContainsKey($name)) { $value = [string]$map[$name] }
        $value = ([regex]::Replace($value.Trim(), "\s+", " "))
        "$name=$value"
    }
    return ($lines -join "`n")
}

function Get-Mac([byte[]]$secret, [string]$text) {
    $hmac = New-Object System.Security.Cryptography.HMACSHA256
    try {
        $hmac.Key = $secret
        $hash = $hmac.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($text))
    } finally {
        $hmac.Dispose()
    }
    return (($hash | ForEach-Object { $_.ToString("x2") }) -join "")
}

function Test-SameHex([string]$left, [string]$right) {
    $a = [System.Text.Encoding]::ASCII.GetBytes($left)
    $b = [System.Text.Encoding]::ASCII.GetBytes($right)
    $diff = $a.Length -bxor $b.Length
    $count = [Math]::Max($a.Length, $b.Length)
    for ($i = 0; $i -lt $count; $i++) {
        $ba = if ($i -lt $a.Length) { $a[$i] } else { 0 }
        $bb = if ($i -lt $b.Length) { $b[$i] } else { 0 }
        $diff = $diff -bor ($ba -bxor $bb)
    }
    return $diff -eq 0
}

function Get-BlockMap([string[]]$blockLines) {
    $map = @{}
    $current = $null
    foreach ($line in $blockLines) {
        if ($line -match "^(id|from|to|lane|priority|time|routed|job|done-when|sig): ?(.*)$") {
            $current = $Matches[1]
            $map[$current] = $Matches[2]
        } elseif ($current) {
            $map[$current] = ($map[$current] + " " + $line).Trim()
        }
    }
    return $map
}

$text = [System.IO.File]::ReadAllText($File)
$lines = $text -split "`r?`n", -1
$start = -1
for ($i = 0; $i -lt $lines.Length; $i++) {
    if ($lines[$i] -match "^id: $( [regex]::Escape($Id) )$") { $start = $i; break }
}
if ($start -lt 0) { throw "job id not found" }
$end = $lines.Length
for ($i = $start + 1; $i -lt $lines.Length; $i++) {
    if ($lines[$i] -match "^(id: |## )") { $end = $i; break }
}
$block = $lines[$start..($end - 1)]
$map = Get-BlockMap $block
$secret = Get-Secret
try {
    $mac = Get-Mac $secret (Get-Canonical $map)
} finally {
    [Array]::Clear($secret, 0, $secret.Length)
}

if ($Verify) {
    $given = if ($map.ContainsKey("sig")) { [string]$map["sig"] } else { "" }
    $ok = (Test-SameHex $mac $given) -and $given.Length -gt 0
    if ($ok) { Write-Output "OK $Id"; exit 0 }
    "$(Get-Date -Format o) SPOOFED $Id" | Add-Content (Join-Path $env:TEMP "bus-sig.log")
    Write-Output "SPOOFED $Id"
    exit 3
}

$sigLine = "sig: $mac"
$replaced = $false
for ($i = $start; $i -lt $end; $i++) {
    if ($lines[$i] -match "^sig: ") { $lines[$i] = $sigLine; $replaced = $true }
}
if (-not $replaced) {
    $head = @()
    if ($start -gt 0) { $head = $lines[0..($start - 1)] }
    $mid = @($lines[$start..($end - 1)]) + @($sigLine)
    $tail = @()
    if ($end -lt $lines.Length) { $tail = $lines[$end..($lines.Length - 1)] }
    $lines = $head + $mid + $tail
}
[System.IO.File]::WriteAllText($File, ($lines -join "`r`n"))
Write-Output "SIGNED $Id"

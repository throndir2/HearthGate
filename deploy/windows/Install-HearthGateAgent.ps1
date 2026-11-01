<#
.SYNOPSIS
Installs or updates the HearthGate agent as a Windows service. Run in an elevated PowerShell.

.EXAMPLE
& .\Install-HearthGateAgent.ps1 -SourceDir .\agent-win-x64 -Token $token -AllowFrom 192.168.50.9
#>
param(
    [Parameter(Mandatory)] [string] $SourceDir,
    [Parameter(Mandatory)] [string] $Token,
    [string] $AllowFrom = "",
    [string] $InstallDir = "C:\HearthGate\Agent",
    [int] $Port = 7177,
    [string] $PalworldSettings = "C:\Program Files (x86)\Steam\steamapps\common\PalServer\Pal\Saved\Config\WindowsServer\PalWorldSettings.ini"
)
$ErrorActionPreference = "Stop"
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script in an elevated (administrator) PowerShell."
}
if ($Token.Length -lt 24) { throw "The token must have at least 24 characters." }

$svc = Get-Service HearthGateAgent -ErrorAction SilentlyContinue
if ($svc -and $svc.Status -ne "Stopped") { Stop-Service HearthGateAgent; $svc.WaitForStatus("Stopped", "00:00:30") }

New-Item -ItemType Directory -Force $InstallDir | Out-Null
$keepConfig = Test-Path (Join-Path $InstallDir "agent.yaml")
Get-ChildItem $SourceDir | Where-Object { -not ($keepConfig -and $_.Name -eq "agent.yaml") } | Copy-Item -Destination $InstallDir -Recurse -Force

# Secrets go to machine environment variables, never into files.
[Environment]::SetEnvironmentVariable("HG_AGENT_TOKEN", $Token, "Machine")
if (Test-Path $PalworldSettings) {
    $m = [regex]::Match((Get-Content $PalworldSettings -Raw), 'AdminPassword="([^"]*)"')
    if ($m.Success -and $m.Groups[1].Value) { [Environment]::SetEnvironmentVariable("HG_PALWORLD_ADMIN_PASSWORD", $m.Groups[1].Value, "Machine") }
}

$exe = Join-Path $InstallDir "HearthGate.Agent.exe"
if (-not $svc) {
    New-Service -Name HearthGateAgent -BinaryPathName "`"$exe`"" -DisplayName "HearthGate Agent" -StartupType Automatic -Description "Host health and game-server control for HearthGate." | Out-Null
}
sc.exe failure HearthGateAgent reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null

Get-NetFirewallRule -DisplayName "HearthGate Agent" -ErrorAction SilentlyContinue | Remove-NetFirewallRule
$rule = @{ DisplayName = "HearthGate Agent"; Direction = "Inbound"; Protocol = "TCP"; LocalPort = $Port; Action = "Allow"; Profile = "Any" }
if ($AllowFrom) { $rule.RemoteAddress = $AllowFrom }
New-NetFirewallRule @rule | Out-Null

Start-Service HearthGateAgent
Get-Service HearthGateAgent | Format-Table Name, Status, StartType -AutoSize

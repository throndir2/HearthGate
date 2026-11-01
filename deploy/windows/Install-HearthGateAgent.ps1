<#
.SYNOPSIS
Installs or updates the HearthGate agent on a Windows game PC.

.DESCRIPTION
The agent runs as the signed-in user from a scheduled task at logon (no window), so game servers
start in the same desktop session as when you start them by hand. A LocalSystem service would
run in session 0, where windowed servers and graceful window closes do not work.

The token goes into a user environment variable (no file). Only the firewall rule needs
administrator rights; if this shell is not elevated, Windows asks once (UAC) for that step.

.EXAMPLE
& .\Install-HearthGateAgent.ps1 -SourceDir .\agent-win-x64 -Token $token -AllowFrom 192.168.50.9
#>
param(
    [Parameter(Mandatory)] [string] $SourceDir,
    [string] $Token = "",
    [string] $AllowFrom = "",
    [string] $InstallDir = "C:\HearthGate\Agent",
    [int] $Port = 7177,
    [switch] $ReplaceConfig
)
$ErrorActionPreference = "Stop"
$taskName = "HearthGate Agent"
$exe = Join-Path $InstallDir "HearthGate.Agent.exe"

if ($Token) {
    if ($Token.Length -lt 24) { throw "The token must have at least 24 characters." }
    [Environment]::SetEnvironmentVariable("HG_AGENT_TOKEN", $Token, "User")
}
elseif (-not [Environment]::GetEnvironmentVariable("HG_AGENT_TOKEN", "User")) {
    throw "Give -Token the first time (the user variable HG_AGENT_TOKEN is not set)."
}

# Stop the running agent before the files are replaced.
if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) { Stop-ScheduledTask -TaskName $taskName }
Get-CimInstance Win32_Process -Filter "Name='HearthGate.Agent.exe'" | Where-Object { $_.ExecutablePath -eq $exe } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
Start-Sleep -Seconds 1

New-Item -ItemType Directory -Force $InstallDir | Out-Null
$keepConfig = (Test-Path (Join-Path $InstallDir "agent.yaml")) -and -not $ReplaceConfig
Get-ChildItem $SourceDir | Where-Object { -not ($keepConfig -and $_.Name -eq "agent.yaml") } | Copy-Item -Destination $InstallDir -Recurse -Force

$user = "$env:USERDOMAIN\$env:USERNAME"
$action = New-ScheduledTaskAction -Execute $exe -WorkingDirectory $InstallDir
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $user
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) `
    -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew -StartWhenAvailable
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings -Principal $principal `
    -Description "HearthGate host agent: health and game-server control for HearthGate." -Force | Out-Null
Start-ScheduledTask -TaskName $taskName

# Firewall: allow the agent port only from the HearthGate server.
$remote = if ($AllowFrom) { $AllowFrom } else { "Any" }
$fw = "Get-NetFirewallRule -DisplayName 'HearthGate Agent' -ErrorAction SilentlyContinue | Remove-NetFirewallRule; " +
      "New-NetFirewallRule -DisplayName 'HearthGate Agent' -Direction Inbound -Protocol TCP -LocalPort $Port -RemoteAddress $remote -Action Allow -Profile Any | Out-Null"
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if ($isAdmin) { Invoke-Expression $fw }
else { Start-Process powershell.exe -Verb RunAs -Wait -WindowStyle Hidden -ArgumentList "-NoProfile", "-Command", $fw }

Start-Sleep -Seconds 3
Get-ScheduledTask -TaskName $taskName | Select-Object TaskName, State | Format-Table -AutoSize
Get-NetFirewallRule -DisplayName "HearthGate Agent" -ErrorAction SilentlyContinue | Select-Object DisplayName, Enabled, Action | Format-Table -AutoSize

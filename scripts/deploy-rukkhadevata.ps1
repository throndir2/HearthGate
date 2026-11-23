<#
.SYNOPSIS
Builds HearthGate on a Docker host over SSH and deploys it with Compose, a health check and rollback.

.DESCRIPTION
The defaults deploy to the NAS "rukkhadevata" (SSH alias), stack folder /Volume1/apps/hearthgate.
The script sends a `git archive` of -Ref to the host and builds the server and agent images there,
so the local computer needs no Docker. It writes the new image tags to the stack .env file
(HEARTHGATE_IMAGE, HEARTHGATE_AGENT_IMAGE), runs `docker compose up -d`, and waits for /healthz on
HEARTHGATE_BIND_ADDRESS:HEARTHGATE_PORT from that .env. If the check fails, it restores the previous
.env and starts the previous images again. -Rollback restores the newest deploy backup of .env.
#>
param(
    [string]$SshHost = 'rukkhadevata',
    [string]$StackDir = '/Volume1/apps/hearthgate',
    [string]$DockerBin = '/Volume1/@apps/DockerEngine/dockerd/bin',
    [string]$Ref = 'HEAD',
    [string]$Tag,
    [switch]$SkipBuild,
    [switch]$Rollback,
    [int]$HealthTimeoutSeconds = 120
)
$ErrorActionPreference = 'Stop'

function Invoke-Remote([string]$Script) {
    $Script | ssh $SshHost "tr -d '\r' | sh -s"
    if ($LASTEXITCODE -ne 0) { throw "Remote step failed on $SshHost (exit $LASTEXITCODE)." }
}

$prelude = @"
set -e
export PATH='$DockerBin':`$PATH
cd '$StackDir'
health() {
  bind=`$(grep '^HEARTHGATE_BIND_ADDRESS=' .env | cut -d= -f2-); port=`$(grep '^HEARTHGATE_PORT=' .env | cut -d= -f2-)
  url="http://`${bind:-127.0.0.1}:`${port:-3110}/healthz"; i=0
  while [ `$i -lt $HealthTimeoutSeconds ]; do
    if [ "`$(curl -fsS -m 5 "`$url" 2>/dev/null)" = ok ]; then echo "Healthy: `$url"; return 0; fi
    sleep 3; i=`$((i+3))
  done
  echo "Not healthy after $HealthTimeoutSeconds s: `$url"; return 1
}
"@

if ($Rollback) {
    Invoke-Remote @"
$prelude
last=`$(ls -1t .env.bak-deploy-* 2>/dev/null | head -1)
[ -n "`$last" ] || { echo 'No .env.bak-deploy-* backup found.'; exit 1; }
cp .env ".env.bak-rollback-`$(date +%Y%m%d-%H%M%S)"; cp "`$last" .env; echo "Restored `$last"
docker compose up -d
health
"@
    return
}

if (-not $Tag) {
    $sha = (git rev-parse --short $Ref).Trim()
    if ($LASTEXITCODE -ne 0) { throw "Unknown git ref: $Ref" }
    $Tag = "0.1.0-$(Get-Date -Format 'yyyyMMdd-HHmm')-$sha"
}

if (-not $SkipBuild) {
    $tar = Join-Path ([IO.Path]::GetTempPath()) "hearthgate-src-$Tag.tar"
    try {
        git archive --format=tar -o $tar $Ref
        if ($LASTEXITCODE -ne 0) { throw 'git archive failed.' }
        Write-Host "Uploading source ($Ref) to $SshHost ..."
        cmd /c "ssh $SshHost `"cat > /tmp/hearthgate-src.tar`" < `"$tar`""
        if ($LASTEXITCODE -ne 0) { throw 'Upload failed.' }
    } finally { Remove-Item $tar -ErrorAction SilentlyContinue }

    Write-Host "Building hearthgate:$Tag and hearthgate-agent:$Tag on $SshHost ..."
    Invoke-Remote @"
set -e
export PATH='$DockerBin':`$PATH DOCKER_BUILDKIT=0
trap 'rm -f /tmp/hearthgate-src.tar' EXIT
docker build -t 'hearthgate:$Tag' -f Dockerfile - < /tmp/hearthgate-src.tar
docker build -t 'hearthgate-agent:$Tag' -f Dockerfile.agent - < /tmp/hearthgate-src.tar
"@
}

Write-Host "Deploying $Tag to ${SshHost}:$StackDir ..."
Invoke-Remote @"
$prelude
bak=".env.bak-deploy-`$(date +%Y%m%d-%H%M%S)"; cp .env "`$bak"
setenv() { if grep -q "^`$1=" .env; then sed -i "s|^`$1=.*|`$1=`$2|" .env; else echo "`$1=`$2" >> .env; fi; }
setenv HEARTHGATE_IMAGE 'hearthgate:$Tag'
setenv HEARTHGATE_AGENT_IMAGE 'hearthgate-agent:$Tag'
docker compose up -d
if health; then exit 0; fi
echo 'Rolling back to the previous images.'
cp "`$bak" .env
docker compose up -d
health || true
exit 1
"@
Write-Host "Deployed $Tag."

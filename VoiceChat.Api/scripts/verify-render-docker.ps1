# Verifies Docker + Render files at monorepo root (where Render expects them).
# Run from repository root:
#   powershell -File Api\VoiceChat.Api\scripts\verify-render-docker.ps1
$ErrorActionPreference = 'Stop'
# scripts/ -> VoiceChat.Api/ -> Api/ -> repo root
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')
$dockerfile = Join-Path $root 'Dockerfile'
$ignore = Join-Path $root '.dockerignore'
$blueprint = Join-Path $root 'render.yaml'

foreach ($p in @($dockerfile, $ignore, $blueprint)) {
    if (-not (Test-Path -LiteralPath $p)) {
        Write-Error "Missing required path: $p"
        exit 1
    }
}

Write-Host "OK: Render / Docker files exist at repo root:"
Write-Host "  - Dockerfile"
Write-Host "  - .dockerignore"
Write-Host "  - render.yaml"
exit 0

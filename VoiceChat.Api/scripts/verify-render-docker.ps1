# Verifies Docker + blueprint files for this API exist (paths relative to monorepo root).
# Run from repository root:
#   powershell -File Api\VoiceChat.Api\scripts\verify-render-docker.ps1
$ErrorActionPreference = 'Stop'
# scripts/ -> VoiceChat.Api/ -> Api/ -> repo root
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')
$dockerfile = Join-Path $root 'Api\VoiceChat.Api\Dockerfile'
$ignore = Join-Path $root 'Api\VoiceChat.Api\.dockerignore'
$blueprint = Join-Path $root 'Api\VoiceChat.Api\render.yaml'

foreach ($p in @($dockerfile, $ignore, $blueprint)) {
    if (-not (Test-Path -LiteralPath $p)) {
        Write-Error "Missing required path: $p"
        exit 1
    }
}

Write-Host "OK: Render / Docker files exist:"
Write-Host "  - Api/VoiceChat.Api/Dockerfile"
Write-Host "  - Api/VoiceChat.Api/.dockerignore"
Write-Host "  - Api/VoiceChat.Api/render.yaml"
exit 0

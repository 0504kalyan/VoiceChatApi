# Copies repo-root Dockerfile assets for a standalone VoiceChatApi-style clone (VoiceChat.sln at clone root).
#
# Usage:
#   powershell -File Api\VoiceChat.Api\sync-to-VoiceChatApi-clone.ps1 -VoiceChatApiRepoRoot "D:\path\to\VoiceChatApi"
#
param(
    [Parameter(Mandatory = $true)]
    [string] $VoiceChatApiRepoRoot
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$apiProjectDir = Resolve-Path (Join-Path $here '..')
$monorepoRoot = Resolve-Path (Join-Path $here '..\..\..')
$target = Resolve-Path -LiteralPath $VoiceChatApiRepoRoot
$projDir = Join-Path $target 'VoiceChat.Api'
$csproj = Join-Path $projDir 'VoiceChat.Api.csproj'

if (-not (Test-Path -LiteralPath $csproj)) {
    Write-Error "Expected: $csproj — pass the clone root (contains VoiceChat.Api\)."
}

Copy-Item -LiteralPath (Join-Path $apiProjectDir 'Dockerfile.for-VoiceChatApi-github-root') -Destination (Join-Path $target 'Dockerfile') -Force

$ignoreSrc = Join-Path $monorepoRoot '.dockerignore'
if (Test-Path -LiteralPath $ignoreSrc) {
    Copy-Item -LiteralPath $ignoreSrc -Destination (Join-Path $target '.dockerignore') -Force
    Write-Host "Copied .dockerignore -> $target"
}

Write-Host "Copied Dockerfile -> $target\Dockerfile"
Write-Host ""
Write-Host "Next (repo root):"
Write-Host "  git add Dockerfile .dockerignore"
Write-Host "  git commit -m ""Add Dockerfile at repo root"""
Write-Host "  git push origin main"

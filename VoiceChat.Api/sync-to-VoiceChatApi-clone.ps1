# Copies repo-root Dockerfile + render.yaml for https://github.com/0504kalyan/VoiceChatApi
# (VoiceChat.sln at root, project under VoiceChat.Api/). Dockerfile belongs at REPO ROOT.
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
    Write-Error "Expected: $csproj — pass the VoiceChatApi clone root (contains VoiceChat.Api\)."
}

Copy-Item -LiteralPath (Join-Path $apiProjectDir 'Dockerfile.for-VoiceChatApi-github-root') -Destination (Join-Path $target 'Dockerfile') -Force
Copy-Item -LiteralPath (Join-Path $apiProjectDir 'render.standalone-repo.yaml') -Destination (Join-Path $target 'render.yaml') -Force

$ignoreSrc = Join-Path $monorepoRoot '.dockerignore'
if (Test-Path -LiteralPath $ignoreSrc) {
    Copy-Item -LiteralPath $ignoreSrc -Destination (Join-Path $target '.dockerignore') -Force
    Write-Host "Copied .dockerignore -> $target"
}

Write-Host "Copied Dockerfile -> $target\Dockerfile"
Write-Host "Copied render.yaml -> $target\render.yaml"
Write-Host ""
Write-Host "Next (repo root):"
Write-Host "  git add Dockerfile render.yaml .dockerignore"
Write-Host "  git commit -m ""Dockerfile at repo root for Render"""
Write-Host "  git push origin main"
Write-Host ""
Write-Host "Render: Root Directory = . ; Dockerfile Path = Dockerfile ; Context = ."

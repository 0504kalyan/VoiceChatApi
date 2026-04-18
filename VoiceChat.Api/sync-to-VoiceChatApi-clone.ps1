# Copies Docker + Render files for the VoiceChatApi repo layout:
#   repo root: VoiceChat.sln
#   VoiceChat.Api/Dockerfile, VoiceChat.Api.csproj, ...
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
$target = Resolve-Path -LiteralPath $VoiceChatApiRepoRoot
$projDir = Join-Path $target 'VoiceChat.Api'
$csproj = Join-Path $projDir 'VoiceChat.Api.csproj'

if (-not (Test-Path -LiteralPath $csproj)) {
    Write-Error "Expected project at: $csproj — pass the clone root of https://github.com/0504kalyan/VoiceChatApi (folder containing VoiceChat.Api\)."
}

Copy-Item -LiteralPath (Join-Path $here 'Dockerfile') -Destination (Join-Path $projDir 'Dockerfile') -Force
Copy-Item -LiteralPath (Join-Path $here '.dockerignore') -Destination (Join-Path $projDir '.dockerignore') -Force
Copy-Item -LiteralPath (Join-Path $here 'render.standalone-repo.yaml') -Destination (Join-Path $target 'render.yaml') -Force

Write-Host "Copied Dockerfile, .dockerignore -> $projDir"
Write-Host "Copied render.yaml -> $target"
Write-Host ""
Write-Host "Next (repo root):"
Write-Host "  git add VoiceChat.Api/Dockerfile VoiceChat.Api/.dockerignore render.yaml"
Write-Host "  git commit -m ""Add Docker + Render paths for VoiceChat.Api subfolder"""
Write-Host "  git push origin main"
Write-Host ""
Write-Host "Render: Root Directory = empty or . ; Dockerfile Path = VoiceChat.Api/Dockerfile ; Context = VoiceChat.Api"
Write-Host "Or use Blueprint from render.yaml at repo root."

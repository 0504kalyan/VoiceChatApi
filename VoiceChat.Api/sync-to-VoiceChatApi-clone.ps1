# Copies Docker + Render files from THIS folder (Api/VoiceChat.Api) into your standalone
# VoiceChatApi Git clone so you can commit and fix Render "Dockerfile not found".
#
# Usage (PowerShell, from repo root or this folder):
#   powershell -File Api\VoiceChat.Api\sync-to-VoiceChatApi-clone.ps1 -VoiceChatApiRepoRoot "D:\path\to\VoiceChatApi"
#
param(
    [Parameter(Mandatory = $true)]
    [string] $VoiceChatApiRepoRoot
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$target = Resolve-Path -LiteralPath $VoiceChatApiRepoRoot

$csproj = Join-Path $target 'VoiceChat.Api.csproj'
if (-not (Test-Path -LiteralPath $csproj)) {
    Write-Error "VoiceChat.Api.csproj not found at: $csproj — wrong folder? Clone https://github.com/0504kalyan/VoiceChatApi and pass that path."
}

Copy-Item -LiteralPath (Join-Path $here 'Dockerfile') -Destination (Join-Path $target 'Dockerfile') -Force
Copy-Item -LiteralPath (Join-Path $here '.dockerignore') -Destination (Join-Path $target '.dockerignore') -Force
Copy-Item -LiteralPath (Join-Path $here 'render.standalone-repo.yaml') -Destination (Join-Path $target 'render.yaml') -Force

Write-Host "Copied Dockerfile, .dockerignore, render.yaml -> $target"
Write-Host ""
Write-Host "Next (in that repo):"
Write-Host "  git status"
Write-Host "  git add Dockerfile .dockerignore render.yaml"
Write-Host "  git commit -m ""Add Docker files for Render"""
Write-Host "  git push origin main"
Write-Host "Then redeploy on Render. Root Directory = empty; Dockerfile Path = Dockerfile"

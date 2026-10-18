# Builds Trade Tracker, publishes it as a GitHub release and updates repo.json, so everyone who
# added the repo gets the new version in /xlplugins.
#
# Usage: .\release.ps1 1.0.1.0
param([Parameter(Mandatory = $true)][string]$Version)

# Not 'Stop': git and gh print progress on stderr, which Windows PowerShell can mistake for errors.
# Each native command's exit code is checked instead.
Set-Location $PSScriptRoot

$owner = 'Stealthzi'
$repo = 'TradeTracker'
$tag = "v$Version"
$out = 'TradeTracker\bin\Release\TradeTracker'

dotnet build TradeTracker\TradeTracker.csproj -c Release -nologo -v q "-p:Version=$Version"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

# repo.json is the built plugin manifest plus where to download it.
$manifest = Get-Content "$out\TradeTracker.json" -Raw -ErrorAction Stop | ConvertFrom-Json
$download = "https://github.com/$owner/$repo/releases/download/$tag/latest.zip"
$manifest | Add-Member -Force DownloadLinkInstall $download
$manifest | Add-Member -Force DownloadLinkUpdate $download
$manifest | Add-Member -Force DownloadLinkTesting $download
$manifest | Add-Member -Force LastUpdate ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())
$json = ConvertTo-Json -InputObject @($manifest) -Depth 5
[IO.File]::WriteAllText("$PSScriptRoot\repo.json", $json, (New-Object Text.UTF8Encoding $false))

git add -A
git commit -m "Release $Version"
if ($LASTEXITCODE -ne 0) { throw 'git commit failed' }
git push origin HEAD
if ($LASTEXITCODE -ne 0) { throw 'git push failed' }

gh release create $tag "$out\latest.zip" --repo "$owner/$repo" --title "Trade Tracker $Version" --notes "Trade Tracker $Version"
if ($LASTEXITCODE -ne 0) { throw 'Creating the GitHub release failed' }

Write-Host "Released $Version. Players get it from https://raw.githubusercontent.com/$owner/$repo/main/repo.json"

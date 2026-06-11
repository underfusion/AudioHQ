# Builds a self-contained, single-file, portable AudioHQ release and zips it.
# Output: release/AudioHQ-<version>-win-x64/AudioHQ.exe  and the matching .zip.
# Version is read from Directory.Build.props (the single source of truth).
$ErrorActionPreference = 'Stop'

$root  = Split-Path -Parent $PSScriptRoot          # repo root (tools/..)
$proj  = Join-Path $root 'src/AudioHQ.App/AudioHQ.App.csproj'
$props = Join-Path $root 'Directory.Build.props'

# Canonical version.
$verMatch = Select-String -Path $props -Pattern '<Version>([^<]+)</Version>'
if (-not $verMatch) { throw "Version not found in $props" }
$version = $verMatch.Matches[0].Groups[1].Value
Write-Host "AudioHQ version: $version"

$rid        = 'win-x64'
$relName    = "AudioHQ-$version-$rid"
$releaseDir = Join-Path $root 'release'
$stageDir   = Join-Path $releaseDir $relName
$zipPath    = Join-Path $releaseDir "$relName-portable.zip"

# A running instance would lock the build output.
Get-Process AudioHQ -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

# Clean previous staging/zip for this version.
if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }
if (Test-Path $zipPath)  { Remove-Item $zipPath -Force }
New-Item -ItemType Directory -Path $stageDir -Force | Out-Null

Write-Host "Publishing (self-contained, single-file, $rid)..."
dotnet publish $proj -c Release -r $rid --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugType=none -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$publishDir = Join-Path $root "src/AudioHQ.App/bin/Release/net7.0-windows/$rid/publish"
if (-not (Test-Path $publishDir)) { throw "publish output not found: $publishDir" }

# Stage the published output, minus debug symbols.
Get-ChildItem $publishDir -File | Where-Object { $_.Extension -ne '.pdb' } |
    ForEach-Object { Copy-Item $_.FullName -Destination $stageDir }

# Zip the named folder (so unzip yields AudioHQ-<version>-win-x64/).
Compress-Archive -Path $stageDir -DestinationPath $zipPath -Force

$exe = Get-Item (Join-Path $stageDir 'AudioHQ.exe')
$zip = Get-Item $zipPath
Write-Host ""
Write-Host "Portable build ready:"
Write-Host ("  Folder : {0}" -f $stageDir)
Write-Host ("  Exe    : {0:N1} MB" -f ($exe.Length / 1MB))
Write-Host ("  Zip    : {0}" -f $zip.FullName)
Write-Host ("           {0:N1} MB" -f ($zip.Length / 1MB))

# Copies the runtime data folders (lang, img, gameref) that are not stored in the repo
# from the upstream v0.3.11.0 release zip into a build output folder.
# The zip is downloaded once and cached in %LOCALAPPDATA%\TS SE Tool\build-cache.
param(
    [string]$Target = (Join-Path $PSScriptRoot "..\TS SE Tool\bin\Release")
)
$ErrorActionPreference = "Stop"

$cache = Join-Path $env:LOCALAPPDATA "TS SE Tool\build-cache"
$zip = Join-Path $cache "TS.SE.Tool.0.3.11.0.zip"
$extracted = Join-Path $cache "0.3.11.0"

if (-not (Test-Path $zip)) {
    New-Item -ItemType Directory -Force $cache | Out-Null
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest "https://github.com/LIPtoH/TS-SE-Tool/releases/download/v0.3.11.0/TS.SE.Tool.0.3.11.0.zip" -OutFile $zip
}
if (-not (Test-Path $extracted)) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($zip, $extracted)
}

foreach ($folder in "lang", "img", "gameref") {
    $dest = Join-Path $Target $folder
    # Merge without overwriting files that are newer in the target (e.g. edited lang files)
    robocopy (Join-Path $extracted $folder) $dest /E /XO /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed for $folder ($LASTEXITCODE)" }
}
Write-Host "Runtime data staged into $Target"

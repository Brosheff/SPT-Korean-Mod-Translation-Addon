param(
    [Parameter(Mandatory = $true)]
    [string]$SPTPath,

    [string]$Configuration = "Release",

    [string]$PythonExe = "python"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Fail([string]$Message) {
    Write-Host "ERROR: $Message" -ForegroundColor Red
    exit 1
}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$spt = [System.IO.Path]::GetFullPath($SPTPath.TrimEnd('\','/'))

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Fail "dotnet SDK was not found in PATH."
}
if (-not (Test-Path (Join-Path $spt 'BepInEx\core\BepInEx.dll'))) {
    Fail "SPTPath does not look like an SPT client root: BepInEx\\core\\BepInEx.dll is missing."
}
if (-not (Test-Path (Join-Path $spt 'SPT_Runtime\SPTarkov.Server.Core.dll'))) {
    Fail "SPTPath does not look like SPT 4.1 server root: SPT_Runtime\\SPTarkov.Server.Core.dll is missing."
}

$clientProject = Join-Path $root 'SPT_Mod_Korean_Addon.csproj'
$serverProject = Join-Path $root 'server-src\SPT_Mod_Korean_Server\SPT_Mod_Korean_Server.csproj'
$restoreConfig = Join-Path $root 'tools\NuGet.Offline.Config'

[xml]$projectXml = Get-Content -LiteralPath $clientProject -Raw
$releaseVersion = $projectXml.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
if ($releaseVersion -notmatch '^\d+\.\d+\.\d+$') { Fail 'Invalid client release version.' }
if (-not (Get-Command $PythonExe -ErrorAction SilentlyContinue)) { Fail 'Python 3 is required for translation validation. Pass -PythonExe with its full path.' }
& $PythonExe (Join-Path $root 'tools\validate_translations.py')
if ($LASTEXITCODE -ne 0) { Fail 'Translation validation failed.' }
& $PythonExe (Join-Path $root 'tools\verify_quartermaster_contracts.py')
if ($LASTEXITCODE -ne 0) { Fail 'Quartermaster contract catalog verification failed.' }
& $PythonExe (Join-Path $root 'tools\build_supported_mods.py')
if ($LASTEXITCODE -ne 0) { Fail 'Supported mod list generation failed.' }

Write-Host '[1/4] Building client addon...' -ForegroundColor Cyan
& dotnet build $clientProject -c $Configuration "-p:ClientRoot=$spt" "-p:RestoreConfigFile=$restoreConfig"
if ($LASTEXITCODE -ne 0) { Fail "Client addon build failed." }

Write-Host '[2/4] Building server addon...' -ForegroundColor Cyan
& dotnet build $serverProject -c $Configuration "-p:SPTPath=$spt" "-p:RestoreConfigFile=$restoreConfig"
if ($LASTEXITCODE -ne 0) { Fail "Server addon build failed." }

$clientDll = Join-Path $root "bin\$Configuration\net48\SPT_Mod_Korean_Addon.dll"
$serverDll = Join-Path $root "server-src\SPT_Mod_Korean_Server\bin\$Configuration\net10.0\SPT_Mod_Korean_Server.dll"
if (-not (Test-Path $clientDll)) { Fail "Built client DLL not found: $clientDll" }
if (-not (Test-Path $serverDll)) { Fail "Built server DLL not found: $serverDll" }

Write-Host '[3/4] Creating install layout...' -ForegroundColor Cyan
$distRoot = Join-Path $root '_dist'
$packageName = "SPT-Korean-Mod-Translation-Addon-SPT4.1.6-$releaseVersion"
$stage = Join-Path $distRoot $packageName
if ((Test-Path -LiteralPath $stage) -or (Test-Path -LiteralPath (Join-Path $distRoot ($packageName + '.zip')))) {
    # Preserve previous packages and local work. Never recursively delete an existing stage.
    $packageName += '-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
    $stage = Join-Path $distRoot $packageName
    if (Test-Path -LiteralPath $stage) { Fail 'Distribution stage already exists.' }
}
New-Item -ItemType Directory -Force -Path $stage | Out-Null

$clientDest = Join-Path $stage 'BepInEx\plugins\SPT_Mod_Korean_Addon'
$serverDest = Join-Path $stage 'SPT_Runtime\user\mods\SPT_Mod_Korean_Server'
New-Item -ItemType Directory -Force -Path $clientDest, $serverDest | Out-Null

Copy-Item $clientDll (Join-Path $clientDest 'SPT_Mod_Korean_Addon.dll') -Force
Copy-Item (Join-Path $root 'translations') (Join-Path $clientDest 'translations') -Recurse -Force
Copy-Item (Join-Path $root 'config-ui') (Join-Path $clientDest 'config-ui') -Recurse -Force
Copy-Item (Join-Path $root 'native-locales') (Join-Path $clientDest 'native-locales') -Recurse -Force
Copy-Item (Join-Path $root 'contract-locales') (Join-Path $clientDest 'contract-locales') -Recurse -Force
Copy-Item $serverDll (Join-Path $serverDest 'SPT_Mod_Korean_Server.dll') -Force
# Release ZIP contains runtime/install files only. Source documentation stays in the GitHub repository.

Write-Host '[4/4] Creating ZIP and SHA256...' -ForegroundColor Cyan
$zip = Join-Path $distRoot ($packageName + '.zip')
if (Test-Path -LiteralPath $zip) { Fail 'Distribution ZIP already exists; refusing to overwrite.' }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
$sha = [System.Security.Cryptography.SHA256]::Create()
$zipStream = [System.IO.File]::OpenRead($zip)
try { $hash = [BitConverter]::ToString($sha.ComputeHash($zipStream)).Replace('-', '').ToLowerInvariant() }
finally { $zipStream.Dispose(); $sha.Dispose() }
$hashFile = $zip + '.sha256.txt'
Set-Content -LiteralPath $hashFile -Value ("$hash  " + [System.IO.Path]::GetFileName($zip)) -Encoding ASCII

Write-Host ''
Write-Host 'DONE' -ForegroundColor Green
Write-Host "Distribution ZIP: $zip"
Write-Host "SHA256:          $hashFile"

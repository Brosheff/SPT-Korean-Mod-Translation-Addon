param(
    # Reference assemblies only; never accept or discover a game installation directory.
    [Parameter(Position = 0)] [string]$ReferenceRoot = '',
    [Parameter(Position = 1)] [string]$PythonExe = 'python',
    [string]$Configuration = 'Release',
    [string]$HandoffManifest = '',
    [string]$CandidateInventory = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Fail([string]$Message) {
    Write-Host "ERROR: $Message" -ForegroundColor Red
    exit 1
}
function Require-File([string]$Path, [string]$Label) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { Fail "$Label is missing: $Path" }
}
function Run-Python([string[]]$Arguments, [string]$Label) {
    & $PythonExe @Arguments
    if ($LASTEXITCODE -ne 0) { Fail "$Label failed (exit code $LASTEXITCODE)." }
}
function Run-Dotnet([string[]]$Arguments, [string]$Label) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { Fail "$Label failed (exit code $LASTEXITCODE)." }
}

# Always resolve the project relative to the script; never inspect a game installation.
$root = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ReferenceRoot)) {
    if (-not [string]::IsNullOrWhiteSpace($env:ADDON_REFERENCE_ROOT)) {
        $ReferenceRoot = $env:ADDON_REFERENCE_ROOT
    } else {
        $ReferenceRoot = Join-Path $root 'references'
    }
}
$ReferenceRoot = $ReferenceRoot.Trim().Trim('"')
try { $refs = [System.IO.Path]::GetFullPath($ReferenceRoot) }
catch { Fail "Invalid reference library path: $ReferenceRoot" }

# Only the listed .dll reference assemblies are required; no whole game folder is used.
$clientReferenceNames = @(
    'BepInEx.dll', '0Harmony.dll', 'UnityEngine.dll',
    'UnityEngine.CoreModule.dll', 'UnityEngine.IMGUIModule.dll',
    'Unity.TextMeshPro.dll', 'UnityEngine.UI.dll',
    'UnityEngine.UIModule.dll', 'Newtonsoft.Json.dll', 'spt-common.dll'
)
$serverReferenceNames = @(
    'SPTarkov.Server.Core.dll', 'SPTarkov.Common.dll',
    'SPTarkov.DI.dll', 'SemanticVersioning.dll', '0Harmony.dll'
)
$missing = @()
foreach ($name in $clientReferenceNames) {
    $path = Join-Path (Join-Path $refs 'client') $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { $missing += "client/$name" }
}
foreach ($name in $serverReferenceNames) {
    $path = Join-Path (Join-Path $refs 'server') $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { $missing += "server/$name" }
}
if ($missing.Count -gt 0) {
    Write-Host "ERROR: Missing local reference DLLs in: $refs" -ForegroundColor Red
    $missing | ForEach-Object { Write-Host " - $_" }
    Write-Host 'Provide compatible reference DLLs under references/client and references/server.'
    Write-Host 'No game installation directory is required or searched.'
    exit 1
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { Fail 'dotnet SDK was not found in PATH.' }
if (-not (Get-Command $PythonExe -ErrorAction SilentlyContinue)) { Fail "Python executable not found: $PythonExe" }

$clientProject = Join-Path $root 'SPT_Mod_Korean_Addon.csproj'
$serverProject = Join-Path $root 'server-src\SPT_Mod_Korean_Server\SPT_Mod_Korean_Server.csproj'
$restoreConfig = Join-Path $root 'docs\tools\NuGet.Offline.Config'
Require-File $clientProject 'Client project'
Require-File $serverProject 'Server project'
Require-File $restoreConfig 'Offline NuGet config'
[xml]$projectXml = Get-Content -LiteralPath $clientProject -Raw
$versionNode = $projectXml.SelectSingleNode('/Project/PropertyGroup/Version')
if ($null -eq $versionNode -or $versionNode.InnerText -notmatch '^\d+\.\d+\.\d+$') { Fail 'Invalid client release version.' }
$releaseVersion = $versionNode.InnerText

Write-Host "Building SPT Korean Addon v$releaseVersion" -ForegroundColor Cyan
Write-Host "Project: $root"
Write-Host "References: $refs"

# Source documentation is GPT/developer guidance, not a build dependency.
# Do not migrate, move, delete, or require docs/note files during a DLL build.
Run-Python @((Join-Path $root 'docs\tools\verify_workflow.py'), 'source') 'Translation source validation'
Run-Python @((Join-Path $root 'docs\tools\validate_translations.py')) 'Translation JSON validation'
Run-Python @((Join-Path $root 'docs\tools\verify_quartermaster_contracts.py')) 'Quartermaster contract validation'
Run-Python @((Join-Path $root 'docs\tools\build_supported_mods.py')) 'Support list generation'

# Optional developer audit, never implicitly required for an ordinary build.
if ([bool]$HandoffManifest -ne [bool]$CandidateInventory) {
    Fail 'Both -HandoffManifest and -CandidateInventory are required together.'
}
if ($HandoffManifest) {
    Run-Python @((Join-Path $root 'docs\tools\verify_workflow.py'), 'handoff', '--manifest', $HandoffManifest, '--inventory', $CandidateInventory, '--require-complete') 'Candidate coverage audit'
}

# Both projects resolve their compile-only reference DLLs from this directory.
# Environment variable avoids command-line quoting problems with Unicode paths.
$env:ADDON_REFERENCE_ROOT = $refs
Write-Host '[1/4] Restoring and building client...' -ForegroundColor Cyan
Run-Dotnet @('restore', $clientProject, '--configfile', $restoreConfig) 'Client restore'
Run-Dotnet @('build', $clientProject, '-c', $Configuration, '--no-restore') 'Client compilation'
Write-Host '[2/4] Restoring and building server...' -ForegroundColor Cyan
Run-Dotnet @('restore', $serverProject, '--configfile', $restoreConfig) 'Server restore'
Run-Dotnet @('build', $serverProject, '-c', $Configuration, '--no-restore') 'Server compilation'

$clientDll = Join-Path $root "bin\$Configuration\net48\SPT_Mod_Korean_Addon.dll"
$serverDll = Join-Path $root "server-src\SPT_Mod_Korean_Server\bin\$Configuration\net10.0\SPT_Mod_Korean_Server.dll"
Require-File $clientDll 'Compiled client DLL'
Require-File $serverDll 'Compiled server DLL'

Write-Host '[3/4] Creating release layout...' -ForegroundColor Cyan
$distRoot = Join-Path $root '_dist'
[System.IO.Directory]::CreateDirectory($distRoot) | Out-Null
$baseName = "SPT-Korean-Mod-Translation-Addon-SPT4.1.6-$releaseVersion"
$packageName = $baseName
$number = 0
while ((Test-Path -LiteralPath (Join-Path $distRoot $packageName)) -or
       (Test-Path -LiteralPath (Join-Path $distRoot ($packageName + '.zip')))) {
    $number++
    $packageName = "$baseName-$(Get-Date -Format 'yyyyMMdd-HHmmss')-$number"
}
$stage = Join-Path $distRoot $packageName
[System.IO.Directory]::CreateDirectory($stage) | Out-Null
$clientDest = Join-Path $stage 'BepInEx\plugins\SPT_Mod_Korean_Addon'
$serverDest = Join-Path $stage 'SPT_Runtime\user\mods\SPT_Mod_Korean_Server'
[System.IO.Directory]::CreateDirectory($clientDest) | Out-Null
[System.IO.Directory]::CreateDirectory($serverDest) | Out-Null
[System.IO.File]::Copy($clientDll, (Join-Path $clientDest 'SPT_Mod_Korean_Addon.dll'), $true)
[System.IO.File]::Copy($serverDll, (Join-Path $serverDest 'SPT_Mod_Korean_Server.dll'), $true)
foreach ($dirName in @('translations', 'config-ui', 'native-locales', 'contract-locales')) {
    $from = Join-Path $root $dirName
    if (-not (Test-Path -LiteralPath $from -PathType Container)) { Fail "Missing runtime translation folder: $dirName" }
    $dest = Join-Path $clientDest $dirName
    [System.IO.Directory]::CreateDirectory($dest) | Out-Null
    Get-ChildItem -LiteralPath $from -File -Recurse -Filter '*.json' | ForEach-Object {
        $relative = $_.FullName.Substring($from.Length).TrimStart([char]'\', [char]'/')
        $destination = Join-Path $dest $relative
        [System.IO.Directory]::CreateDirectory((Split-Path -Parent $destination)) | Out-Null
        [System.IO.File]::Copy($_.FullName, $destination, $true)
    }
}

Write-Host '[4/4] Verifying install ZIP...' -ForegroundColor Cyan
$zip = Join-Path $distRoot ($packageName + '.zip')
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
Run-Python @((Join-Path $root 'docs\tools\verify_workflow.py'), 'distribution', '--stage', $stage, '--zip', $zip) 'Release ZIP validation'
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath ($zip + '.sha256.txt') -Value ($hash + '  ' + [System.IO.Path]::GetFileName($zip)) -Encoding ASCII
Write-Host ''
Write-Host 'DONE' -ForegroundColor Green
Write-Host "Distribution ZIP: $zip"

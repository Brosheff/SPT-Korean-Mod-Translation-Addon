param([string]$ReferenceRoot = '', [string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $PSScriptRoot 'RuntimeContracts\RuntimeContracts.csproj'
$previousReferenceRoot = $env:ADDON_REFERENCE_ROOT
$offline = Join-Path ([System.IO.Path]::GetTempPath()) ([System.IO.Path]::GetRandomFileName() + '.NuGet.Config')
try {
    if ($ReferenceRoot) { $env:ADDON_REFERENCE_ROOT = [System.IO.Path]::GetFullPath($ReferenceRoot) }
    [System.IO.File]::WriteAllText($offline, '<configuration><packageSources><clear /></packageSources></configuration>')
    & dotnet restore $project --configfile $offline --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Runtime contract restore failed.' }
    & dotnet build $project -c $Configuration --no-restore --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Runtime contract compilation failed.' }
    & (Join-Path $PSScriptRoot "RuntimeContracts\bin\$Configuration\net48\RuntimeContracts.exe") $projectRoot
    if ($LASTEXITCODE -ne 0) { throw 'Runtime contract assertions failed.' }
} finally {
    $env:ADDON_REFERENCE_ROOT = $previousReferenceRoot
    if (Test-Path -LiteralPath $offline) { Remove-Item -LiteralPath $offline -Force }
}

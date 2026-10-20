[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$Version = '1.0.0',
    [string]$IsccPath
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$artifactRoot = Join-Path $projectRoot 'artifacts'
$installerOutput = Join-Path $artifactRoot 'installer'
$stagingRoot = Join-Path $artifactRoot ('installer-staging-' + [Guid]::NewGuid().ToString('N'))
$publishDirectory = Join-Path $stagingRoot 'publish'

function Assert-WorkspacePath([string]$Path) {
    $resolvedPath = [IO.Path]::GetFullPath($Path)
    if (-not $resolvedPath.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside the project: $resolvedPath"
    }
    return $resolvedPath
}

if (-not $IsccPath) {
    $compilerCommand = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($compilerCommand) { $IsccPath = $compilerCommand.Source }
    foreach ($programDirectory in @(${env:ProgramFiles(x86)}, $env:ProgramFiles, $env:LOCALAPPDATA)) {
        if ($IsccPath -or -not $programDirectory) { continue }
        foreach ($compilerSuffix in @('Inno Setup 7\ISCC.exe', 'Programs\Inno Setup 7\ISCC.exe', 'Inno Setup 6\ISCC.exe', 'Programs\Inno Setup 6\ISCC.exe')) {
            $compilerCandidate = Join-Path $programDirectory $compilerSuffix
            if (Test-Path -LiteralPath $compilerCandidate -PathType Leaf) { $IsccPath = $compilerCandidate; break }
        }
    }
}
if (-not $IsccPath -or -not (Test-Path -LiteralPath $IsccPath -PathType Leaf)) {
    throw 'Inno Setup 6.3+ is required. Install from https://jrsoftware.org/isdl.php or pass -IsccPath to ISCC.exe.'
}
$compilerVersion = (Get-Item -LiteralPath $IsccPath).VersionInfo.FileVersionRaw
if ($compilerVersion -eq [version]'0.0.0.0') {
    $compilerVersionText = (& $IsccPath --version 2>&1 | Out-String)
    if ($LASTEXITCODE -ne 0 -or $compilerVersionText -notmatch '\b(\d+\.\d+\.\d+)\b') {
        throw 'Unable to determine Inno Setup compiler version.'
    }
    $compilerVersion = [version]$Matches[1]
}
if ($compilerVersion -lt [version]'6.3.0') { throw 'Inno Setup 6.3 or newer is required.' }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET 8 SDK or newer is required.' }

$stagingRoot = Assert-WorkspacePath $stagingRoot
$installerOutput = Assert-WorkspacePath $installerOutput
$installerFile = Join-Path $installerOutput "HNovaReader-$Version-win-x64-Setup.exe"
if (Test-Path -LiteralPath $installerFile) {
    throw "Installer already exists: $installerFile. Use a new version or explicitly remove the previous installer."
}

try {
    New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $installerOutput -Force | Out-Null
    & dotnet publish (Join-Path $projectRoot 'HNovaReader\HNovaReader.csproj') -c Release -r win-x64 --self-contained true -o $publishDirectory "-p:Version=$Version" '-p:DebugType=None' '-p:DebugSymbols=false' '-p:SatelliteResourceLanguages=en'
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $LASTEXITCODE" }
    foreach ($requiredFile in @('HNovaReader.exe', 'coreclr.dll', 'PresentationFramework.dll', 'e_sqlite3.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory $requiredFile))) { throw "Missing self-contained dependency: $requiredFile" }
    }
    & $IsccPath "/DAppVersion=$Version" "/DPublishDir=$publishDirectory" "/DInstallerOutputDir=$installerOutput" (Join-Path $projectRoot 'installer\HNovaReader.iss')
    if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed: $LASTEXITCODE" }
    if (-not (Test-Path -LiteralPath $installerFile)) { throw 'Compiler did not produce the expected installer.' }
    Write-Output "Installer created: $installerFile"
} finally {
    # Remove only this invocation's validated, randomly named staging directory.
    $stagingRoot = Assert-WorkspacePath $stagingRoot
    if (Test-Path -LiteralPath $stagingRoot) { Remove-Item -LiteralPath $stagingRoot -Recurse -Force }
}

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version,

    [string] $InnoCompiler
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$publish = Join-Path $root "outputs\publish-$Version"
$release = Join-Path $root 'outputs\release'
$project = Join-Path $root 'SimVROptimizer.App\SimVROptimizer.App.csproj'
$installerScript = Join-Path $root 'installer\VR-Auto-Optimizer.iss'

if (Test-Path -LiteralPath $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
New-Item -ItemType Directory -Path $publish, $release -Force | Out-Null

dotnet restore $project -r win-x64 --source https://api.nuget.org/v3/index.json
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore -o $publish `
    -p:Version=$Version -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

if ([string]::IsNullOrWhiteSpace($InnoCompiler)) {
    $InnoCompiler = @(
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe'
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $InnoCompiler -or -not (Test-Path -LiteralPath $InnoCompiler)) {
    throw 'Inno Setup 6 compiler was not found.'
}

& $InnoCompiler "/DAppVersion=$Version" "/DSourceDir=$publish" "/DOutputDir=$release" $installerScript
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
$installer = Join-Path $release "VR-Auto-Optimizer-$Version-Setup.exe"
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
Set-Content -LiteralPath ($installer + '.sha256') -Value "$hash *$(Split-Path $installer -Leaf)" -Encoding ascii
Write-Host "Installer: $installer"
Write-Host "SHA-256: $hash"

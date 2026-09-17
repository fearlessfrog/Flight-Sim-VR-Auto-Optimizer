param(
    [string]$ZigPath
)

$ErrorActionPreference = 'Stop'
$sourceDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$outputDirectory = Join-Path $sourceDirectory 'bin'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$source = Join-Path $sourceDirectory 'turbo_layer.cpp'
$output = Join-Path $outputDirectory 'VR_Optimizer_Turbo_Layer.dll'
if ([string]::IsNullOrWhiteSpace($ZigPath)) {
    $defaultZig = Join-Path $env:TEMP 'zig-0.16.0\zig.exe'
    if (Test-Path -LiteralPath $defaultZig) { $ZigPath = $defaultZig }
}

if ($ZigPath -and (Test-Path -LiteralPath $ZigPath)) {
    & $ZigPath c++ -std=c++17 -O2 -shared $source -o $output
}
else {
    $compiler = Get-Command cl.exe -ErrorAction SilentlyContinue
    if (-not $compiler) {
        throw 'No supported C++ compiler was found. Install Zig 0.16 or run from a Visual Studio developer environment.'
    }
    Push-Location $outputDirectory
    try {
        & $compiler.Source /nologo /std:c++17 /O2 /EHsc /LD $source "/Fe:$output"
    }
    finally {
        Pop-Location
    }
}
if ($LASTEXITCODE -ne 0) { throw "Turbo layer compilation failed with exit code $LASTEXITCODE." }

Remove-Item -LiteralPath (Join-Path $outputDirectory 'turbo_layer.lib') -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $outputDirectory 'turbo_layer.exp') -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $outputDirectory 'turbo_layer.obj') -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $outputDirectory 'VR_Optimizer_Turbo_Layer.pdb') -ErrorAction SilentlyContinue

Copy-Item -LiteralPath (Join-Path $sourceDirectory 'VR_Optimizer_Turbo_Layer.json') -Destination $outputDirectory -Force
Copy-Item -LiteralPath (Join-Path $sourceDirectory 'LICENSES.md') -Destination $outputDirectory -Force

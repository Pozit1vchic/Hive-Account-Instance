param([string]$Version = '0.1.0')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$releaseRoot = Join-Path $root 'artifacts\release'
$stage = Join-Path $releaseRoot 'portable'
New-Item -ItemType Directory -Force $stage | Out-Null
Push-Location $root
try {
    dotnet publish src/Hive.App -c Release -r win-x64 --self-contained true --artifacts-path artifacts/current -o $stage
    if ($LASTEXITCODE -ne 0) { throw 'Hive publish failed' }
    dotnet publish src/Hive.Mcp -c Release -r win-x64 --self-contained true --artifacts-path artifacts/current -o (Join-Path $stage 'Mcp')
    if ($LASTEXITCODE -ne 0) { throw 'MCP publish failed' }
    Copy-Item LICENSE,README.md -Destination $stage -Force
    $zip = Join-Path $releaseRoot "Hive-$Version-win-x64-portable.zip"
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
    $compiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    $compilerPath = if ($compiler) { $compiler.Source } else { Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe' }
    if (Test-Path -LiteralPath $compilerPath) {
        & $compilerPath "/DSourceDir=$stage" "/DAppVersion=$Version" (Join-Path $PSScriptRoot 'Hive.iss')
        if ($LASTEXITCODE -ne 0) { throw 'Inno Setup build failed' }
    } else { Write-Warning 'Inno Setup compiler was not found; the portable ZIP was produced without an installer.' }
    Write-Output "Release: $zip"
} finally { Pop-Location }

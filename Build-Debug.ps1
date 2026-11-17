$ErrorActionPreference = 'Stop'

$revit = Get-Process -Name Revit -ErrorAction SilentlyContinue
if ($revit) {
    throw 'Close Revit before building Debug; Revit is loading and locking KStruct.dll in bin\Debug.'
}

$project = Join-Path $PSScriptRoot 'KStruct.csproj'
dotnet build $project -c Debug
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$outputDir = Join-Path $PSScriptRoot 'bin\Debug'
$assemblyPath = Join-Path $outputDir 'KStruct.dll'
if (-not (Test-Path $assemblyPath)) {
    throw "KStruct build output was not found: $assemblyPath"
}

# Assembly reference names are stored as plain UTF-8 strings in the managed metadata.
# A byte scan avoids PowerShell-host-specific System.Reflection.Metadata failures while
# still detecting any BimSpeed assembly name in this build.
$assemblyBytes = [IO.File]::ReadAllBytes((Resolve-Path $assemblyPath).Path)
$assemblyText = [Text.Encoding]::ASCII.GetString($assemblyBytes)
$bimSpeedReferences = @([regex]::Matches($assemblyText, 'BimSpeed[A-Za-z0-9_.]*') |
    ForEach-Object { $_.Value } | Sort-Object -Unique)
if ($bimSpeedReferences.Count -gt 0) {
    $names = $bimSpeedReferences -join ', '
    throw "KStruct has runtime references to BimSpeed assemblies: $names"
}

$unexpectedOutput = @(Get-ChildItem $outputDir -File | Where-Object { $_.Name -match '^BimSpeed' })
if ($unexpectedOutput.Count -gt 0) {
    $names = ($unexpectedOutput | ForEach-Object { $_.Name }) -join ', '
    throw "BimSpeed assemblies were copied into KStruct output: $names"
}

Write-Host 'Dependency check passed: no BimSpeed assembly references or copied BimSpeed files.'

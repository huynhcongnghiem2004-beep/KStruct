param(
    [string]$ExtensionsDirectory = (Join-Path $env:APPDATA 'pyRevit\Extensions')
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$buildDirectory = Join-Path $repoRoot 'bin\PyRevit'
& dotnet build (Join-Path $repoRoot 'KStruct.csproj') --configuration Debug --property:AssemblyName=KStruct.Dev "--property:OutputPath=$buildDirectory" --nologo
if ($LASTEXITCODE -ne 0) { throw 'KStruct Dev build failed.' }
$sourceExtension = Join-Path $repoRoot 'pyrevit\KStructDev.extension'
$targetExtension = Join-Path $ExtensionsDirectory 'KStructDev.extension'
New-Item -ItemType Directory -Path $targetExtension -Force | Out-Null
Copy-Item -Path (Join-Path $sourceExtension '*') -Destination $targetExtension -Recurse -Force
$library = Join-Path $targetExtension 'lib'
New-Item -ItemType Directory -Path $library -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $buildDirectory 'KStruct.Dev.dll') -Destination $library -Force
Write-Output "Installed: $targetExtension"
Write-Output 'In Revit, use pyRevit > Reload, then KStruct Dev > Preview thép cột.'
Write-Output 'After later DLL changes, restart Revit if the loaded WPF assembly remains cached.'

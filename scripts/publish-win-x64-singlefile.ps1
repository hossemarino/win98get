$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path -Path $repoRoot -ChildPath 'Win98Get\Win98Get.csproj'

Write-Host "Publishing single-file self-contained Win98Get (win-x64)..."

# PublishProfile lives under Win98Get/Properties/PublishProfiles
# dotnet publish resolves it by name.
dotnet publish $project /p:PublishProfile=win-x64-singlefile

$outDir = Join-Path -Path $repoRoot -ChildPath 'Win98Get\bin\Release\net10.0-windows\win-x64\publish'
$exe = Join-Path $outDir 'Win98Get.exe'

Write-Host "Output folder: $outDir"
if (Test-Path $exe) {
  Write-Host "Single EXE: $exe"
} else {
  Write-Warning "Expected EXE not found: $exe"
}

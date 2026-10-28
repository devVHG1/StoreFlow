$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'Test-PublicConfiguration.ps1')
dotnet build (Join-Path $projectRoot 'StoreFlow.sln') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Не вдалося зібрати рішення.' }
dotnet test (Join-Path $projectRoot 'StoreFlow.UnitTests') --no-build --nologo
if ($LASTEXITCODE -ne 0) { throw 'Unit-тести виявили помилки.' }
dotnet run --project (Join-Path $projectRoot 'StoreFlow.IntegrationTests') --no-build -- $projectRoot
if ($LASTEXITCODE -ne 0) { throw 'Інтеграційні перевірки виявили помилки.' }

param([string]$Source = './Code/sample-data', [string]$Output = './backups')
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $Output | Out-Null
$name = 'backup_' + (Get-Date -Format 'yyyyMMdd_HHmmss_fff') + '.zip'
dotnet run --project (Join-Path $PSScriptRoot '../Operations') -- backup $Source (Join-Path $Output $name)
exit $LASTEXITCODE

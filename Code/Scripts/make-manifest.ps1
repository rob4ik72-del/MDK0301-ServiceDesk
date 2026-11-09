param([Parameter(Mandatory)][string]$File, [string]$Version = '1.1.0', [string]$Output = 'manifest.json')
dotnet run --project (Join-Path $PSScriptRoot '../Operations') -- manifest $Version $File $Output
exit $LASTEXITCODE

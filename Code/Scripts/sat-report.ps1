param([string]$ApiUrl = 'http://127.0.0.1:5000', [string]$Output = 'SAT.md')
dotnet run --project (Join-Path $PSScriptRoot '../Operations') -- sat $ApiUrl $Output
exit $LASTEXITCODE

param([string]$EnvFile = "$PSScriptRoot/../.env", [string]$Url)
$ErrorActionPreference = 'Stop'
if (!(Test-Path -LiteralPath $EnvFile -PathType Leaf)) { throw "Environment file not found: $EnvFile. Pass -EnvFile ../yarok/.env or create .env from .env.example." }
if (Test-Path -LiteralPath $EnvFile) {
    foreach ($line in Get-Content -LiteralPath $EnvFile) {
        if ($line -match '^\s*([A-Za-z_][A-Za-z0-9_]*)=(.*)$') {
            $value = $Matches[2].Trim()
            if (($value.StartsWith('"') -and $value.EndsWith('"')) -or ($value.StartsWith("'") -and $value.EndsWith("'"))) { $value = $value.Substring(1, $value.Length - 2) }
            [Environment]::SetEnvironmentVariable($Matches[1], $value, 'Process')
        }
    }
}
if (!$env:ASPNETCORE_ENVIRONMENT) { $env:ASPNETCORE_ENVIRONMENT = 'Development' }
if (!$env:ASPNETCORE_URLS) { $env:ASPNETCORE_URLS = 'http://localhost:5050' }
if ($Url) { $env:ASPNETCORE_URLS = $Url }
$env:DOTNET_CLI_HOME = [IO.Path]::GetFullPath("$PSScriptRoot/../.local")
dotnet run --no-launch-profile --project "$PSScriptRoot/../src/Yarok.Api"
exit $LASTEXITCODE

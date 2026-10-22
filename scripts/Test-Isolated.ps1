param([switch]$Database, [string]$EnvFile = "$PSScriptRoot/../../yarok/.env")
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath("$PSScriptRoot/..")
$env:DOTNET_CLI_HOME = Join-Path $root '.local'
# Only the database setting is needed. SMTP and Storage are replaced in memory.
$previous = $env:DATABASE_URL
try {
    if ($Database) {
        foreach ($line in Get-Content -LiteralPath $EnvFile) {
            if ($line -match '^\s*DATABASE_URL=(.*)$') {
                $value = $Matches[1].Trim()
                if (($value.StartsWith('"') -and $value.EndsWith('"')) -or ($value.StartsWith("'") -and $value.EndsWith("'"))) { $value = $value.Substring(1, $value.Length - 2) }
                $env:DATABASE_URL = $value
            }
        }
    }
    $arguments = @('run', '--project', "$root/tests/Yarok.Checks", "-p:BaseOutputPath=$root/.local/checks/")
    if ($Database) { $arguments += @('--', '--database') }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Isolated checks failed' }
} finally { $env:DATABASE_URL = $previous }

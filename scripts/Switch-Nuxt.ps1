param([string]$Source = "$PSScriptRoot/../../yarok")
$ErrorActionPreference = 'Stop'
$sourcePath = (Resolve-Path -LiteralPath $Source).Path
if (!(Test-Path -LiteralPath (Join-Path $sourcePath 'server/api/products/index.get.ts'))) { throw 'Expected original yarok Nuxt server' }
$configPath = Join-Path $sourcePath 'nuxt.config.ts'
$server = Join-Path $sourcePath 'server'
$archive = Join-Path $sourcePath 'legacy-server'
if (Test-Path -LiteralPath $archive) { throw 'legacy-server already exists; inspect it before switching' }
$config = [IO.File]::ReadAllText($configPath)
$config = [regex]::Replace($config, '(?m)^    (databaseUrl|supabaseUrl|supabaseServiceKey|adminLogin|adminPassword|adminSessionSecret|smtpHost|smtpPort|smtpSecure|smtpUser|smtpPassword|contactToEmail):[^\r\n]*\r?\n', '')
if (!$config.Contains('YAROK_BACKEND_URL')) {
    $insertion = @'
export default defineNuxtConfig({
  // Keep browser requests and admin cookies on the Nuxt origin.
  routeRules: {
    '/api/**': {
      proxy: `${process.env.YAROK_BACKEND_URL || 'http://localhost:5050'}/api/**`,
    },
  },
'@
    if (!$config.Contains('export default defineNuxtConfig({')) { throw 'Unexpected Nuxt config' }
    [IO.File]::WriteAllText($configPath, $config.Replace('export default defineNuxtConfig({', $insertion))
}
# Preserve original handlers outside Nuxt server scanning for rollback.
Move-Item -LiteralPath $server -Destination $archive
$drizzle = Join-Path $sourcePath 'drizzle.config.ts'
if (Test-Path -LiteralPath $drizzle) { Move-Item -LiteralPath $drizzle -Destination (Join-Path $archive 'drizzle.config.ts') }
Write-Output 'Nuxt now proxies /api/** to ASP.NET Core. Original server preserved in legacy-server.'

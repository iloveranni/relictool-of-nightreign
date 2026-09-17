param(
    [string]$DotnetExecutable = 'dotnet',
    [string]$PythonExecutable = 'python'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$variables = @{
    DOTNET_CLI_HOME = (Join-Path $PSScriptRoot '.build/dotnet-home')
    DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    DOTNET_NOLOGO = '1'
    DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
    NUGET_PACKAGES = (Join-Path $PSScriptRoot '.build/packages')
    NUGET_HTTP_CACHE_PATH = (Join-Path $PSScriptRoot '.build/nuget-http')
}
$saved = @{}
Push-Location $PSScriptRoot
try {
    foreach ($name in $variables.Keys) {
        $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $variables[$name], 'Process')
    }
    $expected = (Get-Content -LiteralPath 'global.json' -Raw | ConvertFrom-Json).sdk.version
    $version = & $DotnetExecutable --version
    if ($LASTEXITCODE -ne 0 -or "$version".Trim() -ne $expected) {
        throw "Install the official .NET SDK $expected for Windows x64. See docs/BUILD.md."
    }
    & $PythonExecutable -I -B -c 'import sys; assert sys.version_info >= (3, 12), "Python 3.12 or later is required"; print(sys.version.split()[0])'
    if ($LASTEXITCODE -ne 0) { throw 'Python check failed. See docs/BUILD.md.' }
    & $DotnetExecutable restore NightreignRelicTool.sln --configfile NuGet.Config --locked-mode --packages $variables.NUGET_PACKAGES -p:NuGetAudit=false -p:NuGetInteractive=false
    if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed.' }
    & $DotnetExecutable build NightreignRelicTool.sln -c Release --no-restore -p:Platform=x64 "-p:RelicPythonExecutable=$PythonExecutable"
    if ($LASTEXITCODE -ne 0) { throw 'Release x64 build failed.' }
    Write-Output 'Built: src/NightreignRelicTool/bin/x64/Release/net48/NightreignRelicTool.exe'
} finally {
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process') }
    Pop-Location
}

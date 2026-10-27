param(
    [switch]$SkipBuild,
    [string]$DotnetExecutable = 'dotnet',
    [string]$PythonExecutable = 'python'
)
$ErrorActionPreference = 'Stop'
if (-not $SkipBuild) {
    & "$PSScriptRoot/Build.ps1" -DotnetExecutable $DotnetExecutable -PythonExecutable $PythonExecutable
}
$test = Join-Path $PSScriptRoot 'tests/NightreignRelicTool.Integration.Tests/bin/x64/Release/net48/NightreignRelicTool.Integration.Tests.exe'
if (-not (Test-Path -LiteralPath $test)) { throw 'Build the test project first.' }
& $test
if ($LASTEXITCODE -ne 0) { throw 'Synthetic regression checks failed.' }

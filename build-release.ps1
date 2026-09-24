param([ValidateSet('win-x64', 'win-arm64', 'all')][string]$Runtime = 'all')
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$localSdk = Join-Path $env:LOCALAPPDATA 'MiniDeskSdk10\dotnet.exe'
$dotnet = if (Test-Path $localSdk) { $localSdk } else { (Get-Command dotnet).Source }
$project = Join-Path $PSScriptRoot 'MiniDesk.csproj'
$releaseRoot = Join-Path $PSScriptRoot 'Release'
$runtimes = if ($Runtime -eq 'all') { @('win-x64', 'win-arm64') } else { @($Runtime) }
foreach ($rid in $runtimes) {
    # WPF on .NET 10 currently has startup regressions when bundled as a single file.
    # Keep each architecture in its own self-contained directory instead.
    $output = Join-Path $releaseRoot "portable-net10-$rid"
    & $dotnet publish $project -c Release -r $rid --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o $output
    if ($LASTEXITCODE -ne 0) { throw "MiniDesk publish failed for $rid." }
    Write-Output (Join-Path $output 'MiniDesk.exe')
}

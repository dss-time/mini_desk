param([ValidateSet('win-x64', 'win-arm64', 'all')][string]$Runtime = 'all')
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$localSdk = Join-Path $env:LOCALAPPDATA 'MiniDeskSdk10\dotnet.exe'
$dotnet = if (Test-Path $localSdk) { $localSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }
$runtimes = if ($Runtime -eq 'all') { @('win-x64', 'win-arm64') } else { @($Runtime) }
$payload = Join-Path $PSScriptRoot 'Installer\Payload'
$releaseRoot = Join-Path $PSScriptRoot 'Release'
New-Item -ItemType Directory -Force -Path $payload | Out-Null
foreach ($rid in $runtimes) {
    & $PSScriptRoot\build-release.ps1 -Runtime $rid
    if ($LASTEXITCODE -ne 0) { throw "MiniDesk publish failed for $rid." }
    $appOutput = Join-Path $releaseRoot "portable-net10-$rid"
    $payloadArchive = Join-Path $payload 'MiniDesk.zip'
    Compress-Archive -Path (Join-Path $appOutput '*') -DestinationPath $payloadArchive -CompressionLevel Optimal -Force
    $output = Join-Path $PSScriptRoot "Installer\Release\$rid"
    & $dotnet publish (Join-Path $PSScriptRoot 'Installer\MiniDesk.Setup.csproj') -c Release -r $rid --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -p:NoIncremental=true -o $output
    if ($LASTEXITCODE -ne 0) { throw "Installer publish failed for $rid." }
    Copy-Item -LiteralPath (Join-Path $output 'MiniDesk.Setup.exe') -Destination (Join-Path $releaseRoot "MiniDesk-Setup-$($rid.Replace('win-', '')).exe") -Force
}

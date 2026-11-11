<#
.SYNOPSIS
Restore the hash-locked official Google.Protobuf .NET Standard runtime into its local UPM package.
.EXAMPLE
pwsh ./tools/Restore-Protobuf.ps1 -Check
#>
[CmdletBinding()]
param([switch]$Check)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/internal/Protobuf.ps1"
$pin = $script:ProtobufLock.csharp
$archive = Get-ProtobufDownload $pin.url $pin.sha256
$license = Get-ProtobufDownload $pin.licenseUrl $pin.licenseSha256
$unpack = Join-Path $script:ProtobufCache "nuget-$($pin.version)"
Expand-Archive -LiteralPath $archive -DestinationPath $unpack -Force
$dll = Join-Path $unpack 'lib/netstandard2.0/Google.Protobuf.dll'
Assert-ProtobufHash $dll $pin.dllSha256
$package = Join-Path $script:ProtobufRoot 'packages/unity/protobuf-runtime'
foreach ($entry in @(@($dll, 'Runtime/Google.Protobuf.dll', $pin.dllSha256), @($license, 'LICENSE.md', $pin.licenseSha256))) {
    $target = Join-Path $package $entry[1]
    if ($Check) { Assert-ProtobufHash $target $entry[2] }
    else {
        New-Item -ItemType Directory -Force (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath $entry[0] -Destination $target -Force
    }
}
Write-Host "Google.Protobuf $($pin.version) runtime $(if ($Check) { 'verified' } else { 'restored' })."

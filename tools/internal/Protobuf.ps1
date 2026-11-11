$script:ProtobufRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$script:ProtobufLock = Get-Content (Join-Path $script:ProtobufRoot 'protobuf-toolchain.json') -Raw | ConvertFrom-Json
$script:ProtobufCache = Join-Path $script:ProtobufRoot '.cache/protobuf'

function Assert-ProtobufHash([string]$Path, [string]$Hash) {
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Hash) { throw "Protobuf checksum mismatch: $Path" }
}

function Get-ProtobufDownload([string]$Url, [string]$Hash) {
    $directory = Join-Path $script:ProtobufCache 'downloads'
    New-Item -ItemType Directory -Force $directory | Out-Null
    $path = Join-Path $directory ([Uri]$Url).Segments[-1]
    if (-not (Test-Path -LiteralPath $path)) {
        $partial = "$path.partial"
        try {
            Invoke-WebRequest -Uri $Url -OutFile $partial
            Assert-ProtobufHash $partial $Hash
            Move-Item -LiteralPath $partial -Destination $path -Force
        } finally { if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial } }
    }
    Assert-ProtobufHash $path $Hash
    return $path
}

function Resolve-ProtobufCompiler {
    if (-not $IsWindows -or [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne 'X64') {
        throw 'The locked protoc distribution currently supports Windows x64 PowerShell 7 only.'
    }
    $pin = $script:ProtobufLock.protoc
    $archive = Get-ProtobufDownload $pin.url $pin.sha256
    $directory = Join-Path $script:ProtobufCache "protoc-$($pin.version)-win64"
    $compiler = Join-Path $directory 'bin/protoc.exe'
    if (-not (Test-Path -LiteralPath $compiler)) { Expand-Archive -LiteralPath $archive -DestinationPath $directory -Force }
    Assert-ProtobufHash $compiler $pin.exeSha256
    return $compiler
}

function Resolve-ProtobufGoPlugin {
    # Always keep this tool's downloads and Go checksum database inside the workspace.
    $previous = @{}
    foreach ($name in @('GOPATH','GOMODCACHE','GOCACHE','GOBIN','GOSUMDB')) { $previous[$name] = [Environment]::GetEnvironmentVariable($name) }
    try {
        . (Join-Path $PSScriptRoot 'Go-Env.ps1')
        $env:GOPATH = Join-Path $script:ProtobufRoot '.cache/gopath'
        $env:GOMODCACHE = Join-Path $script:ProtobufRoot '.cache/gomod'
        $env:GOCACHE = Join-Path $script:ProtobufRoot '.cache/gobuild'
        $env:GOSUMDB = 'sum.golang.org'
        $env:GOBIN = Join-Path $script:ProtobufCache "go-$($script:ProtobufLock.go.version)"
        $plugin = Join-Path $env:GOBIN 'protoc-gen-go.exe'
        if (-not (Test-Path -LiteralPath $plugin)) {
            & $Go install "google.golang.org/protobuf/cmd/protoc-gen-go@$($script:ProtobufLock.go.version)"
            if ($LASTEXITCODE -ne 0) { throw 'Official protoc-gen-go install failed.' }
        }
        $buildInfo = & $Go version -m $plugin
        if ($LASTEXITCODE -ne 0 -or ($buildInfo -join "`n") -notmatch ([regex]::Escape("google.golang.org/protobuf`t$($script:ProtobufLock.go.version)`t$($script:ProtobufLock.go.moduleSum)"))) {
            throw 'protoc-gen-go does not match the locked official module version and checksum.'
        }
        return $plugin
    } finally {
        foreach ($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name, $previous[$name]) }
    }
}

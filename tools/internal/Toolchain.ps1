<#
.SYNOPSIS
讀取 toolchain.json，提供 Go／Unity／.NET 路徑解析函式。
.DESCRIPTION
分類：內部支援。所有命令從 gamenetkit 根目錄執行。
前提：由其他腳本 dot-source 匯入。
行為與產物：定義共用根目錄與解析函式；通常不需手動執行。
.EXAMPLE
. ./tools/internal/Toolchain.ps1
#>
$script:GameNetKitRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$script:GameNetKitVersions = Get-Content -LiteralPath (Join-Path $script:GameNetKitRoot 'toolchain.json') -Raw | ConvertFrom-Json

function Resolve-GameNetKitUnityEditor {
    param([string]$Path='', [string]$ProjectPath='')
    $version = $script:GameNetKitVersions.unity
    if ($ProjectPath) {
        $versionFile = Join-Path $ProjectPath 'ProjectSettings/ProjectVersion.txt'
        $projectVersion = (Select-String -LiteralPath $versionFile -Pattern '^m_EditorVersion: (.+)$').Matches.Groups[1].Value
        if ($projectVersion -ne $version) { throw "Unity project requires $projectVersion; toolchain.json pins $version. Update both before building." }
    }
    $candidate = if ($Path) { $Path } elseif ($env:GAMENETKIT_UNITY_EDITOR) { $env:GAMENETKIT_UNITY_EDITOR } else {
        $installed = Join-Path ${env:ProgramFiles} "Unity/Hub/Editor/$version/Editor/Unity.exe"
        $portable = Join-Path $script:GameNetKitRoot ".local/toolchains/unity/$version/Editor/Unity.exe"
        if (Test-Path -LiteralPath $installed -PathType Leaf) { $installed } else { $portable }
    }
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { throw "Unity $version was not found: $candidate. Install that Editor or set GAMENETKIT_UNITY_EDITOR." }
    $actual = [Diagnostics.FileVersionInfo]::GetVersionInfo($candidate).ProductVersion
    if (-not $actual -or ($actual -split '_')[0] -ne $version) { throw "Expected Unity $version, found: $actual ($candidate)." }
    return [IO.Path]::GetFullPath($candidate)
}

function Resolve-GameNetKitUnityData {
    param([string]$Path='')
    $candidate = if ($Path) { $Path } elseif ($env:GAMENETKIT_UNITY_DATA) { $env:GAMENETKIT_UNITY_DATA } else {
        Join-Path (Split-Path -Parent (Resolve-GameNetKitUnityEditor)) 'Data'
    }
    if (-not (Test-Path -LiteralPath $candidate -PathType Container)) { throw "Unity Data directory was not found: $candidate" }
    $null = Resolve-GameNetKitUnityEditor -Path (Join-Path (Split-Path -Parent $candidate) 'Unity.exe')
    return [IO.Path]::GetFullPath($candidate)
}

function Resolve-GameNetKitDotNet {
    param([string]$Path='')
    $candidate = if ($Path) { $Path } elseif ($env:GAMENETKIT_DOTNET) { $env:GAMENETKIT_DOTNET } else {
        Join-Path (Resolve-GameNetKitUnityData) 'DotNetSdk/dotnet.exe'
    }
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { throw "The .NET SDK was not found: $candidate. Use -DotNet or GAMENETKIT_DOTNET with a .NET 8 SDK including ASP.NET Core." }
    return [IO.Path]::GetFullPath($candidate)
}

function Resolve-GameNetKitGo {
    param([string]$Path='')
    $version = $script:GameNetKitVersions.go
    $candidate = if ($Path) { $Path } elseif ($env:GAMENETKIT_GO) { $env:GAMENETKIT_GO } else {
        $portable = Join-Path $script:GameNetKitRoot ".local/toolchains/go$version/bin/go.exe"
        $command = Get-Command go -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $portable -PathType Leaf) { $portable } elseif ($command) { $command.Source } else { Join-Path ${env:ProgramFiles} 'Go/bin/go.exe' }
    }
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { throw "Go $version was not found. Install it or set GAMENETKIT_GO." }
    $previousToolchain = $env:GOTOOLCHAIN
    try {
        # Inspect the executable itself without auto-downloading or selecting a different module toolchain.
        $env:GOTOOLCHAIN = 'local'
        $actual = & $candidate version
        if ($LASTEXITCODE -ne 0 -or $actual -notmatch ('\bgo' + [regex]::Escape($version) + '\b')) { throw "Expected Go $version, found: $actual. Set GAMENETKIT_GO to the pinned compiler." }
    } finally { $env:GOTOOLCHAIN = $previousToolchain }
    return [IO.Path]::GetFullPath($candidate)
}

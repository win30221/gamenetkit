<# Exercise the real -PackageOnly entry points with tiny WebGL fixtures and a stand-in Go compiler. #>
$ErrorActionPreference = 'Stop'
$example = Split-Path $PSScriptRoot -Parent
$scratch = Join-Path ([IO.Path]::GetTempPath()) ("tower-duel-package-test-" + [guid]::NewGuid().ToString('N'))
$game = Join-Path $scratch 'examples/tower-duel'
$resources = Join-Path $game 'client/Assets/Resources/TowerDuel'
$plain = Join-Path $game 'client/Builds/WebGL'
$solo = Join-Path $game 'Builds/SoloWeb/WebGL'
$goLog = Join-Path $scratch 'tools/internal/go-calls.txt'
$entryPoints = @{Multiplayer='scripts/Deploy.ps1'; Solo='solo/Deploy.ps1'; Merged='solo/Deploy-Merged.ps1'}

function Set-TestRecord([string]$Directory) {
    $record = @{}
    foreach ($name in 'map','rules') {
        $record[$name + 'Hash'] = (Get-FileHash -LiteralPath (Join-Path $resources "$name.json")).Hash.ToLowerInvariant()
    }
    [IO.File]::WriteAllText((Join-Path $Directory 'tower-duel-content.json'), ($record | ConvertTo-Json))
}

function Test-Package([string]$Mode, [string]$ExpectedError = '', [hashtable]$Options = @{}) {
    if (Test-Path $goLog) { Remove-Item -LiteralPath $goLog }
    $script = Join-Path $game $entryPoints[$Mode]
    $deployDir = Join-Path $scratch '.local/deploy'
    if ($ExpectedError) { $releasesBefore = @(Get-ChildItem $deployDir -Directory -Recurse -Filter bundle).Count }
    try {
        $messages = @(& $script -PackageOnly -VmHost localhost -Domain duel.example.test @Options)
    } catch {
        if (-not $ExpectedError -or $_.Exception.Message -notmatch $ExpectedError) { throw }
        if (Test-Path $goLog) { throw "$Mode accepted a bad Player until after the Go build started." }
        if (@(Get-ChildItem $deployDir -Directory -Recurse -Filter bundle).Count -ne $releasesBefore) {
            throw "$Mode left a partial bundle after rejecting a Player."
        }
        if ($_.Exception.Message -match 'Hash mismatch' -and
            $_.Exception.Message -notmatch 'WebGL build=[0-9a-f]{64}, package=[0-9a-f]{64}') {
            throw "$Mode did not report both content hashes."
        }
        return
    }
    if ($ExpectedError) { throw "$Mode accepted a Player that should have failed: $ExpectedError" }
    $archive = ([string]$messages[-1]) -replace '^.*?package:\s*',''
    $extract = Join-Path $scratch ([guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($extract) | Out-Null
    & tar -xzf $archive -C $extract
    if ($LASTEXITCODE -ne 0) { throw "Could not inspect $archive." }
    $manifest = Get-Content -LiteralPath (Join-Path $extract 'manifest.json') -Raw | ConvertFrom-Json
    $players = @('public')
    if ($Mode -eq 'Merged') { $players += 'modes-public' }
    foreach ($name in 'map','rules') {
        $field = $name + 'Hash'
        $sourceHash = (Get-FileHash -LiteralPath (Join-Path $resources "$name.json")).Hash.ToLowerInvariant()
        $packedHash = (Get-FileHash -LiteralPath (Join-Path $extract "protocol/$name.json")).Hash.ToLowerInvariant()
        if ($sourceHash -cne $packedHash -or $manifest.$field -cne $packedHash) { throw "$Mode packaged the wrong $name.json." }
        foreach ($player in $players) {
            $record = Get-Content -LiteralPath (Join-Path $extract "$player/tower-duel-content.json") -Raw | ConvertFrom-Json
            if ($record.$field -cne $packedHash) { throw "$Mode $player $field differs from the packaged file." }
        }
    }
}

try {
    foreach ($dir in @($resources, "$plain/Build", "$solo/Build", "$game/scripts", "$game/solo/backend", "$game/server",
                       "$scratch/tools/internal", "$scratch/packages/unity/transport/Browser")) {
        [IO.Directory]::CreateDirectory($dir) | Out-Null
    }
    foreach ($file in @($entryPoints.Values) + @('scripts/WebPackageContent.ps1')) {
        Copy-Item -LiteralPath (Join-Path $example $file) -Destination (Join-Path $game $file)
    }
    Copy-Item -LiteralPath (Join-Path $example 'deploy') -Destination $game -Recurse
    Copy-Item -LiteralPath (Join-Path $example 'solo/deploy') -Destination (Join-Path $game 'solo') -Recurse
    [IO.File]::WriteAllText("$scratch/packages/unity/transport/Browser/transport.js", '// fixture')
    [IO.File]::WriteAllText("$scratch/tools/internal/Go-Env.ps1", @'
function New-TestGoBinary {
    $outputIndex = [array]::IndexOf($args, '-o')
    if ($args[0] -ne 'build' -or $outputIndex -lt 0) { throw 'Unexpected test compiler arguments.' }
    [IO.File]::WriteAllText($args[$outputIndex + 1], 'fixture executable')
    [IO.File]::AppendAllText((Join-Path $PSScriptRoot 'go-calls.txt'), "build`n")
    $global:LASTEXITCODE = 0
}
$Go = 'New-TestGoBinary'
'@)
    $mapPath = Join-Path $resources 'map.json'
    $rulesPath = Join-Path $resources 'rules.json'
    [IO.File]::WriteAllText($mapPath, '{"id":"test-map"}')
    $rules = '{"schema":3,"id":"test-revision","cards":[{"id":"swords","hp":70}]}'
    [IO.File]::WriteAllText($rulesPath, $rules)
    foreach ($player in @($plain,$solo)) {
        foreach ($file in 'WebGL.loader.js','WebGL.framework.js.gz','WebGL.data.gz','WebGL.wasm.gz') {
            [IO.File]::WriteAllText("$player/Build/$file", 'fixture')
        }
        Set-TestRecord $player
    }
    [IO.File]::WriteAllText("$plain/index.html", '<html></html>')
    [IO.File]::WriteAllText("$solo/index.html", '<meta content="solo-plugin">')
    [IO.File]::WriteAllText("$plain/tower-duel-build.txt", 'TowerDuel protocol 3 / test-revision')
    [IO.File]::WriteAllText("$solo/solo-build.txt", 'TowerDuel Solo protocol 3 / test-revision')
    foreach ($mode in 'Multiplayer','Solo','Merged') { Test-Package $mode }

    [IO.File]::WriteAllText($rulesPath, $rules.Replace('"hp":70','"hp":90'))
    foreach ($mode in 'Multiplayer','Solo','Merged') { Test-Package $mode 'rulesHash mismatch' }
    Test-Package Multiplayer 'rulesHash mismatch' @{StaticOnly=$true}
    Test-Package Multiplayer 'rulesHash mismatch' @{ClientOnly=$true}
    Set-TestRecord $plain
    Test-Package Multiplayer
    Test-Package Solo 'Solo WebGL.*rulesHash mismatch'
    Test-Package Merged 'Solo WebGL.*rulesHash mismatch'
    Set-TestRecord $solo
    Test-Package Merged

    [IO.File]::AppendAllText($mapPath, "`n")
    Set-TestRecord $solo
    Test-Package Solo
    Test-Package Multiplayer 'Multiplayer WebGL.*mapHash mismatch'
    Test-Package Merged 'Multiplayer WebGL.*mapHash mismatch'
    Set-TestRecord $plain
    $receipt = Join-Path $solo 'tower-duel-content.json'
    $valid = [IO.File]::ReadAllText($receipt)
    $mapHash = (ConvertFrom-Json $valid).mapHash
    Remove-Item -LiteralPath $receipt
    Test-Package Merged 'Solo WebGL.*tower-duel-content.json'
    foreach ($bad in @('{', '[]', '{}', '{"mapHash":42}', ('{"mapHash":"' + $mapHash + '"}'),
                      ('{"mapHash":"' + ('A' * 64) + '"}'), $valid.Replace('}', ',}'))) {
        [IO.File]::WriteAllText($receipt, $bad)
        Test-Package Solo 'Solo WebGL.*tower-duel-content.json'
    }
    Set-TestRecord $solo
    [IO.File]::WriteAllText("$solo/solo-build.txt", 'wrong revision')
    Test-Package Merged 'Solo WebGL.*solo-build.txt'
    Write-Output 'PASS: WebGL package contents, stale multiplayer/Solo builds, invalid records and legacy markers.'
} finally {
    if (Test-Path $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force }
}

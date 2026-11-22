# Shared by the multiplayer, Solo and merged WebGL packages.
function Assert-TowerDuelWebContent([string]$Directory, [hashtable]$Protocol, [string]$PlayerDescription) {
    $path = Join-Path $Directory 'tower-duel-content.json'
    try {
        $document = [System.Text.Json.JsonDocument]::Parse([IO.File]::ReadAllText($path))
    } catch {
        throw "$PlayerDescription - cannot read $path. $($_.Exception.Message)"
    }
    try {
        $record = $document.RootElement
        if ($record.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
            throw "$PlayerDescription - expected a JSON object in $path."
        }
        foreach ($field in 'mapHash', 'rulesHash') {
            $element = [System.Text.Json.JsonElement]::new()
            if (-not $record.TryGetProperty($field, [ref]$element) -or
                $element.ValueKind -ne [System.Text.Json.JsonValueKind]::String) {
                throw "$PlayerDescription - missing or invalid $field in $path."
            }
            $value = $element.GetString()
            if ($value -cnotmatch '\A[0-9a-f]{64}\z') { throw "$PlayerDescription - invalid $field in $path." }
            if ($value -cne $Protocol[$field]) {
                throw "$PlayerDescription - $field mismatch in $path. WebGL build=$value, package=$($Protocol[$field])."
            }
        }
    } finally {
        $document.Dispose()
    }
}

function New-TowerDuelWebPackageContent([string]$ExampleRoot, [string]$Bundle, [System.Collections.IDictionary]$Targets) {
    # Reserve a fresh release so a failed preflight can only remove its own work.
    $releaseDir = Split-Path $Bundle -Parent
    New-Item -ItemType Directory -Path $releaseDir -ErrorAction Stop | Out-Null
    try {
        $resources = Join-Path $ExampleRoot 'client/Assets/Resources/TowerDuel'
        $protocolDir = Join-Path $Bundle 'protocol'
        [IO.Directory]::CreateDirectory($protocolDir) | Out-Null
        foreach ($file in 'map.json','rules.json') {
            Copy-Item -LiteralPath (Join-Path $resources $file) -Destination $protocolDir -ErrorAction Stop
        }
        # Both the preflight and the manifest describe the bytes staged for the server.
        $rulesPath = Join-Path $protocolDir 'rules.json'
        $rules = Get-Content -LiteralPath $rulesPath -Raw -Encoding UTF8 | ConvertFrom-Json -ErrorAction Stop
        $protocol = @{
            Rules = $rules
            mapHash = (Get-FileHash -LiteralPath (Join-Path $protocolDir 'map.json') -Algorithm SHA256).Hash.ToLowerInvariant()
            rulesHash = (Get-FileHash -LiteralPath $rulesPath -Algorithm SHA256).Hash.ToLowerInvariant()
            Markers = @{}
        }

        # Check all requested Players before copying any large WebGL files.
        $players = foreach ($name in $Targets.Keys) {
            if ($name -eq 'Solo') {
                $source = Join-Path $ExampleRoot 'Builds/SoloWeb/WebGL'
                $markerFile = 'solo-build.txt'
                $marker = "TowerDuel Solo protocol $($rules.schema) / $($rules.id)"
                $rebuild = '.\solo\Build.ps1 -Target WebGL'
            } else {
                $source = Join-Path $ExampleRoot 'client/Builds/WebGL'
                $markerFile = 'tower-duel-build.txt'
                $marker = "TowerDuel protocol $($rules.schema) / $($rules.id)"
                $rebuild = '.\scripts\Build.ps1 -Target WebGL'
            }
            $description = "$name WebGL (rebuild with $rebuild)"
            $recordPath = Join-Path $source 'tower-duel-content.json'
            $recordTime = [IO.File]::GetLastWriteTimeUtc($recordPath)
            Assert-TowerDuelWebContent $source $protocol $description
            [pscustomobject]@{Name=$name; Source=$source; Destination=(Join-Path $Bundle $Targets[$name])
                              MarkerFile=$markerFile; Marker=$marker; Description=$description
                              RecordPath=$recordPath; RecordTime=$recordTime}
        }
        foreach ($player in $players) {
            $destination = $player.Destination
            [IO.Directory]::CreateDirectory($destination) | Out-Null
            Get-ChildItem -LiteralPath $player.Source -ErrorAction Stop | ForEach-Object {
                Copy-Item -LiteralPath $_.FullName -Destination $destination -Recurse -Force -ErrorAction Stop
            }
            # A concurrent Unity build removes its previous record before overwriting the Player.
            Assert-TowerDuelWebContent $player.Source $protocol $player.Description
            if ([IO.File]::GetLastWriteTimeUtc($player.RecordPath) -ne $player.RecordTime) {
                throw "$($player.Description) - the Player was rebuilt during packaging. Retry."
            }
            Assert-TowerDuelWebContent $destination $protocol $player.Description
            foreach ($file in 'index.html', $player.MarkerFile, 'Build/WebGL.loader.js', 'Build/WebGL.framework.js.gz', 'Build/WebGL.data.gz', 'Build/WebGL.wasm.gz') {
                if (-not (Test-Path -LiteralPath (Join-Path $destination $file) -PathType Leaf)) {
                    throw "$($player.Description) - missing $file."
                }
            }
            if ((Get-Content -LiteralPath (Join-Path $destination $player.MarkerFile) -Raw).Trim() -ne $player.Marker) {
                throw "$($player.Description) - $($player.MarkerFile) does not match the packaged protocol/rules revision."
            }
            if ($player.Name -eq 'Solo' -and (Get-Content -LiteralPath (Join-Path $destination 'index.html') -Raw) -notmatch 'content="solo-plugin"') {
                throw "$($player.Description) - index.html is missing the Solo adapter marker."
            }
            $protocol.Markers[$player.Name] = $player.Marker
        }
        return $protocol
    } catch {
        [IO.Directory]::Delete($releaseDir, $true)
        throw
    }
}

<# Switch the merged website to the bundled multiplayer-only player without rebuilding. #>
param([ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9.-]+$')][string]$VmHost='172.233.67.181',[string]$IdentityFile='')
$ErrorActionPreference='Stop'
$options=@('-o','BatchMode=yes','-o','ConnectTimeout=15','-o','StrictHostKeyChecking=yes')
if($IdentityFile){$options+=@('-i',(Resolve-Path -LiteralPath $IdentityFile).Path)}
& ssh @options "root@$VmHost" 'sh /opt/rumblebound/tower-duel/current/disable-modes.sh'
if($LASTEXITCODE -ne 0){throw 'Mode removal failed; the previous route was restored where possible.'}

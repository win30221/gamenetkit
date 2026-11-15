<# Remove only the deployed solo test site/services. -Purge also deletes its independent releases. #>
param([ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9.-]+$')][string]$VmHost='172.233.67.181',[string]$IdentityFile='',[switch]$Purge)
$ErrorActionPreference='Stop'
$options=@('-o','BatchMode=yes','-o','ConnectTimeout=15','-o','StrictHostKeyChecking=yes')
if($IdentityFile){$options+=@('-i',(Resolve-Path -LiteralPath $IdentityFile).Path)}
$argument=if($Purge){' --purge'}else{''}
& ssh @options "root@$VmHost" ('sh /opt/rumblebound/tower-duel-solo/current/uninstall.sh'+$argument)
if($LASTEXITCODE -ne 0){throw 'Solo uninstall failed; inspect the remote error'}

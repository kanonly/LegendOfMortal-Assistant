param([Parameter(Mandatory=$true)][string]$CheatEngineDir)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'validate.ps1') -CheatEngineDir $CheatEngineDir
$payload = [xml][IO.File]::ReadAllText((Join-Path $workspace 'build\Assistant.CETRAINER'))
$lua = $payload.CheatTable.LuaScript.InnerText.Replace('__OWNER_PID__','0').Replace('__PIPE_NAME__','ValidationOnly').Replace('__RUNTIME_DIR__',"'ValidationOnly'").Replace('__BUNDLE_VERSION__','ValidationOnly')
[CTValidationLua]::Check((Join-Path $CheatEngineDir 'lua53-64.dll'),$lua,'frontend embedded backend')
if ($payload.SelectNodes('//CheatEntry').Count -ne 3 -or $payload.SelectNodes('//Address').Count -ne 0) {throw 'Frontend payload contains saved runtime addresses'}
$program = Join-Path $workspace '活侠传助手.exe'
$exeBytes = [IO.File]::ReadAllBytes($program)
$peOffset = [BitConverter]::ToInt32($exeBytes,60)
if ([BitConverter]::ToUInt32($exeBytes,$peOffset) -ne 0x00004550 -or [BitConverter]::ToUInt16($exeBytes,$peOffset+4) -ne 0x8664) {throw 'Frontend is not x64'}
$test = Start-Process -FilePath $program -ArgumentList '--self-test' -PassThru -Wait -WindowStyle Hidden
if ($test.ExitCode -ne 0) {throw "Frontend model/search self-test failed: $($test.ExitCode)"}
Write-Output 'PASS: frontend payload XML/Lua; no saved addresses; x64; input draft retention; Simplified Chinese conversion'

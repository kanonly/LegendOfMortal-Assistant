param([Parameter(Mandatory=$true)][string]$GameDir, [string]$DependencyDir)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$buildDir = Join-Path $workspace 'build'
New-Item -ItemType Directory -Path $buildDir -Force | Out-Null
$managed = Join-Path $GameDir 'Mortal_Data\Managed'
if (-not $DependencyDir) {$DependencyDir=Join-Path $workspace 'third-party\managed'}
$core = $DependencyDir
$source = Join-Path $workspace 'src\Runtime.cs'
$hash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.Substring(0,12).ToLowerInvariant()
$assemblyName = 'LOMCT.Runtime.' + $hash
$dll = Join-Path $buildDir ($assemblyName + '.dll')
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:library /optimize+ "/out:$dll" "/reference:$(Join-Path $core '0Harmony.dll')" "/reference:$(Join-Path $managed 'UnityEngine.CoreModule.dll')" $source
if ($LASTEXITCODE -ne 0) { throw 'Runtime compilation failed' }
Add-Type -Path (Join-Path $core 'Mono.Cecil.dll')
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $managed 'Mortal.Core.dll'))
$stats = @($assembly.MainModule.Types | Where-Object Name -EQ GameStatType | ForEach-Object { $_.Fields | Where-Object HasConstant | ForEach-Object { [ordered]@{kind=[int]$_.Constant;name=$_.Name} } })
$relations = @($assembly.MainModule.Types | Where-Object Name -EQ RelationshipStatType | ForEach-Object { $_.Fields | Where-Object HasConstant | ForEach-Object { [ordered]@{kind=[int]$_.Constant;name=$_.Name} } })
$assembly.Dispose()
$primary = @(3,19,31,20,0,1,2,4,5,6,12,29,17,18,21,22,23)
$ordered = @($primary | ForEach-Object { $kind=$_; $stats | Where-Object kind -EQ $kind }) + @($stats | Where-Object kind -NotIn $primary)
$specLines = foreach ($stat in $ordered) {
    $category = if ($stat.kind -in @(3,19,31,20)) {'资源'} elseif ($stat.kind -in @(0,1,2,4,5,6,12,29)) {'基础属性'} elseif ($stat.kind -in @(17,18,21,22,23)) {'技能与抗性'} else {'其他属性（含剧情数值）'}
    "  {$($stat.kind), '$($stat.name)', '$category'},"
}
$lua = [IO.File]::ReadAllText((Join-Path $workspace 'src\base.lua'))
$lua = [regex]::Replace($lua, '(?s)local specs = \{.*?\n\}', ('local specs = {' + "`n" + ($specLines -join "`n") + "`n}"), 1)
$extension = [IO.File]::ReadAllText((Join-Path $workspace 'src\extensions.lua'))
$extension = $extension.Replace('__RUNTIME_HEX__', [Convert]::ToHexString([IO.File]::ReadAllBytes($dll)))
$extension = $extension.Replace('__ASSEMBLY_NAME__', $assemblyName)
$relationLines = $relations | ForEach-Object { "  [$($_.kind)] = '$($_.name)'," }
$extension = $extension.Replace('__RELATION_NAMES__', ($relationLines -join "`n"))
$lua = $lua + "`n" + $extension
$doc = [xml]::new()
$doc.PreserveWhitespace = $false
$doc.LoadXml('<?xml version="1.0" encoding="utf-8"?><CheatTable CheatEngineTableVersion="45"><CheatEntries><CheatEntry><ID>0</ID><Description>"启用／重新定位（先进入存档；取消勾选即关闭）"</Description><Options moHideChildren="1"/><GroupHeader>1</GroupHeader></CheatEntry><CheatEntry><ID>1</ID><Description>"定位后可改值；脚本增强另行勾选；读档前关闭"</Description><GroupHeader>1</GroupHeader></CheatEntry><CheatEntry><ID>2</ID><Description>"刷新只读最终值（改值或换装备后点击）"</Description><GroupHeader>1</GroupHeader></CheatEntry></CheatEntries><UserdefinedSymbols/><LuaScript/></CheatTable>')
[void]$doc.SelectSingleNode('/CheatTable/LuaScript').AppendChild($doc.CreateCDataSection($lua))
$settings = [Xml.XmlWriterSettings]::new()
$settings.Encoding = [Text.UTF8Encoding]::new($false)
$settings.Indent = $true
$writer = [Xml.XmlWriter]::Create((Join-Path $workspace 'LegendOfMortal.CT'),$settings)
try { $doc.Save($writer) } finally { $writer.Dispose() }
[IO.File]::WriteAllText((Join-Path $buildDir 'schema.json'), (@{stats=$stats;relations=$relations;runtime=$assemblyName}|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
Write-Output "Built LegendOfMortal.CT: $($stats.Count) stat types, $($relations.Count) relationship types, embedded $assemblyName"

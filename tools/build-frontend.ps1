param([string]$DependencyDir, [string]$OutputPath)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$front = Join-Path $workspace 'frontend'
$build = Join-Path $workspace 'build'
$framework = (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319')
if (-not $DependencyDir) {$DependencyDir=Join-Path $workspace 'third-party\managed'}
if (-not $OutputPath) {$OutputPath=Join-Path $workspace '活侠传助手.exe'}
$core = $DependencyDir
New-Item -ItemType Directory -Path $build -Force | Out-Null
& (Join-Path $PSScriptRoot 'build-icon.ps1')
$loaderSource=Join-Path $workspace 'src\Bootstrap.cs'
$loaderHash=(Get-FileHash -LiteralPath $loaderSource -Algorithm SHA256).Hash.Substring(0,12).ToLowerInvariant()
$loader=Join-Path $build "LOMCT.Bootstrap.$loaderHash.dll"
& (Join-Path $framework 'csc.exe') /nologo /target:library /optimize+ "/out:$loader" $loaderSource
if ($LASTEXITCODE -ne 0) {throw 'Bootstrap compilation failed'}
$files=Get-Content -Raw -LiteralPath (Join-Path $front 'runtime-files.json') | ConvertFrom-Json
$inputs=[ordered]@{}
foreach($path in $files.managed) {$inputs['managed/'+$path]=(Join-Path $core $path)}
$inputs['managed/Bootstrap.dll']=$loader
foreach($file in Get-ChildItem -LiteralPath (Join-Path $workspace 'third-party') -File) {$inputs['licenses/'+$file.Name]=$file.FullName}
$manifestFiles=@(foreach($entry in $inputs.GetEnumerator()) {
 $info=Get-Item -LiteralPath $entry.Value
 [ordered]@{path=$entry.Key; length=$info.Length; sha256=(Get-FileHash -LiteralPath $entry.Value -Algorithm SHA256).Hash.ToLowerInvariant()}
})
$manifestJson=$manifestFiles | ConvertTo-Json -Depth 5 -Compress
$version=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($manifestJson))).Substring(0,16).ToLowerInvariant()
$manifest=Join-Path $build 'bundle-manifest.json'
[IO.File]::WriteAllText($manifest,(@{version=$version;files=$manifestFiles}|ConvertTo-Json -Depth 5 -Compress),[Text.UTF8Encoding]::new($false))
$archive=Join-Path $build 'Runtime.zip'
$zip=[IO.Compression.ZipArchive]::new([IO.File]::Create($archive),[IO.Compression.ZipArchiveMode]::Create)
try {
 foreach($entry in $inputs.GetEnumerator()) {
  $item=$zip.CreateEntry($entry.Key,[IO.Compression.CompressionLevel]::Optimal)
  $item.LastWriteTime=[DateTimeOffset]::new(2026,1,1,0,0,0,[TimeSpan]::Zero)
  $output=$item.Open(); $input=[IO.File]::OpenRead($entry.Value)
  try {$input.CopyTo($output)} finally {$input.Dispose();$output.Dispose()}
 }
} finally {$zip.Dispose()}
$notices=Join-Path $build 'ThirdParty.txt'
$noticeParts=@([IO.File]::ReadAllText((Join-Path $workspace 'third-party\NOTICE.zh-CN.txt')))
$noticeParts += "Required Notice: Copyright (c) 2026 活侠传助手项目贡献者`n" + [IO.File]::ReadAllText((Join-Path $workspace 'LICENSE'))
foreach($file in Get-ChildItem -LiteralPath (Join-Path $workspace 'third-party') -Filter '*.LICENSE.txt') {$noticeParts += $file.Name+"`n"+[IO.File]::ReadAllText($file.FullName)}
[IO.File]::WriteAllText($notices,($noticeParts -join "`n`n"),[Text.UTF8Encoding]::new($false))
$table = [xml][IO.File]::ReadAllText((Join-Path $workspace 'LegendOfMortal.CT'))
$lua = $table.CheatTable.LuaScript.InnerText
$bootstrap = "LOMUI_CONFIG = {pipe='__PIPE_NAME__', ownerPid=__OWNER_PID__, runtimeDir=__RUNTIME_DIR__, bundleVersion='__BUNDLE_VERSION__'}`n" + $lua + "`n" + [IO.File]::ReadAllText((Join-Path $front 'bridge.lua'))
$table.SelectSingleNode('/CheatTable/LuaScript').RemoveAll()
[void]$table.SelectSingleNode('/CheatTable/LuaScript').AppendChild($table.CreateCDataSection($bootstrap))
$payload = Join-Path $build 'Assistant.CETRAINER'
$xmlSettings = [Xml.XmlWriterSettings]::new(); $xmlSettings.Encoding = [Text.UTF8Encoding]::new($false)
$writer = [Xml.XmlWriter]::Create($payload,$xmlSettings)
try {$table.Save($writer)} finally {$writer.Dispose()}
$arguments = @('/nologo','/target:winexe','/platform:x64','/optimize+','/debug-',"/out:$OutputPath","/win32manifest:$(Join-Path $front 'app.manifest')", "/win32icon:$(Join-Path $workspace 'assets\app.ico')", "/resource:$payload,LOMTrainer.Backend", "/resource:$(Join-Path $front 'Theme.xaml'),LOMTrainer.Theme", "/resource:$archive,LOMTrainer.Runtime", "/resource:$manifest,LOMTrainer.Manifest", "/resource:$notices,LOMTrainer.Notices", "/resource:$(Join-Path $front 'QuickHelp.txt'),LOMTrainer.Help", "/resource:$(Join-Path $workspace 'assets\mascot.png'),LOMTrainer.Mascot", "/resource:$(Join-Path $workspace 'assets\app.ico'),LOMTrainer.Icon")
foreach ($name in @('System.dll','System.Core.dll','System.Xaml.dll','System.Web.Extensions.dll','System.IO.Compression.dll')) {$arguments += "/reference:$(Join-Path $framework $name)"}
foreach ($name in @('PresentationCore.dll','PresentationFramework.dll','WindowsBase.dll')) {$arguments += "/reference:$(Join-Path $framework ('WPF\' + $name))"}
$arguments += @(Get-ChildItem -LiteralPath $front -Filter '*.cs' | ForEach-Object FullName)
& (Join-Path $framework 'csc.exe') @arguments
if ($LASTEXITCODE -ne 0) {throw 'Frontend compilation failed'}
Write-Output "Built $OutputPath (WPF x64, EXTERNAL CE; managed resources $version)"

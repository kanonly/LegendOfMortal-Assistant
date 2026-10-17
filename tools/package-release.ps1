$ErrorActionPreference='Stop'
$workspace=Split-Path -Parent $PSScriptRoot
$dist=Join-Path $workspace 'dist'
$public=Join-Path $dist 'GitHub-发布版'
$release=Join-Path $dist '无内嵌CE'
New-Item -ItemType Directory -Path $public,$release -Force | Out-Null
$sourceFiles=@(foreach($folder in @('frontend','src','third-party','assets','docs')) {Get-ChildItem -LiteralPath (Join-Path $workspace $folder) -Recurse -File})
$sourceFiles+=@(foreach($file in @('README.md','README.zh-CN.md','LICENSE','LICENSE.zh-CN.md','.gitignore','LegendOfMortal.CT','助手使用说明.zh-CN.md','tools/build.ps1','tools/build-icon.ps1','tools/build-frontend.ps1','tools/package-release.ps1','tests/validate.ps1','tests/validate-frontend.ps1','tests/validate-release.ps1','tests/validate-icon.ps1','tests/ExternalReleaseProbe.cs')) {Get-Item -LiteralPath (Join-Path $workspace $file)})
$sourceFiles=@($sourceFiles|Sort-Object FullName -Unique)
function Relative([string]$path) {return [IO.Path]::GetRelativePath($workspace,$path).Replace('\','/')}
function Copy-ReleaseFile([string]$source,[string]$root,[string]$relative) {
 $destination=Join-Path $root $relative
 New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
 Copy-Item -LiteralPath $source -Destination $destination -Force
}
$allowed=@($sourceFiles|ForEach-Object {Relative $_.FullName})
foreach($file in $sourceFiles) {
 $relative=Relative $file.FullName
 if($relative -match '(?i)(cheatengine[^/]*\.(exe|sig)|lua53.*\.dll|MonoDataCollector|Mortal.*\.dll|UnityEngine.*\.dll|codex-clipboard|settings\.json)$'){throw "Forbidden publication input: $relative"}
 Copy-ReleaseFile $file.FullName $public $relative
}
foreach($file in Get-ChildItem -LiteralPath $public -Recurse -File) {
 $relative=[IO.Path]::GetRelativePath($public,$file.FullName).Replace('\','/')
 if($relative -notin $allowed){throw "Unexpected existing file in public directory: $relative"}
 if($file.Extension -in @('.cs','.ps1','.lua','.md','.txt','.json','.CT') -and [IO.File]::ReadAllText($file.FullName) -match '(?i)(C:\\Users\\|D:\\SteamLibrary\\)'){throw "Machine-specific path in $relative"}
}
$releasePaths=@('活侠传助手.exe','README.md','LICENSE','LICENSE.zh-CN.md','助手使用说明.zh-CN.md')
$releasePaths+=@($sourceFiles|Where-Object { (Relative $_.FullName) -match '^(assets|docs)/' -or ((Relative $_.FullName) -match '^third-party/[^/]+\.(txt|json)$') }|ForEach-Object {Relative $_.FullName})
foreach($relative in $releasePaths){Copy-ReleaseFile (Join-Path $workspace $relative) $release $relative}
foreach($file in Get-ChildItem -LiteralPath $release -Recurse -File) {
 $relative=[IO.Path]::GetRelativePath($release,$file.FullName).Replace('\','/')
 if($relative -notin $releasePaths){throw "Unexpected existing release file: $relative"}
}
function Zip-Folder([string]$root,[string]$destination) {
 $zip=[IO.Compression.ZipArchive]::new([IO.File]::Create($destination),[IO.Compression.ZipArchiveMode]::Create)
 try {foreach($file in Get-ChildItem -LiteralPath $root -Recurse -File | Sort-Object FullName) {
  $entry=$zip.CreateEntry([IO.Path]::GetRelativePath($root,$file.FullName).Replace('\','/'),[IO.Compression.CompressionLevel]::Optimal)
  $entry.LastWriteTime=[DateTimeOffset]::new(2026,1,1,0,0,0,[TimeSpan]::Zero)
  $input=[IO.File]::OpenRead($file.FullName);$output=$entry.Open()
  try {$input.CopyTo($output)} finally {$input.Dispose();$output.Dispose()}
 }} finally {$zip.Dispose()}
}
$sourceZip=Join-Path $dist '活侠传助手-无内嵌CE-源码.zip'
$releaseZip=Join-Path $dist '活侠传助手-无内嵌CE.zip'
Zip-Folder $public $sourceZip
Zip-Folder $release $releaseZip
$hashes=foreach($path in @((Join-Path $release '活侠传助手.exe'),$releaseZip,$sourceZip)) {
 (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+[IO.Path]::GetRelativePath($dist,$path)
}
[IO.File]::WriteAllLines((Join-Path $dist '无内嵌CE-SHA256SUMS.txt'),$hashes,[Text.UTF8Encoding]::new($false))
Write-Output 'Packaged dist/无内嵌CE and dist/GitHub-发布版; CE/game binaries and private caches excluded.'

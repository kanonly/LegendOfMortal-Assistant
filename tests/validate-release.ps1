param([string]$Executable, [string]$CheatEnginePath)
$ErrorActionPreference='Stop'
$workspace=Split-Path -Parent $PSScriptRoot
if(-not $Executable){$Executable=Join-Path $workspace '活侠传助手.exe'}
$program=[Reflection.Assembly]::LoadFile([IO.Path]::GetFullPath($Executable))
$names=$program.GetManifestResourceNames()
foreach($name in @('LOMTrainer.Mascot','LOMTrainer.Runtime','LOMTrainer.Manifest','LOMTrainer.Backend','LOMTrainer.Notices')) {
 if($name -notin $names){throw "Missing resource: $name"}
}
$reader=[IO.StreamReader]::new($program.GetManifestResourceStream('LOMTrainer.Manifest'))
try {$manifest=$reader.ReadToEnd()|ConvertFrom-Json} finally {$reader.Dispose()}
$allowed=@('managed/Mono.Cecil.dll','managed/MonoMod.Utils.dll','managed/MonoMod.RuntimeDetour.dll','managed/0Harmony.dll','managed/Bootstrap.dll')
$zip=[IO.Compression.ZipArchive]::new($program.GetManifestResourceStream('LOMTrainer.Runtime'),[IO.Compression.ZipArchiveMode]::Read)
try {
 if($zip.Entries.Count -ne $manifest.files.Count){throw 'Runtime manifest mismatch'}
 foreach($entry in $zip.Entries) {
  if($entry.FullName -notin $allowed -and $entry.FullName -notmatch '^licenses/[^/]+\.(txt|json)$'){throw "Unexpected bundled file: $($entry.FullName)"}
  $expected=$manifest.files|Where-Object path -EQ $entry.FullName
  $stream=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create()
  try {$hash=[Convert]::ToHexString($sha.ComputeHash($stream)).ToLowerInvariant()} finally {$stream.Dispose();$sha.Dispose()}
  if($expected.sha256 -ne $hash -or $expected.length -ne $entry.Length){throw "Bad runtime hash: $($entry.FullName)"}
 }
} finally {$zip.Dispose()}
$type=$program.GetType('LOMTrainer.ExternalCE',$true)
$validate=$type.GetMethod('Validate')
foreach($value in @('',(Join-Path $workspace 'build\missing-ce.exe'))) {
 $rejected=$false
 try {[void]$validate.Invoke($null,@($value))} catch {$rejected=$_.Exception.InnerException -is [ArgumentException]}
 if(-not $rejected){throw 'Invalid CE path was accepted'}
}
if($CheatEnginePath){
 [void]$validate.Invoke($null,@($CheatEnginePath))
 $probe=Join-Path $workspace 'build\ExternalReleaseProbe.exe'
 New-Item -ItemType Directory -Path (Split-Path -Parent $probe) -Force | Out-Null
 & (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:exe /platform:x64 "/out:$probe" "/reference:$Executable" (Join-Path $PSScriptRoot 'ExternalReleaseProbe.cs')
 if($LASTEXITCODE -ne 0){throw 'Release probe compilation failed'}
 & $probe ([IO.Path]::GetFullPath($Executable)) $CheatEnginePath
 if($LASTEXITCODE -ne 0){throw 'Release extraction/path validation failed'}
}
foreach($file in @('LICENSE','LICENSE.zh-CN.md','README.md','assets/mascot.png','assets/setup.png','assets/home.png','assets/ARTWORK.md','docs/validation.md')) {
 if(-not(Test-Path -LiteralPath (Join-Path $workspace $file))){throw "Missing release document: $file"}
}
foreach($file in (Get-Content -Raw -LiteralPath (Join-Path $workspace 'third-party\managed-manifest.json')|ConvertFrom-Json)) {
 $actual=(Get-FileHash -LiteralPath (Join-Path $workspace ('third-party\managed\'+$file.file)) -Algorithm SHA256).Hash.ToLowerInvariant()
 if($actual -ne $file.sha256){throw "Managed dependency changed: $($file.file)"}
}
Write-Output "PASS: no bundled CE/Lua/collector; exact runtime whitelist and hashes; mascot embedded; invalid CE paths rejected; notices and docs present. Runtime $($manifest.version)"

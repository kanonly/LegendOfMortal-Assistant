param([string]$Executable)
$ErrorActionPreference='Stop'
$workspace=Split-Path -Parent $PSScriptRoot
if(-not $Executable){$Executable=Join-Path $workspace '活侠传助手.exe'}
Add-Type -AssemblyName System.Drawing
$bytes=[IO.File]::ReadAllBytes((Join-Path $workspace 'assets\app.ico'))
$expected=@(16,32,48,64,128,256)
if([BitConverter]::ToUInt16($bytes,2) -ne 1 -or [BitConverter]::ToUInt16($bytes,4) -ne 6){throw 'Invalid ICO directory'}
for($i=0;$i -lt 6;$i++) {
 $at=6+16*$i;$size=if($bytes[$at] -eq 0){256}else{[int]$bytes[$at]}
 if($size -ne $expected[$i]){throw 'Wrong icon dimensions'}
 $length=[BitConverter]::ToUInt32($bytes,$at+8);$offset=[BitConverter]::ToUInt32($bytes,$at+12)
 $stream=[IO.MemoryStream]::new($bytes,[int]$offset,[int]$length)
 $frame=[Drawing.Bitmap]::new($stream)
 try {if($frame.Width -ne $size -or $frame.Height -ne $size -or $frame.GetPixel(0,0).A -ne 0){throw 'Icon frame is invalid or not transparent'}} finally {$frame.Dispose();$stream.Dispose()}
}
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class IconResources {
 delegate bool EnumName(IntPtr module, IntPtr type, IntPtr name, IntPtr parameter);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
 [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool EnumResourceNames(IntPtr module, IntPtr type, EnumName callback, IntPtr parameter);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
 [DllImport("kernel32.dll")] static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
 [DllImport("kernel32.dll")] static extern uint SizeofResource(IntPtr module, IntPtr resource);
 [DllImport("kernel32.dll")] static extern IntPtr LockResource(IntPtr resource);
 public static byte[] Group(string path) {
  IntPtr module=LoadLibraryEx(path,IntPtr.Zero,0x22);
  if(module==IntPtr.Zero)throw new Exception("Unable to read executable resources");
  try {
   byte[] group=null;
   EnumName callback=delegate(IntPtr m, IntPtr t, IntPtr n, IntPtr p) {
    IntPtr resource=FindResource(m,n,t);
    group=new byte[SizeofResource(m,resource)];
    Marshal.Copy(LockResource(LoadResource(m,resource)),group,0,group.Length);
    return false;
   };
   EnumResourceNames(module,(IntPtr)14,callback,IntPtr.Zero);
   if(group==null)throw new Exception("Executable has no native icon group");
   return group;
  } finally {FreeLibrary(module);}
 }
}
'@
$group=[IconResources]::Group([IO.Path]::GetFullPath($Executable))
if([BitConverter]::ToUInt16($group,4) -ne 6){throw 'Native icon frame count differs'}
for($i=0;$i -lt 6;$i++) {if($group[6+14*$i] -ne $bytes[6+16*$i]){throw 'Native icon sizes differ'}}
$program=[Reflection.Assembly]::LoadFile([IO.Path]::GetFullPath($Executable))
$stream=$program.GetManifestResourceStream('LOMTrainer.Icon')
if(-not $stream){throw 'WPF window icon resource missing'}
try {$copy=[IO.MemoryStream]::new();$stream.CopyTo($copy);if([Convert]::ToBase64String($copy.ToArray()) -ne [Convert]::ToBase64String($bytes)){throw 'Window icon differs from executable icon'}} finally {$stream.Dispose();if($copy){$copy.Dispose()}}
Write-Output 'PASS: six transparent ICO sizes; native executable group; identical embedded WPF window icon.'

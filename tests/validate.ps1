param(
    [Parameter(Mandatory=$true)][string]$CheatEngineDir
)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$ctPath = Join-Path $workspace 'LegendOfMortal.CT'
$doc = [xml][IO.File]::ReadAllText($ctPath, [Text.Encoding]::UTF8)
$luaPath = Join-Path $CheatEngineDir 'lua53-64.dll'
if (-not ('CTValidationLua' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class CTValidationLua {
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate IntPtr NewState();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int Load(IntPtr l, byte[] b, UIntPtr n, byte[] name, IntPtr mode);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate IntPtr GetString(IntPtr l, int index, out UIntPtr length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void Close(IntPtr l);
    public static void Check(string dll, string code, string label) {
        var lib = NativeLibrary.Load(dll);
        var create = Marshal.GetDelegateForFunctionPointer<NewState>(NativeLibrary.GetExport(lib,"luaL_newstate"));
        var load = Marshal.GetDelegateForFunctionPointer<Load>(NativeLibrary.GetExport(lib,"luaL_loadbufferx"));
        var str = Marshal.GetDelegateForFunctionPointer<GetString>(NativeLibrary.GetExport(lib,"lua_tolstring"));
        var close = Marshal.GetDelegateForFunctionPointer<Close>(NativeLibrary.GetExport(lib,"lua_close"));
        var l = create();
        try {
            var b = System.Text.Encoding.UTF8.GetBytes(code);
            if (load(l,b,(UIntPtr)b.Length,System.Text.Encoding.UTF8.GetBytes(label+"\0"),IntPtr.Zero)!=0) {
                UIntPtr n;
                throw new Exception(label+": "+Marshal.PtrToStringUTF8(str(l,-1,out n)));
            }
        } finally { close(l); NativeLibrary.Free(lib); }
    }
}
'@
}
$lua = $doc.CheatTable.LuaScript.InnerText
if ([string]::IsNullOrWhiteSpace($lua)) { throw 'CT LuaScript missing' }
[CTValidationLua]::Check($luaPath, $lua, 'CT LuaScript')
$entries = @($doc.CheatTable.CheatEntries.CheatEntry)
$root = $entries | Where-Object { $_.ID -eq '0' }
if ($entries.Count -ne 3 -or -not $root) { throw 'Unexpected static CT records' }
if ($root.GroupHeader -ne '1') { throw 'The activation entry must use the Lua group callback' }
if (@($doc.SelectNodes('//Address')).Count -ne 0) { throw 'Static runtime addresses must not be saved' }
foreach ($file in Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.lua') {
    [CTValidationLua]::Check($luaPath, [IO.File]::ReadAllText($file.FullName), $file.Name)
}
Write-Output 'PASS: XML; Lua 5.3 syntax including runtime helper; Lua activation entry; static record structure; no saved runtime addresses.'

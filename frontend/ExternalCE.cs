using System;
using System.Diagnostics;
using System.IO;

namespace LOMTrainer {
    public static class ExternalCE {
        public static readonly string[] RequiredFiles = {
            "lua53-64.dll", "main.lua", "defines.lua", "autorun/monoscript.lua",
            "autorun/dlls/MonoDataCollector64.dll", "autorun/forms/MonoDataCollector.frm"
        };
        public static string Validate(string path) {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("请先在设置中选择已安装的 Cheat Engine 7.5 x64。\n本版本不内嵌 CE。");
            path = Path.GetFullPath(path.Trim().Trim('"'));
            if (!File.Exists(path)) throw new ArgumentException("找不到 CE 程序，请在设置中重新选择。\n" + path);
            using (var reader = new BinaryReader(File.OpenRead(path))) {
                if (reader.BaseStream.Length < 64 || reader.ReadUInt16() != 0x5A4D) throw new ArgumentException("所选文件不是有效的 CE x64 程序。");
                reader.BaseStream.Position = 60;
                int offset = reader.ReadInt32();
                if (offset < 64 || offset > reader.BaseStream.Length - 6) throw new ArgumentException("CE 程序头无效。");
                reader.BaseStream.Position = offset;
                if (reader.ReadUInt32() != 0x4550 || reader.ReadUInt16() != 0x8664) throw new ArgumentException("请选择 64 位 CE：cheatengine-x86_64.exe。");
            }
            var version = FileVersionInfo.GetVersionInfo(path);
            if (version.FileMajorPart != 7 || version.FileMinorPart != 5)
                throw new ArgumentException("当前适配 CE 7.5 x64；所选文件版本为 " + version.FileVersion + "。");
            string folder = Path.GetDirectoryName(path);
            foreach (string name in RequiredFiles)
                if (!File.Exists(Path.Combine(folder,name.Replace('/',Path.DirectorySeparatorChar))))
                    throw new ArgumentException("CE 安装不完整，缺少：" + name + "\n请使用包含 Mono 支持的完整 CE 7.5 x64 安装目录。");
            return path;
        }
    }
}

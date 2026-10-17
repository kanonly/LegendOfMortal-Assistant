using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace LOMTrainer {
    public sealed class BundleFile {public string path, sha256; public long length;}
    public sealed class BundleManifest {public string version; public List<BundleFile> files;}
    public static class Bundle {
        private static readonly object sync = new object();
        private static BundleManifest manifest;
        public static BundleManifest Manifest {
            get { if(manifest == null) manifest=new JavaScriptSerializer().Deserialize<BundleManifest>(ReadText("LOMTrainer.Manifest")); return manifest; }
        }
        public static string Version {get {return Manifest.version;}}
        public static string Folder {get {return Path.Combine(Preferences.Folder,"runtime",Version);}}
        public static string ManagedFolder {get {return Path.Combine(Folder,"managed");}}
        public static string ReadText(string resource) {
            using(var reader=new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream(resource),System.Text.Encoding.UTF8)) return reader.ReadToEnd();
        }
        private static string Hash(Stream stream) {
            using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
        }
        private static bool Valid(string path,BundleFile file) {
            if(!File.Exists(path) || new FileInfo(path).Length != file.length) return false;
            using(var stream=File.OpenRead(path)) return Hash(stream)==file.sha256;
        }
        public static string Ensure() {
            lock(sync) {
                Directory.CreateDirectory(Folder);
                using(var input=Assembly.GetExecutingAssembly().GetManifestResourceStream("LOMTrainer.Runtime"))
                using(var zip=new ZipArchive(input,ZipArchiveMode.Read)) {
                    foreach(BundleFile file in Manifest.files) {
                        string target=Path.GetFullPath(Path.Combine(Folder,file.path.Replace('/',Path.DirectorySeparatorChar)));
                        if(!target.StartsWith(Path.GetFullPath(Folder)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new IOException("辅助资源路径无效。");
                        if(Valid(target,file)) continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        var entry=zip.GetEntry(file.path);
                        if(entry == null) throw new IOException("辅助资源缺少文件："+file.path);
                        string staging=target+".extracting";
                        using(var source=entry.Open()) using(var output=File.Create(staging)) source.CopyTo(output);
                        if(!Valid(staging,file)) {File.Delete(staging); throw new IOException("辅助资源校验失败："+file.path);}
                        if(File.Exists(target)) File.Delete(target);
                        File.Move(staging,target);
                    }
                }
                return Folder;
            }
        }
    }
}

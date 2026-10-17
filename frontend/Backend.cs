using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace LOMTrainer {
    // Native CE 7.5 LuaServer command 3, synchronous/main-thread function calls.
    // Typed arguments avoid building executable Lua from user text.
    public sealed class Backend : IDisposable {
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1,1);
        private readonly string pipeName = "LOMUI_" + Guid.NewGuid().ToString("N");
        private Process ownedCE;
        private bool disposed;
        public int Generation { get; private set; }
        public bool Connected { get; private set; }
        public bool Enhanced { get; private set; }
        public int GamePid { get; private set; }
        public string HarmonyStatus { get; private set; }
        public bool HarmonyCompatible { get; private set; }
        public bool Started { get { return ownedCE != null && !ownedCE.HasExited; } }
        public int BackendPid { get { return Started ? ownedCE.Id : 0; } }
        public static bool GameRunning { get { return Process.GetProcessesByName("Mortal").Length > 0; } }
        public static bool GameReady {
            get {
                foreach(var game in Process.GetProcessesByName("Mortal")) {
                    using(game) {try {
                        if(DateTime.Now-game.StartTime >= TimeSpan.FromSeconds(30) && game.MainWindowHandle != IntPtr.Zero && game.Responding) return true;
                    } catch (InvalidOperationException) {} catch(System.ComponentModel.Win32Exception) {}}
                }
                return false;
            }
        }

        public async Task Start(string cePath = null) {
            if (Started) return;
            if (Enhanced && Process.GetProcessesByName("Mortal").Any(p=>p.Id == GamePid))
                throw new IOException("之前的后台 CE 已退出，增强撤销未确认。请先重启游戏，再重新定位。");
            Enhanced = false;
            if (ownedCE != null) {ownedCE.Dispose(); ownedCE=null;}
            cePath = ExternalCE.Validate(cePath ?? Preferences.Load().CheatEngine);
            await Task.Run(() => Bundle.Ensure());
            string folder = Path.Combine(Preferences.Folder, "sessions", pipeName);
            Directory.CreateDirectory(folder);
            string trainer;
            using (var reader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("LOMTrainer.Backend"), Encoding.UTF8)) trainer = reader.ReadToEnd();
            trainer = trainer.Replace("__PIPE_NAME__", pipeName).Replace("__OWNER_PID__", Process.GetCurrentProcess().Id.ToString())
                .Replace("__RUNTIME_DIR__",LuaString(Bundle.ManagedFolder)).Replace("__BUNDLE_VERSION__",Bundle.Version);
            string file = Path.Combine(folder,"Assistant.CETRAINER");
            File.WriteAllText(file,trainer,new UTF8Encoding(false));
            ownedCE = Process.Start(new ProcessStartInfo(cePath, "\"" + file + "\"") { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(cePath), WindowStyle = ProcessWindowStyle.Hidden });
            if (ownedCE == null) throw new IOException("CE 启动未确认。");
            Exception last = null;
            for (int attempt = 0; attempt < 30; attempt++) {
                if (ownedCE.HasExited) throw new IOException("后台 CE 启动后退出，请检查 CE 文件是否完整。");
                try { await Request("status"); return; }
                catch (Exception e) { last = e; }
                await Task.Delay(400);
            }
            throw new IOException("后台 CE 尚未就绪。若系统显示权限提示，请完成该提示后重试。", last);
        }
        public async Task<Reply> Request(string op, string id = "", string arg = "", long number = 0, bool state = false, int offset = 0) {
            await gate.WaitAsync();
            try {
                if (disposed) throw new ObjectDisposedException("Backend");
                if (!Started) throw new IOException("后台 CE 未运行。");
                Reply r;
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                using (var pipe = new NamedPipeClientStream(".",pipeName,PipeDirection.InOut,PipeOptions.Asynchronous)) {
                    await pipe.ConnectAsync(1200,timeout.Token);
                    var bytes = new MemoryStream();
                    var writer = new BinaryWriter(bytes,Encoding.UTF8);
                    writer.Write((byte)3); writer.Write((byte)0); writer.Write(0); // function name, GUI thread
                    WriteString(writer,"LOMUI_Request",false);
                    writer.Write((byte)7);
                    WriteString(writer,op,true); WriteString(writer,id,true); WriteString(writer,arg,true);
                    writer.Write((byte)2); writer.Write(number);
                    writer.Write((byte)1); writer.Write((byte)(state ? 1 : 0));
                    writer.Write((byte)2); writer.Write((long)Generation);
                    writer.Write((byte)2); writer.Write((long)offset);
                    writer.Write((byte)1); // one return value
                    byte[] packet = bytes.ToArray();
                    await pipe.WriteAsync(packet,0,packet.Length,timeout.Token);
                    byte[] header = await ReadExact(pipe,2,timeout.Token);
                    if (header[0] != 1 || header[1] != 5) throw new IOException("后台返回了无效响应。");
                    byte[] length = await ReadExact(pipe,2,timeout.Token);
                    byte[] body = await ReadExact(pipe,BitConverter.ToUInt16(length,0),timeout.Token);
                    r = new JavaScriptSerializer().Deserialize<Reply>(Encoding.UTF8.GetString(body));
                }
                if (r == null) throw new IOException("后台响应为空。");
                Connected = r.connected; Generation = r.generation; Enhanced = r.enhanced; GamePid = r.pid;
                HarmonyStatus = r.harmonyStatus; HarmonyCompatible = r.harmonyCompatible;
                if (!r.ok) throw new BackendException(r.error ?? "操作未成功。", r.connected);
                return r;
            } catch (BackendException) { throw; }
            catch (Exception error) { Connected = false; throw new IOException("后台连接中断，写入已暂停。" + error.Message,error); }
            finally { gate.Release(); }
        }
        public async Task<List<Entry>> Catalog() {
            var entries = new List<Entry>();
            int total;
            do {
                Reply r = await Request("catalog",offset:entries.Count);
                total = r.total;
                if (r.entries == null || r.entries.Count == 0) { if (entries.Count < total) throw new IOException("项目列表没有完整返回。"); break; }
                entries.AddRange(r.entries);
            } while (entries.Count < total);
            return entries;
        }
        public async Task Stop() { if (Started) await Request("stop"); else if (Connected || Enhanced) throw new IOException("后台 CE 已退出，未能确认增强已撤销；请重启游戏。"); }
        public async Task Shutdown() {
            await Stop();
            if (Started) {
                // Only this application-created process is closed, after cleanup.
                ownedCE.Kill();
                await Task.Run(() => ownedCE.WaitForExit(3000));
            }
            Connected = Enhanced = false;
        }
        public void Dispose() { disposed = true; if (ownedCE != null) ownedCE.Dispose(); }
        private static string LuaString(string value) {
            return "'" + value.Replace("\\","\\\\").Replace("'","\\'").Replace("\r","\\r").Replace("\n","\\n") + "'";
        }
        private static void WriteString(BinaryWriter writer,string value,bool typed) {
            if (typed) writer.Write((byte)5);
            byte[] data = Encoding.UTF8.GetBytes(value ?? "");
            if (data.Length > 60000) throw new ArgumentException("请求内容太长。");
            writer.Write((ushort)data.Length); writer.Write(data);
        }
        private static async Task<byte[]> ReadExact(Stream stream,int size,CancellationToken token) {
            var result = new byte[size];
            int offset = 0;
            while (offset < size) {
                int read = await stream.ReadAsync(result,offset,size-offset,token);
                if (read == 0) throw new EndOfStreamException("后台连接已断开。");
                offset += read;
            }
            return result;
        }
    }
    public sealed class BackendException : Exception {
        public readonly bool StillConnected;
        public BackendException(string message,bool connected) : base(message) { StillConnected = connected; }
    }
}

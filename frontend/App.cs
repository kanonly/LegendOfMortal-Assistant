using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;

namespace LOMTrainer {
    public static class Program {
        [STAThread]
        public static int Main(string[] args) {
            if (args.Contains("--backend-smoke")) {
                int index=Array.IndexOf(args,"--backend-smoke");
                return Smoke(args.Length > index+1 ? args[index+1] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"tests","frontend-smoke.json")).GetAwaiter().GetResult();
            }
            if (args.Contains("--self-test")) return SelfTest();
            using (var single = new Mutex(false,"Local\\LOMTrainer.Desktop")) {
                if (!single.WaitOne(0)) {MessageBox.Show("活侠传助手已经运行，请切换到已打开的窗口。","活侠传助手"); return 0;}
                try {
                    var app=new Application();
                    app.DispatcherUnhandledException+=delegate(object sender,System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e) {
                        MessageBox.Show("界面出现错误："+e.Exception.Message,"活侠传助手"); e.Handled=true;
                    };
                    return app.Run(new MainWindow(args.Contains("--preview")));
                } catch (Exception e) {MessageBox.Show(e.ToString(),"助手启动失败"); return 1;}
                finally {single.ReleaseMutex();}
            }
        }
        private static int SelfTest() {
            var model=new EntryVM(new Entry {id="test",name="唐門金創藥",value=18,parameter=10});
            model.Draft="999"; model.ParameterDraft="27";
            model.Update(new Entry {id="test",name="唐門金創藥",value=19,parameter=2});
            if(model.Draft != "999" || model.ParameterDraft != "27" || model.CurrentText != "19") return 1;
            if(Chinese.Simple("唐門金創藥") != "唐门金创药") return 2;
            return 0;
        }
        private static async Task<int> Smoke(string reportPath) {
            var log=new List<object>(); var backend=new Backend();
            int result=0;
            try {
                await backend.Start();
                log.Add(new {test="auto-start-hidden-backend",pass=backend.Started,pid=backend.BackendPid});
                Reply status=await backend.Request("status");
                log.Add(new {test="typed-ipc-unicode-json",pass=status.ok,message=status.message});
                if (!Backend.GameRunning) {
                    bool rejected=false;
                    try {await backend.Request("connect");} catch(BackendException) {rejected=true;}
                    log.Add(new {test="no-game-write-disabled",pass=rejected && !backend.Connected});
                    if(!rejected) result=1;
                }
                int pid=backend.BackendPid;
                await backend.Shutdown();
                bool exited=!Process.GetProcesses().Any(p=>p.Id == pid);
                log.Add(new {test="cleanup-and-owned-ce-exit",pass=exited});
                if(!exited) result=1;
            } catch(Exception e) {log.Add(new {test="smoke",pass=false,error=e.ToString()}); result=1;}
            try {await backend.Shutdown();} catch {}
            backend.Dispose();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath)));
            File.WriteAllText(reportPath,new JavaScriptSerializer().Serialize(log));
            return result;
        }
    }
}

using System;
using System.IO;
using System.Reflection;
using LOMTrainer;

class ExternalReleaseProbe {
    static int Main(string[] args) {
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e) {
            if(new AssemblyName(e.Name).Name == "活侠传助手") return Assembly.LoadFrom(args[0]);
            return null;
        };
        try {return Run(args);} catch(Exception e) {Console.WriteLine(e);return 1;}
    }
    static int Run(string[] args) {
        string folder=Bundle.Ensure();
        if(Directory.Exists(Path.Combine(folder,"ce"))) throw new Exception("Unexpected CE cache");
        string bootstrap=Path.Combine(folder,"managed","Bootstrap.dll");
        byte[] original=File.ReadAllBytes(bootstrap);
        File.Delete(bootstrap); Bundle.Ensure();
        if(!File.Exists(bootstrap)) throw new Exception("Missing-file repair failed");
        File.WriteAllText(bootstrap,"corrupt-cache-test"); Bundle.Ensure();
        if(Convert.ToBase64String(original)!=Convert.ToBase64String(File.ReadAllBytes(bootstrap))) throw new Exception("Hash repair failed");
        ExternalCE.Validate(args[1]);
        foreach(string path in new[]{"",Path.Combine(folder,"absent.exe")}) {
            bool rejected=false;try {ExternalCE.Validate(path);}catch(ArgumentException){rejected=true;}
            if(!rejected) throw new Exception("Invalid path accepted");
        }
        string testFolder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"incomplete-ce-test");
        Directory.CreateDirectory(testFolder);
        string ceOnly=Path.Combine(testFolder,"ce-only.exe");
        File.Copy(args[1],ceOnly,true);
        try {
            bool rejected=false;try {ExternalCE.Validate(ceOnly);}catch(ArgumentException e){rejected=e.Message.Contains("lua53-64.dll");}
            if(!rejected)throw new Exception("Incomplete installation accepted");
        } finally {File.Delete(ceOnly);}
        Console.WriteLine("PASS: Unicode cache extraction; missing/corrupt-file repair; valid external CE; empty/missing/incomplete CE rejected; no CE cache.");
        return 0;
    }
}

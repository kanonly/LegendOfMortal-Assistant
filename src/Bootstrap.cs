using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;

namespace LOMCT.Loader {
    // No Harmony/BepInEx reference: this assembly can load in an original game.
    public static class Bootstrap {
        private static string source = "未初始化", message = "尚未启用增强";
        private static string folder;
        private static bool registered;
        private static readonly string[] names = {"Mono.Cecil", "MonoMod.Utils", "MonoMod.RuntimeDetour", "0Harmony"};
        private static Assembly Existing(string name) {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                if (a.GetName().Name == name) return a;
            return null;
        }
        private static Assembly Resolve(object sender, ResolveEventArgs args) {
            string name = new AssemblyName(args.Name).Name;
            if (Array.IndexOf(names,name) < 0) return null;
            return Existing(name) ?? Assembly.LoadFrom(Path.Combine(folder,name + ".dll"));
        }
        private static void Check(Assembly a) {
            Type h = a.GetType("HarmonyLib.Harmony",true);
            Type hm = a.GetType("HarmonyLib.HarmonyMethod",true);
            Type at = a.GetType("HarmonyLib.AccessTools",true);
            if (h.GetConstructor(new[]{typeof(string)}) == null || h.GetMethod("UnpatchSelf",Type.EmptyTypes) == null ||
                hm.GetConstructor(new[]{typeof(MethodInfo)}) == null || at.GetMethod("Field",new[]{typeof(Type),typeof(string)}) == null)
                throw new NotSupportedException("已加载的 Harmony 缺少本助手所需 API");
            bool patch = false;
            foreach (MethodInfo m in h.GetMethods()) if (m.Name == "Patch" && m.GetParameters().Length == 5) patch = true;
            if (!patch) throw new NotSupportedException("已加载的 Harmony.Patch 不兼容");
        }
        public static int Prepare() {
            try {
                folder = Path.GetDirectoryName(typeof(Bootstrap).Assembly.Location);
                if (!registered) {AppDomain.CurrentDomain.AssemblyResolve += Resolve; registered=true;}
                Assembly existing = Existing("0Harmony");
                if (existing != null) {
                    Check(existing);
                    source = "复用已加载 Harmony " + existing.GetName().Version;
                } else {
                    foreach (string name in names) if (Existing(name) == null) Assembly.LoadFrom(Path.Combine(folder,name + ".dll"));
                    existing = Existing("0Harmony"); Check(existing);
                    source = "内嵌 HarmonyX " + existing.GetName().Version;
                }
                message = "兼容";
                return 1;
            } catch (Exception e) {
                while (e.InnerException != null) e=e.InnerException;
                message = "增强依赖不兼容：" + e.GetType().Name + ": " + e.Message;
                source = "不可用";
                return 0;
            }
        }
        public static string Describe() {return source + " · " + message;}
        public static string Inventory() {
            var list = new List<string>();
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies()) {
                string n=a.GetName().Name;
                if (n.StartsWith("BepInEx") || Array.IndexOf(names,n)>=0) list.Add(a.GetName().FullName);
            }
            return String.Join("\n",list.ToArray());
        }
    }
}

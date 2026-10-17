using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using HarmonyLib;
using UnityEngine;

namespace LOMCT.Runtime {
    // All patches belong only to this CT. No BepInEx plugin is installed.
    public static class Control {
        private const string Owner = "kanon.lomct.migration.v2";
        private static Harmony harmony;
        private static IntPtr options;
        private static int capabilities;
        private static readonly List<string> report = new List<string>();
        private static Type entityType, levelType, managerType;
        public const int Size = 8192;

        private static Type Type(string name) {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies()) {
                Type t = a.GetType(name, false);
                if (t != null) return t;
            }
            throw new MissingMemberException(name);
        }
        private static object Field(object obj, string name) {
            if (obj == null) return null;
            FieldInfo f = AccessTools.Field(obj.GetType(), name);
            return f == null ? null : f.GetValue(obj);
        }
        private static object Static(Type type, string name) {
            FieldInfo f = AccessTools.Field(type, name);
            return f == null ? null : f.GetValue(null);
        }
        private static int Number(object obj, string name) { return Convert.ToInt32(Field(obj, name)); }
        private static int Read(int offset) { return options == IntPtr.Zero ? 0 : Marshal.ReadInt32(options, offset); }
        private static void Write(int offset, int value) { Marshal.WriteInt32(options, offset, value); }
        private static bool On(int offset) { return Read(4) == 1 && Read(offset) == 1; }
        private static int Scale(int value, int multiplier, bool invert) {
            long n = value;
            if (invert && n < 0) n = -n;
            n *= Math.Max(1, Math.Min(1000, multiplier));
            return (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, n));
        }
        private static MethodInfo Method(string type, string method, params Type[] args) {
            MethodInfo result = AccessTools.Method(Type(type), method, args);
            if (result == null) throw new MissingMethodException(type, method);
            return result;
        }
        private static HarmonyMethod PatchMethod(string name) {
            return new HarmonyMethod(typeof(Control).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic));
        }
        private static void Prefix(MethodBase target, string prefix) { harmony.Patch(target, PatchMethod(prefix)); }
        private static MethodInfo Iterator(string type, string method) {
            foreach (Type nested in Type(type).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)) {
                if (nested.Name.StartsWith("<" + method + ">", StringComparison.Ordinal)) {
                    MethodInfo move = AccessTools.Method(nested, "MoveNext");
                    if (move != null && AccessTools.Field(nested, "<>4__this") != null) return move;
                }
            }
            throw new MissingMethodException(type, method + ".MoveNext");
        }
        private static void Install(int bit, string name, Action action) {
            try { action(); capabilities |= 1 << bit; report.Add(name + ": OK"); }
            catch (Exception e) { report.Add(name + ": " + e.GetType().Name + ": " + e.Message); }
        }
        public static long Attach() {
            if (harmony == null) harmony = new Harmony(Owner);
            harmony.UnpatchSelf();
            if (options == IntPtr.Zero) options = Marshal.AllocHGlobal(Size);
            Marshal.Copy(new byte[Size], 0, options, Size);
            Write(0, 0x4C4F4D32);
            for (int kind = 0; kind < 512; kind++) Write(4096 + kind * 4, 1);
            Write(36, 10); Write(40, 1); Write(64, 10); Write(96, 10); Write(116, 50);
            capabilities = 0; report.Clear();
            managerType = Type("Mortal.Core.PlayerStatManagerData");
            entityType = Type("Mortal.Battle.Entity");
            levelType = Type("Mortal.Battle.GameLevelManager");
            Install(0, "属性变化", delegate {
                Prefix(Method("Mortal.Core.GameStat", "AddValue", typeof(int), typeof(bool)), "StatDelta");
            });
            Install(1, "人际变化", delegate {
                Prefix(Method("Mortal.Core.RelationshipStat", "AddValue", typeof(int)), "RelationshipDelta");
            });
            Install(2, "炼丹升级消费额度", delegate {
                harmony.Patch(Iterator("Mortal.Core.UpgradePanel", "StartHandleUpgarde"), PatchMethod("BudgetBefore"), PatchMethod("BudgetAfter"));
            });
            Install(3, "修炼消费额度", delegate {
                harmony.Patch(Iterator("Mortal.Core.MartialPanel", "StartHandleUpgarde"), PatchMethod("BudgetBefore"), PatchMethod("BudgetAfter"));
            });
            Install(4, "开发门派规模", delegate {
                harmony.Patch(Method("Mortal.Core.UpgradeItemData", "get_RequiredSectLevel"), null, PatchMethod("SectLevel"));
            });
            Install(5, "战役生命", delegate {
                Prefix(Method("Mortal.Battle.CharacterHealth", "ModifyHealth", typeof(int)), "BattleHealth");
            });
            Install(6, "战役内力", delegate {
                Prefix(Method("Mortal.Battle.CharacterMana", "Modify", typeof(int)), "BattleMana");
            });
            Install(7, "我方人数", delegate {
                harmony.Patch(Method("Mortal.Battle.NpcSpawner", "HandleNpcRemove"), null, null, PatchMethod("NpcCountIL"));
            });
            Install(8, "决斗生命", delegate {
                Prefix(Method("Mortal.Combat.CombatStatController", "ModifyHealth", typeof(int)), "CombatHealth");
            });
            Install(9, "决斗内力", delegate {
                Prefix(Method("Mortal.Combat.CombatStatController", "ModifyStamina", typeof(int)), "CombatStamina");
            });
            Install(10, "天命轮盘", delegate {
                Prefix(Method("Mortal.Story.CheckPointManager", "Dice", typeof(string), typeof(int)), "Dice");
            });
            Write(12, capabilities);
            Write(4, 1);
            return options.ToInt64();
        }
        public static int Detach() {
            if (options != IntPtr.Zero) Write(4, 0);
            if (harmony != null) harmony.UnpatchSelf();
            // Retain 8 KB until process exit so a prefix already in flight can
            // safely observe Active=0. Re-enabling resets and reuses the block.
            return 1;
        }
        public static string Report() { return string.Join("\n", report.ToArray()); }
        public static int Capabilities() { return capabilities; }

        private static void StatDelta(object __instance, ref int __0) {
            int kind = Number(__instance, "_type");
            if (kind < 0 || kind >= 512 || !On(1024 + kind * 4)) return;
            object manager = Static(managerType, "_instance");
            IList stats = Field(Field(manager, "_stats"), "_list") as IList;
            if (stats != null && stats.Contains(__instance)) __0 = Scale(__0, Read(4096 + kind * 4), true);
        }
        private static void RelationshipDelta(ref int __0) {
            if (!On(32)) return;
            if (__0 > 0 || (__0 < 0 && Read(40) == 1)) __0 = Scale(__0, Read(36), Read(40) == 1);
        }
        private sealed class BudgetState { public object Panel; public int Before; }
        private static void BudgetBefore(object __instance, out BudgetState __state) {
            __state = null;
            object panel = Field(__instance, "<>4__this");
            if (panel == null || Number(__instance, "<>1__state") != 0) return;
            int flag = panel.GetType().Name == "UpgradePanel" ? 16 : 20;
            if (On(flag)) __state = new BudgetState { Panel = panel, Before = Number(panel, "_currentCount") };
        }
        private static void BudgetAfter(BudgetState __state) {
            if (__state == null) return;
            int after = Number(__state.Panel, "_currentCount");
            if (after < __state.Before) {
                long value = (long)__state.Before * 2 - after;
                AccessTools.Field(__state.Panel.GetType(), "_currentCount").SetValue(__state.Panel, (int)Math.Min(int.MaxValue, value));
            }
        }
        private static void SectLevel(object __instance, ref int __result) {
            int kind = Number(__instance, "_itemType");
            if ((kind <= 2 && On(24)) || (kind >= 11 && On(28))) __result = 0;
        }
        private static int Side(object instance) {
            Component component = instance as Component;
            if (component == null) return -1;
            object entity = component.GetComponent(entityType);
            return entity == null ? -1 : Number(entity, "<CurrentType>k__BackingField");
        }
        private static void BattleHealth(object __instance, ref int __0) {
            if (__0 >= 0 || Read(4) != 1) return;
            int side = Side(__instance);
            if (side == 0 && On(48)) __0 = Scale(__0, 1, true);
            else if (side == 2 && On(60)) __0 = Scale(__0, Read(64), false);
        }
        private static void BattleMana(object __instance, ref int __0) {
            if (Read(4) != 1) return;
            int side = Side(__instance);
            if (side == 0 && __0 < 0 && On(52)) __0 = Scale(__0, 1, true);
            else if (side == 2 && On(56)) __0 = -999999;
        }
        private static bool CombatPlayer(object instance) {
            return string.Equals(Convert.ToString(Field(Field(instance, "_statData"), "Name")), "player", StringComparison.OrdinalIgnoreCase);
        }
        private static void CombatHealth(object __instance, ref int __0) {
            if (__0 >= 0 || Read(4) != 1) return;
            if (CombatPlayer(__instance)) { if (On(80)) __0 = Scale(__0, 1, true); }
            else if (On(92)) __0 = Scale(__0, Read(96), false);
        }
        private static void CombatStamina(object __instance, ref int __0) {
            if (Read(4) != 1) return;
            if (CombatPlayer(__instance)) { if (__0 < 0 && On(84)) __0 = Scale(__0, 1, true); }
            else if (On(88)) __0 = -999999;
        }
        private static void Dice(ref int __1) { if (On(112)) __1 = Math.Max(0, Math.Min(100, Read(116))); }

        private static int NpcCount(int current, object spawner) {
            object level = Static(levelType, "<Instance>k__BackingField");
            if (On(68) && ReferenceEquals(Field(level, "_friendSpawner"), spawner)) return current;
            return current - 1;
        }
        private static IEnumerable<CodeInstruction> NpcCountIL(IEnumerable<CodeInstruction> instructions) {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            int changed = 0;
            for (int i = 1; i + 1 < code.Count; i++) {
                MethodInfo setter = code[i + 1].operand as MethodInfo;
                if (code[i - 1].opcode == OpCodes.Ldc_I4_1 && code[i].opcode == OpCodes.Sub && setter != null && setter.Name == "set_CurrentCount") {
                    code[i - 1].opcode = OpCodes.Ldarg_0;
                    code[i - 1].operand = null;
                    code[i].opcode = OpCodes.Call;
                    code[i].operand = typeof(Control).GetMethod("NpcCount", BindingFlags.Static | BindingFlags.NonPublic);
                    changed++;
                }
            }
            if (changed != 1) throw new InvalidOperationException("NpcSpawner count update did not match once");
            return code;
        }
    }
}

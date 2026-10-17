using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;

namespace LOMTrainer {
    public sealed class Entry {
        public string id, name, section, category, kind, parentId, note, tested;
        public long value, minimum, maximum, parameter;
        public long? finalValue, finalMaximum;
        public bool frozen, enabled, available, hasParameter, hasSecondary, secondary;
    }
    public sealed class Reply {
        public bool ok, connected, enhanced;
        public string error, message, backendVersion, harmonyStatus;
        public bool harmonyCompatible;
        public int generation, pid, total;
        public List<Entry> entries;
    }
    public sealed class EntryVM : INotifyPropertyChanged {
        public Entry Data;
        private string draft, parameterDraft;
        public string Id { get { return Data.id; } }
        public string Name { get { return Chinese.Simple(Data.name); } }
        public string CurrentText { get { return Data.kind == "feature" ? (Data.enabled ? "已开启" : "已关闭") : Data.value.ToString(CultureInfo.InvariantCulture); } }
        public string ListText { get { return Name + "\n" + (Data.section == "relations" ? "好感 " : "数量 ") + CurrentText; } }
        public string FreezeText { get { return Data.frozen ? "已冻结 · 解除" : "冻结数值"; } }
        public string ToggleText { get { return Data.enabled ? "已开启 · 关闭" : "开启功能"; } }
        public string SecondaryText { get { return Data.secondary ? "下降转增长：是" : "下降转增长：否"; } }
        public string DetailText { get { return Data.finalValue.HasValue ? String.Format("最终值 {0} / 上限 {1}  ·  基础范围 {2}～{3}", Data.finalValue, Data.finalMaximum, Data.minimum, Data.maximum) : (Data.maximum == 2147483647 ? "整数" : String.Format("范围 {0}～{1}", Data.minimum, Data.maximum)); } }
        public string Draft { get { return draft; } set { draft = value; Changed("Draft"); } }
        public string ParameterDraft { get { return parameterDraft; } set { parameterDraft = value; Changed("ParameterDraft"); } }
        public EntryVM(Entry data) { Data = data; draft = data.value.ToString(CultureInfo.InvariantCulture); parameterDraft = data.parameter.ToString(CultureInfo.InvariantCulture); }
        public void Update(Entry data) {
            Data = data;
            // Draft fields intentionally remain untouched during polling.
            foreach (string name in new[] {"CurrentText","ListText","FreezeText","ToggleText","SecondaryText","DetailText"}) Changed(name);
        }
        public event PropertyChangedEventHandler PropertyChanged;
        private void Changed(string name) { if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(name)); }
    }
    public sealed class Preferences {
        public string CheatEngine = "";
        public double Width = 1180, Height = 800, FontSize = 15;
        public bool Maximized;
        public static string Folder { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "助手数据"); } }
        public static Preferences Load() {
            try { return new JavaScriptSerializer().Deserialize<Preferences>(File.ReadAllText(Path.Combine(Folder,"settings.json"))) ?? new Preferences(); }
            catch { return new Preferences(); }
        }
        public void Save() {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder,"settings.json"), new JavaScriptSerializer().Serialize(this));
        }
    }
}

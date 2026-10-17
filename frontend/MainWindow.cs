using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace LOMTrainer {
    public static class Chinese {
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern int LCMapString(uint locale,uint flags,string source,int length,StringBuilder result,int count);
        public static string Simple(string text) {
            if (String.IsNullOrEmpty(text)) return "";
            var result = new StringBuilder(text.Length * 2 + 8);
            return LCMapString(0x0804,0x02000000,text,-1,result,result.Capacity) > 0 ? result.ToString() : text;
        }
    }
    public sealed class MainWindow : Window {
        private readonly Backend backend = new Backend();
        private readonly Preferences preferences = Preferences.Load();
        private readonly Dictionary<string,EntryVM> models = new Dictionary<string,EntryVM>();
        private readonly List<FrameworkElement> writes = new List<FrameworkElement>();
        private readonly HashSet<string> visible = new HashSet<string>();
        private readonly Dictionary<string,Button> navigation = new Dictionary<string,Button>();
        private readonly DispatcherTimer timer = new DispatcherTimer { Interval=TimeSpan.FromSeconds(1) };
        private readonly bool preview;
        private bool busy, pollingActive, closing, allowClose, manualStop, faulted;
        private int ticks;
        private string page = "常用", itemCategory = "book", featureCategory = "养成";
        private ContentControl pageHost;
        private TextBlock pageTitle, status, notice, sideStatus;
        private Button reconnect, stopButton;
        private ListBox browser;
        private StackPanel detail;
        private TextBox search;
        private CheckBox ownedOnly;
        private Brush Bg = BrushOf("#10151F"), PanelBg = BrushOf("#1C2534"), Muted = BrushOf("#A0ACBE"), Accent = BrushOf("#BEAEED"), Text = BrushOf("#E8EDF5");
        private static BitmapImage mascot;
        private Image Mascot(double size) {
            if (mascot == null) {
                using (var input=Assembly.GetExecutingAssembly().GetManifestResourceStream("LOMTrainer.Mascot")) {
                    mascot=new BitmapImage(); mascot.BeginInit(); mascot.CacheOption=BitmapCacheOption.OnLoad; mascot.StreamSource=input; mascot.EndInit(); mascot.Freeze();
                }
            }
            var image=new Image {Source=mascot,Width=size,Height=size,Stretch=Stretch.Uniform,Margin=new Thickness(0,0,0,12)};
            RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.HighQuality);
            AutomationProperties.SetName(image,"GPT 助手角色 · 白发紫瞳龙角少女挥手");
            return image;
        }

        public MainWindow(bool isPreview) {
            preview = isPreview;
            Title = "活侠传助手";
            using(var input=Assembly.GetExecutingAssembly().GetManifestResourceStream("LOMTrainer.Icon")) {
                var icons=new IconBitmapDecoder(input,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
                Icon=icons.Frames[icons.Frames.Count-1]; Icon.Freeze();
            }
            Background = Bg; Foreground = Text; FontFamily = new FontFamily("Microsoft YaHei UI");
            FontSize = Math.Max(14,Math.Min(20,preferences.FontSize));
            var work = SystemParameters.WorkArea;
            MinWidth = Math.Min(980,work.Width); MinHeight = Math.Min(600,work.Height);
            Width = Math.Min(work.Width,Math.Max(MinWidth,preferences.Width)); Height = Math.Min(work.Height,Math.Max(MinHeight,preferences.Height));
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            if (preferences.Maximized) WindowState = WindowState.Maximized;
            using (Stream theme = Assembly.GetExecutingAssembly().GetManifestResourceStream("LOMTrainer.Theme")) Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(theme));
            BuildShell();
            if (preview) LoadPreview();
            Render();
            Closing += OnClosing;
            Loaded += async delegate {
                if (preview) Message("界面预览 · 当前为示例数据，不连接游戏，应用与开关已禁用。",false);
                else if (String.IsNullOrWhiteSpace(preferences.CheatEngine)) {page="设置"; Render(); Message("欢迎使用！请先选择已安装的 CE 7.5 x64，再载入存档并点击重新定位。",false);}
                else if (Backend.GameReady) await Operation(Connect);
                else Message("请先启动《活侠传》并载入存档，助手将自动尝试连接。",false);
                timer.Start();
            };
            timer.Tick += async delegate { await Tick(); };
        }
        private static Brush BrushOf(string hex) { return (Brush)new BrushConverter().ConvertFromString(hex); }
        private TextBlock Label(string text,double size=0,Brush color=null) {
            return new TextBlock { Text=text, FontSize=size == 0 ? FontSize : size, Foreground=color ?? Text, TextWrapping=TextWrapping.Wrap, VerticalAlignment=VerticalAlignment.Center };
        }
        private Button Button(string text,Func<Task> action,bool write=false) {
            var button = new Button {Content=text};
            AutomationProperties.SetName(button,text);
            button.Click += async delegate { await Operation(action); };
            if (write) writes.Add(button);
            return button;
        }
        private static void Bound(FrameworkElement element,DependencyProperty property,EntryVM model,string path,BindingMode mode=BindingMode.OneWay) {
            element.SetBinding(property,new Binding(path) { Source=model, Mode=mode, UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged });
        }
        private TextBox NumberInput(EntryVM model,bool parameter=false) {
            var box = new TextBox { MinWidth=85, Margin=new Thickness(0,0,8,0), VerticalContentAlignment=VerticalAlignment.Center };
            Bound(box,TextBox.TextProperty,model,parameter ? "ParameterDraft" : "Draft",BindingMode.TwoWay);
            AutomationProperties.SetName(box,model.Name + (parameter ? " 参数" : " 新值"));
            writes.Add(box);
            return box;
        }
        private Border Card(UIElement child,Thickness? padding=null) {
            return new Border { Background=PanelBg, CornerRadius=new CornerRadius(10), Padding=padding ?? new Thickness(18), Child=child, Margin=new Thickness(0,0,0,12) };
        }
        private static ScrollViewer Scroll(UIElement content) {
            return new ScrollViewer { Content=content, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled, Padding=new Thickness(0,0,10,0) };
        }
        private void BuildShell() {
            var root = new Grid(); root.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(190) }); root.ColumnDefinitions.Add(new ColumnDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
            var side = new DockPanel { Background=BrushOf("#141F2B"), Margin=new Thickness(0,0,18,0) }; Grid.SetRowSpan(side,3); root.Children.Add(side);
            var brand = new StackPanel { Margin=new Thickness(18,22,12,22) };
            brand.Children.Add(Mascot(80));
            brand.Children.Add(Label("活侠传",28,Accent)); brand.Children.Add(Label("可 视 化 助 手",13,Muted)); DockPanel.SetDock(brand,Dock.Top); side.Children.Add(brand);
            var foot = new StackPanel {Margin=new Thickness(18,20,8,20)};
            sideStatus = Label("等待游戏",12,Muted); foot.Children.Add(sideStatus); foot.Children.Add(Label("退出即停止修改",12,Muted));
            DockPanel.SetDock(foot,Dock.Bottom); side.Children.Add(foot);
            var nav = new StackPanel { Margin=new Thickness(8,0,8,0) }; side.Children.Add(nav);
            foreach (string name in new[] {"常用","主角属性","人际关系","物品与功法","增强功能","设置"}) {
                string selected = name;
                var item = new Button { Content=name, Height=48, HorizontalContentAlignment=HorizontalAlignment.Left, Margin=new Thickness(0,0,0,7) };
                item.Click += delegate { page=selected; Render(); };
                navigation[name]=item; nav.Children.Add(item);
            }
            var top = new Grid { Margin=new Thickness(8,24,24,20) }; Grid.SetColumn(top,1); root.Children.Add(top);
            top.ColumnDefinitions.Add(new ColumnDefinition()); top.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
            var headings = new StackPanel(); pageTitle=Label("常用",28); pageTitle.FontWeight=FontWeights.SemiBold; headings.Children.Add(pageTitle);
            status=Label("● 等待游戏与存档",13,Muted); status.Margin=new Thickness(0,8,0,0); headings.Children.Add(status); top.Children.Add(headings);
            var actions = new StackPanel { Orientation=Orientation.Horizontal, VerticalAlignment=VerticalAlignment.Center };
            reconnect=Button("重新定位",Connect); reconnect.Background=Accent; reconnect.Foreground=Bg;
            stopButton=Button("停止全部修改",async delegate { await backend.Stop(); manualStop=true; models.Clear(); Message("全部冻结与增强已停止，已改数值保留。",false); Render(); });
            actions.Children.Add(reconnect); actions.Children.Add(stopButton); Grid.SetColumn(actions,1); top.Children.Add(actions);
            pageHost=new ContentControl { Margin=new Thickness(8,0,18,8), HorizontalContentAlignment=HorizontalAlignment.Stretch, VerticalContentAlignment=VerticalAlignment.Stretch };
            Grid.SetRow(pageHost,1); Grid.SetColumn(pageHost,1); root.Children.Add(pageHost);
            notice=Label("所有功能默认关闭。",13,Muted); notice.Margin=new Thickness(8,8,24,18); notice.MaxHeight=96;
            Grid.SetRow(notice,2); Grid.SetColumn(notice,1); root.Children.Add(notice);
            Content=root;
        }
        private void Render() {
            writes.Clear(); visible.Clear(); browser=null; detail=null;
            pageTitle.Text=page;
            foreach (var pair in navigation) { pair.Value.Background=pair.Key == page ? BrushOf("#38334F") : Brushes.Transparent; pair.Value.Foreground=pair.Key == page ? Accent : Muted; }
            if (page == "常用") pageHost.Content=Home();
            else if (page == "主角属性") pageHost.Content=Attributes();
            else if (page == "人际关系" || page == "物品与功法") pageHost.Content=BrowserPage(page == "人际关系");
            else if (page == "增强功能") pageHost.Content=Features();
            else pageHost.Content=Settings();
            UpdateState();
        }
        private EntryVM Find(string id,string fallback="") {
            EntryVM model;
            if (models.TryGetValue(id,out model)) return model;
            return new EntryVM(new Entry {id=id,name=fallback,kind="stat",minimum=0,maximum=2147483647,available=true});
        }
        private UIElement Home() {
            var panel=new StackPanel();
            var intro=Label("常用资源",18); intro.Margin=new Thickness(0,0,0,12); panel.Children.Add(intro);
            var cards=new UniformGrid {Columns=2};
            foreach (var pair in new[] {new[]{"stat:3","银两"},new[]{"stat:19","行动次数"},new[]{"stat:31","武学点数"},new[]{"stat:20","个人贡献度"}}) {
                EntryVM model=Find(pair[0],pair[1]); visible.Add(model.Id);
                var body=new StackPanel(); body.Children.Add(Label(pair[1],14,Muted));
                var value=Label("—",32,Accent); value.Margin=new Thickness(0,8,0,12);
                if (models.ContainsKey(model.Id)) Bound(value,TextBlock.TextProperty,model,"CurrentText"); body.Children.Add(value);
                var input=new DockPanel(); var apply=Button("应用",delegate {return Apply(model);},true); DockPanel.SetDock(apply,Dock.Right); input.Children.Add(apply); input.Children.Add(NumberInput(model)); body.Children.Add(input);
                var freeze=Button("冻结数值",delegate {return Freeze(model);},true); Bound(freeze,ContentControl.ContentProperty,model,"FreezeText"); freeze.Margin=new Thickness(0,12,0,0); body.Children.Add(freeze);
                var card=Card(body); card.Margin=new Thickness(0,0,12,12); cards.Children.Add(card);
            }
            panel.Children.Add(cards);
            var label=Label("常用增强",18); label.Margin=new Thickness(0,10,0,12); panel.Children.Add(label);
            foreach (string id in new[] {"feature:invert:3","feature:invert:31","feature:invert:20","feature:relationship"}) {
                EntryVM model; if (models.TryGetValue(id,out model)) panel.Children.Add(FeatureCard(model));
            }
            if (models.Count == 0) panel.Children.Add(Card(Label("连接存档后，这里会显示资源不减反增与好感倍率开关。",0,Muted)));
            panel.Children.Add(Label("物品、功法和人物的单独修改，请从左侧进入对应页面。",13,Muted));
            return Scroll(panel);
        }
        private UIElement Editor(EntryVM model,bool detailed=true) {
            visible.Add(model.Id);
            var outer=new StackPanel();
            var row=new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition());
            foreach (double width in new[] {100.0,108.0,82.0,128.0}) row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(width)});
            var title=Label(model.Name); title.Margin=new Thickness(0,0,10,0); row.Children.Add(title);
            var current=Label("",18,Accent); Bound(current,TextBlock.TextProperty,model,"CurrentText"); Grid.SetColumn(current,1); row.Children.Add(current);
            var input=NumberInput(model); Grid.SetColumn(input,2); row.Children.Add(input);
            var apply=Button("应用",delegate {return Apply(model);},true); Grid.SetColumn(apply,3); row.Children.Add(apply);
            var freeze=Button("冻结",delegate {return Freeze(model);},true); Bound(freeze,ContentControl.ContentProperty,model,"FreezeText"); Grid.SetColumn(freeze,4); row.Children.Add(freeze);
            outer.Children.Add(row);
            if (detailed) {
                var info=Label("",12,Muted); Bound(info,TextBlock.TextProperty,model,"DetailText"); info.Margin=new Thickness(0,8,0,0); outer.Children.Add(info);
                if (!String.IsNullOrEmpty(model.Data.note)) outer.Children.Add(Label(model.Data.note,12,Accent));
            }
            return Card(outer,new Thickness(15));
        }
        private UIElement Attributes() {
            var panel=new StackPanel();
            var refresh=Button("刷新最终值",async delegate { await backend.Request("refresh"); await Poll(models.Values.Where(m=>m.Data.section == "stats").Select(m=>m.Id)); Message("最终值已刷新。",false); },true);
            refresh.HorizontalAlignment=HorizontalAlignment.Left; refresh.Margin=new Thickness(0,0,0,12); panel.Children.Add(refresh);
            panel.Children.Add(Label("左侧是当前基础值；输入后点击应用。最终值包含加成，轻功等封顶后仍可能显示 100。",13,Muted));
            foreach (string category in new[] {"资源","基础属性","技能与抗性","其他属性（含剧情数值）"}) {
                var rows=new StackPanel();
                foreach (EntryVM model in models.Values.Where(m=>m.Data.section == "stats" && m.Data.category == category)) rows.Children.Add(Editor(model));
                var expander=new Expander { Header=category, IsExpanded=category != "其他属性（含剧情数值）", Content=rows, Margin=new Thickness(0,12,0,0) }; panel.Children.Add(expander);
            }
            if (models.Count == 0) panel.Children.Add(Empty());
            return Scroll(panel);
        }
        private UIElement BrowserPage(bool relations) {
            var root=new DockPanel();
            var controls=new StackPanel {Margin=new Thickness(0,0,0,12)};
            if (!relations) {
                var categories=new ComboBox {Margin=new Thickness(0,0,0,10)};
                string[] keys={"book","misc","special","consumable"};
                foreach (string name in new[] {"功法书籍","杂项","宝物","消耗品"}) categories.Items.Add(name);
                categories.SelectedIndex=Array.IndexOf(keys,itemCategory);
                categories.SelectionChanged+=delegate {itemCategory=keys[categories.SelectedIndex]; FilterBrowser(relations);}; controls.Children.Add(categories);
            }
            search=new TextBox {ToolTip="支持简体／繁体名称与物品 ID",Margin=new Thickness(0,0,0,4)};
            AutomationProperties.SetName(search,relations ? "搜索人物名称" : "搜索物品名称或 ID");
            search.TextChanged+=delegate {FilterBrowser(relations);};
            controls.Children.Add(Label(relations ? "搜索人物名称" : "物品／功法 → 分类 → 搜索选择 → 详情",12,Muted)); controls.Children.Add(search);
            ownedOnly=new CheckBox {Content=relations ? "仅显示已结识人物" : "仅显示已拥有物品",IsChecked=!relations};
            ownedOnly.Checked+=delegate {FilterBrowser(relations);}; ownedOnly.Unchecked+=delegate {FilterBrowser(relations);}; controls.Children.Add(ownedOnly);
            DockPanel.SetDock(controls,Dock.Top); root.Children.Add(controls);
            var split=new Grid(); split.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(0.38,GridUnitType.Star),MinWidth=220}); split.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(16)}); split.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(0.62,GridUnitType.Star)});
            browser=new ListBox();
            browser.ItemTemplate=(DataTemplate)XamlReader.Parse("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><TextBlock Text='{Binding ListText}' TextWrapping='Wrap' LineHeight='24'/></DataTemplate>");
            browser.SelectionChanged+=delegate {ShowDetail(browser.SelectedItem as EntryVM);}; split.Children.Add(browser);
            detail=new StackPanel(); var scroller=Scroll(detail); Grid.SetColumn(scroller,2); split.Children.Add(scroller); root.Children.Add(split);
            FilterBrowser(relations);
            return root;
        }
        private void FilterBrowser(bool relations) {
            if (browser == null || search == null || ownedOnly == null) return;
            string term=Chinese.Simple(search.Text).Trim(); string section=relations ? "relations" : "items";
            var items=models.Values.Where(m=>m.Data.section == section && String.IsNullOrEmpty(m.Data.parentId));
            if (!relations) items=items.Where(m=>m.Data.category == itemCategory);
            if (ownedOnly.IsChecked == true) items=items.Where(m=>relations ? IsKnown(m.Id) : m.Data.value > 0);
            if (term.Length > 0) items=items.Where(m=>Chinese.Simple(m.Name + " " + m.Id).IndexOf(term,StringComparison.OrdinalIgnoreCase) >= 0);
            string selected=browser.SelectedItem is EntryVM ? ((EntryVM)browser.SelectedItem).Id : null;
            var list=items.ToList(); browser.ItemsSource=list;
            browser.SelectedItem=list.FirstOrDefault(m=>m.Id == selected);
            if (browser.SelectedItem == null && list.Count > 0) browser.SelectedIndex=0;
            if (list.Count == 0) { detail.Children.Clear(); detail.Children.Add(Card(Label("当前筛选下没有项目。可清空搜索或取消上方筛选条件。",0,Muted))); }
        }
        private bool IsKnown(string id) { EntryVM active; return models.TryGetValue(id + ":active",out active) && active.Data.value == 1; }
        private void ShowDetail(EntryVM model) {
            if (detail == null) return;
            visible.Clear(); writes.Clear(); detail.Children.Clear();
            if (model == null) { UpdateState(); return; }
            detail.Children.Add(Label(model.Name,21,Accent)); detail.Children.Add(Label(model.Id,12,Muted));
            detail.Children.Add(DetailEditor(model,model.Data.section == "relations" ? "好感度" : "持有数量"));
            foreach (EntryVM child in models.Values.Where(m=>m.Data.parentId == model.Id)) {
                if (child.Data.kind == "boolean") {
                    visible.Add(child.Id);
                    var button=Button(IsKnown(model.Id) ? "已结识／显示 · 设为否" : "未结识／隐藏 · 设为是",async delegate {await backend.Request("set",child.Id,number:child.Data.value == 1 ? 0 : 1); await Poll(new[]{child.Id}); ShowDetail(model);},true);
                    button.Margin=new Thickness(0,14,0,12); detail.Children.Add(button);
                } else detail.Children.Add(new Expander {Header=child.Name,Content=DetailEditor(child,child.Name),IsExpanded=false,Margin=new Thickness(0,8,0,0)});
            }
            if (model.Data.category == "book") detail.Children.Add(Label("直接改修炼等级不会追补升级奖励。",13,Accent));
            UpdateState();
        }
        private UIElement DetailEditor(EntryVM model,string title) {
            visible.Add(model.Id);
            var body=new StackPanel { Margin=new Thickness(0,14,0,0) }; body.Children.Add(Label(title,14,Muted));
            var current=Label("",30,Accent); Bound(current,TextBlock.TextProperty,model,"CurrentText"); current.Margin=new Thickness(0,6,0,10); body.Children.Add(current);
            body.Children.Add(NumberInput(model));
            var actions=new WrapPanel {Margin=new Thickness(0,12,0,8)}; actions.Children.Add(Button("应用新值",delegate {return Apply(model);},true));
            var freeze=Button("冻结",delegate {return Freeze(model);},true); Bound(freeze,ContentControl.ContentProperty,model,"FreezeText"); actions.Children.Add(freeze); body.Children.Add(actions);
            var info=Label("",12,Muted); Bound(info,TextBlock.TextProperty,model,"DetailText"); body.Children.Add(info);
            return Card(body);
        }
        private UIElement Features() {
            var root=new DockPanel(); var tools=new StackPanel {Margin=new Thickness(0,0,0,14)};
            var categories=new ComboBox(); foreach (string name in new[] {"养成","属性变化","好感","战役","决斗","轮盘"}) categories.Items.Add(name); categories.SelectedItem=featureCategory;
            tools.Children.Add(categories); var filter=new TextBox {Margin=new Thickness(0,10,0,0),ToolTip="搜索本类功能"}; AutomationProperties.SetName(filter,"搜索增强功能"); tools.Children.Add(filter);
            tools.Children.Add(Label("开启任一功能时自动初始化增强；其余开关保持关闭。",13,Muted)); DockPanel.SetDock(tools,Dock.Top); root.Children.Add(tools);
            var rows=new StackPanel();
            Action populate=delegate {
                visible.Clear(); writes.Clear(); rows.Children.Clear(); featureCategory=(string)categories.SelectedItem;
                foreach (EntryVM model in models.Values.Where(m=>m.Data.section == "features" && m.Data.category == featureCategory && Chinese.Simple(m.Name).IndexOf(Chinese.Simple(filter.Text),StringComparison.OrdinalIgnoreCase) >= 0)) rows.Children.Add(FeatureCard(model));
                if (rows.Children.Count == 0) rows.Children.Add(Empty()); UpdateState();
            };
            categories.SelectionChanged+=delegate {populate();}; filter.TextChanged+=delegate {populate();}; populate(); root.Children.Add(Scroll(rows));
            return root;
        }
        private UIElement FeatureCard(EntryVM model) {
            visible.Add(model.Id);
            var body=new StackPanel(); var head=new DockPanel();
            var toggle=Button("开启功能",async delegate { Reply r=await backend.Request("feature",model.Id,"enabled",state:!model.Data.enabled); Update(r); Message("功能状态已确认。",false); },true);
            toggle.Tag=model.Id;
            Bound(toggle,ContentControl.ContentProperty,model,"ToggleText"); DockPanel.SetDock(toggle,Dock.Right); head.Children.Add(toggle);
            var names=new StackPanel(); names.Children.Add(Label(model.Name,16)); names.Children.Add(Label(model.Data.tested,12,Muted)); head.Children.Add(names); body.Children.Add(head);
            if (model.Data.hasParameter || model.Data.hasSecondary) {
                var settings=new StackPanel();
                if (model.Data.hasParameter) {
                    settings.Children.Add(Label(model.Id == "feature:dice" ? "轮盘点数（0～100）" : "倍率（1～1000）",13,Muted));
                    var input=new DockPanel {Margin=new Thickness(0,8,0,4)};
                    var apply=Button("应用参数",async delegate {
                        long number=Parse(model.ParameterDraft);
                        Update(await backend.Request("feature",model.Id,"parameter",number)); Message("参数已应用。",false);
                    },true); DockPanel.SetDock(apply,Dock.Right); input.Children.Add(apply); input.Children.Add(NumberInput(model,true)); settings.Children.Add(input);
                    apply.Tag=model.Id;
                    foreach(FrameworkElement child in input.Children) child.Tag=model.Id;
                }
                if (model.Data.hasSecondary) {
                    var secondary=Button("下降转增长",async delegate {Update(await backend.Request("feature",model.Id,"secondary",state:!model.Data.secondary));},true);
                    secondary.Tag=model.Id;
                    Bound(secondary,ContentControl.ContentProperty,model,"SecondaryText"); secondary.Margin=new Thickness(0,10,0,0); settings.Children.Add(secondary);
                }
                body.Children.Add(new Expander {Header="参数设置",Content=settings,Margin=new Thickness(0,8,0,0)});
            }
            if (!model.Data.available) body.Children.Add(Label("当前游戏版本的该入口不可用。",12,Accent));
            return Card(body);
        }
        private UIElement Settings() {
            var panel=new StackPanel();
            panel.Children.Add(Label("显示与连接",19,Accent));
            var display=new StackPanel(); display.Children.Add(Label("界面字号",14,Muted));
            var font=new ComboBox {Margin=new Thickness(0,8,0,0),Width=180,HorizontalAlignment=HorizontalAlignment.Left};
            foreach (double size in new[] {14.0,15.0,16.0,18.0,20.0}) font.Items.Add(size);
            font.SelectedItem=FontSize; font.SelectionChanged+=delegate {if (font.SelectedItem != null) {FontSize=(double)font.SelectedItem; preferences.FontSize=FontSize; preferences.Save(); Render();}}; display.Children.Add(font); panel.Children.Add(Card(display));
            var connection=new StackPanel(); connection.Children.Add(Label("外部 CE 7.5 x64 · 辅助资源 " + Bundle.Version,14,Accent));
            connection.Children.Add(Label("首次使用请选择完整安装目录中的 cheatengine-x86_64.exe。无需手动打开 CE、加载 CT 或输入 Lua。",14,Muted));
            var path=new TextBox {Text=preferences.CheatEngine ?? "",Margin=new Thickness(0,12,0,10),IsEnabled=!backend.Started};
            AutomationProperties.SetName(path,"CE 程序路径"); connection.Children.Add(path);
            var browse=new Button {Content="选择 CE 程序",IsEnabled=!backend.Started};
            browse.Click+=delegate {
                var dialog=new OpenFileDialog {Filter="CE 7.5 x64|cheatengine-x86_64.exe|可执行程序|*.exe",FileName=path.Text};
                if(dialog.ShowDialog(this)==true) path.Text=dialog.FileName;
            };
            var buttons=new WrapPanel(); buttons.Children.Add(browse);
            var save=Button("检查并保存路径",delegate {
                if(backend.Started) throw new ArgumentException("请先断开后台，再更换 CE 路径。");
                preferences.CheatEngine=ExternalCE.Validate(path.Text); preferences.Save();
                faulted=false; manualStop=false; Message("CE 路径已检查并保存。载入存档后点击重新定位。",false); Render();
                return Task.FromResult(0);
            }); save.IsEnabled=!backend.Started; buttons.Children.Add(save);
            var disconnect=Button("断开后台",async delegate {
                await backend.Shutdown(); models.Clear(); manualStop=true; faulted=false;
                Message("冻结与增强已清理，自有 CE 后台已退出。可以更换路径。",false); Render();
            }); disconnect.IsEnabled=backend.Started; buttons.Children.Add(disconnect); connection.Children.Add(buttons);
            connection.Children.Add(Label("后台运行时锁定路径；更换前请先断开后台。原版按需加载增强依赖，BepInEx 环境优先复用兼容 Harmony。",13,Muted));
            connection.Children.Add(Label(backend.HarmonyStatus ?? "尚未启用增强",14,Muted));
            panel.Children.Add(Card(connection));
            panel.Children.Add(new Expander {Header="使用说明",Content=Card(Label(Bundle.ReadText("LOMTrainer.Help"),14,Muted)),Margin=new Thickness(0,8,0,8)});
            panel.Children.Add(new Expander {Header="第三方组件与许可",Content=Card(Label(Bundle.ReadText("LOMTrainer.Notices"),13,Muted)),Margin=new Thickness(0,8,0,8)});
            var about=new StackPanel(); var portrait=Mascot(180); portrait.HorizontalAlignment=HorizontalAlignment.Left; about.Children.Add(portrait);
            about.Children.Add(Label("一起把工具做得更顺手",18,Accent));
            about.Children.Add(Label("本项目主要逆向分析、代码实现、界面开发及文档编写由 GPT 辅助完成；项目发起者负责需求、方向决策、角色参考提供及游戏内操作与测试配合。",14,Muted));
            about.Children.Add(Label("GPT娘角色来源：ZipZipPipe\n免费、非盈利 · 源码公开 · 非商业许可\n自有代码采用 PolyForm Noncommercial 1.0.0；第三方组件按各自许可证授权。角色图由 imagegen 参考用户提供的形象生成。",13,Accent));
            panel.Children.Add(new Expander {Header="关于本项目 · GPT 与你共同完成",Content=Card(about),Margin=new Thickness(0,8,0,8)});
            panel.Children.Add(Card(Label("日常无需打开 CE 或执行 Lua。助手只退出自己启动的 CE。\n关闭助手会停止冻结和增强；已修改数值保留。\n读档前停止全部修改，读档后重新定位。",14,Muted)));
            var time=new StackPanel(); foreach (EntryVM model in models.Values.Where(m=>m.Data.section == "time")) time.Children.Add(Editor(model));
            panel.Children.Add(new Expander {Header="高级：游戏时间",Content=time,IsExpanded=false});
            panel.Children.Add(Label("实战状态：战役、决斗、轮盘和完整升级流程仍待对应场景验证。",13,Accent));
            return Scroll(panel);
        }
        private UIElement Empty() {return Card(Label("连接存档后显示项目。若筛选无结果，请调整分类或搜索词。",0,Muted));}
        private static long Parse(string value) {long number; if (!Int64.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out number) || number < Int32.MinValue || number > Int32.MaxValue) throw new ArgumentException("请输入有效的四字节有符号整数。"); return number;}
        private async Task Apply(EntryVM model) {Update(await backend.Request("set",model.Id,number:Parse(model.Draft))); Message("新值已应用。",false);}
        private async Task Freeze(EntryVM model) {Update(await backend.Request("freeze",model.Id,state:!model.Data.frozen)); Message("冻结状态已确认。",false);}
        private void Update(Reply reply) {
            if (reply.entries != null) foreach (Entry entry in reply.entries) {EntryVM existing; if (models.TryGetValue(entry.id,out existing)) existing.Update(entry); else models.Add(entry.id,new EntryVM(entry));}
            UpdateState();
        }
        private async Task Connect() {
            if (preview) return;
            if (!Backend.GameReady) {Message("游戏尚未启动就绪。请启动游戏、等待至少 30 秒并载入存档后重新定位。",false); return;}
            manualStop=false; faulted=false;
            Message("正在启动后台并定位存档…",false);
            await backend.Start(preferences.CheatEngine);
            await backend.Request("connect");
            var catalog=await backend.Catalog(); models.Clear(); foreach (Entry entry in catalog) models.Add(entry.id,new EntryVM(entry));
            Message("已连接存档，所有冻结和增强默认关闭。",false); Render();
        }
        private async Task Poll(IEnumerable<string> ids) {
            var keys=ids.Distinct().Where(models.ContainsKey).ToList();
            for (int i=0;i<keys.Count;i+=30) Update(await backend.Request("read",String.Join("\n",keys.Skip(i).Take(30))));
        }
        private async Task Tick() {
            if (preview || busy || closing) return;
            ticks++;
            if (backend.Connected) {
                await Operation(async delegate {
                    var ids=new HashSet<string>(visible);
                    if (browser != null) foreach (EntryVM model in browser.Items) {var container=browser.ItemContainerGenerator.ContainerFromItem(model) as ListBoxItem; if (container != null && container.IsVisible) ids.Add(model.Id);}
                    if (ids.Count == 0) await backend.Request("status"); else await Poll(ids);
                },true);
            } else if (!manualStop && !faulted && !String.IsNullOrWhiteSpace(preferences.CheatEngine) && Backend.GameReady && ticks % 5 == 0) await Operation(Connect);
        }
        private async Task Operation(Func<Task> action,bool polling=false) {
            if (busy || closing || (preview && action == (Func<Task>)Connect)) return;
            if (polling && pollingActive) return;
            if (polling) pollingActive=true;
            else {busy=true; UpdateState();}
            Exception error=null;
            try {await action();}
            catch (Exception e) {error=e;}
            if (error != null) {
                bool ordinary=error is ArgumentException || error is BackendException;
                var failure=error as BackendException;
                if (failure != null && !failure.StillConnected && models.Count > 0) faulted=true;
                Message(error.Message,true);
                if (!ordinary) {
                    faulted=true;
                    if (backend.Started) {
                        try {await backend.Stop(); Message(error.Message + " 已停止后台修改，请重新定位。",true);}
                        catch {
                            if (!backend.Started && !backend.Enhanced) Message("后台 CE 已退出，写入已暂停。点击重新定位即可重新连接。",true);
                            else Message(error.Message + " 未能确认冻结与增强已停止，请重启游戏。",true);
                        }
                    } else if (backend.Enhanced) Message(error.Message + " 增强撤销未确认，请重启游戏。",true);
                }
            }
            if (polling) pollingActive=false; else busy=false;
            UpdateState();
        }
        private void UpdateState() {
            bool connected=backend.Connected && !faulted;
            status.Text=preview ? "● 界面预览 · 示例数据" : connected ? "● 已连接存档 · PID " + backend.GamePid : busy ? "● 正在连接／处理…" : manualStop ? "● 已停止全部修改" : faulted ? "● 连接已中断，请重新定位" : "● 等待游戏与存档";
            status.Foreground=connected ? BrushOf("#8AC8B2") : Muted;
            sideStatus.Text=preview ? "预览模式" : connected ? "CE 后台运行中" : "等待连接";
            foreach (FrameworkElement control in writes) {
                EntryVM model;
                bool available=!(control.Tag is string) || !models.TryGetValue((string)control.Tag,out model) || model.Data.available;
                control.IsEnabled=connected && !busy && !closing && !preview && available;
            }
            reconnect.IsEnabled=!busy && !closing && !preview; stopButton.IsEnabled=!busy && !closing && backend.Started && !preview;
        }
        private void Message(string text,bool error) {notice.Text=text; notice.Foreground=error ? BrushOf("#F0A5A0") : Muted;}
        private async void OnClosing(object sender,CancelEventArgs args) {
            if (allowClose) return;
            args.Cancel=true;
            if (closing) return;
            closing=true; timer.Stop(); UpdateState();
            try {
                if (busy) {Message("正在完成当前操作，随后停止全部修改…",false); while (busy) await Task.Delay(100);}
                if (!preview) await backend.Shutdown();
                preferences.Width=RestoreBounds.Width; preferences.Height=RestoreBounds.Height; preferences.Maximized=WindowState == WindowState.Maximized; preferences.Save();
                allowClose=true; backend.Dispose(); Close();
            } catch (Exception error) {
                closing=false; faulted=true; Message("退出清理未确认：" + error.Message + " 请处理后再次关闭；已修改数值不会自动回滚。",true); UpdateState();
                // A dead backend cannot retry cleanup. Make an explicit exit choice.
                if (!backend.Started && MessageBox.Show(this,"后台已退出，无法确认增强撤销。请重启游戏。\n是否关闭助手窗口？","清理未确认",MessageBoxButton.YesNo,MessageBoxImage.Warning) == MessageBoxResult.Yes) {allowClose=true; backend.Dispose(); Close();}
            }
        }
        private void LoadPreview() {
            foreach (var pair in new[] {new[]{"stat:3","银两","59884"},new[]{"stat:19","行动次数","2"},new[]{"stat:31","武学点数","36"},new[]{"stat:20","个人贡献度","160"}}) models.Add(pair[0],new EntryVM(new Entry {id=pair[0],name=pair[1],section="stats",category="资源",kind="stat",value=Int64.Parse(pair[2]),minimum=0,maximum=999999,available=true}));
            string[] names={"体力","内力","轻功","魅力","学问","心理卫生","嘴力","防御"}; int[] kinds={0,1,2,4,5,6,12,29};
            for (int i=0;i<names.Length;i++) {string id="stat:" + kinds[i]; models.Add(id,new EntryVM(new Entry {id=id,name=names[i],section="stats",category="基础属性",kind="stat",value=20+i,minimum=0,maximum=100,finalValue=76+i,finalMaximum=100,available=true}));}
            foreach (string id in new[]{"feature:invert:3","feature:invert:31","feature:invert:20","feature:relationship"}) models.Add(id,new EntryVM(new Entry {id=id,name=id.EndsWith("3") ? "银两不减反增" : id.EndsWith("31") ? "武学点数不减反增" : id.EndsWith("20") ? "个人贡献度不减反增" : "好感度增长加倍",section="features",category="养成",kind="feature",hasParameter=true,parameter=10,minimum=1,maximum=1000,available=true,tested="界面预览示例"}));
            foreach (var pair in new[]{new[]{"book","太祖长拳","0001"},new[]{"book","唐门暗器总纲","0002"},new[]{"misc","唐门改良金创药","0001"},new[]{"special","门派信物","0001"},new[]{"consumable","消耗品示例","0001"}}) {
                string id="item:"+pair[0]+":"+pair[2]; models.Add(id,new EntryVM(new Entry {id=id,name=pair[1]+" ["+pair[2]+"]",section="items",category=pair[0],kind=pair[0],value=pair[0]=="misc" ? 18 : 1,minimum=0,maximum=2147483647,available=true}));
                if(pair[0]=="book") foreach(string field in new[]{"level","points"}) {string child=id+":"+field;models.Add(child,new EntryVM(new Entry {id=child,parentId=id,name=field=="level" ? "当前修炼等级" : "当前修炼点数",section="items",category="book",kind=field,value=field=="level" ? 3 : 20,minimum=0,maximum=2147483647,available=true}));}
            }
        }
    }
}

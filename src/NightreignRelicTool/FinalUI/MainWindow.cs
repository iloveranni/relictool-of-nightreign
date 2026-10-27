using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using NightreignRelicTool.Core;
using NightreignRelicTool.CustomEffectSearch;
using NightreignRelicTool.Localization;

namespace NightreignRelicTool.FinalUI
{
    public sealed class MainWindow : ShellWindow
    {
        public readonly CustomEffectSearchViewModel Model;
        public readonly WindowPreferences Preferences;
        public Grid Workspace, CalculationStatus, SaveStatus;
        public SharedHeightCards ResultGrid;
        public Button EnglishButton, ChineseButton, ZoomOut, ZoomIn, ClearSearchButton, RestoreButton, PositionButton, ImportButton, RefreshButton;
        public TextBox SearchBox;
        public TextBlock Placeholder, CountLabel, ReadStatus, ResultStatus;
        public StackPanel RuleList, Choices, Nav;
        public Panel CupTabs;
        public WrapPanel Categories;
        public ScrollViewer ChoiceScroll, RuleScroll, ResultScroll;
        public ComboBox CharacterBox, SaveSourceBox, SaveSlotBox;
        public PositionWindow Position;
        public AboutWindow About;
        public RevenantBusyIndicator BusyIndicator;
        public const double ResultBodyFontSize = 17, ResultValueFontSize = 16, ResultLineHeight = 24, ResultEffectGap = 8;
        private TextBlock playerTitle, versionLabel, sourceFile;
        private bool syncing, disposed, refreshQueued;
        [Flags] private enum RefreshPart { None = 0, Identity = 1, Filters = 2, Choices = 4, Rules = 8, Results = 16, Controls = 32, Scale = 64, All = 127 }
        private RefreshPart pendingRefresh;
        private readonly string preferencePath;
        private readonly bool initializeOnLoad;
        private readonly DispatcherTimer saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        private readonly INotifyCollectionChanged[] collections;
        private readonly ResultRetention retained = new ResultRetention();
        private CustomEffectVesselResultViewModel observedResult;
        private CustomEffectRelicSlotViewModel[] observedSlots = new CustomEffectRelicSlotViewModel[0];

        public MainWindow() : this(new CustomEffectSearchViewModel()) { }
        public MainWindow(CustomEffectSearchViewModel model, bool initializeOnLoad = true, string preferencePath = null)
            : base("Relic Tool", 1480, 930, true)
        {
            Model = model ?? throw new ArgumentNullException("model"); DataContext = model;
            this.initializeOnLoad = initializeOnLoad;
            this.preferencePath = preferencePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UserData", "CustomEffectSearch", "window-preferences.json");
            Preferences = WindowPreferences.Load(this.preferencePath);
            MinWidth = Math.Min(1100, SystemParameters.WorkArea.Width); MinHeight = Math.Min(650, SystemParameters.WorkArea.Height);
            Width = Math.Min(Preferences.MainWidth, SystemParameters.WorkArea.Width); Height = Math.Min(Preferences.MainHeight, SystemParameters.WorkArea.Height);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/NightreignRelicTool;component/FinalUI/SelectorStyles.xaml", UriKind.Relative) });
            BuildTitle(); BuildWorkspace();
            collections = new INotifyCollectionChanged[] { Model.Characters, Model.SaveSlots, Model.SaveSources, Model.PrimaryCategories,
                Model.Categories, Model.PresetGroups, Model.VisibleEffects, Model.VisiblePresets, Model.Rules, Model.Results };
            foreach (var collection in collections) collection.CollectionChanged += CollectionChanged;
            Model.PropertyChanged += ModelChanged; LocalizationService.Current.Changed += LanguageChanged;
            saveTimer.Tick += SaveTick; SizeChanged += RememberBounds; Loaded += WindowLoaded;
            RefreshAll();
        }
        private async void WindowLoaded(object sender, RoutedEventArgs args)
        {
            Loaded -= WindowLoaded;
            if (initializeOnLoad) await RunOperation(Model.InitializeAsync, "初始化");
        }
        private void BuildTitle()
        {
            var title = new Grid { Margin = new Thickness(12, 0, 4, 0) };
            title.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star }); title.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Auto });
            var identity = new StackPanel { Orientation = Orientation.Horizontal };
            var leaf = Ui.Button("", OpenAbout); leaf.Width = 38; leaf.Padding = new Thickness(4);
            // #6: local template only; preserve button keyboard activation/focus.
            var leafChrome = new FrameworkElementFactory(typeof(Border)); leafChrome.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            leafChrome.SetValue(Border.PaddingProperty, new Thickness(4));
            var leafContent = new FrameworkElementFactory(typeof(ContentPresenter)); leafContent.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); leafContent.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); leafChrome.AppendChild(leafContent);
            leaf.Template = new ControlTemplate(typeof(Button)) { VisualTree = leafChrome };
            System.Windows.Automation.AutomationProperties.SetName(leaf, "关于软件");
            leaf.Content = new Image { Source = Icon, Width = 26, Height = 26 }; identity.Children.Add(leaf);
            playerTitle = Ui.Label("Relic Tool", 21); playerTitle.MaxWidth = 320; playerTitle.TextTrimming = TextTrimming.CharacterEllipsis; playerTitle.TextWrapping = TextWrapping.NoWrap; identity.Children.Add(playerTitle);
            versionLabel = Ui.Label("", 12, Ui.Muted); versionLabel.Margin = new Thickness(16, 0, 0, 0); identity.Children.Add(versionLabel); Ui.Place(title, identity, 0);
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            EnglishButton = LanguageChoice("EN", "en"); ChineseButton = LanguageChoice("中", "zh-CN");
            right.Children.Add(EnglishButton); right.Children.Add(ChineseButton);
            ZoomOut = Ui.Button("−", () => Model.AdjustUiScale(-1)); ZoomIn = Ui.Button("+", () => Model.AdjustUiScale(1));
            foreach (var b in new[] { ZoomOut, ZoomIn }) { b.Width = 40; b.Margin = new Thickness(8, 0, 0, 0); right.Children.Add(b); }
            Ui.Place(title, right, 0, 1); Ui.Place(Header, title, 0);
        }
        private Button LanguageChoice(string text, string language)
        {
            var button = Ui.Button(text, () => {
                try { LocalizationService.Current.Choose(language); }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
                { Model.ReportBoundaryFailure("保存语言偏好"); }
            });
            button.Tag = language; button.Width = 40; button.Padding = new Thickness(0); button.Margin = new Thickness(8, 0, 0, 0);
            var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); border.AppendChild(content);
            button.Template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            WindowDrag.SetIsExcluded(button, true); return button;
        }
        private ComboBox SelectBox(Action<int> action)
        {
            var box = Ui.Combo(new object[0], -1, i => { if (!syncing && !disposed && !Model.IsReading) action(i); });
            box.Style = (Style)FindResource("PreviewQuietSelector");
            return box;
        }
        private void BuildWorkspace()
        {
            Workspace = new Grid { Margin = new Thickness(16, 6, 16, 16) }; // #9: lift the common top by 6 DIP, including the cup bar.
            foreach (double width in new[] { 22d, 12, 23, 12, 55 })
                Workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width, width == 12 ? GridUnitType.Pixel : GridUnitType.Star) });
            Body.Content = Workspace;
            var left = Ui.Rows(Ui.Auto, Ui.Auto, Ui.Auto, Ui.Auto, Ui.Star, Ui.Auto);
            // #9: role label removed; retain the original grid and proportions.
            CharacterBox = SelectBox(i => Model.SelectedCharacter = Model.Characters[i]); CharacterBox.Style = (Style)FindResource("PreviewCharacterSelector"); CharacterBox.MinHeight = 44; CharacterBox.Margin = new Thickness(0, 0, 0, 8); Ui.Place(left, CharacterBox, 1);
            Nav = new StackPanel { Margin = new Thickness(0, 0, 0, 16) }; Ui.Place(left, Nav, 2);
            var ruleHeader = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            ruleHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star }); ruleHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Auto });
            CountLabel = Ui.Label("", 15); Ui.Place(ruleHeader, CountLabel, 0);
            var clear = Ui.Button("清空", Model.ClearRules); clear.Padding = new Thickness(8, 4, 8, 4); clear.MinHeight = 28;
            Ui.Place(ruleHeader, clear, 0, 1); Ui.Place(left, ruleHeader, 3);
            RuleList = new StackPanel(); RuleScroll = Ui.Scroll(RuleList); Ui.Place(left, RuleScroll, 4); Ui.Place(left, BuildSave(), 5);
            var leftScroll = Ui.Scroll(left); leftScroll.SizeChanged += (s, e) => left.Height = Math.Max(700, leftScroll.ActualHeight);
            Ui.Place(Workspace, Ui.Panel(leftScroll, 16), 0, 0);
            var middle = Ui.Rows(Ui.Auto, Ui.Auto, Ui.Star);
            var search = new Grid { Height = 44, Margin = new Thickness(0, 0, 0, 16) };
            search.SizeChanged += (s, e) => search.Clip = new RectangleGeometry(new Rect(0, 0, search.ActualWidth, search.ActualHeight), 6, 6);
            SearchBox = new TextBox { FontSize = 15, Padding = new Thickness(10, 0, 40, 0), VerticalContentAlignment = VerticalAlignment.Center,
                Background = Ui.Brush("#181613"), Foreground = Ui.Text, CaretBrush = Ui.Gold, BorderThickness = new Thickness(0), SelectionBrush = Ui.Brush("#5B492B") };
            SearchBox.TextChanged += (s, e) => { if (!syncing) Model.SearchText = SearchBox.Text; UpdateSearchChrome(); }; search.Children.Add(SearchBox);
            Placeholder = Ui.Label("搜索词条或遗物", 15, Ui.Faint); Placeholder.IsHitTestVisible = false; Placeholder.Margin = new Thickness(12, 0, 44, 0); search.Children.Add(Placeholder);
            ClearSearchButton = Ui.Button("×", () => { Model.SearchText = ""; SearchBox.Text = ""; SearchBox.Focus(); }, false, "清空关键词");
            ClearSearchButton.Width = 36; ClearSearchButton.HorizontalAlignment = HorizontalAlignment.Right; ClearSearchButton.Margin = new Thickness(0, 4, 4, 4); search.Children.Add(ClearSearchButton); Ui.Place(middle, search, 0);
            Categories = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) }; Categories.SizeChanged += (s, e) => ConstrainCategories();
            var categoryScroll = Ui.Scroll(Categories); categoryScroll.MaxHeight = 240; categoryScroll.Margin = new Thickness(0, 0, 0, 12); Ui.Place(middle, categoryScroll, 1);
            Choices = new StackPanel(); ChoiceScroll = Ui.Scroll(Choices); Ui.Place(middle, ChoiceScroll, 2); Ui.Place(Workspace, Ui.Panel(middle, 16), 0, 2);
            var right = Ui.Rows(Ui.Auto, Ui.Auto, Ui.Star);
            CupTabs = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(4, 0, 4, 4) }; Ui.Place(right, CupTabs, 0);
            var actionBar = new DockPanel { LastChildFill = false, MinHeight = 36 }; // #1: busy row must not grow from 32 to 36 during replacement.
            var actionGroup = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            RestoreButton = Ui.Button("恢复最优", Model.RestoreOptimalSelected); PositionButton = Ui.Button("显示位置", OpenPosition);
            foreach (var button in new[] { RestoreButton, PositionButton }) { button.Height = 32; button.VerticalAlignment = VerticalAlignment.Center; button.Padding = new Thickness(10, 4, 10, 4); actionGroup.Children.Add(button); }
            DockPanel.SetDock(actionGroup, Dock.Right); actionBar.Children.Add(actionGroup);
            CalculationStatus = new Grid { Height = 36, Visibility = Visibility.Collapsed };
            actionBar.SizeChanged += (s, e) => { CalculationStatus.Width = Math.Max(0, actionBar.ActualWidth - actionGroup.ActualWidth - 12); BusyIndicator.Width = Math.Max(48, CalculationStatus.Width * Model.UiScale); }; DockPanel.SetDock(CalculationStatus, Dock.Left); actionBar.Children.Add(CalculationStatus); Ui.Place(right, actionBar, 1);
            // #10: retain a detached compatibility field, with no visual row or success message.
            ResultStatus = Ui.Label("", 13, Ui.Muted);
            ResultGrid = new SharedHeightCards { Margin = new Thickness(0, 6, 0, 0) }; ResultScroll = Ui.Scroll(ResultGrid);
            ResultScroll.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = Application.Current.FindResource("FinalQuietScrollBar"); Ui.Place(right, ResultScroll, 2);
            ResultScroll.SizeChanged += (s, e) => ResultGrid.MinHeight = Math.Max(0, ResultScroll.ActualHeight - 6);
            // #9: measured cup names may wrap; all three top controls follow that natural height.
            CupTabs.SizeChanged += (s, e) => { double h = Math.Max(44, CupTabs.ActualHeight); CharacterBox.Height = h; search.Height = h; };
            var resultPanel = Ui.Panel(right, 16, "#0D0C0B");
            resultPanel.Padding = new Thickness(16, 16, 4, 0); // R5: reduce only the right inset; keep the common column bottom.
            Ui.Place(Workspace, resultPanel, 0, 4);
        }
        private UIElement BuildSave()
        {
            var save = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            SaveSourceBox = SelectBox(i => SelectSource(Model.SaveSources[i])); SaveSourceBox.Margin = new Thickness(0, 2, 0, 2); SaveSourceBox.FontSize = 12; SaveSourceBox.MinHeight = 28; SaveSourceBox.Padding = new Thickness(10, 4, 10, 4); save.Children.Add(SaveSourceBox);
            var sourceText = new FrameworkElementFactory(typeof(TextBlock)); sourceText.SetBinding(TextBlock.TextProperty, new Binding("DisplayName"));
            sourceText.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); SaveSourceBox.ItemTemplate = new DataTemplate { VisualTree = sourceText };
            sourceFile = Ui.Label("", 11, Ui.Faint); sourceFile.Margin = new Thickness(11, 0, 0, 0); save.Children.Add(sourceFile);
            SaveSlotBox = SelectBox(i => Model.SelectedSaveSlot = Model.SaveSlots[i]); SaveSlotBox.Margin = new Thickness(0, 4, 0, 2); SaveSlotBox.FontSize = 12; SaveSlotBox.MinHeight = 28; SaveSlotBox.Padding = new Thickness(10, 4, 10, 4); save.Children.Add(SaveSlotBox);
            SaveStatus = new Grid { MinHeight = 22 }; ReadStatus = Ui.Label("", 11, Ui.Muted); ReadStatus.Margin = new Thickness(11, 0, 0, 0); SaveStatus.Children.Add(ReadStatus);
            BusyIndicator = new RevenantBusyIndicator(); SaveStatus.Children.Add(BusyIndicator); save.Children.Add(SaveStatus);
            var actions = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) }; ImportButton = Ui.Button("导入存档", ImportSave); RefreshButton = Ui.Button("刷新", RefreshSaves);
            foreach (var button in new[] { ImportButton, RefreshButton }) { button.FontSize = 12; button.MinHeight = 28; button.Padding = new Thickness(8, 3, 8, 3); }
            actions.Children.Add(ImportButton); actions.Children.Add(RefreshButton); save.Children.Add(actions); return save;
        }
        private static string Display(object item, string property)
        { return Convert.ToString(DisplayConverter.Render(item, property, item.GetType().GetProperty(property).GetValue(item, null))); }
        private void CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            QueueRefresh(ReferenceEquals(sender, Model.Rules) ? RefreshPart.Rules | RefreshPart.Choices : ReferenceEquals(sender, Model.Results) ? RefreshPart.Results
                : ReferenceEquals(sender, Model.VisibleEffects) || ReferenceEquals(sender, Model.VisiblePresets) ? RefreshPart.Choices
                : ReferenceEquals(sender, Model.Categories) || ReferenceEquals(sender, Model.PrimaryCategories) || ReferenceEquals(sender, Model.PresetGroups) ? RefreshPart.Filters : RefreshPart.Identity);
        }
        private void ModelChanged(object sender, PropertyChangedEventArgs e)
        {
            string p = e.PropertyName;
            if (string.IsNullOrEmpty(p) || p == "SelectedCharacter") QueueRefresh(RefreshPart.All);
            else if (p == "UiScale" || p == "UiScaleIndex") QueueRefresh(RefreshPart.Scale);
            else if (p == "SelectedResult" || p == "CanRestoreOptimal") QueueRefresh(RefreshPart.Results | RefreshPart.Controls);
            else if (p == "NoRulesVisibility" || p == "HasRulesVisibility") QueueRefresh(RefreshPart.Rules | RefreshPart.Choices);
            else if (p == "SearchText") QueueRefresh(RefreshPart.Choices);
            else if (p == "SelectedCategory" || p == "SelectedPrimaryCategory" || p == "SelectedPresetGroup") QueueRefresh(RefreshPart.Filters | RefreshPart.Choices);
            else if (p == "SelectedSaveSource" || p == "SelectedSaveSlot" || p == "SavePlayerNameDisplay" || p == "SaveCompactStatus" || p == "DataVersionLabel") QueueRefresh(RefreshPart.Identity | RefreshPart.Controls);
            else if (p == "IsReading" || p == "IsSearching" || p == "IsReplacing" || p == "HasSave" || p == "CanShowPosition") QueueRefresh(RefreshPart.Controls | RefreshPart.Results);
            else if (p == "StatusText" || p == "DataError" || p == "NotificationText") QueueRefresh(RefreshPart.Controls);
        }
        private void LanguageChanged(object sender, EventArgs e) { QueueRefresh(RefreshPart.All); }
        private void QueueRefresh(RefreshPart part)
        {
            if (disposed) return; pendingRefresh |= part;
            if (refreshQueued) return; refreshQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(() => { refreshQueued = false; var next = pendingRefresh; pendingRefresh = RefreshPart.None; if (!disposed) Refresh(next); }));
        }
        public void RefreshAll() { Refresh(RefreshPart.All); }
        private void Refresh(RefreshPart parts)
        {
            if (disposed) return;
            syncing = true;
            try
            {
                if (SearchBox.Text != (Model.SearchText ?? "")) SearchBox.Text = Model.SearchText ?? "";
                if ((parts & RefreshPart.Identity) != 0)
                {
                CharacterBox.ItemsSource = Model.Characters.Select(x => (object)L.G("character", x.Id, x.Name)).ToArray(); CharacterBox.SelectedIndex = Model.Characters.IndexOf(Model.SelectedCharacter);
                // Player and source names are user content: never run them through the UI translation dictionary.
                SaveSourceBox.ItemsSource = Model.SaveSources; SaveSourceBox.SelectedIndex = Model.SaveSources.IndexOf(Model.SelectedSaveSource);
                SaveSlotBox.ItemsSource = Model.SaveSlots.Select(x => (object)(string.IsNullOrWhiteSpace(x.Inventory.PlayerName)
                    ? L.U("未命名") + x.DisplayName.Substring("未命名".Length) : x.DisplayName)).ToArray(); SaveSlotBox.SelectedIndex = Model.SaveSlots.IndexOf(Model.SelectedSaveSlot);
                }
            }
            finally { syncing = false; }
            playerTitle.Text = Model.HasSave ? Model.SavePlayerNameDisplay : "Relic Tool"; versionLabel.Text = "Regulation " + Model.ShortDataVersionLabel;
            sourceFile.Text = Model.SelectedSaveSource == null ? L.U("未选择存档") : Model.SelectedSaveSource.SourceFileName;
            CharacterBox.IsEnabled = !Model.IsReading; SaveSlotBox.IsEnabled = !Model.IsReading && Model.HasSave; SaveSourceBox.IsEnabled = !Model.IsReading;
            ImportButton.IsEnabled = RefreshButton.IsEnabled = !Model.IsReading;
            string notice = Model.IsDataLoaded ? Model.NotificationText : Model.DataError;
            ReadStatus.Text = Display(Model, "SaveCompactStatus") + (string.IsNullOrWhiteSpace(notice) ? "" : "\n" + L.U(notice));
            ReadStatus.Foreground = string.IsNullOrWhiteSpace(notice) ? Ui.Muted : Brushes.Salmon;
            Ui.SetText(ResultStatus, notice); ResultStatus.Visibility = Visibility.Collapsed;
            foreach (var button in new[] { EnglishButton, ChineseButton })
            {
                bool selected = (string)button.Tag == LocalizationService.Current.Language;
                button.Foreground = selected ? Ui.Gold : Ui.Muted;
                System.Windows.Automation.AutomationProperties.SetItemStatus(button, selected ? "selected" : "unselected");
            }
            if ((parts & RefreshPart.Filters) != 0)
            {
            Nav.Children.Clear();
            foreach (var page in Model.PrimaryCategories)
            {
                var p = page; var b = Ui.Button(L.G("primary", p.Name, L.U(p.Name)), () => Model.SelectedPrimaryCategory = p, ReferenceEquals(p, Model.SelectedPrimaryCategory));
                Ui.WrapButtonText(b); b.Margin = new Thickness(0, 2, 0, 2); Nav.Children.Add(b);
            }
            RefreshCategories();
            }
            UpdateSearchChrome();
            if ((parts & RefreshPart.Choices) != 0) RefreshChoices();
            if ((parts & RefreshPart.Rules) != 0) RefreshRules();
            if ((parts & RefreshPart.Results) != 0) RefreshResults();
            if ((parts & RefreshPart.Scale) != 0) ApplyScale();
            UpdateBusy();
        }
        public void UpdateSearchChrome()
        {
            bool empty = string.IsNullOrEmpty(SearchBox.Text); Placeholder.Visibility = empty ? Visibility.Visible : Visibility.Collapsed; ClearSearchButton.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        }
        private void RefreshCategories()
        {
            Categories.Children.Clear();
            if (Model.IsPresetCategorySelected)
                foreach (var group in Model.PresetGroups) { var g = group; CategoryButton(L.U(g.Name), () => Model.TogglePresetGroup(g), ReferenceEquals(g, Model.SelectedPresetGroup)); }
            else foreach (var category in Model.Categories) { var c = category; CategoryButton(L.G("category", c.Id, c.Name), () => Model.ToggleCategory(c), ReferenceEquals(c, Model.SelectedCategory)); }
            ConstrainCategories();
        }
        private void CategoryButton(string name, Action action, bool selected)
        {
            var button = Ui.Button(name, action, selected); Ui.WrapButtonText(button); button.Margin = new Thickness(0, 0, 8, 8); button.Padding = new Thickness(8, 5, 8, 5); button.FontSize = 14; Categories.Children.Add(button);
        }
        private void ConstrainCategories() { if (Categories.ActualWidth > 0) foreach (Button b in Categories.Children) b.MaxWidth = Math.Max(1, Categories.ActualWidth - 8); }
        private object VariantTooltip(CustomEffectVariantTooltipLine[] lines)
        {
            if (lines.Length == 0) return null; var panel = new StackPanel();
            foreach (var line in lines) panel.Children.Add(Ui.Label(L.U(line.TierLabel) + "  " + L.Numeric(line.ValueLabel), 14)); return panel;
        }
        private void RefreshChoices()
        {
            double offset = ChoiceScroll.VerticalOffset; Choices.Children.Clear();
            if (Model.IsPresetCategorySelected)
                foreach (var preset in Model.VisiblePresets)
                {
                    var p = preset; var button = ChoiceButton(Display(p, "Name"), () => Model.AddPreset(p), Model.Rules.Any(x => x.PresetItemId == p.Preset.ItemId));
                    var body = new StackPanel(); body.Children.Add(Ui.Label(Display(p, "Name"), 15));
                    foreach (var effect in p.SelectionEffects) { var label = Ui.Label(Display(effect, "Name"), 13, Ui.Muted); label.Margin = new Thickness(0, 6, 0, 0); body.Children.Add(label); }
                    button.ContentTemplate = null; button.Content = body; Choices.Children.Add(button);
                }
            else foreach (var effect in Model.VisibleEffects)
            {
                var item = effect; var button = ChoiceButton(Display(item, "Name"), () => Model.AddEffect(item), Model.Rules.Any(x => x.SelectorId == item.SelectorId));
                button.ToolTip = VariantTooltip(item.VariantTooltipLines); Choices.Children.Add(button);
            }
            if (Choices.Children.Count == 0) { var label = Ui.Label("请检查关键词，清空搜索框或取消当前标签。", 14, Ui.Muted); label.Margin = new Thickness(8, 16, 8, 8); Choices.Children.Add(label); }
            ChoiceScroll.ScrollToVerticalOffset(offset);
        }
        private static Button ChoiceButton(string name, Action action, bool selected)
        {
            var button = Ui.Button(name, action, selected); Ui.WrapButtonText(button); button.Margin = new Thickness(0, 0, 0, 4); button.Padding = new Thickness(10, 9, 10, 9); button.FontSize = 15; return button;
        }
        private void RefreshRules()
        {
            double offset = RuleScroll.VerticalOffset; RuleList.Children.Clear(); Ui.SetText(CountLabel, "已选择  " + Model.Rules.Count + "/10");
            foreach (var rule in Model.Rules)
            {
                var item = rule; int index = Model.Rules.IndexOf(item); var body = new StackPanel();
                var label = Ui.Label(Display(item, "DisplayName"), 15); label.ToolTip = VariantTooltip(item.VariantTooltipLines); body.Children.Add(label);
                var actions = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) }; var number = Ui.Label((index + 1).ToString("00"), 12, Ui.Faint); number.Width = 24; actions.Children.Add(number);
                actions.Children.Add(Ui.Button(item.IsRequired ? "必须" : "想要", () => Model.ToggleRuleRequirement(item), item.IsRequired));
                if (item.IsRequired && item.CanAdjustQuantity)
                {
                    var quantity = Ui.Combo(Enumerable.Range(1, 6).Select(n => (object)("×" + n)).ToArray(), item.QuantityTarget - 1, i => item.QuantityTarget = i + 1);
                    quantity.Style = (Style)FindResource("PreviewQuietSelector");
                    quantity.Name = "RuleQuantity"; quantity.Tag = item; quantity.Width = 68; quantity.MinHeight = 32; quantity.FontSize = 12; quantity.Margin = new Thickness(4, 0, 4, 0);
                    actions.Children.Add(quantity);
                }
                var up = Ui.Button("↑", () => Model.MoveRule(item, -1), false, "上移"); up.IsEnabled = index > 0; actions.Children.Add(up);
                var down = Ui.Button("↓", () => Model.MoveRule(item, 1), false, "下移"); down.IsEnabled = index + 1 < Model.Rules.Count; actions.Children.Add(down);
                actions.Children.Add(Ui.Button("×", () => Model.DeleteRule(item), false, "删除")); body.Children.Add(actions);
                var border = Ui.Panel(body, 8, "#161410"); border.Margin = new Thickness(0, 0, 0, 8); RuleList.Children.Add(border);
            }
            RuleScroll.ScrollToVerticalOffset(offset);
        }
        private object VesselTooltip(CustomEffectVesselTooltip tooltip)
        {
            if (tooltip == null || !tooltip.HasContent) return null; var panel = new StackPanel { MaxWidth = 420 };
            foreach (var rule in tooltip.Rules)
            {
                panel.Children.Add(Ui.Label(Display(rule, "DisplayName"), 14));
                if (!string.IsNullOrWhiteSpace(rule.TotalLine)) panel.Children.Add(Ui.Label(Display(rule, "TotalLine").Replace(" 条", L.U(" 条")), 14, Ui.Gold));
                else if (!string.IsNullOrWhiteSpace(rule.TierCountLine)) panel.Children.Add(Ui.Label(Display(rule, "TierCountLine"), 14, Ui.Gold));
            }
            return panel;
        }
        public void RefreshResults()
        {
            if (!ReferenceEquals(observedResult, Model.SelectedResult))
            {
                if (observedResult != null) observedResult.PropertyChanged -= ResultChanged;
                observedResult = Model.SelectedResult;
                if (observedResult != null) observedResult.PropertyChanged += ResultChanged;
            }
            var currentSlots = observedResult == null ? new CustomEffectRelicSlotViewModel[0] : observedResult.Slots;
            if (!ReferenceEquals(observedSlots, currentSlots))
            {
                foreach (var slot in observedSlots) slot.PropertyChanged -= ResultChanged;
                observedSlots = currentSlots;
                foreach (var slot in observedSlots) slot.PropertyChanged += ResultChanged;
            }
            bool rebuildTabs = retained.TabsChanged(Model.Results, LocalizationService.Current.Revision);
            if (rebuildTabs) CupTabs.Children.Clear();
            if (rebuildTabs) foreach (var result in Model.Results)
            {
                var r = result; bool selected = ReferenceEquals(r, Model.SelectedResult); var stack = new StackPanel(); stack.Children.Add(Ui.Label(Display(r, "VesselName"), 14, selected ? Ui.Gold : Ui.Muted));
                var dots = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
                foreach (var color in r.SlotColors) dots.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = ColorBrush(color.ColorId), Margin = new Thickness(0, 0, 4, 0) });
                stack.Children.Add(dots); var button = Ui.Button("", () => Model.SelectedResult = r, selected); button.Content = stack; button.ToolTip = VesselTooltip(r.VesselTooltip);
                button.MinHeight = 44; button.Margin = new Thickness(0, 0, 4, 0); button.Padding = new Thickness(8, 5, 8, 5); button.IsEnabled = !Model.IsReading && !Model.IsSearching && !Model.IsReplacing; CupTabs.Children.Add(button);
            }
            for (int i = 0; i < CupTabs.Children.Count; i++) {
                var b = (Button)CupTabs.Children[i]; bool selected = ReferenceEquals(Model.Results[i], Model.SelectedResult);
                b.Background = selected ? Ui.Brush("#282219") : Brushes.Transparent;
                ((TextBlock)((StackPanel)b.Content).Children[0]).Foreground = selected ? Ui.Gold : Ui.Muted;
                b.ToolTip = VesselTooltip(Model.Results[i].VesselTooltip);
                b.IsEnabled = !Model.IsReading && !Model.IsSearching && !Model.IsReplacing;
            }
            retained.Update(this);
            RestoreButton.IsEnabled = Model.CanRestoreOptimal;
            PositionButton.IsEnabled = Model.CanShowPosition;
            if (Position != null && !Model.IsReplacing) Position.RefreshContent();
        }
        private void ResultChanged(object sender, PropertyChangedEventArgs e) { QueueRefresh(RefreshPart.Results); }
        public static Brush ColorBrush(int color) { return Ui.Brush(Ui.ColorHex[Math.Max(0, Math.Min(4, color))]); }
        public Border CreateResultCard(CustomEffectRelicSlotViewModel slot)
        {
            var panel = Ui.Rows(Ui.Auto, Ui.Star, Ui.Auto); var heading = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            heading.Children.Add(new Border { Height = 19, Padding = new Thickness(3, 0, 3, 0), Child = Ui.Label(slot.SlotLabel, 12, ColorBrush(slot.RelicColorId)) });
            if (slot.Current.ShouldDisplayName) { var name = Ui.Label(Display(slot.Current, "Name"), 12, Ui.Muted); name.Margin = new Thickness(8, 0, 0, 0); heading.Children.Add(name); } Ui.Place(panel, heading, 0);
            var effects = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Name = "ResultEffects" };
            foreach (var effect in slot.Current.Effects) effects.Children.Add(EffectLine(effect, true));
            if (effects.Children.Count > 0) ((FrameworkElement)effects.Children[effects.Children.Count - 1]).Margin = new Thickness(0); Ui.Place(panel, effects, 1);
            var actions = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 10, 0, 0), Name = "ResultActions" };
            var previous = Ui.Button("‹ 上一个", () => Step(slot, -1)); previous.IsEnabled = slot.CanPrevious && Model.CanShowPosition; DockPanel.SetDock(previous, Dock.Left); actions.Children.Add(previous);
            var next = Ui.Button("下一个 ›", () => Step(slot, 1)); next.IsEnabled = slot.CanNext && Model.CanShowPosition; DockPanel.SetDock(next, Dock.Right); actions.Children.Add(next);
            foreach (var button in new[] { previous, next }) { button.FontSize = 12; button.Padding = new Thickness(6, 4, 6, 4); button.Margin = new Thickness(0); } Ui.Place(panel, actions, 2); return Ui.Panel(panel, 12, "#181613");
        }
        public UIElement EffectLine(CustomEffectEffectLine effect, bool values)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, values ? ResultEffectGap : 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Auto });
            var text = Ui.Label(Display(effect, "Name"), values ? ResultBodyFontSize : 15, !effect.IsApplicable ? Ui.Faint : effect.IsNegative ? Ui.Negative : Ui.Text);
            if (values) { text.LineHeight = ResultLineHeight; text.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; }
            if (effect.IsApplicable && effect.IsCore) text.FontWeight = FontWeights.SemiBold; Ui.Place(grid, text, 0);
            if (values && effect.IsApplicable && effect.IsValueConfirmed && !string.IsNullOrWhiteSpace(effect.Value))
            {
                var value = Ui.Label(L.Numeric(effect.Value), ResultValueFontSize, effect.IsNegative ? Ui.Negative : Ui.Gold); value.Margin = new Thickness(8, 0, 0, 0); value.MaxWidth = 112; value.VerticalAlignment = VerticalAlignment.Top; value.TextAlignment = TextAlignment.Right; Ui.Place(grid, value, 0, 1);
            }
            return grid;
        }
        private void UpdateBusy()
        {
            bool calculating = Model.IsSearching || Model.IsReplacing;
            var target = calculating && !Model.IsReading ? CalculationStatus : SaveStatus;
            BusyIndicator.VerticalAlignment = VerticalAlignment.Bottom; var parent = BusyIndicator.Parent as Panel;
            if (!ReferenceEquals(parent, target)) { if (parent != null) parent.Children.Remove(BusyIndicator); target.Children.Add(BusyIndicator); }
            BusyIndicator.BusyState = Model.IsReading ? BusyState.ReadingSave : calculating ? BusyState.Calculating : BusyState.Idle;
            CalculationStatus.Visibility = calculating && !Model.IsReading ? Visibility.Visible : Visibility.Collapsed;
            SaveStatus.MinHeight = Model.IsReading ? 48 : 22;
            ReadStatus.Visibility = Model.IsReading ? Visibility.Collapsed : Visibility.Visible;
        }
        private void ApplyScale()
        {
            Workspace.LayoutTransform = new ScaleTransform(Model.UiScale, Model.UiScale); BusyIndicator.LayoutTransform = new ScaleTransform(1 / Model.UiScale, 1 / Model.UiScale);
            ZoomOut.IsEnabled = Model.CanZoomOut; ZoomIn.IsEnabled = Model.CanZoomIn;
        }
        private async Task RunOperation(Func<Task> action, string boundary)
        {
            if (disposed) return;
            try { await action(); }
            catch (Exception) { if (!disposed) Model.ReportBoundaryFailure(boundary); }
        }
        private async void ImportSave()
        {
            var dialog = new OpenFileDialog { Title = L.U("选择 Nightreign 存档（只读检查）"), Filter = L.U("Nightreign 存档 (*.sl2;*.co2)|*.sl2;*.co2|所有文件 (*.*)|*.*"), CheckFileExists = true, Multiselect = false };
            try { if (dialog.ShowDialog(this) == true) await RunOperation(() => Model.ImportSaveAsync(dialog.FileName), "存档导入"); }
            catch (Exception) { if (!disposed) Model.ReportBoundaryFailure("存档导入"); }
        }
        private async void RefreshSaves() { await RunOperation(Model.RefreshSavesAsync, "存档刷新"); }
        private async void SelectSource(SaveSourceInfo source) { await RunOperation(() => Model.SelectSaveSourceAsync(source), "存档选择"); }
        private async void Step(CustomEffectRelicSlotViewModel slot, int delta) { await RunOperation(() => Model.StepSlotAsync(slot, delta), "单槽替换"); }
        public void OpenPosition()
        {
            if (!Model.CanShowPosition) return;
            if (Position != null) { if (Position.WindowState == WindowState.Minimized) Position.WindowState = WindowState.Normal; if (Position.Docking.IsDocked) Position.Docking.Animate(true); Position.Activate(); return; }
            Position = new PositionWindow(this); Position.Closed += (s, e) => Position = null; Position.Show();
        }
        public void OpenAbout()
        {
            if (About != null) { About.Activate(); return; }
            About = new AboutWindow { Owner = this }; About.Closed += (s, e) => About = null; About.Show();
        }
        public void Changed() { if (!disposed) { saveTimer.Stop(); saveTimer.Start(); } }
        private void RememberBounds(object sender, SizeChangedEventArgs e)
        { if (WindowState == WindowState.Normal) { Preferences.MainWidth = Width; Preferences.MainHeight = Height; Changed(); } }
        private void SaveTick(object sender, EventArgs e) { saveTimer.Stop(); SavePreferences(); }
        private void SavePreferences()
        {
            try { Preferences.Save(preferencePath); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { if (!disposed) Model.ReportBoundaryFailure("保存界面状态"); }
        }
        protected override void OnClosed(EventArgs e)
        {
            disposed = true; Model.PropertyChanged -= ModelChanged; LocalizationService.Current.Changed -= LanguageChanged;
            foreach (var collection in collections) collection.CollectionChanged -= CollectionChanged;
            if (observedResult != null) observedResult.PropertyChanged -= ResultChanged;
            foreach (var slot in observedSlots) slot.PropertyChanged -= ResultChanged;
            if (About != null) About.Close(); if (Position != null) Position.Close();
            saveTimer.Stop(); saveTimer.Tick -= SaveTick; BusyIndicator.Dispose(); SavePreferences(); Model.Dispose(); base.OnClosed(e);
        }
    }
}

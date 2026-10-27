using System;
using NightreignRelicTool.Localization;
using NightreignRelicTool.CustomEffectSearch;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NightreignRelicTool.FinalUI
{
    // WPF measures this panel against the actual vertical ScrollViewer viewport width.
    public sealed class ResponsiveCards : Panel
    {
        public int Columns { get; private set; }
        public const double Gap = 12, MinimumCardWidth = 250;
        public static int ColumnCount(double width) { return Math.Max(1, Math.Min(3, (int)Math.Floor((width + Gap) / (MinimumCardWidth + Gap)))); }
        protected override Size MeasureOverride(Size available)
        {
            double width = double.IsInfinity(available.Width) ? MinimumCardWidth : available.Width;
            Columns = ColumnCount(width); double cell = Math.Max(0, (width - Gap * (Columns - 1)) / Columns), height = 0;
            for (int start = 0; start < InternalChildren.Count; start += Columns)
            {
                double rowHeight = 0;
                for (int i = start; i < Math.Min(start + Columns, InternalChildren.Count); i++)
                { InternalChildren[i].Measure(new Size(cell, double.PositiveInfinity)); rowHeight = Math.Max(rowHeight, InternalChildren[i].DesiredSize.Height); }
                height += rowHeight + (start == 0 ? 0 : Gap);
            }
            return new Size(width, height);
        }
        protected override Size ArrangeOverride(Size final)
        {
            int cols = ColumnCount(final.Width); double cell = Math.Max(0, (final.Width - Gap * (cols - 1)) / cols), y = 0;
            for (int start = 0; start < InternalChildren.Count; start += cols)
            {
                double rowHeight = 0;
                for (int i = start; i < Math.Min(start + cols, InternalChildren.Count); i++)
                {
                    var child = InternalChildren[i]; child.Arrange(new Rect((i - start) * (cell + Gap), y, cell, child.DesiredSize.Height));
                    rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
                }
                y += rowHeight + Gap;
            }
            Columns = cols; return final;
        }
    }
    public sealed class PositionWindow : ShellWindow
    {
        private readonly MainWindow main;
        public EdgeDock Docking { get; private set; }
        private WindowPreferences State { get { return main.Preferences; } }
        public ScrollViewer Scroller;
        public ResponsiveCards CardsPanel;
        public Grid Toolbar;
        public Slider OpacitySlider;
        public Button ZoomOut, ZoomIn, Pin, ListButton, CardsButton, MinimizeButton, CloseButton;
        public UIElement ContentPanel;
        private readonly List<SolidColorBrush> surfaces = new List<SolidColorBrush>();
        private bool wideList;
        public PositionWindow(MainWindow main) : base("遗物位置", main.Preferences.PositionWidth, main.Preferences.PositionHeight, false)
        {
            this.main = main; MinWidth = Math.Min(520, SystemParameters.WorkArea.Width); MinHeight = Math.Min(260, SystemParameters.WorkArea.Height);
            Width = Math.Min(Math.Max(MinWidth, Width), SystemParameters.WorkArea.Width); Height = Math.Min(Math.Max(MinHeight, Height), SystemParameters.WorkArea.Height);
            Left = State.PositionLeft; Top = State.PositionTop;
            if (Left + Width < SystemParameters.VirtualScreenLeft + 40 || Left > SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 40) Left = SystemParameters.WorkArea.Left + 60;
            if (Top + 40 < SystemParameters.VirtualScreenTop || Top > SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 40) Top = SystemParameters.WorkArea.Top + 60;
            // Reuse the shell's first row as the only utility bar; no title or footer row.
            Shell.RowDefinitions[0].Height = new GridLength(40);
            HeaderDrag.Dispose(); WindowDrag.Attach(this, Shell);
            Header.Children.Clear(); Header.ColumnDefinitions.Clear();
            Toolbar = new Grid { Margin = new Thickness(8, 4, 8, 4) };
            Toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Auto });
            Toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            Toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Auto });
            Toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star, MinWidth = 12 });
            Toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Auto });
            Header.Children.Add(Toolbar);
            var modes = new StackPanel { Orientation = Orientation.Horizontal };
            ListButton = Compact("列表", () => SetMode(false)); CardsButton = Compact("卡片", () => SetMode(true));
            foreach (var button in new[] { ListButton, CardsButton }) { button.Width = 48; button.Padding = new Thickness(6, 0, 6, 0); modes.Children.Add(button); }
            ListButton.Margin = new Thickness(0, 0, 4, 0); Ui.Place(Toolbar, modes, 0, 0);
            var overlay = new StackPanel { Orientation = Orientation.Horizontal };
            ZoomOut = Compact("−", () => AdjustScale(-1)); ZoomIn = Compact("+", () => AdjustScale(1));
            foreach (var button in new[] { ZoomOut, ZoomIn }) { button.Width = 36; button.FontSize = 16; overlay.Children.Add(button); }
            ZoomOut.Margin = new Thickness(0, 0, 4, 0);
            System.Windows.Automation.AutomationProperties.SetName(ZoomOut, "缩小界面"); System.Windows.Automation.AutomationProperties.SetName(ZoomIn, "放大界面");
            var alphaLabel = Ui.Label("透明度", 12, Ui.Muted); alphaLabel.MinWidth = 36; alphaLabel.Margin = new Thickness(6, 0, 4, 0); overlay.Children.Add(alphaLabel);
            // 14 DIP thumb leaves a 90–120 DIP useful travel range.
            OpacitySlider = new Slider { Minimum = .4, Maximum = 1, Value = State.Opacity, Width = 104, SmallChange = .05, LargeChange = .1, IsMoveToPointEnabled = true };
            System.Windows.Automation.AutomationProperties.SetName(OpacitySlider, "背景透明度");
            OpacitySlider.ValueChanged += (s, e) => { State.Opacity = e.NewValue; ApplyBackgroundAlpha(); main.Changed(); }; overlay.Children.Add(OpacitySlider);
            Toolbar.SizeChanged += (s, e) => OpacitySlider.Width = Math.Max(104, Math.Min(134, Toolbar.ActualWidth - 398));
            Pin = Compact("置顶", TogglePin); Pin.Width = 52; Pin.Margin = new Thickness(6, 0, 0, 0); overlay.Children.Add(Pin); Ui.Place(Toolbar, overlay, 0, 2);
            var captions = new StackPanel { Orientation = Orientation.Horizontal };
            MinimizeButton = WindowButton("−", () => WindowState = WindowState.Minimized, "最小化"); CloseButton = WindowButton("×", Close, "关闭");
            foreach (var button in new[] { MinimizeButton, CloseButton }) { button.Width = 32; button.MinWidth = 32; button.Height = 32; button.Margin = new Thickness(0); button.Padding = new Thickness(0); captions.Children.Add(button); }
            MinimizeButton.Margin = new Thickness(0, 0, 4, 0); Ui.Place(Toolbar, captions, 0, 4);
            Scroller = Ui.Scroll(null); Scroller.Margin = new Thickness(16, 12, 16, 16); Body.Content = Scroller;
            Scroller.SizeChanged += (s, e) => { bool wide = ContentWidth > 640; if (!State.Cards && wide != wideList) RefreshContent(); };
            LocationChanged += (s, e) => RememberBounds(); SizeChanged += (s, e) => RememberBounds();
            Closed += (s, e) => { RememberBounds(); main.Changed(); };
            RefreshContent();
            Docking = new EdgeDock(this);
        }
        private static Button Compact(string text, Action action)
        {
            var button = Ui.Button(text, action); button.Width = 32; button.MinWidth = 32; button.MinHeight = 32; button.Height = 32;
            button.FontSize = 12; button.Padding = new Thickness(0); button.Margin = new Thickness(0); return button;
        }
        private SolidColorBrush Surface(string hex)
        {
            var brush = (SolidColorBrush)Ui.Brush(hex); surfaces.Add(brush); return brush;
        }
        private void ApplyBackgroundAlpha()
        {
            foreach (var brush in surfaces) { var color = brush.Color; color.A = (byte)Math.Round(State.Opacity * 255); brush.Color = color; }
        }
        private double ContentWidth { get { return Math.Max(0, Scroller.ActualWidth - SystemParameters.VerticalScrollBarWidth) / WindowPreferences.PositionScales[State.PositionScale]; } }
        private void RememberBounds()
        {
            if (WindowState != WindowState.Normal || (Docking != null && Docking.IsDocked)) return;
            State.PositionLeft = Left; State.PositionTop = Top; State.PositionWidth = Width; State.PositionHeight = Height; main.Changed();
        }
        public void SetMode(bool cards) { State.Cards = cards; RefreshContent(); main.Changed(); }
        public void AdjustScale(int delta) { State.PositionScale = Math.Max(0, Math.Min(2, State.PositionScale + delta)); RefreshContent(); main.Changed(); }
        public void TogglePin()
        {
            Topmost = !Topmost; Ui.SetText(Pin, Topmost ? "已置顶" : "置顶");
            Pin.Foreground = Topmost ? Ui.Gold : Ui.Muted; RefreshContent();
            // Pinning is session-only, as in the current product. It never touches opacity.
        }
        public void RefreshContent()
        {
            surfaces.Clear(); Shell.Background = Surface("#0A0908"); Header.Background = Surface("#050505");
            Pin.Background = Topmost ? Surface("#282219") : Brushes.Transparent;
            wideList = ContentWidth > 640;
            ZoomOut.IsEnabled = State.PositionScale > 0; ZoomIn.IsEnabled = State.PositionScale < 2;
            ListButton.Foreground = State.Cards ? Ui.Muted : Ui.Gold; CardsButton.Foreground = State.Cards ? Ui.Gold : Ui.Muted;
            ListButton.Background = State.Cards ? Brushes.Transparent : Surface("#282219"); CardsButton.Background = State.Cards ? Surface("#282219") : Brushes.Transparent;
            var stones = main.Model.CanShowPosition ? main.Model.SelectedResult.Slots.ToArray() : new CustomEffectRelicSlotViewModel[0];
            if (State.Cards)
            {
                CardsPanel = new ResponsiveCards();
                foreach (var stone in stones) { var card = Ui.Panel(StoneContent(stone, false), 12); card.Background = Surface("#181613"); CardsPanel.Children.Add(card); }
                ContentPanel = CardsPanel;
            }
            else
            {
                CardsPanel = null; var list = new StackPanel();
                foreach (var stone in stones) list.Children.Add(new Border { BorderBrush = Ui.Brush("#2A261F"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 10, 0, 10), Child = StoneContent(stone, wideList) });
                ContentPanel = list;
            }
            double scale = WindowPreferences.PositionScales[State.PositionScale]; ((FrameworkElement)ContentPanel).LayoutTransform = new ScaleTransform(scale, scale);
            double offset = Scroller.VerticalOffset; Scroller.Content = ContentPanel; Scroller.ScrollToVerticalOffset(offset);
            ApplyBackgroundAlpha();
        }
        private UIElement StoneContent(CustomEffectRelicSlotViewModel stone, bool wide)
        {
            var type = Ui.Label(L.U(stone.SlotLabel) + " · " + L.U(stone.RelicColorName), 13, MainWindow.ColorBrush(stone.RelicColorId));
            var position = Ui.Label(stone.SlotColorId == 4 ? string.Empty : L.U(stone.Current.PositionText), 13, Ui.Gold); position.FontWeight = FontWeights.SemiBold; position.HorizontalAlignment = HorizontalAlignment.Right; position.MaxWidth = 180; position.TextWrapping = TextWrapping.Wrap;
            position.Visibility = stone.SlotColorId == 4 ? Visibility.Collapsed : Visibility.Visible;
            var effects = new StackPanel();
            foreach (var effect in stone.Current.Effects) effects.Children.Add(main.EffectLine(effect, false));
            if (wide)
            {
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(158) });
                type.VerticalAlignment = VerticalAlignment.Top; position.VerticalAlignment = VerticalAlignment.Top; effects.Margin = new Thickness(0, 0, 16, 0);
                Ui.Place(grid, type, 0); Ui.Place(grid, effects, 0, 1); Ui.Place(grid, position, 0, 2); return grid;
            }
            var body = Ui.Rows(Ui.Auto, Ui.Auto);
            var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star }); header.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Auto });
            Ui.Place(header, type, 0); Ui.Place(header, position, 0, 1); Ui.Place(body, header, 0); Ui.Place(body, effects, 1); return body;
        }
    }
}

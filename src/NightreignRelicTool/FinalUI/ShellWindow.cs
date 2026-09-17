using System;
using System.Linq;
using System.Globalization;
using System.Windows.Data;
using NightreignRelicTool.Localization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Markup;

namespace NightreignRelicTool.FinalUI
{
    public static class Ui
    {
        public static Brush Brush(string hex) { return (Brush)new BrushConverter().ConvertFromString(hex); }
        public static readonly Brush Text = Brush("#F2E9D7"), Muted = Brush("#B8AD9B"), Gold = Brush("#D8AE56"), Faint = Brush("#787268"), Negative = Brush("#B77A72");
        public static readonly string[] ColorHex = { "#C4776C", "#8AA9C6", "#C5A35B", "#86A48A", "#C7C3B7" };
        public static TextBlock Label(string text, double size = 15, Brush color = null)
        {
            var label = new TextBlock { FontSize = size, Foreground = color ?? Text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            SetText(label, text); return label;
        }
        public static Button Button(string text, Action action, bool selected = false, string tooltip = null)
        {
            var button = new Button { Content = text, Style = (Style)Application.Current.FindResource("FinalButton"),
                Foreground = selected ? Gold : Muted, Background = selected ? Brush("#282219") : Brushes.Transparent };
            SetText(button, text);
            if (!string.IsNullOrWhiteSpace(tooltip)) button.ToolTip = Label(tooltip, 14);
            WindowDrag.SetIsExcluded(button, true);
            button.Click += (s, e) => action();
            return button;
        }
        public static void SetText(FrameworkElement element, string text)
        {
            var binding = new MultiBinding { Converter = new UiTextConverter() };
            binding.Bindings.Add(new Binding { Source = text ?? "" });
            binding.Bindings.Add(new Binding("Revision") { Source = LocalizationService.Current });
            element.SetBinding(element is TextBlock ? TextBlock.TextProperty : ContentControl.ContentProperty, binding);
        }
        public static void WrapButtonText(Button button)
        {
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding());
            text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            button.ContentTemplate = new DataTemplate { VisualTree = text };
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        }
        public static ScrollViewer Scroll(UIElement content)
        {
            return new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, CanContentScroll = false, PanningMode = PanningMode.VerticalOnly };
        }
        public static void Place(Grid grid, UIElement child, int row, int column = 0) { Grid.SetRow(child, row); Grid.SetColumn(child, column); grid.Children.Add(child); }
        public static Grid Rows(params GridLength[] rows)
        {
            var grid = new Grid(); foreach (var row in rows) grid.RowDefinitions.Add(new RowDefinition { Height = row }); return grid;
        }
        public static GridLength Auto = GridLength.Auto, Star = new GridLength(1, GridUnitType.Star);
        public static Border Panel(UIElement content, double padding = 16, string background = "#11100F")
        {
            return new Border { Background = Brush(background), CornerRadius = new CornerRadius(8), Padding = new Thickness(padding), Child = content };
        }
        public static ComboBox Combo(object[] items, int selected, Action<int> changed)
        {
            var combo = new ComboBox { ItemsSource = items, SelectedIndex = selected, FontSize = 15,
                Style = (Style)Application.Current.FindResource("CustomEffectComboBox"), MinHeight = 36 };
            var factory = new FrameworkElementFactory(typeof(TextBlock));
            var labelBinding = new MultiBinding { Converter = new UiTextConverter() };
            labelBinding.Bindings.Add(new Binding());
            labelBinding.Bindings.Add(new Binding("Revision") { Source = LocalizationService.Current });
            factory.SetBinding(TextBlock.TextProperty, labelBinding);
            factory.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            combo.ItemTemplate = new DataTemplate { VisualTree = factory };
            WindowDrag.SetIsExcluded(combo, true);
            combo.SelectionChanged += (s, e) => { if (combo.SelectedIndex >= 0) changed(combo.SelectedIndex); };
            return combo;
        }
    }
    public sealed class UiTextConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type type, object parameter, CultureInfo culture)
        {
            return L.U(System.Convert.ToString(values[0], CultureInfo.InvariantCulture));
        }
        public object[] ConvertBack(object value, Type[] types, object parameter, CultureInfo culture) { throw new NotSupportedException(); }
    }
    public class ShellWindow : Window
    {
        public Grid Shell { get; private set; }
        public Grid Header { get; private set; }
        public ContentControl Body { get; private set; }
        private HwndSource source;
        protected WindowDragBehavior HeaderDrag;
        public ShellWindow(string title, double width, double height, bool maximize)
        {
            // App.xaml provides these in production; direct window hosts use the identical dictionaries.
            if (Application.Current != null && Application.Current.TryFindResource("FinalButton") == null)
            {
                if (Application.Current.TryFindResource("CustomEffectComboBox") == null)
                    Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/NightreignRelicTool;component/Themes/CustomEffectSearchTheme.xaml", UriKind.Relative) });
                Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/NightreignRelicTool;component/FinalUI/FinalTheme.xaml", UriKind.Relative) });
            }
            Title = title; Width = width; Height = height; FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 15;
            var titleBinding = new MultiBinding { Converter = new UiTextConverter() };
            titleBinding.Bindings.Add(new Binding { Source = title });
            titleBinding.Bindings.Add(new Binding("Revision") { Source = LocalizationService.Current });
            SetBinding(TitleProperty,titleBinding);
            Foreground = Ui.Text; Background = Brushes.Transparent; WindowStyle = WindowStyle.None; AllowsTransparency = true;
            ResizeMode = ResizeMode.CanResize; UseLayoutRounding = true; SnapsToDevicePixels = true;
            using (var icon = Assembly.GetExecutingAssembly().GetManifestResourceStream("NightreignRelicTool.FinalUI.Icon.ico"))
            { var frame = BitmapFrame.Create(icon, BitmapCreateOptions.None, BitmapCacheOption.OnLoad); Icon = frame; }
            WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, CornerRadius = new CornerRadius(10), ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0) });
            Shell = Ui.Rows(new GridLength(52), Ui.Star); Shell.Background = Ui.Brush("#0A0908");
            Shell.SizeChanged += (s, e) => Shell.Clip = new RectangleGeometry(new Rect(0, 0, Shell.ActualWidth, Shell.ActualHeight), 10, 10);
            Content = Shell;
            Header = new Grid { Background = Ui.Brush("#050505") };
            Header.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star });
            Header.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Auto });
            HeaderDrag = WindowDrag.Attach(this, Header);
            Header.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2 && maximize && !WindowDrag.IsExcludedSource(e.OriginalSource as DependencyObject)) ToggleMaximize(); };
            var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 4, 12, 4) };
            controls.Children.Add(WindowButton("−", () => WindowState = WindowState.Minimized, "最小化"));
            if (maximize) controls.Children.Add(WindowButton("□", ToggleMaximize, "最大化／还原"));
            controls.Children.Add(WindowButton("×", Close, "关闭"));
            Ui.Place(Header, controls, 0, 1); Ui.Place(Shell, Header, 0);
            Body = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
            Ui.Place(Shell, Body, 1);
        }
        protected Button WindowButton(string text, Action action, string tooltip)
        {
            var button = Ui.Button(text, action); button.Style = (Style)Application.Current.FindResource("FinalCaptionButton");
            System.Windows.Automation.AutomationProperties.SetName(button, tooltip);
            button.Width = 40; button.FontSize = 20; button.Margin = new Thickness(4, 0, 0, 0); return button;
        }
        public void ToggleMaximize() { WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; }
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e); source = PresentationSource.FromVisual(this) as HwndSource; if (source != null) source.AddHook(Hook);
        }
        protected override void OnClosed(EventArgs e) { if (source != null) source.RemoveHook(Hook); base.OnClosed(e); }
        private static IntPtr Hook(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled)
        {
            if (msg != 0x24) return IntPtr.Zero;
            var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info)) return IntPtr.Zero;
            var bounds = (MinMax)Marshal.PtrToStructure(lp, typeof(MinMax));
            bounds.MaxPosition = new PointI { X = info.Work.Left - info.Monitor.Left, Y = info.Work.Top - info.Monitor.Top };
            bounds.MaxSize = new PointI { X = info.Work.Right - info.Work.Left, Y = info.Work.Bottom - info.Work.Top };
            Marshal.StructureToPtr(bounds, lp, false); handled = true; return IntPtr.Zero;
        }
        [StructLayout(LayoutKind.Sequential)] private struct PointI { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct RectI { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct MinMax { public PointI Reserved, MaxSize, MaxPosition, MinTrack, MaxTrack; }
        [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public RectI Monitor, Work; public int Flags; }
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    }
}

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace NightreignRelicTool.FinalUI
{
    public enum BusyState { Idle, Calculating, ReadingSave, Importing, Refreshing }

    public sealed class RevenantBusyIndicator : Grid, IDisposable
    {
        public const int FrameWidth = 48, FrameHeight = 32, FrameCount = 8;
        public static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(125);
        private static readonly Lazy<BitmapSource[]> frames = new Lazy<BitmapSource[]>(LoadFrames);
        public static BitmapSource[] Frames { get { return frames.Value; } }
        public static int DecodeCount { get; private set; }
        public static readonly DependencyProperty IsBusyProperty = DependencyProperty.Register("IsBusy", typeof(bool), typeof(RevenantBusyIndicator),
            new PropertyMetadata(false, BusyChanged));
        public static readonly DependencyProperty BusyStateProperty = DependencyProperty.Register("BusyState", typeof(BusyState), typeof(RevenantBusyIndicator),
            new PropertyMetadata(BusyState.Idle, StateChanged));
        public bool IsBusy { get { return (bool)GetValue(IsBusyProperty); } set { SetValue(IsBusyProperty, value); } }
        public BusyState BusyState { get { return (BusyState)GetValue(BusyStateProperty); } set { SetValue(BusyStateProperty, value); } }
        public readonly Image Sprite;
        public readonly TranslateTransform Crawl = new TranslateTransform();
        private readonly DispatcherTimer timer;
        private readonly System.Diagnostics.Stopwatch travel = new System.Diagnostics.Stopwatch();
        private bool renderSubscribed;
        private Window host;
        private bool disposed;
        private double dpiX = 1, spriteWidth = FrameWidth;
        public int FrameIndex { get; private set; }
        public long TickCount { get; private set; }
        public bool IsTimerRunning { get { return timer.IsEnabled; } }
        public double SpriteWidth { get { return spriteWidth; } }
        public RevenantBusyIndicator()
        {
            Width = 240; Height = 48; ClipToBounds = true; Background = Brushes.Transparent;
            HorizontalAlignment = HorizontalAlignment.Left; IsHitTestVisible = false;
            SnapsToDevicePixels = true; UseLayoutRounding = true; Visibility = Visibility.Collapsed;
            Sprite = new Image { Width = FrameWidth, Height = FrameHeight, Stretch = Stretch.Fill,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
                SnapsToDevicePixels = true, UseLayoutRounding = true, RenderTransform = Crawl };
            RenderOptions.SetBitmapScalingMode(Sprite, BitmapScalingMode.NearestNeighbor); Children.Add(Sprite);
            timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = FrameInterval }; timer.Tick += Tick;
            Loaded += OnLoaded; Unloaded += OnUnloaded; Reset();
        }
        private static BitmapSource[] LoadFrames()
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/NightreignRelicTool;component/Assets/revenant_crawl_spritesheet.png", UriKind.Absolute));
            if (info == null) throw new InvalidDataException("Revenant WPF resource is missing.");
            var bitmap = new BitmapImage();
            using (var input = info.Stream)
            {
                bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = input; bitmap.EndInit(); bitmap.Freeze();
            }
            if (bitmap.PixelWidth != 384 || bitmap.PixelHeight != 32) throw new InvalidDataException("Unexpected Revenant sprite dimensions.");
            DecodeCount++;
            return Enumerable.Range(0, FrameCount).Select(i => {
                var frame = new CroppedBitmap(bitmap, new Int32Rect(i * FrameWidth, 0, FrameWidth, FrameHeight)); frame.Freeze(); return (BitmapSource)frame;
            }).ToArray();
        }
        private static void BusyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var indicator = (RevenantBusyIndicator)d;
            if (!(bool)e.NewValue) indicator.SetCurrentValue(BusyStateProperty, BusyState.Idle);
            else if (indicator.BusyState == BusyState.Idle) indicator.SetCurrentValue(BusyStateProperty, BusyState.Calculating);
            indicator.UpdatePlayback();
        }
        private static void StateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var indicator = (RevenantBusyIndicator)d; indicator.SetCurrentValue(IsBusyProperty, (BusyState)e.NewValue != BusyState.Idle); indicator.UpdatePlayback();
        }
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            DetachHost(); host = Window.GetWindow(this);
            if (host != null) { host.StateChanged += HostStateChanged; host.Closed += HostClosed; }
            UpdatePlayback();
        }
        private void OnUnloaded(object sender, RoutedEventArgs e) { Stop(); DetachHost(); }
        private void HostStateChanged(object sender, EventArgs e) { UpdatePlayback(); }
        private void HostClosed(object sender, EventArgs e) { Stop(); DetachHost(); }
        private void DetachHost()
        {
            if (host == null) return; host.StateChanged -= HostStateChanged; host.Closed -= HostClosed; host = null;
        }
        private void UpdatePlayback()
        {
            if (disposed || !IsBusy) { Stop(); return; }
            Visibility = Visibility.Visible;
            if (!IsLoaded || host == null || host.WindowState == WindowState.Minimized) { timer.Stop(); PauseTravel(); return; }
            if (timer.IsEnabled) return;
            var dpi = VisualTreeHelper.GetDpi(this); dpiX = dpi.DpiScaleX;
            int pixelScale = Math.Max(1, (int)Math.Round(dpiX));
            Sprite.Width = spriteWidth = FrameWidth * pixelScale / dpiX;
            Sprite.Height = FrameHeight * pixelScale / dpi.DpiScaleY;
            Sprite.Source = Frames[FrameIndex]; timer.Start(); travel.Start(); if (!renderSubscribed) { CompositionTarget.Rendering += MoveSprite; renderSubscribed = true; }
        }
        private void Tick(object sender, EventArgs e) { Advance(); }
        internal void Advance()
        {
            if (!IsBusy || disposed) return;
            FrameIndex = (FrameIndex + 1) % FrameCount; Sprite.Source = Frames[FrameIndex];
            // #4: keep the original 125 ms pose cycle independent from translation.
            TickCount++;
        }
        public static double NextX(double x, double trackWidth, double width, double step)
        {
            return x >= trackWidth ? -width : x + step;
        }
        private void Reset() { FrameIndex = 0; Crawl.X = -spriteWidth; if (frames.IsValueCreated) Sprite.Source = Frames[0]; }
        private void MoveSprite(object sender, EventArgs e) {
            double runway = Math.Max(0, ActualWidth - spriteWidth - 20);
            Crawl.X = runway * (1 - Math.Exp(-travel.Elapsed.TotalSeconds / 6.5));
        }
        private void PauseTravel() { travel.Stop(); if (renderSubscribed) { CompositionTarget.Rendering -= MoveSprite; renderSubscribed = false; } }
        private void Stop() { timer.Stop(); PauseTravel(); travel.Reset(); Visibility = Visibility.Collapsed; Reset(); }
        public void Dispose()
        {
            if (disposed) return; disposed = true; Stop(); DetachHost(); timer.Tick -= Tick; Loaded -= OnLoaded; Unloaded -= OnUnloaded;
        }
    }

}

using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace NightreignRelicTool.FinalUI
{
    // A control opts out once; its entire template / visual subtree is excluded.
    public static class WindowDrag
    {
        public static readonly DependencyProperty IsExcludedProperty = DependencyProperty.RegisterAttached(
            "IsExcluded", typeof(bool), typeof(WindowDrag), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));
        public static bool GetIsExcluded(DependencyObject element) { return (bool)element.GetValue(IsExcludedProperty); }
        public static void SetIsExcluded(DependencyObject element, bool value) { element.SetValue(IsExcludedProperty, value); }
        public static bool IsExcludedSource(DependencyObject source)
        {
            for (var current = source; current != null; current = Parent(current)) if (GetIsExcluded(current)) return true;
            return false;
        }
        private static DependencyObject Parent(DependencyObject item)
        {
            if (item is Visual || item is Visual3D) return VisualTreeHelper.GetParent(item);
            var content = item as FrameworkContentElement;
            return content != null ? content.Parent : LogicalTreeHelper.GetParent(item);
        }
        public static WindowDragBehavior Attach(Window window, UIElement surface) { return new WindowDragBehavior(window, surface); }
    }
    public sealed class WindowDragBehavior : IDisposable
    {
        private readonly Window window;
        private readonly UIElement surface;
        private Point start;
        private bool candidate, disposed;
        public WindowDragBehavior(Window window, UIElement surface)
        {
            this.window = window; this.surface = surface;
            surface.PreviewMouseLeftButtonDown += Down; surface.PreviewMouseMove += Move; surface.PreviewMouseLeftButtonUp += Up;
            surface.MouseLeave += Leave; window.Closed += Closed;
        }
        public static bool ExceedsThreshold(Vector distance)
        {
            return Math.Abs(distance.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(distance.Y) > SystemParameters.MinimumVerticalDragDistance;
        }
        private void Down(object sender, MouseButtonEventArgs e)
        {
            candidate = !WindowDrag.IsExcludedSource(e.OriginalSource as DependencyObject);
            if (candidate) start = e.GetPosition(window);
            // Do not capture or handle mouse-down: buttons, selection and scrolling keep their input.
        }
        private void Move(object sender, MouseEventArgs e)
        {
            if (!candidate) return;
            if (e.LeftButton != MouseButtonState.Pressed) { candidate = false; return; }
            var distance = e.GetPosition(window) - start;
            if (!ExceedsThreshold(distance)) return;
            candidate = false;
            // Preserve the movement that crossed the threshold before entering the native loop.
            // Otherwise a single coalesced mouse move is lost and the window lags the pointer.
            if (window.WindowState == WindowState.Normal) { window.Left += distance.X; window.Top += distance.Y; }
            try { window.DragMove(); e.Handled = true; }
            catch (InvalidOperationException) { /* Mouse released while entering the native move loop. */ }
        }
        private void Up(object sender, MouseButtonEventArgs e) { candidate = false; }
        private void Leave(object sender, MouseEventArgs e) { if (e.LeftButton != MouseButtonState.Pressed) candidate = false; }
        private void Closed(object sender, EventArgs e) { Dispose(); }
        public void Dispose()
        {
            if (disposed) return; disposed = true; candidate = false;
            surface.PreviewMouseLeftButtonDown -= Down; surface.PreviewMouseMove -= Move; surface.PreviewMouseLeftButtonUp -= Up;
            surface.MouseLeave -= Leave; window.Closed -= Closed;
        }
    }
}

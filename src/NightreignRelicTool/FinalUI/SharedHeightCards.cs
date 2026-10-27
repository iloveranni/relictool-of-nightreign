using System;
using System.Windows;
using System.Windows.Controls;

namespace NightreignRelicTool.FinalUI
{
    // Measure at the real two-column width, with no height constraint. Arrange the
    // current group at its tallest natural height; never write that height to a child.
    // This lets a later, shorter group shrink without retaining an old measurement.
    public sealed class SharedHeightCards : Panel
    {
        public const double MinimumCardHeight = 190, Gap = 12;
        public double SharedHeight { get; private set; }

        protected override Size MeasureOverride(Size available)
        {
            double width = double.IsInfinity(available.Width) ? ActualWidth : available.Width;
            double cardWidth = Math.Max(0, (width - Gap) / 2);
            double tallest = MinimumCardHeight;
            foreach (UIElement card in InternalChildren)
            {
                card.Measure(new Size(cardWidth, double.PositiveInfinity));
                tallest = Math.Max(tallest, card.DesiredSize.Height);
            }
            SharedHeight = Math.Ceiling(tallest);
            int rows = (InternalChildren.Count + 1) / 2;
            return new Size(width, rows == 0 ? 0 : rows * SharedHeight + (rows - 1) * Gap);
        }

        protected override Size ArrangeOverride(Size final)
        {
            double cardWidth = Math.Max(0, (final.Width - Gap) / 2);
            int rows = (InternalChildren.Count + 1) / 2;
            double height = rows == 0 ? SharedHeight : Math.Max(SharedHeight, (final.Height - (rows - 1) * Gap) / rows);
            for (int i = 0; i < InternalChildren.Count; i++)
                InternalChildren[i].Arrange(new Rect((i % 2) * (cardWidth + Gap), (i / 2) * (height + Gap), cardWidth, height));
            return final;
        }
    }
}

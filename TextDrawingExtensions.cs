using SkiaSharp;

namespace EasyFortniteStats_ImageApi;

public enum VerticalTextAlignment
{
    Top,
    Center,
    Baseline,
    Bottom
}

public static class TextDrawingExtensions
{
    extension(SKCanvas canvas)
    {
        public void DrawAlignedText(string text,
            SKPoint anchor,
            SKFont font,
            SKPaint paint,
            SKTextAlign horizontalAlignment = SKTextAlign.Left,
            VerticalTextAlignment verticalAlignment = VerticalTextAlignment.Top)
        {
            font.MeasureText(text, out var bounds, paint);
            var baseline = verticalAlignment switch
            {
                VerticalTextAlignment.Top => anchor.Y - bounds.Top,
                VerticalTextAlignment.Center => anchor.Y - bounds.MidY,
                VerticalTextAlignment.Baseline => anchor.Y,
                VerticalTextAlignment.Bottom => anchor.Y - bounds.Bottom,
                _ => throw new ArgumentOutOfRangeException(nameof(verticalAlignment), verticalAlignment, null)
            };

            canvas.DrawText(text, anchor.X, baseline, horizontalAlignment, font, paint);
        }

        public void DrawAlignedText(string text,
            SKRect area,
            SKFont font,
            SKPaint paint,
            SKTextAlign horizontalAlignment = SKTextAlign.Left,
            VerticalTextAlignment verticalAlignment = VerticalTextAlignment.Top)
        {
            var x = horizontalAlignment switch
            {
                SKTextAlign.Left => area.Left,
                SKTextAlign.Center => area.MidX,
                SKTextAlign.Right => area.Right,
                _ => throw new ArgumentOutOfRangeException(nameof(horizontalAlignment), horizontalAlignment, null)
            };

            var y = verticalAlignment switch
            {
                VerticalTextAlignment.Top => area.Top,
                VerticalTextAlignment.Center => area.MidY,
                VerticalTextAlignment.Baseline => area.Top,
                VerticalTextAlignment.Bottom => area.Bottom,
                _ => throw new ArgumentOutOfRangeException(nameof(verticalAlignment), verticalAlignment, null)
            };

            canvas.DrawAlignedText(text, new SKPoint(x, y), font, paint, horizontalAlignment, verticalAlignment);
        }
    }
}

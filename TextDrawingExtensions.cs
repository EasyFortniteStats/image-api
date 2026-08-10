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
            SKPaint paint,
            SKTextAlign horizontalAlignment = SKTextAlign.Left,
            VerticalTextAlignment verticalAlignment = VerticalTextAlignment.Top)
    {
        var bounds = new SKRect();
        paint.MeasureText(text, ref bounds);
        var baseline = verticalAlignment switch
        {
            VerticalTextAlignment.Top => anchor.Y - bounds.Top,
            VerticalTextAlignment.Center => anchor.Y - bounds.MidY,
            VerticalTextAlignment.Baseline => anchor.Y,
            VerticalTextAlignment.Bottom => anchor.Y - bounds.Bottom,
            _ => throw new ArgumentOutOfRangeException(nameof(verticalAlignment), verticalAlignment, null)
        };

            var previousAlignment = paint.TextAlign;
            try
            {
                paint.TextAlign = horizontalAlignment;
                canvas.DrawText(text, anchor.X, baseline, paint);
            }
            finally
            {
                paint.TextAlign = previousAlignment;
            }
    }

        public void DrawAlignedText(string text,
            SKRect area,
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

            canvas.DrawAlignedText(text, new SKPoint(x, y), paint, horizontalAlignment, verticalAlignment);
        }
    }
}

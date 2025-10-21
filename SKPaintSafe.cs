// ReSharper disable InconsistentNaming
using SkiaSharp;

namespace EasyFortniteStats_ImageApi;

public sealed class SKPaintSafe : SKPaint
{
    private SKShader? _shader;

    public new SKShader? Shader
    {
        get => base.Shader;
        set
        {
            // Dispose old shader to prevent leaks if changed
            _shader?.Dispose();
            _shader = value; // Hold reference to prevent early GC

            base.Shader = value;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _shader?.Dispose();
            _shader = null;
        }

        base.Dispose(disposing);
    }
}
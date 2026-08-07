using System.Collections.Concurrent;
using SkiaSharp;

namespace EasyFortniteStats_ImageApi;

/// <summary>
/// Owns native Skia resources that are shared for the lifetime of the application.
/// </summary>
public sealed class SharedAssets : IDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<SKBitmap?>> _bitmaps = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<SKTypeface>> _fonts = new(StringComparer.Ordinal);
    private bool _disposed;

    public ValueTask<SKBitmap?> GetBitmap(string format, string? arg1)
    {
        return arg1 is null ? ValueTask.FromResult<SKBitmap?>(null) : GetBitmap(string.Format(format, arg1));
    }

    public ValueTask<SKBitmap?> GetBitmap(string? path)
    {
        if (path is null)
            return ValueTask.FromResult<SKBitmap?>(null);

        ObjectDisposedException.ThrowIf(_disposed, this);
        var lazyBitmap = _bitmaps.GetOrAdd(path, static assetPath =>
            new Lazy<SKBitmap?>(() => File.Exists(assetPath) ? SKBitmap.Decode(assetPath) : null,
                LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            return ValueTask.FromResult(lazyBitmap.Value);
        }
        catch
        {
            ((ICollection<KeyValuePair<string, Lazy<SKBitmap?>>>)_bitmaps)
                .Remove(new KeyValuePair<string, Lazy<SKBitmap?>>(path, lazyBitmap));
            throw;
        }
    }

    public ValueTask<SKTypeface> GetFont(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var lazyTypeface = _fonts.GetOrAdd(path, static assetPath =>
            new Lazy<SKTypeface>(() => SKTypeface.FromFile(assetPath)
                ?? throw new InvalidOperationException($"Could not load font '{assetPath}'."),
                LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            return ValueTask.FromResult(lazyTypeface.Value);
        }
        catch
        {
            ((ICollection<KeyValuePair<string, Lazy<SKTypeface>>>)_fonts)
                .Remove(new KeyValuePair<string, Lazy<SKTypeface>>(path, lazyTypeface));
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (var bitmap in _bitmaps.Values)
        {
            if (bitmap.IsValueCreated)
                bitmap.Value?.Dispose();
        }

        foreach (var font in _fonts.Values)
        {
            if (font.IsValueCreated)
                font.Value.Dispose();
        }

        _bitmaps.Clear();
        _fonts.Clear();
    }
}

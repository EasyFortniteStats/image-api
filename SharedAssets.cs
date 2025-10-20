using System.Runtime.InteropServices;
using Microsoft.Extensions.Caching.Memory;
using SkiaSharp;

namespace EasyFortniteStats_ImageApi;

public class SharedAssets
{
    private static readonly MemoryCacheEntryOptions CacheOptions = new() { Priority = CacheItemPriority.NeverRemove };
    private static readonly SemaphoreSlim Semaphore = new(1);
    private readonly IMemoryCache _memoryCache;

    public SharedAssets(IMemoryCache memoryCache)
    {
        _memoryCache = memoryCache;
    }

    public async ValueTask<SKBitmap?> GetBitmap(string format, string? arg1)
    {
        if (arg1 is null) return null;
        var path = string.Format(format, arg1);
        return await GetBitmap(path);
    }

    public async ValueTask<SKBitmap?> GetBitmap(string? path)
    {
        if (path is null) return null;

        var key = $"bmp_{path}";
        var cached = _memoryCache.Get<SKBitmap?>(key);
        if (cached is not null) return cached;

        await Semaphore.WaitAsync();

        cached = _memoryCache.Get<SKBitmap?>(key);
        if (cached is not null)
        {
            Semaphore.Release();
            return cached;
        }

        if (!File.Exists(path))
        {
            _memoryCache.Set(key, (SKBitmap?)null, CacheOptions);
            Semaphore.Release();
            return null;
        }

        using var data = await ReadToSkData(path); // TODO: test if should dispose
        var bitmap = SKBitmap.Decode(data);
        _memoryCache.Set(key, bitmap, CacheOptions);
        Semaphore.Release();
        return bitmap;
    }

    public async ValueTask<SKTypeface> GetFont(string path)
    {
        var key = $"font_{path}";
        var cached = _memoryCache.Get<SKTypeface>(key);
        if (cached is not null) return cached;

        await Semaphore.WaitAsync();

        cached = _memoryCache.Get<SKTypeface>(key);
        if (cached is not null)
        {
            Semaphore.Release();
            return cached;
        }

        using var data = await ReadToSkData(path); // TODO: test if should dispose
        var typeface = SKTypeface.FromData(data);
        _memoryCache.Set(key, typeface, CacheOptions);
        Semaphore.Release();
        return typeface;
    }

    private static async Task<SKData> ReadToSkData(string path)
    {
        UnmanagedMemoryStream? fileDataBufferStream = null;
        FileStream? fileStream = null;

        try
        {
            fileStream = File.OpenRead(path);
            var fileSize = fileStream.Length;
            nint fileDataBufferPtr;

            unsafe
            {
                var fileDataBuffer = NativeMemory.Alloc((nuint)fileSize);
                fileDataBufferPtr = (nint)fileDataBuffer;
                fileDataBufferStream =
                    new UnmanagedMemoryStream((byte*)fileDataBuffer, fileSize, fileSize, FileAccess.ReadWrite);
            }

            await fileStream.CopyToAsync(fileDataBufferStream);

            unsafe
            {
                var data = SKData.Create(fileDataBufferPtr, (int)fileSize,
                    (address, _) => NativeMemory.Free(address.ToPointer()));
                return data;
            }
        }
        finally
        {
            if (fileDataBufferStream is not null)
                await fileDataBufferStream.DisposeAsync();
            if (fileStream is not null)
                await fileStream.DisposeAsync();
        }
    }
}
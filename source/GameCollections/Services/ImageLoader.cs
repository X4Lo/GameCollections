using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace GameCollections.Services
{
    /// <summary>
    /// Decodes images off the UI thread at a reduced size and caches the results.
    /// </summary>
    public static class ImageLoader
    {
        private const int MaxCacheEntries = 400;

        private static readonly ILogger logger = LogManager.GetLogger();
        private static readonly object cacheLock = new object();
        private static readonly Dictionary<string, BitmapSource> cache = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
        private static readonly SemaphoreSlim limiter = new SemaphoreSlim(Math.Max(2, Environment.ProcessorCount / 2));

        public static async Task<BitmapSource> LoadAsync(string path, int decodeWidth)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            await limiter.WaitAsync().ConfigureAwait(false);
            try
            {
                return await Task.Run(() => Load(path, decodeWidth)).ConfigureAwait(false);
            }
            finally
            {
                limiter.Release();
            }
        }

        /// <summary>
        /// Returns a frozen bitmap decoded to <paramref name="decodeWidth"/> pixels wide, or null when
        /// the file is missing or cannot be decoded.
        /// </summary>
        public static BitmapSource Load(string path, int decodeWidth)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    return null;
                }

                // Including the write time means a replaced file is never served from a stale cache entry.
                var key = $"{info.FullName}|{decodeWidth}|{info.LastWriteTimeUtc.Ticks}|{info.Length}";
                lock (cacheLock)
                {
                    if (cache.TryGetValue(key, out var cached))
                    {
                        return cached;
                    }
                }

                BitmapImage bitmap;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    if (decodeWidth > 0)
                    {
                        bitmap.DecodePixelWidth = decodeWidth;
                    }

                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                    bitmap.Freeze();
                }

                lock (cacheLock)
                {
                    if (cache.Count >= MaxCacheEntries)
                    {
                        cache.Clear();
                    }

                    cache[key] = bitmap;
                }

                return bitmap;
            }
            catch (Exception e)
            {
                logger.Warn(e, $"Failed to load image {path}.");
                return null;
            }
        }
    }
}

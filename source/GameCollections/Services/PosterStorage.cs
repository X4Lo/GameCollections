using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GameCollections.Services
{
    /// <summary>
    /// Keeps copies of custom posters inside the plugin data folder so they survive the
    /// original file being moved or deleted.
    /// </summary>
    public class PosterStorage
    {
        public static readonly string[] SupportedExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };

        private static readonly ILogger logger = LogManager.GetLogger();
        private readonly string directory;

        public PosterStorage(string directory)
        {
            this.directory = directory;
        }

        public static bool IsSupportedFile(string path)
        {
            var extension = Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
            return SupportedExtensions.Contains(extension);
        }

        /// <summary>
        /// Copies an image into the poster folder and returns the stored file name.
        /// A new name is used on every import so cached images of the old poster are never reused.
        /// </summary>
        public string Import(Guid collectionId, string sourcePath)
        {
            if (!IsSupportedFile(sourcePath))
            {
                throw new NotSupportedException(Loc.Get("LOCGameCollections_ErrorPosterFormat"));
            }

            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException(Loc.Get("LOCGameCollections_ErrorPosterMissing"), sourcePath);
            }

            Directory.CreateDirectory(directory);
            var fileName = $"{collectionId:N}_{DateTime.UtcNow.Ticks}{Path.GetExtension(sourcePath).ToLowerInvariant()}";
            File.Copy(sourcePath, Path.Combine(directory, fileName), false);
            return fileName;
        }

        public string GetFullPath(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            // Only plain file names are stored; strip anything else so the path cannot leave the folder.
            return Path.Combine(directory, Path.GetFileName(fileName));
        }

        public void Delete(string fileName)
        {
            var path = GetFullPath(fileName);
            if (path == null)
            {
                return;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception e)
            {
                logger.Warn(e, $"Failed to delete poster {path}.");
            }
        }

        /// <summary>
        /// Removes poster files left behind by collections that no longer reference them.
        /// </summary>
        public void DeleteUnreferenced(IEnumerable<string> referencedFileNames)
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            var referenced = new HashSet<string>(
                referencedFileNames.Where(n => !string.IsNullOrWhiteSpace(n)).Select(Path.GetFileName),
                StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    if (!referenced.Contains(Path.GetFileName(file)))
                    {
                        Delete(Path.GetFileName(file));
                    }
                }
            }
            catch (Exception e)
            {
                logger.Warn(e, "Failed to clean up unreferenced posters.");
            }
        }
    }
}

using GameCollections.Models;
using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GameCollections.Services
{
    public class RepositoryLoadResult
    {
        public List<GameCollection> Collections { get; }

        /// <summary>
        /// Message to show the user when data had to be recovered or could not be read; null otherwise.
        /// </summary>
        public string Warning { get; }

        public RepositoryLoadResult(List<GameCollection> collections, string warning)
        {
            Collections = collections;
            Warning = warning;
        }
    }

    /// <summary>
    /// Reads and writes the collections JSON file in the plugin's user data folder.
    /// Writes go to a temporary file first and are then swapped in, keeping the previous
    /// version as a backup, so an interrupted save never leaves a half-written data file.
    /// </summary>
    public class CollectionRepository
    {
        public const int CurrentSchemaVersion = 1;
        private const string DataFileName = "collections.json";

        private static readonly ILogger logger = LogManager.GetLogger();
        private readonly string dataDirectory;
        private readonly string filePath;
        private readonly string backupPath;
        private readonly string tempPath;

        public CollectionRepository(string dataDirectory)
        {
            this.dataDirectory = dataDirectory;
            filePath = Path.Combine(dataDirectory, DataFileName);
            backupPath = filePath + ".bak";
            tempPath = filePath + ".tmp";
        }

        public RepositoryLoadResult Load()
        {
            if (!File.Exists(filePath) && !File.Exists(backupPath))
            {
                return new RepositoryLoadResult(new List<GameCollection>(), null);
            }

            string preservedCopy = null;
            if (File.Exists(filePath))
            {
                if (TryRead(filePath, out var collections))
                {
                    return new RepositoryLoadResult(collections, null);
                }

                // Move the unreadable file aside so the next save cannot overwrite it.
                preservedCopy = PreserveUnreadableFile(filePath);
            }

            if (File.Exists(backupPath) && TryRead(backupPath, out var backupCollections))
            {
                logger.Warn("Collections data file was unreadable, restored from backup.");
                return new RepositoryLoadResult(backupCollections, Loc.Get("LOCGameCollections_WarningRestoredFromBackup"));
            }

            return new RepositoryLoadResult(
                new List<GameCollection>(),
                Loc.Format("LOCGameCollections_WarningDataUnreadable", preservedCopy ?? filePath));
        }

        public void Save(IEnumerable<GameCollection> collections)
        {
            Directory.CreateDirectory(dataDirectory);
            var payload = new CollectionsDataFile
            {
                SchemaVersion = CurrentSchemaVersion,
                Collections = collections.ToList()
            };

            var json = Serialization.ToJson(payload, true);
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                var bytes = new UTF8Encoding(false).GetBytes(json);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }

            if (!File.Exists(filePath))
            {
                File.Move(tempPath, filePath);
                return;
            }

            try
            {
                File.Replace(tempPath, filePath, backupPath, true);
            }
            catch (Exception e) when (e is IOException || e is PlatformNotSupportedException || e is UnauthorizedAccessException)
            {
                // File.Replace is not supported on every file system; fall back to copy-based replacement.
                logger.Warn(e, "File.Replace failed, falling back to copy.");
                File.Copy(filePath, backupPath, true);
                File.Copy(tempPath, filePath, true);
                File.Delete(tempPath);
            }
        }

        private static bool TryRead(string path, out List<GameCollection> collections)
        {
            collections = null;
            try
            {
                var text = File.ReadAllText(path, Encoding.UTF8);
                var data = Serialization.FromJson<CollectionsDataFile>(text);
                if (data == null)
                {
                    logger.Error($"Collections file {path} contained no data.");
                    return false;
                }

                if (data.SchemaVersion > CurrentSchemaVersion)
                {
                    logger.Warn($"Collections file {path} was written by a newer plugin version ({data.SchemaVersion}).");
                }

                collections = Sanitize(data.Collections);
                return true;
            }
            catch (Exception e)
            {
                logger.Error(e, $"Failed to read collections file {path}.");
                return false;
            }
        }

        private static string PreserveUnreadableFile(string path)
        {
            try
            {
                var target = Path.Combine(
                    Path.GetDirectoryName(path),
                    $"{Path.GetFileNameWithoutExtension(path)}.unreadable-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                File.Move(path, target);
                return target;
            }
            catch (Exception e)
            {
                logger.Error(e, $"Failed to move unreadable collections file {path}.");
                return null;
            }
        }

        /// <summary>
        /// Repairs records that could have been damaged by manual edits so the rest of the plugin can rely on them.
        /// </summary>
        private static List<GameCollection> Sanitize(List<GameCollection> source)
        {
            var result = new List<GameCollection>();
            if (source == null)
            {
                return result;
            }

            var seenIds = new HashSet<Guid>();
            foreach (var collection in source)
            {
                if (collection == null)
                {
                    continue;
                }

                if (collection.Id == Guid.Empty || !seenIds.Add(collection.Id))
                {
                    collection.Id = Guid.NewGuid();
                    seenIds.Add(collection.Id);
                }

                collection.Title = CollectionManager.NormalizeTitle(collection.Title);
                if (collection.Title.Length == 0)
                {
                    collection.Title = Loc.Get("LOCGameCollections_UntitledCollection");
                }

                collection.Description = CollectionManager.NormalizeDescription(collection.Description);
                collection.Rating = CollectionManager.NormalizeRating(collection.Rating);
                collection.GameIds = (collection.GameIds ?? new List<Guid>())
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .ToList();

                if (collection.CreatedAt == default(DateTime))
                {
                    collection.CreatedAt = DateTime.UtcNow;
                }

                if (collection.ModifiedAt == default(DateTime))
                {
                    collection.ModifiedAt = collection.CreatedAt;
                }

                result.Add(collection);
            }

            return result;
        }
    }
}

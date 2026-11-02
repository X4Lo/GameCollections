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
                if (TryRead(filePath, out var collections, out var migration))
                {
                    return new RepositoryLoadResult(collections, FinishMigration(collections, migration));
                }

                // Move the unreadable file aside so the next save cannot overwrite it.
                preservedCopy = PreserveUnreadableFile(filePath);
            }

            if (File.Exists(backupPath) && TryRead(backupPath, out var backupCollections, out _))
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
                SchemaVersion = CollectionsDataMigrator.CurrentVersion,
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

        /// <summary>
        /// Keeps a copy of the file as it was before a migration or from a newer version, and saves the migrated
        /// data. Returns a warning for the user, or null. A failed save isn't fatal: the data is loaded, and
        /// the file is simply migrated again next time.
        /// </summary>
        private string FinishMigration(List<GameCollection> collections, DataMigrationResult migration)
        {
            if (!migration.WasMigrated && !migration.IsNewerThanSupported)
            {
                return null;
            }

            var copyPath = $"{filePath}.v{migration.FromVersion}.bak";
            try
            {
                if (!File.Exists(copyPath))
                {
                    File.Copy(filePath, copyPath);
                }

                if (migration.WasMigrated)
                {
                    Save(collections);
                    logger.Info($"Migrated collections data from version {migration.FromVersion} to {CollectionsDataMigrator.CurrentVersion}.");
                }
            }
            catch (Exception e)
            {
                logger.Error(e, "Failed to finish migrating the collections data file.");
            }

            return migration.IsNewerThanSupported ? Loc.Format("LOCGameCollections_WarningNewerData", copyPath) : null;
        }

        private static bool TryRead(string path, out List<GameCollection> collections, out DataMigrationResult migration)
        {
            collections = null;
            migration = null;
            try
            {
                var text = File.ReadAllText(path, Encoding.UTF8);
                migration = CollectionsDataMigrator.Migrate(text);
                var data = Serialization.FromJson<CollectionsDataFile>(migration.Json);
                if (data == null)
                {
                    logger.Error($"Collections file {path} contained no data.");
                    return false;
                }

                if (migration.IsNewerThanSupported)
                {
                    logger.Warn($"Collections file {path} was written by a newer plugin version ({migration.FromVersion}).");
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
                SanitizeRules(collection.Rules);

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

        private static void SanitizeRules(DynamicRules rules)
        {
            if (rules == null)
            {
                return;
            }

            rules.Blocks = (rules.Blocks ?? new List<RuleBlock>()).Where(b => b != null).ToList();
            foreach (var block in rules.Blocks)
            {
                block.Conditions = (block.Conditions ?? new List<RuleCondition>()).Where(c => c != null).ToList();
            }
        }
    }
}

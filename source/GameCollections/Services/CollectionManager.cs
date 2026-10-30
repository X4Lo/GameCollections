using GameCollections.Models;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GameCollections.Services
{
    public class CollectionsChangedEventArgs : EventArgs
    {
        /// <summary>
        /// IDs of the collections that were added, updated or removed.
        /// </summary>
        public IReadOnlyCollection<Guid> ChangedIds { get; }

        public CollectionsChangedEventArgs(IReadOnlyCollection<Guid> changedIds)
        {
            ChangedIds = changedIds;
        }
    }

    /// <summary>
    /// Owns the in-memory collection list and every operation on it. Each mutation is persisted
    /// immediately and announced through <see cref="CollectionsChanged"/>.
    /// All members must be called on the UI thread.
    /// </summary>
    public class CollectionManager
    {
        public const int MaxTitleLength = 100;
        public const int MaxDescriptionLength = 2000;
        public const double MinRating = 0;
        public const double MaxRating = 10;

        private static readonly ILogger logger = LogManager.GetLogger();
        private readonly IPlayniteAPI playniteApi;
        private readonly CollectionRepository repository;
        private readonly PosterStorage posterStorage;
        private readonly List<GameCollection> collections = new List<GameCollection>();

        public event EventHandler<CollectionsChangedEventArgs> CollectionsChanged;

        public IReadOnlyList<GameCollection> Collections => collections;

        public CollectionManager(IPlayniteAPI playniteApi, CollectionRepository repository, PosterStorage posterStorage)
        {
            this.playniteApi = playniteApi;
            this.repository = repository;
            this.posterStorage = posterStorage;
        }

        /// <summary>
        /// Loads collections from disk and returns a warning to show the user, or null.
        /// </summary>
        public string Load()
        {
            var result = repository.Load();
            collections.Clear();
            collections.AddRange(result.Collections);
            return result.Warning;
        }

        public GameCollection GetCollection(Guid id)
        {
            return collections.FirstOrDefault(c => c.Id == id);
        }

        #region Validation

        public static string NormalizeTitle(string title)
        {
            return title?.Trim() ?? string.Empty;
        }

        public static string NormalizeDescription(string description)
        {
            var trimmed = description?.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                return null;
            }

            return trimmed.Length > MaxDescriptionLength ? trimmed.Substring(0, MaxDescriptionLength) : trimmed;
        }

        public static double NormalizeRating(double rating)
        {
            if (double.IsNaN(rating) || double.IsInfinity(rating))
            {
                return MinRating;
            }

            return Math.Round(Math.Max(MinRating, Math.Min(MaxRating, rating)), 1, MidpointRounding.AwayFromZero);
        }

        public static bool TitlesEqual(string first, string second)
        {
            return string.Equals(NormalizeTitle(first), NormalizeTitle(second), StringComparison.CurrentCultureIgnoreCase);
        }

        public bool IsTitleTaken(string title, Guid? ignoredId = null)
        {
            return collections.Any(c => c.Id != ignoredId && TitlesEqual(c.Title, title));
        }

        /// <summary>
        /// Returns a user-facing error message for an invalid title, or null when the title can be used.
        /// </summary>
        public string ValidateTitle(string title, Guid? ignoredId = null)
        {
            var normalized = NormalizeTitle(title);
            if (normalized.Length == 0)
            {
                return Loc.Get("LOCGameCollections_ErrorTitleRequired");
            }

            if (normalized.Length > MaxTitleLength)
            {
                return Loc.Format("LOCGameCollections_ErrorTitleTooLong", MaxTitleLength);
            }

            if (IsTitleTaken(normalized, ignoredId))
            {
                return Loc.Format("LOCGameCollections_ErrorTitleDuplicate", normalized);
            }

            return null;
        }

        #endregion

        #region Collection operations

        public GameCollection CreateCollection(string title, string description, double rating, string posterSourcePath, IEnumerable<Guid> gameIds)
        {
            var collection = BuildNewCollection(title, gameIds);
            collection.Description = NormalizeDescription(description);
            collection.Rating = NormalizeRating(rating);
            if (!string.IsNullOrEmpty(posterSourcePath))
            {
                collection.PosterFileName = posterStorage.Import(collection.Id, posterSourcePath);
            }

            collections.Add(collection);
            Persist(new[] { collection.Id });
            return collection;
        }

        public void UpdateCollection(Guid id, string title, string description, double rating, PosterChange posterChange, string posterSourcePath)
        {
            var collection = GetCollection(id);
            if (collection == null)
            {
                throw new InvalidOperationException(Loc.Get("LOCGameCollections_ErrorCollectionNotFound"));
            }

            var error = ValidateTitle(title, id);
            if (error != null)
            {
                throw new InvalidOperationException(error);
            }

            // Import first: if copying the new poster fails, nothing has been changed yet.
            string obsoletePoster = null;
            var newPoster = collection.PosterFileName;
            if (posterChange == PosterChange.Replace)
            {
                newPoster = posterStorage.Import(id, posterSourcePath);
                obsoletePoster = collection.PosterFileName;
            }
            else if (posterChange == PosterChange.Remove)
            {
                newPoster = null;
                obsoletePoster = collection.PosterFileName;
            }

            collection.Title = NormalizeTitle(title);
            collection.Description = NormalizeDescription(description);
            collection.Rating = NormalizeRating(rating);
            collection.PosterFileName = newPoster;
            collection.ModifiedAt = DateTime.UtcNow;

            // The old poster is only deleted once the data file no longer references it.
            if (Persist(new[] { id }) && obsoletePoster != null)
            {
                posterStorage.Delete(obsoletePoster);
            }
        }

        /// <summary>
        /// Deletes the collection record only. Games referenced by it are not touched.
        /// </summary>
        public bool DeleteCollection(Guid id)
        {
            var collection = GetCollection(id);
            if (collection == null)
            {
                return false;
            }

            collections.Remove(collection);
            if (Persist(new[] { id }) && collection.PosterFileName != null)
            {
                posterStorage.Delete(collection.PosterFileName);
            }

            return true;
        }

        public void AddGames(Guid collectionId, IEnumerable<Guid> gameIds)
        {
            var collection = GetCollection(collectionId);
            if (collection != null && AddDistinct(collection, gameIds))
            {
                Persist(new[] { collectionId });
            }
        }

        /// <summary>
        /// Removes game references from a collection. The games stay in the Playnite library.
        /// </summary>
        public void RemoveGames(Guid collectionId, IEnumerable<Guid> gameIds)
        {
            var collection = GetCollection(collectionId);
            if (collection != null && RemoveIds(collection, new HashSet<Guid>(gameIds)))
            {
                Persist(new[] { collectionId });
            }
        }

        /// <summary>
        /// Applies the result of the "Add to Collection" dialog in a single save:
        /// <paramref name="membership"/> maps collection IDs to whether the games should be in them,
        /// and every title in <paramref name="newCollectionTitles"/> becomes a new collection containing the games.
        /// </summary>
        public void ApplyMembership(IReadOnlyCollection<Guid> gameIds, IDictionary<Guid, bool> membership, IEnumerable<string> newCollectionTitles)
        {
            var changed = new List<Guid>();
            foreach (var title in newCollectionTitles)
            {
                if (ValidateTitle(title) != null)
                {
                    continue;
                }

                var collection = BuildNewCollection(title, gameIds);
                collections.Add(collection);
                changed.Add(collection.Id);
            }

            var gameIdSet = new HashSet<Guid>(gameIds);
            foreach (var pair in membership)
            {
                var collection = GetCollection(pair.Key);
                if (collection == null)
                {
                    continue;
                }

                var modified = pair.Value ? AddDistinct(collection, gameIds) : RemoveIds(collection, gameIdSet);
                if (modified)
                {
                    changed.Add(collection.Id);
                }
            }

            if (changed.Count > 0)
            {
                Persist(changed);
            }
        }

        /// <summary>
        /// Drops references to games that were removed from the Playnite library.
        /// </summary>
        public void RemoveGamesFromAllCollections(IEnumerable<Guid> gameIds)
        {
            var removed = new HashSet<Guid>(gameIds);
            var changed = collections.Where(c => RemoveIds(c, removed)).Select(c => c.Id).ToList();
            if (changed.Count > 0)
            {
                Persist(changed);
            }
        }

        /// <summary>
        /// Drops references to games that no longer exist in the library, e.g. ones removed while the plugin was disabled.
        /// </summary>
        public void RemoveMissingGames()
        {
            var missing = collections
                .SelectMany(c => c.GameIds)
                .Distinct()
                .Where(id => playniteApi.Database.Games.Get(id) == null)
                .ToList();

            if (missing.Count > 0)
            {
                logger.Info($"Removing {missing.Count} game reference(s) no longer present in the library.");
                RemoveGamesFromAllCollections(missing);
            }
        }

        public void DeleteUnreferencedPosters()
        {
            posterStorage.DeleteUnreferenced(collections.Select(c => c.PosterFileName));
        }

        #endregion

        #region Games and posters

        /// <summary>
        /// Returns the collection's games that still exist in the library, in collection order.
        /// </summary>
        public List<Game> GetGames(GameCollection collection)
        {
            return collection.GameIds
                .Select(id => playniteApi.Database.Games.Get(id))
                .Where(g => g != null)
                .ToList();
        }

        public Game GetFirstGame(GameCollection collection)
        {
            foreach (var id in collection.GameIds)
            {
                var game = playniteApi.Database.Games.Get(id);
                if (game != null)
                {
                    return game;
                }
            }

            return null;
        }

        public bool HasCustomPoster(GameCollection collection)
        {
            return !string.IsNullOrEmpty(collection.PosterFileName);
        }

        public string GetCustomPosterPath(GameCollection collection)
        {
            var path = posterStorage.GetFullPath(collection.PosterFileName);
            return path != null && File.Exists(path) ? path : null;
        }

        /// <summary>
        /// Picks the image shown for a collection: the custom poster, otherwise the cover of the
        /// first game in the collection, otherwise null (the placeholder is shown).
        /// </summary>
        public string ResolvePosterPath(GameCollection collection)
        {
            return GetCustomPosterPath(collection) ?? GetGameCoverPath(GetFirstGame(collection));
        }

        public string GetGameCoverPath(Game game)
        {
            if (game == null || string.IsNullOrWhiteSpace(game.CoverImage))
            {
                return null;
            }

            // Covers that have not been downloaded yet are stored as URLs; treat them as unavailable.
            if (game.CoverImage.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                game.CoverImage.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            try
            {
                var path = playniteApi.Database.GetFullFilePath(game.CoverImage);
                return File.Exists(path) ? path : null;
            }
            catch (Exception e)
            {
                logger.Warn(e, $"Failed to resolve cover path for game {game.Id}.");
                return null;
            }
        }

        #endregion

        private GameCollection BuildNewCollection(string title, IEnumerable<Guid> gameIds)
        {
            var error = ValidateTitle(title);
            if (error != null)
            {
                throw new InvalidOperationException(error);
            }

            var now = DateTime.UtcNow;
            var collection = new GameCollection
            {
                Id = Guid.NewGuid(),
                Title = NormalizeTitle(title),
                CreatedAt = now,
                ModifiedAt = now
            };

            if (gameIds != null)
            {
                AddDistinct(collection, gameIds);
                collection.ModifiedAt = now;
            }

            return collection;
        }

        private static bool AddDistinct(GameCollection collection, IEnumerable<Guid> gameIds)
        {
            var existing = new HashSet<Guid>(collection.GameIds);
            var added = false;
            foreach (var id in gameIds)
            {
                if (id != Guid.Empty && existing.Add(id))
                {
                    collection.GameIds.Add(id);
                    added = true;
                }
            }

            if (added)
            {
                collection.ModifiedAt = DateTime.UtcNow;
            }

            return added;
        }

        private static bool RemoveIds(GameCollection collection, HashSet<Guid> gameIds)
        {
            var removed = collection.GameIds.RemoveAll(gameIds.Contains) > 0;
            if (removed)
            {
                collection.ModifiedAt = DateTime.UtcNow;
            }

            return removed;
        }

        private bool Persist(IReadOnlyCollection<Guid> changedIds)
        {
            var saved = false;
            try
            {
                repository.Save(collections);
                saved = true;
            }
            catch (Exception e)
            {
                logger.Error(e, "Failed to save collections.");
                playniteApi.Notifications.Add(new NotificationMessage(
                    "GameCollections_SaveError",
                    Loc.Format("LOCGameCollections_ErrorSaveFailed", e.Message),
                    NotificationType.Error));
            }

            try
            {
                CollectionsChanged?.Invoke(this, new CollectionsChangedEventArgs(changedIds));
            }
            catch (Exception e)
            {
                logger.Error(e, "Error while handling collection changes.");
            }

            return saved;
        }
    }
}

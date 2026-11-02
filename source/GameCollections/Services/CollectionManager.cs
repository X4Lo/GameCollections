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
    /// The outcome of searching the library for one dynamic collection.
    /// </summary>
    public class DynamicMatchResult
    {
        public Guid CollectionId { get; set; }

        /// <summary>
        /// The rules the search used, to detect that they were edited in the meantime.
        /// </summary>
        public DynamicRules Rules { get; set; }
        public List<Guid> GameIds { get; set; }
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
        private Dictionary<Guid, List<GameCollection>> collectionsByGame;

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
            collectionsByGame = null;
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

        public GameCollection CreateCollection(string title, string description, double rating, string posterSourcePath, IEnumerable<Guid> gameIds, DynamicRules rules = null)
        {
            var collection = BuildNewCollection(title, gameIds);
            collection.Description = NormalizeDescription(description);
            collection.Rating = NormalizeRating(rating);
            if (!string.IsNullOrEmpty(posterSourcePath))
            {
                collection.PosterFileName = posterStorage.Import(collection.Id, posterSourcePath);
            }

            if (rules != null)
            {
                collection.Rules = rules;
                MergeMatches(collection, FindMatches(rules, playniteApi.Database.Games, DateTime.Now));
            }

            collections.Add(collection);
            Persist(new[] { collection.Id });
            return collection;
        }

        /// <summary>
        /// Saves the collection's properties. Passing rules makes it dynamic and replaces its games with
        /// the matches; passing null makes it manual, and a dynamic collection keeps its current games.
        /// </summary>
        public void UpdateCollection(Guid id, string title, string description, double rating, PosterChange posterChange, string posterSourcePath, DynamicRules rules = null)
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
            collection.Rules = rules;
            if (rules != null)
            {
                MergeMatches(collection, FindMatches(rules, playniteApi.Database.Games, DateTime.Now));
            }

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
            if (collection != null && !collection.IsDynamic && AddDistinct(collection, gameIds))
            {
                Persist(new[] { collectionId });
            }
        }

        /// <summary>
        /// Removes game references from a manual collection. The games stay in the Playnite library.
        /// The games of a dynamic collection are decided by its rules, so they can't be removed by hand.
        /// </summary>
        public void RemoveGames(Guid collectionId, IEnumerable<Guid> gameIds)
        {
            var collection = GetCollection(collectionId);
            if (collection != null && !collection.IsDynamic && RemoveIds(collection, new HashSet<Guid>(gameIds)))
            {
                Persist(new[] { collectionId });
            }
        }

        /// <summary>
        /// Moves games within a collection, keeping their relative order. They are placed next to
        /// <paramref name="anchorId"/>: after it when <paramref name="after"/> is set, otherwise before it.
        /// A null anchor means the end of the collection when <paramref name="after"/> is set, otherwise the start.
        /// </summary>
        public bool MoveGames(Guid collectionId, IEnumerable<Guid> gameIds, Guid? anchorId, bool after)
        {
            var collection = GetCollection(collectionId);
            if (collection == null)
            {
                return false;
            }

            var moving = new HashSet<Guid>(gameIds);
            if (anchorId.HasValue && moving.Contains(anchorId.Value))
            {
                return false;
            }

            var moved = collection.GameIds.Where(moving.Contains).ToList();
            var remaining = collection.GameIds.Where(id => !moving.Contains(id)).ToList();
            int index;
            if (anchorId.HasValue)
            {
                index = remaining.IndexOf(anchorId.Value);
                if (index < 0)
                {
                    return false;
                }

                index += after ? 1 : 0;
            }
            else
            {
                index = after ? remaining.Count : 0;
            }

            remaining.InsertRange(index, moved);
            if (moved.Count == 0 || remaining.SequenceEqual(collection.GameIds))
            {
                return false;
            }

            collection.GameIds = remaining;
            collection.ModifiedAt = DateTime.UtcNow;
            Persist(new[] { collectionId });
            return true;
        }

        /// <summary>
        /// Re-checks one dynamic collection against the whole library right away.
        /// </summary>
        public void RefreshCollection(Guid collectionId)
        {
            var collection = GetCollection(collectionId);
            if (collection != null && collection.IsDynamic &&
                MergeMatches(collection, FindMatches(collection.Rules, playniteApi.Database.Games, DateTime.Now)))
            {
                Persist(new[] { collectionId });
            }
        }

        /// <summary>
        /// Finds the games matching the rules, sorted by name. Safe to call on a background thread.
        /// </summary>
        public static List<Guid> FindMatches(DynamicRules rules, IEnumerable<Game> games, DateTime now)
        {
            var isMatch = RuleEngine.Compile(rules, now);
            return games
                .Where(isMatch)
                .OrderBy(g => string.IsNullOrEmpty(g.SortingName) ? g.Name : g.SortingName, StringComparer.CurrentCultureIgnoreCase)
                .Select(g => g.Id)
                .ToList();
        }

        /// <summary>
        /// Applies background search results in a single save. A result is skipped if the collection's
        /// rules were changed after the search started, since it would be out of date.
        /// </summary>
        public void ApplyMatches(IEnumerable<DynamicMatchResult> results)
        {
            var changed = new List<Guid>();
            foreach (var result in results)
            {
                var collection = GetCollection(result.CollectionId);
                if (collection != null && ReferenceEquals(collection.Rules, result.Rules) && MergeMatches(collection, result.GameIds))
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
        /// Updates dynamic collections after the given games were added or changed. Only these games
        /// are tested, so the cost depends on how many changed and not on the size of the library.
        /// </summary>
        public void ApplyGameChanges(ICollection<Guid> gameIds)
        {
            var changed = new List<Guid>();
            var now = DateTime.Now;
            foreach (var collection in collections.Where(c => c.IsDynamic))
            {
                var isMatch = RuleEngine.Compile(collection.Rules, now);
                var current = new HashSet<Guid>(collection.GameIds);
                var modified = false;
                foreach (var id in gameIds)
                {
                    var matches = isMatch(playniteApi.Database.Games.Get(id));
                    if (matches && current.Add(id))
                    {
                        collection.GameIds.Add(id);
                        modified = true;
                    }
                    else if (!matches && current.Remove(id))
                    {
                        collection.GameIds.Remove(id);
                        modified = true;
                    }
                }

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
        /// Makes the collection hold exactly the matching games. Games already in it keep their position,
        /// which preserves any manual ordering, and new ones go at the end.
        /// </summary>
        private static bool MergeMatches(GameCollection collection, IReadOnlyCollection<Guid> matches)
        {
            var matching = new HashSet<Guid>(matches);
            var merged = collection.GameIds.Where(matching.Contains).ToList();
            var current = new HashSet<Guid>(merged);
            merged.AddRange(matches.Where(id => !current.Contains(id)));
            if (merged.SequenceEqual(collection.GameIds))
            {
                return false;
            }

            collection.GameIds = merged;
            return true;
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
        /// Returns the collections containing the game. The lookup is cached between changes because
        /// this runs every time a different game is selected in the library.
        /// </summary>
        public IReadOnlyList<GameCollection> GetCollectionsForGame(Guid gameId)
        {
            if (collectionsByGame == null)
            {
                collectionsByGame = new Dictionary<Guid, List<GameCollection>>();
                foreach (var collection in collections.OrderBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase))
                {
                    foreach (var id in collection.GameIds)
                    {
                        if (!collectionsByGame.TryGetValue(id, out var list))
                        {
                            collectionsByGame[id] = list = new List<GameCollection>();
                        }

                        list.Add(collection);
                    }
                }
            }

            return collectionsByGame.TryGetValue(gameId, out var result) ? result : (IReadOnlyList<GameCollection>)new GameCollection[0];
        }

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
            collectionsByGame = null;
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

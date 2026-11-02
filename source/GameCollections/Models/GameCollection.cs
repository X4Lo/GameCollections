using Playnite.SDK.Data;
using System;
using System.Collections.Generic;

namespace GameCollections.Models
{
    /// <summary>
    /// Persisted collection record. Games are referenced by their Playnite database IDs only;
    /// no game metadata is duplicated here.
    /// </summary>
    public class GameCollection
    {
        public Guid Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public double Rating { get; set; }

        /// <summary>
        /// File name of the custom poster inside the plugin's poster folder, or null when none is assigned.
        /// </summary>
        public string PosterFileName { get; set; }

        public List<Guid> GameIds { get; set; } = new List<Guid>();

        /// <summary>
        /// The rules of a dynamic collection, or null for a manual one. For a dynamic collection
        /// <see cref="GameIds"/> holds the latest matches, so it is available without searching the library.
        /// </summary>
        public DynamicRules Rules { get; set; }

        /// <summary>
        /// How the collection's page groups its games. Only affects how they're shown.
        /// </summary>
        public GameGroupField GroupBy { get; set; }

        [DontSerialize]
        public bool IsDynamic => Rules != null;

        public DateTime CreatedAt { get; set; }
        public DateTime ModifiedAt { get; set; }
    }

    /// <summary>
    /// Root object of the collections data file.
    /// </summary>
    public class CollectionsDataFile
    {
        /// <summary>
        /// The version of this file's format. See <c>CollectionsDataMigrator</c>.
        /// </summary>
        public int SchemaVersion { get; set; }
        public List<GameCollection> Collections { get; set; } = new List<GameCollection>();
    }

    public enum PosterChange
    {
        Keep,
        Replace,
        Remove
    }

    public enum CollectionsViewMode
    {
        Grid,
        List
    }
}

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
        public DateTime CreatedAt { get; set; }
        public DateTime ModifiedAt { get; set; }
    }

    /// <summary>
    /// Root object of the collections data file.
    /// </summary>
    public class CollectionsDataFile
    {
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

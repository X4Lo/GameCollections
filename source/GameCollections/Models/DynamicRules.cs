using System;
using System.Collections.Generic;

namespace GameCollections.Models
{
    // The enums below are stored in the data file by number, so existing values must never be renumbered.

    public enum RuleField
    {
        Name = 0,
        Platform = 1,
        Genre = 2,
        Developer = 3,
        Publisher = 4,
        Category = 5,
        Feature = 6,
        Tag = 7,
        Series = 8,
        CompletionStatus = 9,
        ReleaseDate = 10,
        LastPlayed = 11,
        UserScore = 12,
        CriticScore = 13,
        CommunityScore = 14,
        TimePlayed = 15,
        PlayCount = 16
    }

    public enum RuleOperator
    {
        Contains = 0,
        NotContains = 1,
        IncludesAny = 2,
        IncludesAll = 3,
        Excludes = 4,
        AtLeast = 5,
        AtMost = 6,
        Between = 7,
        Before = 8,
        After = 9,
        InLastDays = 10,
        OlderThanDays = 11,
        IsEmpty = 12
    }

    public enum BlockJoin
    {
        And = 0,
        Or = 1
    }

    public enum RuleValueKind
    {
        Text,
        Items,
        Number,
        Date
    }

    /// <summary>
    /// One test on a game. Which value members are used depends on the field and operator.
    /// </summary>
    public class RuleCondition
    {
        public RuleField Field { get; set; }
        public RuleOperator Operator { get; set; }

        /// <summary>
        /// The search text for names, or the first date (YYYY, YYYY-MM or YYYY-MM-DD).
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// The second date of a "between" condition.
        /// </summary>
        public string Text2 { get; set; }

        /// <summary>
        /// The score, hours, play count or number of days.
        /// </summary>
        public double? Number { get; set; }

        /// <summary>
        /// The upper bound of a "between" condition.
        /// </summary>
        public double? Number2 { get; set; }

        /// <summary>
        /// IDs of the selected platforms, genres, tags etc. IDs are stored rather than names so renaming a tag keeps the rule working.
        /// </summary>
        public List<Guid> ItemIds { get; set; } = new List<Guid>();
    }

    /// <summary>
    /// A group of conditions that must all be true.
    /// </summary>
    public class RuleBlock
    {
        /// <summary>
        /// How this block combines with the result of the blocks above it. Not used on the first block.
        /// </summary>
        public BlockJoin Join { get; set; }

        public List<RuleCondition> Conditions { get; set; } = new List<RuleCondition>();
    }

    /// <summary>
    /// The rules of a dynamic collection. Blocks are combined from top to bottom.
    /// </summary>
    public class DynamicRules
    {
        public List<RuleBlock> Blocks { get; set; } = new List<RuleBlock>();
    }
}

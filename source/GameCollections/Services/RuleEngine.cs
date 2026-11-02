using GameCollections.Models;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GameCollections.Services
{
    /// <summary>
    /// Turns dynamic collection rules into a predicate over games. Compiled predicates only read
    /// in-memory game fields, so they are cheap and safe to run on a background thread.
    /// </summary>
    public static class RuleEngine
    {
        private const double SecondsPerHour = 3600;

        public static RuleValueKind GetValueKind(RuleField field)
        {
            switch (field)
            {
                case RuleField.Name:
                    return RuleValueKind.Text;
                case RuleField.ReleaseDate:
                case RuleField.LastPlayed:
                    return RuleValueKind.Date;
                case RuleField.UserScore:
                case RuleField.CriticScore:
                case RuleField.CommunityScore:
                case RuleField.TimePlayed:
                case RuleField.PlayCount:
                    return RuleValueKind.Number;
                default:
                    return RuleValueKind.Items;
            }
        }

        public static IReadOnlyList<RuleOperator> GetOperators(RuleField field)
        {
            switch (GetValueKind(field))
            {
                case RuleValueKind.Text:
                    return new[] { RuleOperator.Contains, RuleOperator.NotContains };
                case RuleValueKind.Date:
                    return new[] { RuleOperator.After, RuleOperator.Before, RuleOperator.Between, RuleOperator.InLastDays, RuleOperator.OlderThanDays, RuleOperator.IsEmpty };
                case RuleValueKind.Number:
                    return IsScore(field)
                        ? new[] { RuleOperator.AtLeast, RuleOperator.AtMost, RuleOperator.Between, RuleOperator.IsEmpty }
                        : new[] { RuleOperator.AtLeast, RuleOperator.AtMost, RuleOperator.Between };
                default:
                    // A game has only one completion status, so "all of" could never match two of them.
                    return field == RuleField.CompletionStatus
                        ? new[] { RuleOperator.IncludesAny, RuleOperator.Excludes, RuleOperator.IsEmpty }
                        : new[] { RuleOperator.IncludesAny, RuleOperator.IncludesAll, RuleOperator.Excludes, RuleOperator.IsEmpty };
            }
        }

        public static bool IsScore(RuleField field)
        {
            return field == RuleField.UserScore || field == RuleField.CriticScore || field == RuleField.CommunityScore;
        }

        /// <summary>
        /// Relative date rules change their result as time passes, without any game changing.
        /// </summary>
        public static bool DependsOnCurrentTime(DynamicRules rules)
        {
            return rules?.Blocks != null && rules.Blocks.Any(b => b.Conditions != null && b.Conditions.Any(c =>
                c.Operator == RuleOperator.InLastDays || c.Operator == RuleOperator.OlderThanDays));
        }

        /// <summary>
        /// Parses YYYY, YYYY-MM or YYYY-MM-DD into the first and last day of that period.
        /// </summary>
        public static bool TryParseDatePeriod(string text, out DateTime start, out DateTime end)
        {
            start = end = default(DateTime);
            var value = text?.Trim();
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            var culture = CultureInfo.InvariantCulture;
            if (DateTime.TryParseExact(value, "yyyy-M-d", culture, DateTimeStyles.None, out start))
            {
                end = start;
                return true;
            }

            if (DateTime.TryParseExact(value, "yyyy-M", culture, DateTimeStyles.None, out start))
            {
                end = start.AddMonths(1).AddDays(-1);
                return true;
            }

            if (DateTime.TryParseExact(value, "yyyy", culture, DateTimeStyles.None, out start))
            {
                end = start.AddYears(1).AddDays(-1);
                return true;
            }

            return false;
        }

        /// <summary>
        /// True when the condition has every value its operator needs, and those values are valid.
        /// </summary>
        public static bool IsComplete(RuleCondition condition)
        {
            if (condition == null)
            {
                return false;
            }

            if (condition.Operator == RuleOperator.IsEmpty)
            {
                return true;
            }

            switch (GetValueKind(condition.Field))
            {
                case RuleValueKind.Text:
                    return !string.IsNullOrWhiteSpace(condition.Text);
                case RuleValueKind.Items:
                    return condition.ItemIds != null && condition.ItemIds.Count > 0;
                case RuleValueKind.Number:
                    return condition.Number >= 0 && (condition.Operator != RuleOperator.Between || condition.Number2 >= condition.Number);
                default:
                    return IsDateConditionComplete(condition);
            }
        }

        public static bool IsComplete(DynamicRules rules)
        {
            return rules?.Blocks != null && rules.Blocks.Count > 0 && rules.Blocks.All(b =>
                b.Conditions != null && b.Conditions.Count > 0 && b.Conditions.All(IsComplete));
        }

        /// <summary>
        /// Builds a predicate for the rules. Blocks are combined strictly from top to bottom, matching how
        /// they read in the editor. Hidden games never match, and neither do damaged or incomplete rules,
        /// so a broken rule can never fill a collection with the whole library.
        /// </summary>
        public static Func<Game, bool> Compile(DynamicRules rules, DateTime now)
        {
            if (!IsComplete(rules))
            {
                return game => false;
            }

            var blocks = rules.Blocks
                .Select(b => new { b.Join, Test = CompileBlock(b, now) })
                .ToList();

            return game =>
            {
                if (game == null || game.Hidden)
                {
                    return false;
                }

                var result = blocks[0].Test(game);
                for (var i = 1; i < blocks.Count; i++)
                {
                    result = blocks[i].Join == BlockJoin.And
                        ? result && blocks[i].Test(game)
                        : result || blocks[i].Test(game);
                }

                return result;
            };
        }

        private static Func<Game, bool> CompileBlock(RuleBlock block, DateTime now)
        {
            var tests = block.Conditions.Select(c => CompileCondition(c, now)).ToList();
            return game =>
            {
                foreach (var test in tests)
                {
                    if (!test(game))
                    {
                        return false;
                    }
                }

                return true;
            };
        }

        private static Func<Game, bool> CompileCondition(RuleCondition condition, DateTime now)
        {
            switch (GetValueKind(condition.Field))
            {
                case RuleValueKind.Text:
                    return CompileText(condition);
                case RuleValueKind.Items:
                    return CompileItems(condition);
                case RuleValueKind.Number:
                    return CompileNumber(condition);
                default:
                    return CompileDate(condition, now);
            }
        }

        private static Func<Game, bool> CompileText(RuleCondition condition)
        {
            var query = condition.Text.Trim();
            var negate = condition.Operator == RuleOperator.NotContains;
            return game => (game.Name != null && game.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) != negate;
        }

        private static Func<Game, bool> CompileItems(RuleCondition condition)
        {
            var getIds = GetItemIds(condition.Field);
            var wanted = new HashSet<Guid>(condition.ItemIds ?? new List<Guid>());
            switch (condition.Operator)
            {
                case RuleOperator.IncludesAll:
                    return game =>
                    {
                        var ids = getIds(game);
                        return ids != null && wanted.All(ids.Contains);
                    };
                case RuleOperator.Excludes:
                    return game =>
                    {
                        var ids = getIds(game);
                        return ids == null || !ids.Any(wanted.Contains);
                    };
                case RuleOperator.IsEmpty:
                    return game =>
                    {
                        var ids = getIds(game);
                        return ids == null || ids.Count == 0;
                    };
                default:
                    return game =>
                    {
                        var ids = getIds(game);
                        return ids != null && ids.Any(wanted.Contains);
                    };
            }
        }

        private static Func<Game, IList<Guid>> GetItemIds(RuleField field)
        {
            switch (field)
            {
                case RuleField.Platform: return g => g.PlatformIds;
                case RuleField.Genre: return g => g.GenreIds;
                case RuleField.Developer: return g => g.DeveloperIds;
                case RuleField.Publisher: return g => g.PublisherIds;
                case RuleField.Category: return g => g.CategoryIds;
                case RuleField.Feature: return g => g.FeatureIds;
                case RuleField.Tag: return g => g.TagIds;
                case RuleField.Series: return g => g.SeriesIds;
                case RuleField.CompletionStatus:
                    return g => g.CompletionStatusId == Guid.Empty ? null : new[] { g.CompletionStatusId };
                default:
                    throw new ArgumentOutOfRangeException(nameof(field));
            }
        }

        private static Func<Game, bool> CompileNumber(RuleCondition condition)
        {
            var getValue = GetNumber(condition.Field);
            var low = condition.Number ?? 0;
            var high = condition.Number2 ?? 0;
            switch (condition.Operator)
            {
                case RuleOperator.AtMost:
                    return game => getValue(game) <= low;
                case RuleOperator.Between:
                    return game => getValue(game) >= low && getValue(game) <= high;
                case RuleOperator.IsEmpty:
                    return game => getValue(game) == null;
                default:
                    return game => getValue(game) >= low;
            }
        }

        private static Func<Game, double?> GetNumber(RuleField field)
        {
            switch (field)
            {
                case RuleField.UserScore: return g => g.UserScore;
                case RuleField.CriticScore: return g => g.CriticScore;
                case RuleField.CommunityScore: return g => g.CommunityScore;
                case RuleField.TimePlayed: return g => g.Playtime / SecondsPerHour;
                case RuleField.PlayCount: return g => g.PlayCount;
                default:
                    throw new ArgumentOutOfRangeException(nameof(field));
            }
        }

        private static Func<Game, bool> CompileDate(RuleCondition condition, DateTime now)
        {
            Func<Game, DateTime?> getDate;
            if (condition.Field == RuleField.ReleaseDate)
            {
                getDate = g => g.ReleaseDate?.Date;
            }
            else
            {
                getDate = g => g.LastActivity;
            }

            var days = condition.Number ?? 0;
            TryParseDatePeriod(condition.Text, out var start, out var end);
            TryParseDatePeriod(condition.Text2, out _, out var end2);
            switch (condition.Operator)
            {
                case RuleOperator.Before:
                    return game => getDate(game)?.Date < start;
                case RuleOperator.After:
                    return game => getDate(game)?.Date > end;
                case RuleOperator.Between:
                    return game => getDate(game)?.Date >= start && getDate(game)?.Date <= end2;
                case RuleOperator.InLastDays:
                    return game => getDate(game) >= now.AddDays(-days);
                case RuleOperator.OlderThanDays:
                    return game => getDate(game) < now.AddDays(-days);
                default:
                    return game => getDate(game) == null;
            }
        }

        private static bool IsDateConditionComplete(RuleCondition condition)
        {
            switch (condition.Operator)
            {
                case RuleOperator.InLastDays:
                case RuleOperator.OlderThanDays:
                    return condition.Number >= 0;
                case RuleOperator.Between:
                    return TryParseDatePeriod(condition.Text, out var start, out _) &&
                        TryParseDatePeriod(condition.Text2, out _, out var end) &&
                        end >= start;
                default:
                    return TryParseDatePeriod(condition.Text, out _, out _);
            }
        }
    }
}

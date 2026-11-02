using GameCollections.Models;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GameCollections.Services
{
    /// <summary>
    /// A selectable value of a rule field, such as one platform or tag.
    /// </summary>
    public class RuleOption
    {
        public Guid Id { get; }
        public string Name { get; }

        public RuleOption(Guid id, string name)
        {
            Id = id;
            Name = name;
        }
    }

    /// <summary>
    /// User-facing names for rule fields and operators, the values a field can take, and rule summaries.
    /// </summary>
    public class RuleText
    {
        private readonly IPlayniteAPI playniteApi;
        private readonly Dictionary<RuleField, List<RuleOption>> optionsCache = new Dictionary<RuleField, List<RuleOption>>();

        public RuleText(IPlayniteAPI playniteApi)
        {
            this.playniteApi = playniteApi;
        }

        public static string FieldName(RuleField field)
        {
            return Loc.Get("LOCGameCollections_RuleField" + field);
        }

        public static string OperatorName(RuleField field, RuleOperator op)
        {
            // Dates read differently from the other kinds: "is empty" is "has no date", and so on.
            return Loc.Get(op == RuleOperator.IsEmpty && RuleEngine.GetValueKind(field) == RuleValueKind.Date
                ? "LOCGameCollections_RuleOperatorNoDate"
                : "LOCGameCollections_RuleOperator" + op);
        }

        /// <summary>
        /// Returns the values that can be chosen for a field, sorted by name. Loaded once per dialog.
        /// </summary>
        public IReadOnlyList<RuleOption> GetOptions(RuleField field)
        {
            if (!optionsCache.TryGetValue(field, out var options))
            {
                options = LoadItems(field)
                    .Where(i => i != null && !string.IsNullOrWhiteSpace(i.Name))
                    .Select(i => new RuleOption(i.Id, i.Name))
                    .OrderBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                optionsCache[field] = options;
            }

            return options;
        }

        /// <summary>
        /// Describes rules in one line, for example: Platform includes any of PC and Time played at least 10.
        /// </summary>
        public string Describe(DynamicRules rules)
        {
            if (rules?.Blocks == null)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            foreach (var block in rules.Blocks)
            {
                var text = string.Join(" " + Loc.Get("LOCGameCollections_RuleAnd") + " ", block.Conditions.Select(Describe));
                if (parts.Count > 0)
                {
                    parts.Add(Loc.Get(block.Join == BlockJoin.And ? "LOCGameCollections_RuleAnd" : "LOCGameCollections_RuleOr").ToUpper(CultureInfo.CurrentCulture));
                }

                parts.Add(rules.Blocks.Count > 1 ? "(" + text + ")" : text);
            }

            return string.Join(" ", parts);
        }

        private string Describe(RuleCondition condition)
        {
            var label = FieldName(condition.Field) + " " + OperatorName(condition.Field, condition.Operator);
            if (condition.Operator == RuleOperator.IsEmpty)
            {
                return label;
            }

            switch (RuleEngine.GetValueKind(condition.Field))
            {
                case RuleValueKind.Text:
                    return $"{label} \"{condition.Text}\"";
                case RuleValueKind.Items:
                    var options = GetOptions(condition.Field);
                    var names = condition.ItemIds.Select(id => options.FirstOrDefault(o => o.Id == id)?.Name).Where(n => n != null);
                    return label + " " + string.Join(", ", names);
                case RuleValueKind.Number:
                    return condition.Operator == RuleOperator.Between ? $"{label} {condition.Number} – {condition.Number2}" : $"{label} {condition.Number}";
                default:
                    if (condition.Operator == RuleOperator.InLastDays || condition.Operator == RuleOperator.OlderThanDays)
                    {
                        return $"{label} {condition.Number} {Loc.Get("LOCGameCollections_RuleUnitDays")}";
                    }

                    return condition.Operator == RuleOperator.Between ? $"{label} {condition.Text} – {condition.Text2}" : $"{label} {condition.Text}";
            }
        }

        private IEnumerable<DatabaseObject> LoadItems(RuleField field)
        {
            var database = playniteApi.Database;
            switch (field)
            {
                case RuleField.Platform: return database.Platforms;
                case RuleField.Genre: return database.Genres;
                case RuleField.Category: return database.Categories;
                case RuleField.Feature: return database.Features;
                case RuleField.Tag: return database.Tags;
                case RuleField.Series: return database.Series;
                case RuleField.CompletionStatus: return database.CompletionStatuses;
                case RuleField.Developer: return LoadCompanies(g => g.DeveloperIds);
                case RuleField.Publisher: return LoadCompanies(g => g.PublisherIds);
                default: return Enumerable.Empty<DatabaseObject>();
            }
        }

        // Developers and publishers share one long company list, so only those actually used in that role are offered.
        private IEnumerable<DatabaseObject> LoadCompanies(Func<Game, List<Guid>> getIds)
        {
            var used = new HashSet<Guid>(playniteApi.Database.Games.Where(g => getIds(g) != null).SelectMany(getIds));
            return playniteApi.Database.Companies.Where(c => used.Contains(c.Id));
        }
    }
}

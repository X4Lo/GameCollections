using GameCollections.Models;
using GameCollections.Services;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using System.Windows.Threading;

namespace GameCollections.ViewModels
{
    public class RuleFieldChoice
    {
        public RuleField Field { get; }
        public string Label { get; }

        public RuleFieldChoice(RuleField field)
        {
            Field = field;
            Label = RuleText.FieldName(field);
        }
    }

    public class RuleOperatorChoice
    {
        public RuleOperator Operator { get; }
        public string Label { get; }

        public RuleOperatorChoice(RuleField field, RuleOperator op)
        {
            Operator = op;
            Label = RuleText.OperatorName(field, op);
        }
    }

    /// <summary>
    /// A row in the value picker of a list field such as platforms or tags.
    /// </summary>
    public class RuleOptionItem : ObservableObject
    {
        private readonly Action<RuleOptionItem> selectionChanged;

        public Guid Id { get; }
        public string Name { get; }

        private bool isSelected;
        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (isSelected != value)
                {
                    SetValue(ref isSelected, value);
                    selectionChanged(this);
                }
            }
        }

        public RelayCommand RemoveCommand { get; }

        public RuleOptionItem(RuleOption option, Action<RuleOptionItem> selectionChanged)
        {
            Id = option.Id;
            Name = option.Name;
            this.selectionChanged = selectionChanged;
            RemoveCommand = new RelayCommand(() => IsSelected = false);
        }
    }

    /// <summary>
    /// One row of the rule builder: field, operator and the value editor for that combination.
    /// </summary>
    public class RuleConditionViewModel : ObservableObject
    {
        private static readonly RuleField[] FieldOrder =
        {
            RuleField.Name, RuleField.Platform, RuleField.Genre, RuleField.Developer, RuleField.Publisher,
            RuleField.Category, RuleField.Feature, RuleField.Tag, RuleField.Series, RuleField.CompletionStatus,
            RuleField.ReleaseDate, RuleField.LastPlayed, RuleField.TimePlayed, RuleField.PlayCount,
            RuleField.UserScore, RuleField.CriticScore, RuleField.CommunityScore
        };

        private readonly RuleText ruleText;
        private readonly Action changed;

        public static IReadOnlyList<RuleFieldChoice> Fields { get; } = FieldOrder.Select(f => new RuleFieldChoice(f)).ToList();

        public RuleBlockViewModel Block { get; }

        private RuleField field;
        public RuleField Field
        {
            get => field;
            set
            {
                if (field == value)
                {
                    return;
                }

                SetValue(ref field, value);
                // Values from another field wouldn't make sense here, except that a name or date is re-typed anyway.
                text = text2 = numberText = numberText2 = string.Empty;
                OnPropertyChanged(nameof(Text));
                OnPropertyChanged(nameof(Text2));
                OnPropertyChanged(nameof(NumberText));
                OnPropertyChanged(nameof(NumberText2));
                LoadFieldState(null);
                Operator = Operators[0].Operator;
                OnPropertyChanged(nameof(Operators));
                RaiseEditorChanged();
            }
        }

        public IReadOnlyList<RuleOperatorChoice> Operators { get; private set; }

        private RuleOperator op;
        public RuleOperator Operator
        {
            get => op;
            set
            {
                SetValue(ref op, value);
                RaiseEditorChanged();
            }
        }

        private string text = string.Empty;
        public string Text { get => text; set => SetEditorValue(ref text, value); }

        private string text2 = string.Empty;
        public string Text2 { get => text2; set => SetEditorValue(ref text2, value); }

        private string numberText = string.Empty;
        public string NumberText { get => numberText; set => SetEditorValue(ref numberText, value); }

        private string numberText2 = string.Empty;
        public string NumberText2 { get => numberText2; set => SetEditorValue(ref numberText2, value); }

        public ObservableCollection<RuleOptionItem> SelectedItems { get; } = new ObservableCollection<RuleOptionItem>();

        private ListCollectionView optionsView;
        public ListCollectionView OptionsView { get => optionsView; private set => SetValue(ref optionsView, value); }

        private string optionSearch = string.Empty;
        public string OptionSearch
        {
            get => optionSearch;
            set
            {
                SetValue(ref optionSearch, value ?? string.Empty);
                OptionsView?.Refresh();
            }
        }

        public bool HasNoOptions => OptionsView == null || OptionsView.SourceCollection.Cast<object>().FirstOrDefault() == null;

        private RuleValueKind Kind => RuleEngine.GetValueKind(field);
        private bool IsDayCount => op == RuleOperator.InLastDays || op == RuleOperator.OlderThanDays;
        private bool NeedsValue => op != RuleOperator.IsEmpty;

        public bool ShowText => Kind == RuleValueKind.Text;
        public bool ShowItems => Kind == RuleValueKind.Items && NeedsValue;
        public bool ShowDate => Kind == RuleValueKind.Date && NeedsValue && !IsDayCount;
        public bool ShowDate2 => ShowDate && op == RuleOperator.Between;
        public bool ShowNumber => (Kind == RuleValueKind.Number && NeedsValue) || (Kind == RuleValueKind.Date && IsDayCount);
        public bool ShowNumber2 => Kind == RuleValueKind.Number && op == RuleOperator.Between;

        public string UnitText
        {
            get
            {
                if (IsDayCount)
                {
                    return Loc.Get("LOCGameCollections_RuleUnitDays");
                }

                switch (field)
                {
                    case RuleField.TimePlayed: return Loc.Get("LOCGameCollections_RuleUnitHours");
                    case RuleField.PlayCount: return Loc.Get("LOCGameCollections_RuleUnitTimes");
                    case RuleField.UserScore:
                    case RuleField.CriticScore:
                    case RuleField.CommunityScore: return Loc.Get("LOCGameCollections_RuleUnitScore");
                    default: return null;
                }
            }
        }

        /// <summary>
        /// Explains a value that was typed but can't be used. Values not filled in yet aren't errors.
        /// </summary>
        public string ValueError
        {
            get
            {
                if (ShowDate)
                {
                    var hasBadDate = (text.Trim().Length > 0 && !RuleEngine.TryParseDatePeriod(text, out _, out _)) ||
                        (ShowDate2 && text2.Trim().Length > 0 && !RuleEngine.TryParseDatePeriod(text2, out _, out _));
                    if (hasBadDate)
                    {
                        return Loc.Get("LOCGameCollections_RuleErrorDate");
                    }

                    return ShowDate2 && text.Trim().Length > 0 && text2.Trim().Length > 0 && !RuleEngine.IsComplete(ToModel())
                        ? Loc.Get("LOCGameCollections_RuleErrorRange")
                        : null;
                }

                if (ShowNumber)
                {
                    var hasBadNumber = (numberText.Trim().Length > 0 && !IsValidNumber(numberText)) ||
                        (ShowNumber2 && numberText2.Trim().Length > 0 && !IsValidNumber(numberText2));
                    if (hasBadNumber)
                    {
                        return Loc.Get("LOCGameCollections_RuleErrorNumber");
                    }

                    return ShowNumber2 && numberText.Trim().Length > 0 && numberText2.Trim().Length > 0 && !RuleEngine.IsComplete(ToModel())
                        ? Loc.Get("LOCGameCollections_RuleErrorRange")
                        : null;
                }

                return null;
            }
        }

        public string ItemsButtonText => SelectedItems.Count == 0
            ? Loc.Get("LOCGameCollections_RuleChooseValues")
            : Loc.Get("LOCGameCollections_RuleChangeValues");

        public bool IsComplete => RuleEngine.IsComplete(ToModel());

        public RelayCommand RemoveCommand { get; }

        public RuleConditionViewModel(RuleBlockViewModel block, RuleText ruleText, RuleCondition source, Action changed)
        {
            Block = block;
            this.ruleText = ruleText;
            this.changed = changed;
            RemoveCommand = new RelayCommand(() => block.RemoveCondition(this));

            field = source?.Field ?? RuleField.Platform;
            LoadFieldState(source?.ItemIds);
            op = source != null && Operators.Any(o => o.Operator == source.Operator) ? source.Operator : Operators[0].Operator;
            if (source != null)
            {
                text = source.Text ?? string.Empty;
                text2 = source.Text2 ?? string.Empty;
                numberText = source.Number?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
                numberText2 = source.Number2?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
            }
        }

        public RuleCondition ToModel()
        {
            var condition = new RuleCondition { Field = field, Operator = op };
            if (ShowText || ShowDate)
            {
                condition.Text = text.Trim();
            }

            if (ShowDate2)
            {
                condition.Text2 = text2.Trim();
            }

            if (ShowNumber)
            {
                condition.Number = ParseNumber(numberText);
            }

            if (ShowNumber2)
            {
                condition.Number2 = ParseNumber(numberText2);
            }

            if (ShowItems)
            {
                condition.ItemIds = SelectedItems.Select(i => i.Id).ToList();
            }

            return condition;
        }

        private void LoadFieldState(IList<Guid> selectedIds)
        {
            Operators = RuleEngine.GetOperators(field).Select(o => new RuleOperatorChoice(field, o)).ToList();
            SelectedItems.Clear();
            optionSearch = string.Empty;
            OnPropertyChanged(nameof(OptionSearch));

            if (Kind != RuleValueKind.Items)
            {
                OptionsView = null;
                return;
            }

            var items = ruleText.GetOptions(field).Select(o => new RuleOptionItem(o, OnOptionSelectionChanged)).ToList();
            // Selections referring to a deleted tag or genre are dropped here, as there is nothing left to show for them.
            if (selectedIds != null)
            {
                var selected = new HashSet<Guid>(selectedIds);
                foreach (var item in items.Where(i => selected.Contains(i.Id)))
                {
                    item.IsSelected = true;
                }
            }

            OptionsView = new ListCollectionView(items) { Filter = FilterOption };
            OnPropertyChanged(nameof(HasNoOptions));
        }

        private bool FilterOption(object obj)
        {
            var query = optionSearch.Trim();
            return query.Length == 0 || ((RuleOptionItem)obj).Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private void OnOptionSelectionChanged(RuleOptionItem item)
        {
            if (item.IsSelected)
            {
                SelectedItems.Add(item);
            }
            else
            {
                SelectedItems.Remove(item);
            }

            OnPropertyChanged(nameof(ItemsButtonText));
            changed?.Invoke();
        }

        private void SetEditorValue(ref string target, string value)
        {
            SetValue(ref target, value ?? string.Empty);
            OnPropertyChanged(nameof(ValueError));
            changed?.Invoke();
        }

        private void RaiseEditorChanged()
        {
            OnPropertyChanged(nameof(ShowText));
            OnPropertyChanged(nameof(ShowItems));
            OnPropertyChanged(nameof(ShowDate));
            OnPropertyChanged(nameof(ShowDate2));
            OnPropertyChanged(nameof(ShowNumber));
            OnPropertyChanged(nameof(ShowNumber2));
            OnPropertyChanged(nameof(UnitText));
            OnPropertyChanged(nameof(ValueError));
            OnPropertyChanged(nameof(ItemsButtonText));
            changed?.Invoke();
        }

        private static bool IsValidNumber(string value)
        {
            return ParseNumber(value) >= 0;
        }

        private static double? ParseNumber(string value)
        {
            return double.TryParse(value?.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out var number) && !double.IsNaN(number) && !double.IsInfinity(number)
                ? number
                : (double?)null;
        }
    }

    /// <summary>
    /// A group of conditions that must all be true.
    /// </summary>
    public class RuleBlockViewModel : ObservableObject
    {
        private readonly DynamicRulesViewModel owner;
        private readonly RuleText ruleText;

        public ObservableCollection<RuleConditionViewModel> Conditions { get; } = new ObservableCollection<RuleConditionViewModel>();

        private BlockJoin join;
        public BlockJoin Join
        {
            get => join;
            set
            {
                SetValue(ref join, value);
                OnPropertyChanged(nameof(IsAnd));
                OnPropertyChanged(nameof(IsOr));
                owner.OnRulesChanged();
            }
        }

        public bool IsAnd { get => join == BlockJoin.And; set { if (value) { Join = BlockJoin.And; } } }
        public bool IsOr { get => join == BlockJoin.Or; set { if (value) { Join = BlockJoin.Or; } } }

        // Radio buttons are grouped by name, and each block's pair is its own group.
        public string JoinGroupName { get; } = Guid.NewGuid().ToString("N");

        private bool isFirst;
        public bool IsFirst { get => isFirst; set => SetValue(ref isFirst, value); }

        private string title;
        public string Title { get => title; set => SetValue(ref title, value); }

        public RelayCommand AddConditionCommand { get; }
        public RelayCommand RemoveCommand { get; }

        public RuleBlockViewModel(DynamicRulesViewModel owner, RuleText ruleText, RuleBlock source)
        {
            this.owner = owner;
            this.ruleText = ruleText;
            join = source?.Join ?? BlockJoin.And;
            AddConditionCommand = new RelayCommand(() => AddCondition(null));
            RemoveCommand = new RelayCommand(() => owner.RemoveBlock(this));

            if (source?.Conditions != null)
            {
                foreach (var condition in source.Conditions)
                {
                    Conditions.Add(new RuleConditionViewModel(this, ruleText, condition, owner.OnRulesChanged));
                }
            }

            if (Conditions.Count == 0)
            {
                Conditions.Add(new RuleConditionViewModel(this, ruleText, null, owner.OnRulesChanged));
            }
        }

        public void AddCondition(RuleCondition source)
        {
            Conditions.Add(new RuleConditionViewModel(this, ruleText, source, owner.OnRulesChanged));
            owner.OnRulesChanged();
        }

        /// <summary>
        /// A block always keeps one condition: removing the last one removes the block, or resets
        /// it when it is the only block, so the builder is never left with nothing to fill in.
        /// </summary>
        public void RemoveCondition(RuleConditionViewModel condition)
        {
            Conditions.Remove(condition);
            if (Conditions.Count == 0)
            {
                if (owner.Blocks.Count > 1)
                {
                    owner.RemoveBlock(this);
                    return;
                }

                Conditions.Add(new RuleConditionViewModel(this, ruleText, null, owner.OnRulesChanged));
            }

            owner.OnRulesChanged();
        }

        public RuleBlock ToModel()
        {
            return new RuleBlock { Join = join, Conditions = Conditions.Select(c => c.ToModel()).ToList() };
        }
    }

    /// <summary>
    /// The rule builder of a dynamic collection, with a live preview of the matching games.
    /// </summary>
    public class DynamicRulesViewModel : ObservableObject
    {
        private const int PreviewNameCount = 6;

        private readonly IPlayniteAPI playniteApi;
        private readonly RuleText ruleText;
        private readonly DispatcherTimer previewTimer;

        public ObservableCollection<RuleBlockViewModel> Blocks { get; } = new ObservableCollection<RuleBlockViewModel>();

        public event EventHandler RulesChanged;

        public bool IsComplete => RuleEngine.IsComplete(ToModel());

        private int matchCount;
        public int MatchCount { get => matchCount; private set => SetValue(ref matchCount, value); }

        private string previewText;
        public string PreviewText { get => previewText; private set => SetValue(ref previewText, value); }

        private string previewNames;
        public string PreviewNames { get => previewNames; private set => SetValue(ref previewNames, value); }

        public RelayCommand AddBlockCommand { get; }

        public DynamicRulesViewModel(IPlayniteAPI playniteApi, DynamicRules source)
        {
            this.playniteApi = playniteApi;
            ruleText = new RuleText(playniteApi);
            AddBlockCommand = new RelayCommand(AddBlock);

            // Waits for a pause in typing, so the library isn't searched on every keystroke.
            previewTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
            previewTimer.Tick += (s, e) =>
            {
                previewTimer.Stop();
                UpdatePreview();
            };

            if (source?.Blocks != null)
            {
                foreach (var block in source.Blocks)
                {
                    Blocks.Add(new RuleBlockViewModel(this, ruleText, block));
                }
            }

            if (Blocks.Count == 0)
            {
                Blocks.Add(new RuleBlockViewModel(this, ruleText, null));
            }

            UpdateBlockTitles();
            UpdatePreview();
        }

        public DynamicRules ToModel()
        {
            return new DynamicRules { Blocks = Blocks.Select(b => b.ToModel()).ToList() };
        }

        public void OnRulesChanged()
        {
            previewTimer?.Stop();
            previewTimer?.Start();
            OnPropertyChanged(nameof(IsComplete));
            RulesChanged?.Invoke(this, EventArgs.Empty);
        }

        public void RemoveBlock(RuleBlockViewModel block)
        {
            if (Blocks.Count > 1)
            {
                Blocks.Remove(block);
                UpdateBlockTitles();
                OnRulesChanged();
            }
        }

        public void UpdatePreview()
        {
            var rules = ToModel();
            if (!RuleEngine.IsComplete(rules))
            {
                MatchCount = 0;
                PreviewText = Loc.Get("LOCGameCollections_RulePreviewIncomplete");
                PreviewNames = null;
                return;
            }

            var isMatch = RuleEngine.Compile(rules, DateTime.Now);
            var matches = playniteApi.Database.Games.Where(isMatch).ToList();
            MatchCount = matches.Count;
            PreviewText = Loc.Format("LOCGameCollections_RulePreviewCount", Loc.GameCount(matches.Count));
            PreviewNames = matches.Count == 0
                ? null
                : string.Join(", ", matches.Select(g => g.Name).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).Take(PreviewNameCount)) +
                    (matches.Count > PreviewNameCount ? "…" : string.Empty);
        }

        private void AddBlock()
        {
            Blocks.Add(new RuleBlockViewModel(this, ruleText, null));
            UpdateBlockTitles();
            OnRulesChanged();
        }

        private void UpdateBlockTitles()
        {
            for (var i = 0; i < Blocks.Count; i++)
            {
                Blocks[i].IsFirst = i == 0;
                Blocks[i].Title = Loc.Format("LOCGameCollections_RuleBlockTitle", i + 1);
            }
        }
    }
}

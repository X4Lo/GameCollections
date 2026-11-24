using GameCollections.Models;
using GameCollections.Services;
using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace GameCollections.Settings
{
    /// <summary>
    /// Which version of the extension's icon the sidebar shows.
    /// </summary>
    public enum SidebarIconStyle
    {
        Colored = 0,
        White = 1,
        Black = 2,
        Theme = 3
    }

    /// <summary>
    /// A choice in one of the settings view's dropdowns.
    /// </summary>
    public class SettingOption
    {
        public object Value { get; }
        public string Label { get; }

        public SettingOption(object value, string label)
        {
            Value = value;
            Label = label;
        }
    }

    /// <summary>
    /// A common cover shape that fills both aspect ratio fields at once.
    /// </summary>
    public class AspectPreset
    {
        public string Name { get; }
        public double Width { get; }
        public double Height { get; }
        public string Label => $"{Name} ({Width}:{Height})";

        public AspectPreset(string name, double width, double height)
        {
            Name = name;
            Width = width;
            Height = height;
        }

        public bool Matches(double width, double height)
        {
            return Math.Abs(width - Width) < 1e-9 && Math.Abs(height - Height) < 1e-9;
        }
    }

    public class GameCollectionsSettings : ObservableObject
    {
        // Ratios outside this range would make cards absurdly thin or tall.
        private const double MinAspectRatio = 0.25;
        private const double MaxAspectRatio = 4;
        private const double DefaultAspectRatio = 1.5;

        private SidebarIconStyle sidebarIcon = SidebarIconStyle.Theme;
        public SidebarIconStyle SidebarIcon { get => sidebarIcon; set => SetValue(ref sidebarIcon, value); }

        // Shape of collection posters on the Collections page, as width : height, and how images fill it.
        private double collectionPosterAspectWidth = 2;
        public double CollectionPosterAspectWidth { get => collectionPosterAspectWidth; set => SetValue(ref collectionPosterAspectWidth, value); }

        private double collectionPosterAspectHeight = 3;
        public double CollectionPosterAspectHeight { get => collectionPosterAspectHeight; set => SetValue(ref collectionPosterAspectHeight, value); }

        private Stretch collectionPosterStretch = Stretch.UniformToFill;
        public Stretch CollectionPosterStretch { get => collectionPosterStretch; set => SetValue(ref collectionPosterStretch, value); }

        // The same for game covers on a collection's page.
        private double gameCoverAspectWidth = 2;
        public double GameCoverAspectWidth { get => gameCoverAspectWidth; set => SetValue(ref gameCoverAspectWidth, value); }

        private double gameCoverAspectHeight = 3;
        public double GameCoverAspectHeight { get => gameCoverAspectHeight; set => SetValue(ref gameCoverAspectHeight, value); }

        private Stretch gameCoverStretch = Stretch.UniformToFill;
        public Stretch GameCoverStretch { get => gameCoverStretch; set => SetValue(ref gameCoverStretch, value); }

        /// <summary>
        /// Height of a collection poster per unit of width.
        /// </summary>
        [DontSerialize]
        public double CollectionPosterRatio => Ratio(collectionPosterAspectWidth, collectionPosterAspectHeight);

        /// <summary>
        /// Height of a game cover per unit of width.
        /// </summary>
        [DontSerialize]
        public double GameCoverRatio => Ratio(gameCoverAspectWidth, gameCoverAspectHeight);

        private static double Ratio(double width, double height)
        {
            if (!IsValidAspectValue(width) || !IsValidAspectValue(height))
            {
                return DefaultAspectRatio;
            }

            return Math.Max(MinAspectRatio, Math.Min(MaxAspectRatio, height / width));
        }

        public static bool IsValidAspectValue(double value)
        {
            return value > 0 && !double.IsInfinity(value) && !double.IsNaN(value);
        }

        // Remembered UI state rather than a user-facing option, so it is not shown in the settings view.
        private CollectionsViewMode viewMode = CollectionsViewMode.Grid;
        public CollectionsViewMode ViewMode { get => viewMode; set => SetValue(ref viewMode, value); }

        // Also remembered UI state: the collection page's header and the size of its game cards.
        private bool collectionHeaderCollapsed;
        public bool CollectionHeaderCollapsed { get => collectionHeaderCollapsed; set => SetValue(ref collectionHeaderCollapsed, value); }

        private double gameCardWidth = 130;
        public double GameCardWidth { get => gameCardWidth; set => SetValue(ref gameCardWidth, value); }

        private bool showDynamicCollectionsInDetails = true;
        public bool ShowDynamicCollectionsInDetails { get => showDynamicCollectionsInDetails; set => SetValue(ref showDynamicCollectionsInDetails, value); }

        private bool addDetailsListToAnyTheme = true;
        public bool AddDetailsListToAnyTheme { get => addDetailsListToAnyTheme; set => SetValue(ref addDetailsListToAnyTheme, value); }

        private bool useThemeSpecificPlacement;
        public bool UseThemeSpecificPlacement { get => useThemeSpecificPlacement; set => SetValue(ref useThemeSpecificPlacement, value); }

        // For themes, which can bind to this to lay out their details view around the list.
        private bool isDetailsControlVisible;
        [DontSerialize]
        public bool IsDetailsControlVisible { get => isDetailsControlVisible; set => SetValue(ref isDetailsControlVisible, value); }
    }

    public class GameCollectionsSettingsViewModel : ObservableObject, ISettings
    {
        private readonly GameCollectionsPlugin plugin;
        private GameCollectionsSettings editingClone;

        public event EventHandler SettingsChanged;

        private GameCollectionsSettings settings;
        public GameCollectionsSettings Settings
        {
            get => settings;
            set
            {
                settings = value;
                OnPropertyChanged();
            }
        }

        public IReadOnlyList<SettingOption> SidebarIconOptions { get; } = new List<SettingOption>
        {
            new SettingOption(SidebarIconStyle.Colored, Loc.Get("LOCGameCollections_SettingIconColored")),
            new SettingOption(SidebarIconStyle.Theme, Loc.Get("LOCGameCollections_SettingIconTheme")),
            new SettingOption(SidebarIconStyle.White, Loc.Get("LOCGameCollections_SettingIconWhite")),
            new SettingOption(SidebarIconStyle.Black, Loc.Get("LOCGameCollections_SettingIconBlack"))
        };

        public IReadOnlyList<SettingOption> StretchOptions { get; } = new List<SettingOption>
        {
            new SettingOption(Stretch.None, Loc.Get("LOCGameCollections_StretchNone")),
            new SettingOption(Stretch.Fill, Loc.Get("LOCGameCollections_StretchFill")),
            new SettingOption(Stretch.Uniform, Loc.Get("LOCGameCollections_StretchUniform")),
            new SettingOption(Stretch.UniformToFill, Loc.Get("LOCGameCollections_StretchUniformToFill"))
        };

        public IReadOnlyList<AspectPreset> AspectPresets => Presets;

        public static readonly IReadOnlyList<AspectPreset> Presets = new List<AspectPreset>
        {
            new AspectPreset(Loc.Get("LOCGameCollections_PresetDvdBox"), 27, 38),
            new AspectPreset(Loc.Get("LOCGameCollections_PresetEpic"), 3, 4),
            new AspectPreset(Loc.Get("LOCGameCollections_PresetGog"), 22, 31),
            new AspectPreset(Loc.Get("LOCGameCollections_PresetIgdb"), 3, 4),
            new AspectPreset(Loc.Get("LOCGameCollections_PresetSquare"), 1, 1),
            new AspectPreset(Loc.Get("LOCGameCollections_PresetSteamBanner"), 92, 43),
            new AspectPreset(Loc.Get("LOCGameCollections_PresetSteamCover"), 2, 3),
            new AspectPreset(Loc.Get("LOCGameCollections_PresetTwitch"), 3, 4)
        };

        public GameCollectionsSettingsViewModel(GameCollectionsPlugin plugin)
        {
            // The plugin instance is required for Save/Load because Playnite stores data per plugin.
            this.plugin = plugin;
            Settings = plugin.LoadPluginSettings<GameCollectionsSettings>() ?? new GameCollectionsSettings();
        }

        public void Save()
        {
            plugin.SavePluginSettings(Settings);
        }

        public void BeginEdit()
        {
            editingClone = Serialization.GetClone(Settings);
        }

        public void CancelEdit()
        {
            Settings = editingClone;
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void EndEdit()
        {
            Save();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            if (!GameCollectionsSettings.IsValidAspectValue(Settings.CollectionPosterAspectWidth) ||
                !GameCollectionsSettings.IsValidAspectValue(Settings.CollectionPosterAspectHeight))
            {
                errors.Add(Loc.Get("LOCGameCollections_SettingCollectionAspectInvalid"));
            }

            if (!GameCollectionsSettings.IsValidAspectValue(Settings.GameCoverAspectWidth) ||
                !GameCollectionsSettings.IsValidAspectValue(Settings.GameCoverAspectHeight))
            {
                errors.Add(Loc.Get("LOCGameCollections_SettingGameAspectInvalid"));
            }

            return errors.Count == 0;
        }
    }
}

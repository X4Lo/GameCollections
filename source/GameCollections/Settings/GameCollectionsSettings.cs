using GameCollections.Models;
using Playnite.SDK;
using Playnite.SDK.Data;
using System.Collections.Generic;

namespace GameCollections.Settings
{
    public class GameCollectionsSettings : ObservableObject
    {
        // Remembered UI state rather than a user-facing option, so it is not shown in the settings view.
        private CollectionsViewMode viewMode = CollectionsViewMode.Grid;
        public CollectionsViewMode ViewMode { get => viewMode; set => SetValue(ref viewMode, value); }
    }

    public class GameCollectionsSettingsViewModel : ObservableObject, ISettings
    {
        private readonly GameCollectionsPlugin plugin;
        private GameCollectionsSettings editingClone;

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
        }

        public void EndEdit()
        {
            Save();
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            return true;
        }
    }
}

using GameCollections.Models;
using GameCollections.Services;
using GameCollections.Settings;
using Playnite.SDK;
using Playnite.SDK.Controls;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace GameCollections.Views
{
    /// <summary>
    /// A collection shown in the game details view.
    /// </summary>
    public class DetailsCollectionItem : ObservableObject
    {
        private const int PosterDecodeWidth = 170;
        private static readonly ILogger logger = LogManager.GetLogger();

        public string Title { get; }
        public bool IsDynamic { get; }
        public RelayCommand OpenCommand { get; }

        private ImageSource poster;
        public ImageSource Poster { get => poster; private set => SetValue(ref poster, value); }

        public DetailsCollectionItem(GameCollection collection, string posterPath, Action<Guid> open)
        {
            Title = collection.Title;
            IsDynamic = collection.IsDynamic;
            var id = collection.Id;
            OpenCommand = new RelayCommand(() => open(id));
            _ = LoadPosterAsync(posterPath);
        }

        private async Task LoadPosterAsync(string path)
        {
            try
            {
                Poster = await ImageLoader.LoadAsync(path, PosterDecodeWidth);
            }
            catch (Exception e)
            {
                logger.Error(e, $"Failed to load poster for collection {Title}.");
            }
        }
    }

    /// <summary>
    /// Lists the collections of the selected game, in themes that place this custom element.
    /// Playnite creates one instance per place the theme uses it and supplies the game through the game context.
    /// </summary>
    public partial class GameCollectionsControl : PluginUserControl
    {
        private readonly CollectionManager manager;
        private readonly GameCollectionsSettingsViewModel settings;
        private readonly Action<Guid> openCollection;

        private bool isInjected;

        /// <summary>
        /// True for the instance this extension added itself, rather than one placed by a theme. It lasts for
        /// the whole session and may sit in an unselected tab, which is never loaded, so it stays subscribed.
        /// </summary>
        public bool IsInjected
        {
            get => isInjected;
            set
            {
                isInjected = value;
                if (value)
                {
                    Subscribe();
                }
            }
        }

        /// <summary>
        /// Replaces the header with elements made to match the theme. With none, only the list remains, for
        /// when something around it, such as a tab, already names it.
        /// </summary>
        public void SetHeader(params UIElement[] elements)
        {
            HeaderHost.Children.Clear();
            foreach (var element in elements)
            {
                HeaderHost.Children.Add(element);
            }
        }

        public GameCollectionsControl(CollectionManager manager, GameCollectionsSettingsViewModel settings, Action<Guid> openCollection)
        {
            this.manager = manager;
            this.settings = settings;
            this.openCollection = openCollection;
            InitializeComponent();
            Visibility = Visibility.Collapsed;

            // Subscribed only while shown, so a control the theme discards doesn't stay alive through these events.
            Loaded += (s, e) =>
            {
                Subscribe();
                UpdateItems();
            };
            Unloaded += (s, e) =>
            {
                if (!IsInjected)
                {
                    Unsubscribe();
                }
            };
        }

        private void Subscribe()
        {
            // Removing first keeps this safe to call more than once.
            Unsubscribe();
            manager.CollectionsChanged += Manager_CollectionsChanged;
            settings.SettingsChanged += Settings_SettingsChanged;
        }

        private void Unsubscribe()
        {
            manager.CollectionsChanged -= Manager_CollectionsChanged;
            settings.SettingsChanged -= Settings_SettingsChanged;
        }

        public override void GameContextChanged(Game oldContext, Game newContext)
        {
            UpdateItems();
        }

        private void Manager_CollectionsChanged(object sender, CollectionsChangedEventArgs e)
        {
            UpdateItems();
        }

        private void Settings_SettingsChanged(object sender, EventArgs e)
        {
            UpdateItems();
        }

        private void UpdateItems()
        {
            var game = GameContext;
            var collections = game == null
                ? new List<GameCollection>()
                : manager.GetCollectionsForGame(game.Id)
                    .Where(c => !c.IsDynamic || settings.Settings.ShowDynamicCollectionsInDetails)
                    .ToList();

            ItemsList.ItemsSource = collections
                .Select(c => new DetailsCollectionItem(c, manager.ResolvePosterPath(c), openCollection))
                .ToList();

            var hasItems = collections.Count > 0;
            Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
            settings.Settings.IsDetailsControlVisible = hasItems;
        }
    }
}

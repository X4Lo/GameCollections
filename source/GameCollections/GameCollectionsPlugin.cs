using GameCollections.Services;
using GameCollections.Settings;
using GameCollections.ViewModels;
using GameCollections.Views;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Controls;

namespace GameCollections
{
    public class GameCollectionsPlugin : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly GameCollectionsSettingsViewModel settings;
        private readonly CollectionManager manager;
        private readonly DialogService dialogs;
        private CollectionsRootViewModel rootViewModel;

        public override Guid Id { get; } = Guid.Parse("3f6b8d2e-5c41-4a7e-9e0b-6d2f1a8c7b54");

        public GameCollectionsPlugin(IPlayniteAPI api) : base(api)
        {
            settings = new GameCollectionsSettingsViewModel(this);
            Properties = new GenericPluginProperties
            {
                HasSettings = true
            };

            var dataDirectory = GetPluginUserDataPath();
            manager = new CollectionManager(
                api,
                new CollectionRepository(dataDirectory),
                new PosterStorage(Path.Combine(dataDirectory, "Posters")));
            dialogs = new DialogService(api, manager);
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            // Loading happens here rather than in the constructor because the game database
            // is only guaranteed to be available once Playnite has started.
            try
            {
                var warning = manager.Load();
                if (warning != null)
                {
                    PlayniteApi.Notifications.Add(new NotificationMessage("GameCollections_LoadWarning", warning, NotificationType.Error));
                }

                manager.RemoveMissingGames();
                manager.DeleteUnreferencedPosters();
            }
            catch (Exception e)
            {
                logger.Error(e, "Failed to load collections.");
                PlayniteApi.Notifications.Add(new NotificationMessage(
                    "GameCollections_LoadError",
                    Loc.Format("LOCGameCollections_ErrorLoadFailed", e.Message),
                    NotificationType.Error));
            }

            PlayniteApi.Database.Games.ItemCollectionChanged += Games_ItemCollectionChanged;
            PlayniteApi.Database.Games.ItemUpdated += Games_ItemUpdated;
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            PlayniteApi.Database.Games.ItemCollectionChanged -= Games_ItemCollectionChanged;
            PlayniteApi.Database.Games.ItemUpdated -= Games_ItemUpdated;
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            yield return new GameMenuItem
            {
                Description = Loc.Get("LOCGameCollections_MenuAddToCollection"),
                Icon = Path.Combine(GetPluginDirectory(), "icon.png"),
                Action = menuArgs =>
                {
                    try
                    {
                        dialogs.ShowAddToCollection(menuArgs.Games);
                    }
                    catch (Exception e)
                    {
                        logger.Error(e, "Failed to open the Add to Collection dialog.");
                        PlayniteApi.Dialogs.ShowErrorMessage(e.Message, Loc.Get("LOCGameCollections_PluginName"));
                    }
                }
            };
        }

        public override IEnumerable<SidebarItem> GetSidebarItems()
        {
            yield return new SidebarItem
            {
                Title = Loc.Get("LOCGameCollections_SidebarTitle"),
                Type = SiderbarItemType.View,
                Icon = Path.Combine(GetPluginDirectory(), "icon.png"),
                // A new view is built on every open, while the view model (current page, search,
                // sort) persists so the user returns to where they left off.
                Opened = () =>
                {
                    if (rootViewModel == null)
                    {
                        rootViewModel = new CollectionsRootViewModel(PlayniteApi, manager, dialogs, settings);
                    }

                    return new CollectionsRootView { DataContext = rootViewModel };
                }
            };
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return settings;
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new GameCollectionsSettingsView();
        }

        private void Games_ItemCollectionChanged(object sender, ItemCollectionChangedEventArgs<Game> e)
        {
            if (e.RemovedItems == null || e.RemovedItems.Count == 0)
            {
                return;
            }

            var removedIds = e.RemovedItems.Select(g => g.Id).ToList();
            RunOnUiThread(() => manager.RemoveGamesFromAllCollections(removedIds));
        }

        private void Games_ItemUpdated(object sender, ItemUpdatedEventArgs<Game> e)
        {
            if (rootViewModel == null || e.UpdatedItems == null || e.UpdatedItems.Count == 0)
            {
                return;
            }

            var updatedIds = new HashSet<Guid>(e.UpdatedItems.Select(u => u.NewData.Id));
            RunOnUiThread(() => rootViewModel.OnGamesUpdated(updatedIds));
        }

        private void RunOnUiThread(Action action)
        {
            void SafeAction()
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    logger.Error(e, "Failed to process library change.");
                }
            }

            var dispatcher = PlayniteApi.MainView.UIDispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                SafeAction();
            }
            else
            {
                dispatcher.BeginInvoke((Action)SafeAction);
            }
        }

        private static string GetPluginDirectory()
        {
            return Path.GetDirectoryName(typeof(GameCollectionsPlugin).Assembly.Location);
        }
    }
}

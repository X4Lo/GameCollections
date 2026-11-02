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
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace GameCollections
{
    public class GameCollectionsPlugin : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private const string SourceName = "GameCollections";
        private const string CollectionsControlName = "CollectionsControl";

        private readonly GameCollectionsSettingsViewModel settings;
        private readonly CollectionManager manager;
        private readonly DialogService dialogs;
        private DynamicCollectionRefresher refresher;
        private DetailsViewIntegration detailsIntegration;
        private CollectionsRootViewModel rootViewModel;
        private SidebarItem sidebarItem;

        public GameCollectionsSettingsViewModel Settings => settings;

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

            AddSettingsSupport(new AddSettingsSupportArgs
            {
                SourceName = SourceName,
                SettingsRoot = $"{nameof(Settings)}.{nameof(Settings.Settings)}"
            });

            AddCustomElementSupport(new AddCustomElementSupportArgs
            {
                ElementList = new List<string> { CollectionsControlName },
                SourceName = SourceName
            });
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

            RunOnUiThread(() =>
            {
                refresher = new DynamicCollectionRefresher(PlayniteApi, manager, PlayniteApi.MainView.UIDispatcher ?? Dispatcher.CurrentDispatcher);
                refresher.Start();

                if (PlayniteApi.ApplicationInfo.Mode == ApplicationMode.Desktop)
                {
                    detailsIntegration = new DetailsViewIntegration(
                        PlayniteApi,
                        settings,
                        () => new GameCollectionsControl(manager, settings, OpenCollectionInWindow),
                        () => Application.Current?.MainWindow,
                        PlayniteApi.MainView.UIDispatcher ?? Dispatcher.CurrentDispatcher);
                    detailsIntegration.Start();
                }
            });
        }

        public override void OnGameSelected(OnGameSelectedEventArgs args)
        {
            var games = args.NewValue;
            RunOnUiThread(() => detailsIntegration?.OnGameSelected(games));
        }

        public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args)
        {
            RunOnUiThread(() => refresher?.ScheduleFullRefresh(TimeSpan.FromSeconds(1)));
        }

        public override Control GetGameViewControl(GetGameViewControlArgs args)
        {
            // The control relies on Desktop mode's theme styles, and collections are a Desktop mode feature.
            if (args.Name != CollectionsControlName || PlayniteApi.ApplicationInfo.Mode != ApplicationMode.Desktop)
            {
                return null;
            }

            return new GameCollectionsControl(manager, settings, OpenCollectionInWindow);
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            PlayniteApi.Database.Games.ItemCollectionChanged -= Games_ItemCollectionChanged;
            PlayniteApi.Database.Games.ItemUpdated -= Games_ItemUpdated;
            RunOnUiThread(() =>
            {
                refresher?.Stop();
                detailsIntegration?.Stop();
            });
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
            sidebarItem = sidebarItem ?? new SidebarItem
            {
                Title = Loc.Get("LOCGameCollections_SidebarTitle"),
                Type = SiderbarItemType.View,
                Icon = Path.Combine(GetPluginDirectory(), "icon.png"),
                // A new view is built on every open, while the view model (current page, search,
                // sort) persists so the user returns to where they left off.
                Opened = () =>
                {
                    return new CollectionsRootView { DataContext = GetRootViewModel() };
                }
            };

            yield return sidebarItem;
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
            if (e.RemovedItems != null && e.RemovedItems.Count > 0)
            {
                var removedIds = e.RemovedItems.Select(g => g.Id).ToList();
                RunOnUiThread(() => manager.RemoveGamesFromAllCollections(removedIds));
            }

            if (e.AddedItems != null && e.AddedItems.Count > 0)
            {
                var addedIds = e.AddedItems.Select(g => g.Id).ToList();
                RunOnUiThread(() => refresher?.NotifyGamesChanged(addedIds));
            }
        }

        private void Games_ItemUpdated(object sender, ItemUpdatedEventArgs<Game> e)
        {
            if (e.UpdatedItems == null || e.UpdatedItems.Count == 0)
            {
                return;
            }

            var updatedIds = new HashSet<Guid>(e.UpdatedItems.Select(u => u.NewData.Id));
            RunOnUiThread(() =>
            {
                rootViewModel?.OnGamesUpdated(updatedIds);
                refresher?.NotifyGamesChanged(updatedIds);
            });
        }

        private CollectionsRootViewModel GetRootViewModel()
        {
            if (rootViewModel == null)
            {
                rootViewModel = new CollectionsRootViewModel(PlayniteApi, manager, dialogs, settings)
                {
                    SwitchToSidebarView = SwitchToSidebarView
                };
            }

            return rootViewModel;
        }

        private void SwitchToSidebarView(Guid collectionId)
        {
            // Queued so the collection's modal window has finished closing before the main view changes.
            (PlayniteApi.MainView.UIDispatcher ?? Dispatcher.CurrentDispatcher).BeginInvoke((Action)(() =>
            {
                if (!SidebarNavigator.TryActivate(Application.Current?.MainWindow, sidebarItem))
                {
                    // The page is already showing in the sidebar view, so one click gets there.
                    PlayniteApi.Notifications.Add(new NotificationMessage(
                        "GameCollections_OpenManually",
                        Loc.Format("LOCGameCollections_OpenCollectionsManually", manager.GetCollection(collectionId)?.Title),
                        NotificationType.Info));
                }
            }));
        }

        private void OpenCollectionInWindow(Guid collectionId)
        {
            try
            {
                GetRootViewModel().OpenCollectionInWindow(collectionId);
            }
            catch (Exception e)
            {
                logger.Error(e, $"Failed to open collection {collectionId}.");
                PlayniteApi.Dialogs.ShowErrorMessage(e.Message, Loc.Get("LOCGameCollections_PluginName"));
            }
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

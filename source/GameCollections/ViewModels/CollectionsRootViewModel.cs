using GameCollections.Services;
using GameCollections.Settings;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace GameCollections.ViewModels
{
    /// <summary>
    /// Navigation state of the Collections sidebar view. It outlives the view itself, so the open
    /// page, search text and sort choices are kept when the user leaves and comes back.
    /// </summary>
    public class CollectionsRootViewModel : ObservableObject, ICollectionActions
    {
        private readonly IPlayniteAPI playniteApi;
        private readonly CollectionManager manager;
        private readonly DialogService dialogs;

        public CollectionsHomeViewModel Home { get; }

        private CollectionDetailsViewModel windowPage;

        /// <summary>
        /// Brings the sidebar view to the front; supplied by the plugin, which owns the sidebar item.
        /// </summary>
        public Action<Guid> SwitchToSidebarView { get; set; }

        private object currentPage;
        public object CurrentPage { get => currentPage; private set => SetValue(ref currentPage, value); }

        // Shared by all collection pages so the chosen sort carries over between collections.
        public GameSortField GameSortField { get; set; } = GameSortField.CollectionOrder;
        public bool GameSortDescending { get; set; }

        public CollectionsRootViewModel(IPlayniteAPI playniteApi, CollectionManager manager, DialogService dialogs, GameCollectionsSettingsViewModel settings)
        {
            this.playniteApi = playniteApi;
            this.manager = manager;
            this.dialogs = dialogs;
            Home = new CollectionsHomeViewModel(manager, this, settings);
            CurrentPage = Home;
            manager.CollectionsChanged += OnCollectionsChanged;
        }

        public void ShowHome()
        {
            CurrentPage = Home;
        }

        public void OpenCollection(Guid id)
        {
            var card = Home.GetCard(id);
            if (card != null)
            {
                Home.SelectedCard = card;
                CurrentPage = new CollectionDetailsViewModel(playniteApi, manager, this, card);
            }
        }

        /// <summary>
        /// Opens the collection's page in its own window, for places outside the sidebar view.
        /// </summary>
        public void OpenCollectionInWindow(Guid id)
        {
            var card = Home.GetCard(id);
            if (card != null)
            {
                windowPage = new CollectionDetailsViewModel(playniteApi, manager, this, card);
                try
                {
                    dialogs.ShowCollectionPage(windowPage);
                }
                finally
                {
                    windowPage = null;
                }
            }
        }

        /// <summary>
        /// Closes the window showing the collection and goes to its page in the sidebar view.
        /// </summary>
        public void ShowInCollectionsSection(Guid id)
        {
            OpenCollection(id);
            windowPage?.CloseWindow?.Invoke();
            SwitchToSidebarView?.Invoke(id);
        }

        public void CreateCollection()
        {
            var id = dialogs.ShowCreateCollection();
            if (id.HasValue)
            {
                Home.SelectedCard = Home.GetCard(id.Value);
            }
        }

        public void EditCollection(Guid id)
        {
            var collection = manager.GetCollection(id);
            if (collection != null)
            {
                dialogs.ShowEditCollection(collection);
            }
        }

        public void DeleteCollection(Guid id)
        {
            var collection = manager.GetCollection(id);
            if (collection == null)
            {
                return;
            }

            var answer = playniteApi.Dialogs.ShowMessage(
                Loc.Format("LOCGameCollections_DeleteConfirmMessage", collection.Title),
                Loc.Get("LOCGameCollections_DeleteConfirmTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (answer == MessageBoxResult.Yes)
            {
                manager.DeleteCollection(id);
            }
        }

        public void OnGamesUpdated(ISet<Guid> gameIds)
        {
            Home.OnGamesUpdated(gameIds);
            (CurrentPage as CollectionDetailsViewModel)?.OnGamesUpdated(gameIds);
            windowPage?.OnGamesUpdated(gameIds);
        }

        private void OnCollectionsChanged(object sender, CollectionsChangedEventArgs e)
        {
            Home.Sync(e.ChangedIds);
            if (CurrentPage is CollectionDetailsViewModel details)
            {
                if (manager.GetCollection(details.CollectionId) == null)
                {
                    ShowHome();
                }
                else if (e.ChangedIds == null || e.ChangedIds.Contains(details.CollectionId))
                {
                    details.Reload();
                }
            }

            if (windowPage != null)
            {
                if (manager.GetCollection(windowPage.CollectionId) == null)
                {
                    windowPage.CloseWindow?.Invoke();
                }
                else if (e.ChangedIds == null || e.ChangedIds.Contains(windowPage.CollectionId))
                {
                    windowPage.Reload();
                }
            }
        }
    }
}

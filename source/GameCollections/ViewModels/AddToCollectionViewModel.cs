using GameCollections.Services;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace GameCollections.ViewModels
{
    /// <summary>
    /// One row of the "Add to Collection" dialog.
    /// </summary>
    public class CollectionToggleItem : ObservableObject
    {
        /// <summary>
        /// Collection ID, or null for a collection created in the dialog that is not saved yet.
        /// </summary>
        public Guid? CollectionId { get; }
        public string Title { get; }
        public string GameCountText { get; }
        public bool IsNew => CollectionId == null;

        /// <summary>
        /// Membership before the dialog opened: true when all selected games are in the collection,
        /// false when none are, null when only some are.
        /// </summary>
        public bool? OriginalState { get; }

        /// <summary>
        /// Only mixed rows can return to the "leave unchanged" state.
        /// </summary>
        public bool IsThreeState => OriginalState == null;

        private bool? isChecked;
        public bool? IsChecked { get => isChecked; set => SetValue(ref isChecked, value); }

        public CollectionToggleItem(Guid? collectionId, string title, string gameCountText, bool? originalState)
        {
            CollectionId = collectionId;
            Title = title;
            GameCountText = gameCountText;
            OriginalState = originalState;
            isChecked = originalState;
        }

        public void Toggle()
        {
            IsChecked = IsChecked != true;
        }
    }

    public class AddToCollectionViewModel : ObservableObject
    {
        private readonly CollectionManager manager;
        private readonly IReadOnlyList<Game> games;
        private readonly Action<bool> close;

        public ObservableCollection<CollectionToggleItem> Items { get; } = new ObservableCollection<CollectionToggleItem>();
        public ListCollectionView ItemsView { get; }

        public string HeaderText { get; }

        private string searchText = string.Empty;
        public string SearchText
        {
            get => searchText;
            set
            {
                SetValue(ref searchText, value ?? string.Empty);
                ItemsView.Refresh();
                RaiseStateChanged();
            }
        }

        private CollectionToggleItem selectedItem;
        public CollectionToggleItem SelectedItem { get => selectedItem; set => SetValue(ref selectedItem, value); }

        public bool IsEmpty => Items.Count == 0;
        public bool HasNoResults => !IsEmpty && ItemsView.Count == 0;
        public bool ShowEmptyState => IsEmpty && SearchTitle.Length == 0;

        /// <summary>
        /// True when the search text can become a new collection: it is a valid title and no
        /// existing or pending collection already uses it.
        /// </summary>
        public bool CanCreateFromSearch =>
            SearchTitle.Length > 0 &&
            SearchTitle.Length <= CollectionManager.MaxTitleLength &&
            !Items.Any(i => CollectionManager.TitlesEqual(i.Title, SearchTitle));

        public string CreateButtonText => Loc.Format("LOCGameCollections_CreateCollectionNamed", SearchTitle);
        public string NoResultsText => Loc.Format("LOCGameCollections_NoCollectionsMatch", SearchTitle);

        public RelayCommand CreateFromSearchCommand { get; }
        public RelayCommand SaveCommand { get; }
        public RelayCommand CancelCommand { get; }

        private string SearchTitle => CollectionManager.NormalizeTitle(searchText);

        public AddToCollectionViewModel(CollectionManager manager, IReadOnlyList<Game> games, Action<bool> close)
        {
            this.manager = manager;
            this.games = games;
            this.close = close;

            HeaderText = games.Count == 1
                ? Loc.Format("LOCGameCollections_AddToCollectionHeaderSingle", games[0].Name)
                : Loc.Format("LOCGameCollections_AddToCollectionHeaderMultiple", games.Count);

            var gameIds = games.Select(g => g.Id).ToList();
            // The games of a dynamic collection are decided by its rules, so it can't be chosen here.
            foreach (var collection in manager.Collections.Where(c => !c.IsDynamic))
            {
                var memberIds = new HashSet<Guid>(collection.GameIds);
                var containedCount = gameIds.Count(memberIds.Contains);
                bool? state = containedCount == 0 ? false : containedCount == gameIds.Count ? (bool?)true : null;
                Items.Add(new CollectionToggleItem(collection.Id, collection.Title, Loc.GameCount(manager.GetGames(collection).Count), state));
            }

            ItemsView = new ListCollectionView(Items)
            {
                Filter = FilterItem,
                CustomSort = Comparer<CollectionToggleItem>.Create(CompareItems)
            };

            CreateFromSearchCommand = new RelayCommand(CreateFromSearch);
            SaveCommand = new RelayCommand(Save);
            CancelCommand = new RelayCommand(() => close(false));
        }

        public void CreateFromSearch()
        {
            if (!CanCreateFromSearch)
            {
                return;
            }

            var item = new CollectionToggleItem(null, SearchTitle, Loc.Get("LOCGameCollections_NewCollectionBadge"), false);
            item.IsChecked = true;
            Items.Add(item);
            SearchText = string.Empty;
            SelectedItem = item;
        }

        /// <summary>
        /// Enter in the search box creates a collection when the text is new, or toggles the
        /// only matching collection. Returns false when nothing was done.
        /// </summary>
        public bool HandleSearchEnter()
        {
            if (CanCreateFromSearch && ItemsView.Count == 0)
            {
                CreateFromSearch();
                return true;
            }

            if (SearchTitle.Length > 0 && ItemsView.Count == 1)
            {
                var item = ItemsView.Cast<CollectionToggleItem>().First();
                item.Toggle();
                SelectedItem = item;
                return true;
            }

            return false;
        }

        private void Save()
        {
            var membership = new Dictionary<Guid, bool>();
            foreach (var item in Items.Where(i => !i.IsNew && i.IsChecked.HasValue && i.IsChecked != i.OriginalState))
            {
                membership[item.CollectionId.Value] = item.IsChecked.Value;
            }

            var newTitles = Items.Where(i => i.IsNew && i.IsChecked == true).Select(i => i.Title).ToList();
            manager.ApplyMembership(games.Select(g => g.Id).ToList(), membership, newTitles);
            close(true);
        }

        private bool FilterItem(object obj)
        {
            if (SearchTitle.Length == 0)
            {
                return true;
            }

            var item = (CollectionToggleItem)obj;
            return CultureInfo.CurrentCulture.CompareInfo.IndexOf(item.Title, SearchTitle, CompareOptions.IgnoreCase) >= 0;
        }

        private static int CompareItems(CollectionToggleItem first, CollectionToggleItem second)
        {
            // Collections created in this dialog stay on top so they are easy to spot.
            if (first.IsNew != second.IsNew)
            {
                return first.IsNew ? -1 : 1;
            }

            return string.Compare(first.Title, second.Title, StringComparison.CurrentCultureIgnoreCase);
        }

        private void RaiseStateChanged()
        {
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasNoResults));
            OnPropertyChanged(nameof(ShowEmptyState));
            OnPropertyChanged(nameof(CanCreateFromSearch));
            OnPropertyChanged(nameof(CreateButtonText));
            OnPropertyChanged(nameof(NoResultsText));
        }
    }
}

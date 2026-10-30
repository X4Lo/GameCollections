using GameCollections.Services;
using Playnite.SDK;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace GameCollections.ViewModels
{
    public enum GameSortField
    {
        CollectionOrder,
        Name,
        ReleaseDate,
        LastPlayed,
        Playtime,
        DateAdded
    }

    public class SortOption
    {
        public GameSortField Field { get; }
        public string Label { get; }

        public SortOption(GameSortField field, string label)
        {
            Field = field;
            Label = label;
        }
    }

    /// <summary>
    /// The page of a single collection: header plus its games.
    /// </summary>
    public class CollectionDetailsViewModel : ObservableObject
    {
        private const string AscendingGlyph = "\uea5e";
        private const string DescendingGlyph = "\uea5b";
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly IPlayniteAPI playniteApi;
        private readonly CollectionManager manager;
        private readonly CollectionsRootViewModel root;

        public CollectionCardViewModel Header { get; }
        public Guid CollectionId => Header.Id;

        public ObservableCollection<CollectionGameViewModel> Games { get; } = new ObservableCollection<CollectionGameViewModel>();
        public ListCollectionView GamesView { get; }

        /// <summary>
        /// The view's current selection; set by the view.
        /// </summary>
        public IList SelectedGames { get; set; }

        public IReadOnlyList<SortOption> SortOptions { get; } = new List<SortOption>
        {
            new SortOption(GameSortField.CollectionOrder, Loc.Get("LOCGameCollections_SortCollectionOrder")),
            new SortOption(GameSortField.Name, Loc.Get("LOCGameCollections_SortName")),
            new SortOption(GameSortField.ReleaseDate, Loc.Get("LOCGameCollections_SortReleaseDate")),
            new SortOption(GameSortField.LastPlayed, Loc.Get("LOCGameCollections_SortLastPlayed")),
            new SortOption(GameSortField.Playtime, Loc.Get("LOCGameCollections_SortPlaytime")),
            new SortOption(GameSortField.DateAdded, Loc.Get("LOCGameCollections_SortDateAdded"))
        };

        private string searchText = string.Empty;
        public string SearchText
        {
            get => searchText;
            set
            {
                SetValue(ref searchText, value ?? string.Empty);
                RefreshView();
            }
        }

        public GameSortField SortField
        {
            get => root.GameSortField;
            set
            {
                root.GameSortField = value;
                OnPropertyChanged();
                ApplySort();
            }
        }

        public bool SortDescending
        {
            get => root.GameSortDescending;
            set
            {
                root.GameSortDescending = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SortDirectionGlyph));
                OnPropertyChanged(nameof(SortDirectionTooltip));
                ApplySort();
            }
        }

        public string SortDirectionGlyph => SortDescending ? DescendingGlyph : AscendingGlyph;
        public string SortDirectionTooltip => Loc.Get(SortDescending ? "LOCGameCollections_SortDescending" : "LOCGameCollections_SortAscending");

        private bool installedOnly;
        public bool InstalledOnly
        {
            get => installedOnly;
            set
            {
                SetValue(ref installedOnly, value);
                RefreshView();
            }
        }

        public bool IsEmpty => Games.Count == 0;
        public bool HasNoResults => !IsEmpty && GamesView.Count == 0;
        public string VisibleCountText => GamesView.Count == Games.Count
            ? Loc.GameCount(Games.Count)
            : Loc.Format("LOCGameCollections_FilteredGameCount", GamesView.Count, Games.Count);

        public RelayCommand BackCommand { get; }
        public RelayCommand ToggleSortDirectionCommand { get; }
        public RelayCommand ShowAllInLibraryCommand { get; }
        public RelayCommand RemoveSelectedCommand { get; }
        public RelayCommand PlaySelectedCommand { get; }

        public CollectionDetailsViewModel(IPlayniteAPI playniteApi, CollectionManager manager, CollectionsRootViewModel root, CollectionCardViewModel header)
        {
            this.playniteApi = playniteApi;
            this.manager = manager;
            this.root = root;
            Header = header;

            GamesView = new ListCollectionView(Games) { Filter = FilterGame };
            ApplySort();

            BackCommand = new RelayCommand(root.ShowHome);
            ToggleSortDirectionCommand = new RelayCommand(() => SortDescending = !SortDescending);
            ShowAllInLibraryCommand = new RelayCommand(ShowAllInLibrary);
            RemoveSelectedCommand = new RelayCommand(RemoveSelected);
            PlaySelectedCommand = new RelayCommand(PlaySelected);

            Reload();
        }

        /// <summary>
        /// Re-reads the collection's games, reusing existing tiles so loaded covers are kept.
        /// </summary>
        public void Reload()
        {
            var collection = manager.GetCollection(CollectionId);
            if (collection == null)
            {
                return;
            }

            var games = manager.GetGames(collection);
            var currentIds = new HashSet<Guid>(games.Select(g => g.Id));
            foreach (var tile in Games.Where(g => !currentIds.Contains(g.Id)).ToList())
            {
                Games.Remove(tile);
            }

            var existing = Games.ToDictionary(g => g.Id);
            for (var i = 0; i < games.Count; i++)
            {
                if (existing.TryGetValue(games[i].Id, out var tile))
                {
                    tile.Update(games[i], i);
                }
                else
                {
                    Games.Add(new CollectionGameViewModel(games[i], i, manager, this));
                }
            }

            RefreshView();
        }

        public void OnGamesUpdated(ISet<Guid> gameIds)
        {
            if (Games.Any(g => gameIds.Contains(g.Id)))
            {
                Reload();
            }
        }

        public void PlayGame(CollectionGameViewModel tile)
        {
            try
            {
                var game = playniteApi.Database.Games.Get(tile.Id);
                if (game == null)
                {
                    return;
                }

                if (game.IsInstalled)
                {
                    playniteApi.StartGame(game.Id);
                }
                else
                {
                    playniteApi.InstallGame(game.Id);
                }
            }
            catch (Exception e)
            {
                logger.Error(e, $"Failed to start game {tile.Id}.");
                playniteApi.Dialogs.ShowErrorMessage(e.Message, Loc.Get("LOCGameCollections_PluginName"));
            }
        }

        /// <summary>
        /// Playnite has no API to open a game's detail page directly, so the game is selected
        /// in the library view, which shows its details there.
        /// </summary>
        public void ShowGameInLibrary(CollectionGameViewModel tile)
        {
            try
            {
                playniteApi.MainView.SelectGame(tile.Id);
                playniteApi.MainView.SwitchToLibraryView();
            }
            catch (Exception e)
            {
                logger.Error(e, $"Failed to show game {tile.Id} in library.");
            }
        }

        /// <summary>
        /// Removes the game from this collection only; when the game is part of a multi-selection,
        /// the whole selection is removed.
        /// </summary>
        public void RemoveGame(CollectionGameViewModel tile)
        {
            var selected = SelectedGames?.Cast<CollectionGameViewModel>().ToList() ?? new List<CollectionGameViewModel>();
            var targets = selected.Contains(tile) ? selected : new List<CollectionGameViewModel> { tile };
            manager.RemoveGames(CollectionId, targets.Select(t => t.Id));
        }

        private void RemoveSelected()
        {
            var selected = SelectedGames?.Cast<CollectionGameViewModel>().ToList();
            if (selected != null && selected.Count > 0)
            {
                manager.RemoveGames(CollectionId, selected.Select(t => t.Id));
            }
        }

        private void PlaySelected()
        {
            var tile = SelectedGames?.Cast<CollectionGameViewModel>().FirstOrDefault();
            if (tile != null)
            {
                PlayGame(tile);
            }
        }

        private void ShowAllInLibrary()
        {
            if (Games.Count == 0)
            {
                return;
            }

            try
            {
                playniteApi.MainView.SelectGames(Games.Select(g => g.Id).ToList());
                playniteApi.MainView.SwitchToLibraryView();
            }
            catch (Exception e)
            {
                logger.Error(e, "Failed to select collection games in library.");
            }
        }

        private bool FilterGame(object obj)
        {
            var tile = (CollectionGameViewModel)obj;
            if (installedOnly && !tile.IsInstalled)
            {
                return false;
            }

            var query = searchText.Trim();
            return query.Length == 0 ||
                CultureInfo.CurrentCulture.CompareInfo.IndexOf(tile.Name ?? string.Empty, query, CompareOptions.IgnoreCase) >= 0;
        }

        private void ApplySort()
        {
            GamesView.CustomSort = new GameTileComparer(SortField, SortDescending);
            RaiseStateChanged();
        }

        private void RefreshView()
        {
            GamesView.Refresh();
            RaiseStateChanged();
        }

        private void RaiseStateChanged()
        {
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasNoResults));
            OnPropertyChanged(nameof(VisibleCountText));
        }

        private class GameTileComparer : IComparer
        {
            private readonly GameSortField field;
            private readonly bool descending;

            public GameTileComparer(GameSortField field, bool descending)
            {
                this.field = field;
                this.descending = descending;
            }

            public int Compare(object x, object y)
            {
                var a = (CollectionGameViewModel)x;
                var b = (CollectionGameViewModel)y;
                var result = CompareField(a, b);
                if (result == 0 && field != GameSortField.Name)
                {
                    result = CompareNames(a, b);
                }

                return descending ? -result : result;
            }

            private int CompareField(CollectionGameViewModel a, CollectionGameViewModel b)
            {
                switch (field)
                {
                    case GameSortField.Name:
                        return CompareNames(a, b);
                    case GameSortField.ReleaseDate:
                        return Nullable.Compare(a.Game.ReleaseDate?.Date, b.Game.ReleaseDate?.Date);
                    case GameSortField.LastPlayed:
                        return Nullable.Compare(a.Game.LastActivity, b.Game.LastActivity);
                    case GameSortField.Playtime:
                        return a.Game.Playtime.CompareTo(b.Game.Playtime);
                    case GameSortField.DateAdded:
                        return Nullable.Compare(a.Game.Added, b.Game.Added);
                    default:
                        return a.Order.CompareTo(b.Order);
                }
            }

            private static int CompareNames(CollectionGameViewModel a, CollectionGameViewModel b)
            {
                var first = string.IsNullOrEmpty(a.Game.SortingName) ? a.Name : a.Game.SortingName;
                var second = string.IsNullOrEmpty(b.Game.SortingName) ? b.Name : b.Game.SortingName;
                return string.Compare(first, second, StringComparison.CurrentCultureIgnoreCase);
            }
        }
    }
}

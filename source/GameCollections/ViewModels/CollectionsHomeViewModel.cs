using GameCollections.Models;
using GameCollections.Services;
using GameCollections.Settings;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace GameCollections.ViewModels
{
    /// <summary>
    /// The page listing every collection.
    /// </summary>
    public class CollectionsHomeViewModel : ObservableObject
    {
        private const string GridGlyph = "\uef5d";
        private const string ListGlyph = "\uef74";

        private readonly CollectionManager manager;
        private readonly ICollectionActions actions;
        private readonly GameCollectionsSettingsViewModel settings;

        public ObservableCollection<CollectionCardViewModel> Cards { get; } = new ObservableCollection<CollectionCardViewModel>();
        public ListCollectionView CardsView { get; }

        private string searchText = string.Empty;
        public string SearchText
        {
            get => searchText;
            set
            {
                SetValue(ref searchText, value ?? string.Empty);
                CardsView.Refresh();
                RaiseStateChanged();
            }
        }

        private CollectionCardViewModel selectedCard;
        public CollectionCardViewModel SelectedCard { get => selectedCard; set => SetValue(ref selectedCard, value); }

        public bool IsGridView => settings.Settings.ViewMode == CollectionsViewMode.Grid;
        public bool IsListView => !IsGridView;
        public string ViewModeToggleGlyph => IsGridView ? ListGlyph : GridGlyph;
        public string ViewModeToggleTooltip => Loc.Get(IsGridView ? "LOCGameCollections_SwitchToListView" : "LOCGameCollections_SwitchToGridView");

        public string CountText => Loc.CollectionCount(Cards.Count);
        public bool IsEmpty => Cards.Count == 0;
        public bool HasNoResults => !IsEmpty && CardsView.Count == 0;
        public string NoResultsText => Loc.Format("LOCGameCollections_NoCollectionsMatch", searchText.Trim());

        public RelayCommand CreateCollectionCommand { get; }
        public RelayCommand ToggleViewModeCommand { get; }

        public CollectionsHomeViewModel(CollectionManager manager, CollectionsRootViewModel root, GameCollectionsSettingsViewModel settings)
        {
            this.manager = manager;
            this.actions = root;
            this.settings = settings;

            CardsView = new ListCollectionView(Cards)
            {
                Filter = FilterCard,
                CustomSort = Comparer<CollectionCardViewModel>.Create((a, b) => string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase))
            };

            CreateCollectionCommand = new RelayCommand(() => root.CreateCollection());
            ToggleViewModeCommand = new RelayCommand(ToggleViewMode);
            Sync(null);
        }

        public CollectionCardViewModel GetCard(Guid id)
        {
            return Cards.FirstOrDefault(c => c.Id == id);
        }

        /// <summary>
        /// Brings the cards in line with the manager. Only cards listed in <paramref name="changedIds"/>
        /// are refreshed; null refreshes all of them.
        /// </summary>
        public void Sync(IReadOnlyCollection<Guid> changedIds)
        {
            var currentIds = new HashSet<Guid>(manager.Collections.Select(c => c.Id));
            foreach (var card in Cards.Where(c => !currentIds.Contains(c.Id)).ToList())
            {
                Cards.Remove(card);
            }

            var existing = Cards.ToDictionary(c => c.Id);
            foreach (var collection in manager.Collections)
            {
                if (!existing.TryGetValue(collection.Id, out var card))
                {
                    Cards.Add(new CollectionCardViewModel(collection, manager, actions));
                }
                else if (changedIds == null || changedIds.Contains(collection.Id))
                {
                    card.Update(collection);
                }
            }

            // Titles may have changed, so re-apply sorting and filtering.
            var selected = SelectedCard;
            CardsView.Refresh();
            SelectedCard = selected != null && Cards.Contains(selected) ? selected : null;
            RaiseStateChanged();
        }

        /// <summary>
        /// Reloads cards whose automatic poster comes from one of the updated games.
        /// </summary>
        public void OnGamesUpdated(ISet<Guid> gameIds)
        {
            foreach (var card in Cards.Where(c => c.PosterGameId.HasValue && gameIds.Contains(c.PosterGameId.Value)))
            {
                var collection = manager.GetCollection(card.Id);
                if (collection != null)
                {
                    card.Update(collection);
                }
            }
        }

        private void ToggleViewMode()
        {
            settings.Settings.ViewMode = IsGridView ? CollectionsViewMode.List : CollectionsViewMode.Grid;
            settings.Save();
            OnPropertyChanged(nameof(IsGridView));
            OnPropertyChanged(nameof(IsListView));
            OnPropertyChanged(nameof(ViewModeToggleGlyph));
            OnPropertyChanged(nameof(ViewModeToggleTooltip));
        }

        private bool FilterCard(object obj)
        {
            var query = searchText.Trim();
            if (query.Length == 0)
            {
                return true;
            }

            var card = (CollectionCardViewModel)obj;
            return CultureInfo.CurrentCulture.CompareInfo.IndexOf(card.Title ?? string.Empty, query, CompareOptions.IgnoreCase) >= 0;
        }

        private void RaiseStateChanged()
        {
            OnPropertyChanged(nameof(CountText));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasNoResults));
            OnPropertyChanged(nameof(NoResultsText));
        }
    }
}

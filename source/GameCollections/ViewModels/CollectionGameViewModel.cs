using GameCollections.Services;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Media;

namespace GameCollections.ViewModels
{
    /// <summary>
    /// A game tile on the collection page. The cover is loaded only once the tile scrolls into view.
    /// </summary>
    public class CollectionGameViewModel : ObservableObject
    {
        private const int CoverDecodeWidth = 240;
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly CollectionManager manager;
        private readonly CollectionDetailsViewModel owner;
        private bool coverRequested;
        private string coverPath;
        private int coverVersion;

        private Game game;
        public Game Game => game;
        public Guid Id => game.Id;
        public string Name => game.Name;
        public bool IsInstalled => game.IsInstalled;

        /// <summary>
        /// Position of the game in the collection, used for the "collection order" sort.
        /// </summary>
        public int Order { get; private set; }

        public string PlayActionText => Loc.Get(IsInstalled ? "LOCGameCollections_Play" : "LOCGameCollections_Install");

        private ImageSource cover;
        public ImageSource Cover { get => cover; private set => SetValue(ref cover, value); }

        public RelayCommand PlayCommand { get; }
        public RelayCommand ShowInLibraryCommand { get; }
        public RelayCommand RemoveCommand { get; }
        public RelayCommand MoveToTopCommand { get; }
        public RelayCommand MoveUpCommand { get; }
        public RelayCommand MoveDownCommand { get; }
        public RelayCommand MoveToBottomCommand { get; }

        public bool CanRemove => owner.CanRemove;

        public CollectionGameViewModel(Game game, int order, CollectionManager manager, CollectionDetailsViewModel owner)
        {
            this.game = game;
            this.manager = manager;
            this.owner = owner;
            Order = order;
            PlayCommand = new RelayCommand(() => owner.PlayGame(this));
            ShowInLibraryCommand = new RelayCommand(() => owner.ShowGameInLibrary(this));
            RemoveCommand = new RelayCommand(() => owner.RemoveGame(this));
            MoveToTopCommand = new RelayCommand(() => owner.MoveToEdge(this, false), () => owner.CanReorder);
            MoveUpCommand = new RelayCommand(() => owner.MoveOneStep(this, false), () => owner.CanReorder);
            MoveDownCommand = new RelayCommand(() => owner.MoveOneStep(this, true), () => owner.CanReorder);
            MoveToBottomCommand = new RelayCommand(() => owner.MoveToEdge(this, true), () => owner.CanReorder);
        }

        public void Update(Game updatedGame, int order)
        {
            game = updatedGame;
            Order = order;
            OnPropertyChanged(nameof(Game));
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(IsInstalled));
            OnPropertyChanged(nameof(PlayActionText));
            OnPropertyChanged(nameof(CanRemove));

            if (coverRequested && manager.GetGameCoverPath(game) != coverPath)
            {
                _ = LoadCoverAsync();
            }
        }

        public void RequestCover()
        {
            if (coverRequested)
            {
                return;
            }

            coverRequested = true;
            _ = LoadCoverAsync();
        }

        private async Task LoadCoverAsync()
        {
            var version = ++coverVersion;
            var path = manager.GetGameCoverPath(game);
            coverPath = path;
            try
            {
                var image = await ImageLoader.LoadAsync(path, CoverDecodeWidth);
                if (version == coverVersion)
                {
                    Cover = image;
                }
            }
            catch (Exception e)
            {
                logger.Error(e, $"Failed to load cover for game {game.Id}.");
            }
        }
    }
}

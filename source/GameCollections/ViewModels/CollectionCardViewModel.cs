using GameCollections.Models;
using GameCollections.Services;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Media;

namespace GameCollections.ViewModels
{
    /// <summary>
    /// Actions shared by every place that shows a collection.
    /// </summary>
    public interface ICollectionActions
    {
        void OpenCollection(Guid id);
        void EditCollection(Guid id);
        void DeleteCollection(Guid id);
    }

    /// <summary>
    /// Display state of one collection, used by the grid card, the list row and the collection page header.
    /// </summary>
    public class CollectionCardViewModel : ObservableObject
    {
        // Wide enough for the largest place a poster is drawn (the collection page header) on scaled displays.
        private const int PosterDecodeWidth = 320;
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly CollectionManager manager;
        private int posterVersion;

        public Guid Id { get; }

        private string title;
        public string Title { get => title; private set => SetValue(ref title, value); }

        private string description;
        public string Description { get => description; private set => SetValue(ref description, value); }

        private double rating;
        public double Rating { get => rating; private set => SetValue(ref rating, value); }

        private int gameCount;
        public int GameCount { get => gameCount; private set => SetValue(ref gameCount, value); }

        private ImageSource poster;
        public ImageSource Poster { get => poster; private set => SetValue(ref poster, value); }

        public string RatingText => Loc.Rating(rating);
        public string GameCountText => Loc.GameCount(gameCount);
        public bool HasDescription => !string.IsNullOrWhiteSpace(description);

        /// <summary>
        /// The game whose cover is used as the automatic poster, if any.
        /// </summary>
        public Guid? PosterGameId { get; private set; }

        public RelayCommand OpenCommand { get; }
        public RelayCommand EditCommand { get; }
        public RelayCommand DeleteCommand { get; }

        public CollectionCardViewModel(GameCollection collection, CollectionManager manager, ICollectionActions actions)
        {
            this.manager = manager;
            Id = collection.Id;
            OpenCommand = new RelayCommand(() => actions.OpenCollection(Id));
            EditCommand = new RelayCommand(() => actions.EditCollection(Id));
            DeleteCommand = new RelayCommand(() => actions.DeleteCollection(Id));
            Update(collection);
        }

        public void Update(GameCollection collection)
        {
            Title = collection.Title;
            Description = collection.Description;
            Rating = collection.Rating;
            GameCount = manager.GetGames(collection).Count;
            OnPropertyChanged(nameof(RatingText));
            OnPropertyChanged(nameof(GameCountText));
            OnPropertyChanged(nameof(HasDescription));

            PosterGameId = manager.HasCustomPoster(collection) ? null : manager.GetFirstGame(collection)?.Id;
            _ = LoadPosterAsync(manager.ResolvePosterPath(collection));
        }

        private async Task LoadPosterAsync(string path)
        {
            var version = ++posterVersion;
            if (path == null)
            {
                Poster = null;
                return;
            }

            try
            {
                var image = await ImageLoader.LoadAsync(path, PosterDecodeWidth);
                if (version == posterVersion)
                {
                    Poster = image;
                }
            }
            catch (Exception e)
            {
                logger.Error(e, $"Failed to load poster for collection {Id}.");
            }
        }
    }
}

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
    /// Backs the dialog used both to create a collection and to edit an existing one.
    /// </summary>
    public class EditCollectionViewModel : ObservableObject
    {
        private const int PreviewDecodeWidth = 320;
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly IPlayniteAPI playniteApi;
        private readonly CollectionManager manager;
        private readonly GameCollection existing;
        private PosterChange posterChange = PosterChange.Keep;
        private string posterSourcePath;
        private int previewVersion;

        public Action<bool> CloseRequested { get; set; }
        public Guid? SavedCollectionId { get; private set; }

        public bool IsEditing => existing != null;
        public string WindowTitle => Loc.Get(IsEditing ? "LOCGameCollections_EditCollectionWindowTitle" : "LOCGameCollections_CreateCollectionWindowTitle");
        public int MaxTitleLength => CollectionManager.MaxTitleLength;
        public int MaxDescriptionLength => CollectionManager.MaxDescriptionLength;

        private string title = string.Empty;
        public string Title
        {
            get => title;
            set
            {
                SetValue(ref title, value ?? string.Empty);
                titleTouched = true;
                RaiseValidationChanged();
            }
        }

        private string description = string.Empty;
        public string Description { get => description; set => SetValue(ref description, value ?? string.Empty); }

        private double rating;
        public double Rating
        {
            get => rating;
            set
            {
                SetValue(ref rating, CollectionManager.NormalizeRating(value));
                OnPropertyChanged(nameof(RatingText));
            }
        }

        public string RatingText => Loc.Rating(rating);

        private ImageSource posterPreview;
        public ImageSource PosterPreview { get => posterPreview; private set => SetValue(ref posterPreview, value); }

        private string posterHint;
        public string PosterHint { get => posterHint; private set => SetValue(ref posterHint, value); }

        public bool HasCustomPoster =>
            posterChange == PosterChange.Replace ||
            (posterChange == PosterChange.Keep && existing != null && manager.HasCustomPoster(existing));

        private string errorMessage;
        public string ErrorMessage { get => errorMessage; private set => SetValue(ref errorMessage, value); }

        // A fresh "create" dialog should not open with a "title is required" error.
        private bool titleTouched;
        public string TitleError => titleTouched || IsEditing ? manager.ValidateTitle(title, existing?.Id) : null;
        public bool CanSave => manager.ValidateTitle(title, existing?.Id) == null;

        public RelayCommand SaveCommand { get; }
        public RelayCommand CancelCommand { get; }
        public RelayCommand BrowsePosterCommand { get; }
        public RelayCommand RemovePosterCommand { get; }

        public EditCollectionViewModel(IPlayniteAPI playniteApi, CollectionManager manager, GameCollection existing)
        {
            this.playniteApi = playniteApi;
            this.manager = manager;
            this.existing = existing;

            if (existing != null)
            {
                title = existing.Title;
                description = existing.Description ?? string.Empty;
                rating = existing.Rating;
            }

            SaveCommand = new RelayCommand(Save);
            CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(false));
            BrowsePosterCommand = new RelayCommand(BrowsePoster);
            RemovePosterCommand = new RelayCommand(RemovePoster);

            _ = UpdatePosterPreviewAsync();
        }

        private void BrowsePoster()
        {
            var path = playniteApi.Dialogs.SelectImagefile();
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            if (!PosterStorage.IsSupportedFile(path))
            {
                ErrorMessage = Loc.Get("LOCGameCollections_ErrorPosterFormat");
                return;
            }

            // Make sure the image can actually be decoded before accepting it.
            if (ImageLoader.Load(path, PreviewDecodeWidth) == null)
            {
                ErrorMessage = Loc.Get("LOCGameCollections_ErrorPosterUnreadable");
                return;
            }

            ErrorMessage = null;
            posterChange = PosterChange.Replace;
            posterSourcePath = path;
            OnPropertyChanged(nameof(HasCustomPoster));
            _ = UpdatePosterPreviewAsync();
        }

        private void RemovePoster()
        {
            posterChange = PosterChange.Remove;
            posterSourcePath = null;
            OnPropertyChanged(nameof(HasCustomPoster));
            _ = UpdatePosterPreviewAsync();
        }

        /// <summary>
        /// Shows the poster the collection will have after saving, including the automatic fallback.
        /// </summary>
        private async Task UpdatePosterPreviewAsync()
        {
            var version = ++previewVersion;
            string path;
            if (posterChange == PosterChange.Replace)
            {
                path = posterSourcePath;
                PosterHint = Loc.Get("LOCGameCollections_PosterHintCustom");
            }
            else
            {
                var custom = posterChange == PosterChange.Keep && existing != null ? manager.GetCustomPosterPath(existing) : null;
                var automatic = existing != null ? manager.GetGameCoverPath(manager.GetFirstGame(existing)) : null;
                if (custom != null)
                {
                    path = custom;
                    PosterHint = Loc.Get("LOCGameCollections_PosterHintCustom");
                }
                else if (automatic != null)
                {
                    path = automatic;
                    PosterHint = Loc.Get("LOCGameCollections_PosterHintAutomatic");
                }
                else
                {
                    path = null;
                    PosterHint = Loc.Get("LOCGameCollections_PosterHintPlaceholder");
                }
            }

            try
            {
                var image = await ImageLoader.LoadAsync(path, PreviewDecodeWidth);
                if (version == previewVersion)
                {
                    PosterPreview = image;
                }
            }
            catch (Exception e)
            {
                logger.Error(e, "Failed to load poster preview.");
            }
        }

        private void Save()
        {
            titleTouched = true;
            RaiseValidationChanged();
            if (!CanSave)
            {
                return;
            }

            try
            {
                if (existing == null)
                {
                    var created = manager.CreateCollection(title, description, rating, posterChange == PosterChange.Replace ? posterSourcePath : null, null);
                    SavedCollectionId = created.Id;
                }
                else
                {
                    manager.UpdateCollection(existing.Id, title, description, rating, posterChange, posterSourcePath);
                    SavedCollectionId = existing.Id;
                }

                CloseRequested?.Invoke(true);
            }
            catch (Exception e)
            {
                logger.Error(e, "Failed to save collection.");
                ErrorMessage = Loc.Format("LOCGameCollections_ErrorSaveCollection", e.Message);
            }
        }

        private void RaiseValidationChanged()
        {
            OnPropertyChanged(nameof(TitleError));
            OnPropertyChanged(nameof(CanSave));
        }
    }
}

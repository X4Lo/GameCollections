using GameCollections.Models;
using GameCollections.ViewModels;
using GameCollections.Views;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Windows;

namespace GameCollections.Services
{
    /// <summary>
    /// Opens the plugin's dialogs in windows created by Playnite so they get the active theme.
    /// </summary>
    public class DialogService
    {
        private readonly IPlayniteAPI playniteApi;
        private readonly CollectionManager manager;

        public DialogService(IPlayniteAPI playniteApi, CollectionManager manager)
        {
            this.playniteApi = playniteApi;
            this.manager = manager;
        }

        public bool ShowAddToCollection(IReadOnlyList<Game> games)
        {
            if (games == null || games.Count == 0)
            {
                return false;
            }

            var window = CreateWindow(Loc.Get("LOCGameCollections_AddToCollectionWindowTitle"), 460, 560, 360, 380);
            var viewModel = new AddToCollectionViewModel(manager, games, result => Close(window, result));
            window.Content = new AddToCollectionView { DataContext = viewModel };
            return window.ShowDialog() == true;
        }

        /// <summary>
        /// Shows the create dialog and returns the new collection's ID, or null when cancelled.
        /// </summary>
        public Guid? ShowCreateCollection()
        {
            return ShowEditor(null);
        }

        public bool ShowEditCollection(GameCollection collection)
        {
            return ShowEditor(collection).HasValue;
        }

        private Guid? ShowEditor(GameCollection collection)
        {
            var viewModel = new EditCollectionViewModel(playniteApi, manager, collection);
            var window = CreateWindow(viewModel.WindowTitle, 640, 480, 520, 420);
            viewModel.CloseRequested = result => Close(window, result);
            window.Content = new EditCollectionView { DataContext = viewModel };
            return window.ShowDialog() == true ? viewModel.SavedCollectionId : null;
        }

        private Window CreateWindow(string title, double width, double height, double minWidth, double minHeight)
        {
            var window = playniteApi.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMinimizeButton = false,
                ShowMaximizeButton = false,
                ShowCloseButton = true
            });

            // Keep dialogs usable on small or scaled screens.
            var workArea = SystemParameters.WorkArea;
            window.Title = title;
            window.MinWidth = Math.Min(minWidth, workArea.Width);
            window.MinHeight = Math.Min(minHeight, workArea.Height);
            window.Width = Math.Max(window.MinWidth, Math.Min(width, workArea.Width * 0.9));
            window.Height = Math.Max(window.MinHeight, Math.Min(height, workArea.Height * 0.9));
            window.ResizeMode = ResizeMode.CanResize;
            window.Owner = playniteApi.Dialogs.GetCurrentAppWindow();
            window.WindowStartupLocation = window.Owner != null
                ? WindowStartupLocation.CenterOwner
                : WindowStartupLocation.CenterScreen;
            return window;
        }

        private static void Close(Window window, bool result)
        {
            try
            {
                window.DialogResult = result;
            }
            catch (InvalidOperationException)
            {
                // DialogResult can only be set while the window is shown modally.
                window.Close();
            }
        }
    }
}

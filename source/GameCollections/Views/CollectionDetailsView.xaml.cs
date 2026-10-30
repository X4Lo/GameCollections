using GameCollections.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace GameCollections.Views
{
    public partial class CollectionDetailsView : UserControl
    {
        private readonly DispatcherTimer coverTimer;
        private ScrollViewer scrollViewer;

        public CollectionDetailsView()
        {
            InitializeComponent();

            // Covers are requested only for tiles near the viewport, batched to avoid work on every scroll tick.
            coverTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(80) };
            coverTimer.Tick += (s, e) =>
            {
                coverTimer.Stop();
                RequestVisibleCovers();
            };

            GamesList.SelectionChanged += (s, e) => SyncSelection();
            DataContextChanged += (s, e) => SyncSelection();
            Loaded += OnLoaded;
            Unloaded += (s, e) => coverTimer.Stop();
        }

        private CollectionDetailsViewModel ViewModel => DataContext as CollectionDetailsViewModel;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (scrollViewer == null)
            {
                scrollViewer = FindDescendant<ScrollViewer>(GamesList);
                if (scrollViewer != null)
                {
                    scrollViewer.ScrollChanged += (s, args) => ScheduleCoverRequest();
                }
            }

            ScheduleCoverRequest();
        }

        private void SyncSelection()
        {
            if (ViewModel != null)
            {
                ViewModel.SelectedGames = GamesList.SelectedItems;
            }
        }

        private void GameItem_Loaded(object sender, RoutedEventArgs e)
        {
            ScheduleCoverRequest();
        }

        private void GamesList_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ScheduleCoverRequest();
        }

        private void ScheduleCoverRequest()
        {
            coverTimer.Stop();
            coverTimer.Start();
        }

        private void RequestVisibleCovers()
        {
            if (scrollViewer == null || !IsLoaded)
            {
                return;
            }

            // Load one extra screen above and below so covers are ready before they scroll in.
            var margin = scrollViewer.ViewportHeight;
            foreach (var item in GamesList.Items)
            {
                if (!(GamesList.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement container) || !container.IsVisible)
                {
                    continue;
                }

                try
                {
                    var top = container.TransformToAncestor(scrollViewer).Transform(new Point(0, 0)).Y;
                    if (top + container.ActualHeight >= -margin && top <= scrollViewer.ViewportHeight + margin)
                    {
                        ((CollectionGameViewModel)item).RequestCover();
                    }
                }
                catch (InvalidOperationException)
                {
                    // The container is being detached from the visual tree.
                }
            }
        }

        private void GameItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem item && item.DataContext is CollectionGameViewModel game)
            {
                game.PlayCommand.Execute(null);
                e.Handled = true;
            }
        }

        private void GamesList_KeyDown(object sender, KeyEventArgs e)
        {
            if (ViewModel == null)
            {
                return;
            }

            switch (e.Key)
            {
                case Key.Enter:
                    ViewModel.PlaySelectedCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.Delete:
                    ViewModel.RemoveSelectedCommand.Execute(null);
                    e.Handled = true;
                    break;
            }
        }

        private static T FindDescendant<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match)
                {
                    return match;
                }

                var nested = FindDescendant<T>(child);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }
    }
}

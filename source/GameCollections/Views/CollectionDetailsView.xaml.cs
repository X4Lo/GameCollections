using GameCollections.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace GameCollections.Views
{
    public partial class CollectionDetailsView : UserControl
    {
        private const string DragFormat = "GameCollections.Games";
        // Dragging this close to the top or bottom of the list scrolls it.
        private const double AutoScrollMargin = 40;
        private const double AutoScrollStep = 24;

        private readonly DispatcherTimer coverTimer;
        private ScrollViewer scrollViewer;
        private Point? dragStart;
        private bool collapseSelectionOnRelease;

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
            // Tiles are read from the visual tree because with groups they're nested, and a game can appear more than once.
            var containers = new List<ListBoxItem>();
            CollectItems(GamesList, containers);
            foreach (var container in containers)
            {
                if (!container.IsVisible || !(container.DataContext is CollectionGameViewModel tile))
                {
                    continue;
                }

                try
                {
                    var top = container.TransformToAncestor(scrollViewer).Transform(new Point(0, 0)).Y;
                    if (top + container.ActualHeight >= -margin && top <= scrollViewer.ViewportHeight + margin)
                    {
                        tile.RequestCover();
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
                case Key.Delete when !ViewModel.CanRemove:
                    break;
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

        private void GamesList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Windows reports arrow keys pressed with Alt as a "system" key.
            if (ViewModel == null || e.Key != Key.System || Keyboard.Modifiers != ModifierKeys.Alt)
            {
                return;
            }

            bool later;
            switch (e.SystemKey)
            {
                case Key.Left:
                case Key.Up:
                    later = false;
                    break;
                case Key.Right:
                case Key.Down:
                    later = true;
                    break;
                default:
                    return;
            }

            e.Handled = true;
            var tile = GamesList.SelectedItems.Cast<CollectionGameViewModel>().FirstOrDefault();
            if (tile != null)
            {
                ViewModel.MoveOneStep(tile, later);
                GamesList.ScrollIntoView(tile);
            }
        }

        private void GameItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            dragStart = null;
            collapseSelectionOnRelease = false;
            if (ViewModel == null || !ViewModel.CanReorder || e.ClickCount != 1)
            {
                return;
            }

            dragStart = e.GetPosition(GamesList);

            // Clicking a selected game would normally reduce the selection to it at once, making it
            // impossible to drag several games. That is put off until the button is released without a drag.
            var item = (ListBoxItem)sender;
            if (item.IsSelected && GamesList.SelectedItems.Count > 1 && Keyboard.Modifiers == ModifierKeys.None)
            {
                collapseSelectionOnRelease = true;
                e.Handled = true;
            }
        }

        private void GameItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (collapseSelectionOnRelease)
            {
                GamesList.SelectedItem = ((ListBoxItem)sender).DataContext;
            }

            collapseSelectionOnRelease = false;
            dragStart = null;
        }

        private void GameItem_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (dragStart == null || e.LeftButton != MouseButtonState.Pressed || ViewModel == null)
            {
                return;
            }

            var offset = e.GetPosition(GamesList) - dragStart.Value;
            if (Math.Abs(offset.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(offset.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            dragStart = null;
            collapseSelectionOnRelease = false;
            var tile = (CollectionGameViewModel)((ListBoxItem)sender).DataContext;
            var selected = GamesList.SelectedItems.Cast<CollectionGameViewModel>().ToList();
            var dragged = selected.Contains(tile) ? selected : new List<CollectionGameViewModel> { tile };
            var ids = dragged.Select(t => t.Id).ToArray();
            DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(DragFormat, ids), DragDropEffects.Move);
            DropIndicator.Visibility = Visibility.Collapsed;
        }

        private void GamesList_DragOver(object sender, DragEventArgs e)
        {
            e.Handled = true;
            e.Effects = DragDropEffects.None;
            if (ViewModel == null || !ViewModel.CanReorder || !e.Data.GetDataPresent(DragFormat))
            {
                DropIndicator.Visibility = Visibility.Collapsed;
                return;
            }

            AutoScroll(e);
            var target = FindDropTarget(e.GetPosition(GamesList));
            if (target == null)
            {
                DropIndicator.Visibility = Visibility.Collapsed;
                return;
            }

            e.Effects = DragDropEffects.Move;
            var bounds = target.Bounds;
            Canvas.SetLeft(DropIndicator, (target.After ? bounds.Right + 2 : bounds.Left - 5));
            Canvas.SetTop(DropIndicator, bounds.Top);
            DropIndicator.Height = bounds.Height;
            DropIndicator.Visibility = Visibility.Visible;
        }

        private void GamesList_DragLeave(object sender, DragEventArgs e)
        {
            DropIndicator.Visibility = Visibility.Collapsed;
        }

        private void GamesList_Drop(object sender, DragEventArgs e)
        {
            DropIndicator.Visibility = Visibility.Collapsed;
            e.Handled = true;
            if (ViewModel == null || !(e.Data.GetData(DragFormat) is Guid[] ids))
            {
                return;
            }

            var target = FindDropTarget(e.GetPosition(GamesList));
            if (target != null)
            {
                ViewModel.MoveGames(ids, target.Tile.Id, target.After);
            }
        }

        private void AutoScroll(DragEventArgs e)
        {
            if (scrollViewer == null)
            {
                return;
            }

            var y = e.GetPosition(scrollViewer).Y;
            if (y < AutoScrollMargin)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - AutoScrollStep);
            }
            else if (y > scrollViewer.ViewportHeight - AutoScrollMargin)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + AutoScrollStep);
            }
        }

        /// <summary>
        /// Finds where a drop at this point lands: the tile on the pointer's row closest to it,
        /// and which side of that tile. Pointing past the last row means after the last game.
        /// </summary>
        private DropTarget FindDropTarget(Point point)
        {
            var tiles = new List<DropTarget>();
            foreach (var item in GamesList.Items)
            {
                if (GamesList.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement container && container.IsVisible)
                {
                    var topLeft = container.TranslatePoint(new Point(0, 0), GamesList);
                    tiles.Add(new DropTarget
                    {
                        Tile = (CollectionGameViewModel)item,
                        Bounds = new Rect(topLeft, new Size(container.ActualWidth, container.ActualHeight))
                    });
                }
            }

            if (tiles.Count == 0)
            {
                return null;
            }

            var row = tiles.Where(t => point.Y >= t.Bounds.Top && point.Y <= t.Bounds.Bottom + 8).ToList();
            if (row.Count == 0)
            {
                var last = tiles[tiles.Count - 1];
                if (point.Y > last.Bounds.Bottom)
                {
                    last.After = true;
                    return last;
                }

                return null;
            }

            var closest = row.OrderBy(t => Math.Abs(point.X - (t.Bounds.Left + t.Bounds.Width / 2))).First();
            closest.After = point.X > closest.Bounds.Left + closest.Bounds.Width / 2;
            return closest;
        }

        private class DropTarget
        {
            public CollectionGameViewModel Tile { get; set; }
            public Rect Bounds { get; set; }
            public bool After { get; set; }
        }

        private static void CollectItems(DependencyObject parent, List<ListBoxItem> items)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is ListBoxItem item)
                {
                    items.Add(item);
                }
                else
                {
                    CollectItems(child, items);
                }
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

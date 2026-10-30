using GameCollections.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace GameCollections.Views
{
    public partial class AddToCollectionView : UserControl
    {
        public AddToCollectionView()
        {
            InitializeComponent();
            Loaded += (s, e) => Dispatcher.BeginInvoke(DispatcherPriority.Input, new System.Action(SearchInput.FocusInput));
            SearchInput.PreviewKeyDown += SearchInput_PreviewKeyDown;
        }

        private AddToCollectionViewModel ViewModel => DataContext as AddToCollectionViewModel;

        private void SearchInput_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (ViewModel == null)
            {
                return;
            }

            if (e.Key == Key.Enter)
            {
                // Unhandled Enter falls through to the default (Save) button.
                e.Handled = ViewModel.HandleSearchEnter();
            }
            else if (e.Key == Key.Down && CollectionsList.Items.Count > 0)
            {
                FocusList();
                e.Handled = true;
            }
        }

        private void CollectionsList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space && CollectionsList.SelectedItem is CollectionToggleItem item)
            {
                item.Toggle();
                e.Handled = true;
            }
            else if (e.Key == Key.Up && CollectionsList.SelectedIndex <= 0)
            {
                SearchInput.FocusInput();
                e.Handled = true;
            }
        }

        private void FocusList()
        {
            if (CollectionsList.SelectedIndex < 0)
            {
                CollectionsList.SelectedIndex = 0;
            }

            CollectionsList.ScrollIntoView(CollectionsList.SelectedItem);
            CollectionsList.UpdateLayout();
            if (CollectionsList.ItemContainerGenerator.ContainerFromIndex(CollectionsList.SelectedIndex) is UIElement container)
            {
                container.Focus();
            }
        }
    }
}

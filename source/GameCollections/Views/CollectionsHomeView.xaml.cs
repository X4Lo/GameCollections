using GameCollections.ViewModels;
using System.Windows.Controls;
using System.Windows.Input;

namespace GameCollections.Views
{
    public partial class CollectionsHomeView : UserControl
    {
        public CollectionsHomeView()
        {
            InitializeComponent();
        }

        // Grid cards open on a single click, like tiles; buttons and the context menu handle their own clicks.
        private void CardItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem item && item.IsMouseOver && item.DataContext is CollectionCardViewModel card)
            {
                card.OpenCommand.Execute(null);
                e.Handled = true;
            }
        }

        private void RowItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem item && item.DataContext is CollectionCardViewModel card)
            {
                card.OpenCommand.Execute(null);
                e.Handled = true;
            }
        }

        private void Collections_KeyDown(object sender, KeyEventArgs e)
        {
            if (!(sender is ListBox list) || !(list.SelectedItem is CollectionCardViewModel card))
            {
                return;
            }

            switch (e.Key)
            {
                case Key.Enter:
                    card.OpenCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.F2:
                    card.EditCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.Delete:
                    card.DeleteCommand.Execute(null);
                    e.Handled = true;
                    break;
            }
        }
    }
}

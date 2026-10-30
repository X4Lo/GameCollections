using System;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace GameCollections.Views
{
    public partial class EditCollectionView : UserControl
    {
        public EditCollectionView()
        {
            InitializeComponent();
            Loaded += (s, e) => Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                TitleInput.Focus();
                Keyboard.Focus(TitleInput);
                TitleInput.SelectAll();
            }));
        }
    }
}

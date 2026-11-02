using System;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace GameCollections.Views
{
    public partial class RuleBuilderView : UserControl
    {
        public RuleBuilderView()
        {
            InitializeComponent();
        }

        private void PickerPopup_Opened(object sender, EventArgs e)
        {
            var popup = (Popup)sender;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (popup.Child != null && FindSearchBox(popup.Child) is SearchBox searchBox)
                {
                    searchBox.FocusInput();
                }
            }));
        }

        private static SearchBox FindSearchBox(Visual parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i) as Visual;
                if (child is SearchBox match)
                {
                    return match;
                }

                var nested = child == null ? null : FindSearchBox(child);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }
    }
}

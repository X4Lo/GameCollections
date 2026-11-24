using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace GameCollections.Settings
{
    public partial class GameCollectionsSettingsView : UserControl
    {
        public GameCollectionsSettingsView()
        {
            InitializeComponent();
        }

        // Opens the list of aspect ratio presets below the button. The button's Tag says which
        // fields it fills: "Collection" for collection posters, "Game" for game covers.
        private void PresetsButton_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            if (!(DataContext is GameCollectionsSettingsViewModel vm) || vm.Settings == null)
            {
                return;
            }

            var settings = vm.Settings;
            var forCollections = (string)button.Tag == "Collection";
            var width = forCollections ? settings.CollectionPosterAspectWidth : settings.GameCoverAspectWidth;
            var height = forCollections ? settings.CollectionPosterAspectHeight : settings.GameCoverAspectHeight;

            var menu = new ContextMenu
            {
                PlacementTarget = button,
                Placement = PlacementMode.Bottom
            };

            foreach (var preset in vm.AspectPresets)
            {
                var item = new MenuItem
                {
                    Header = preset.Label,
                    IsCheckable = false,
                    IsChecked = preset.Matches(width, height)
                };
                item.Click += (s, args) =>
                {
                    if (forCollections)
                    {
                        settings.CollectionPosterAspectWidth = preset.Width;
                        settings.CollectionPosterAspectHeight = preset.Height;
                    }
                    else
                    {
                        settings.GameCoverAspectWidth = preset.Width;
                        settings.GameCoverAspectHeight = preset.Height;
                    }
                };
                menu.Items.Add(item);
            }

            menu.IsOpen = true;
        }
    }
}

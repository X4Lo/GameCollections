using System;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace GameCollections.Views
{
    /// <summary>
    /// Sidebar icon drawn in the sidebar button's text colour, like the font glyphs Playnite and
    /// other extensions use. Themes change that colour when the button is hovered or selected, so the
    /// icon follows those shades. The picture only supplies the shape: its opacity is used as a mask,
    /// so the fainter cards behind the folder stay fainter.
    /// </summary>
    public static class ThemedSidebarIcon
    {
        public static FrameworkElement Create(string maskPath)
        {
            var mask = new BitmapImage();
            mask.BeginInit();
            mask.UriSource = new Uri(maskPath, UriKind.Absolute);
            mask.CacheOption = BitmapCacheOption.OnLoad;
            mask.EndInit();
            mask.Freeze();

            var icon = new Rectangle
            {
                OpacityMask = new ImageBrush(mask) { Stretch = Stretch.Uniform },
                SnapsToDevicePixels = true
            };

            // Foreground and font size are inherited from the sidebar button. The size matches a
            // glyph icon's, which is about as tall as the font size.
            var self = new RelativeSource(RelativeSourceMode.Self);
            icon.SetBinding(Shape.FillProperty, new Binding { Path = new PropertyPath(TextElement.ForegroundProperty), RelativeSource = self });
            icon.SetBinding(FrameworkElement.WidthProperty, new Binding { Path = new PropertyPath(TextElement.FontSizeProperty), RelativeSource = self });
            icon.SetBinding(FrameworkElement.HeightProperty, new Binding { Path = new PropertyPath(TextElement.FontSizeProperty), RelativeSource = self });
            return icon;
        }
    }
}

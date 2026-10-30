using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GameCollections.Views
{
    public partial class PosterImage : UserControl
    {
        public const string FolderGlyph = "\uef36";

        public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
            nameof(Source), typeof(ImageSource), typeof(PosterImage), new PropertyMetadata(null, OnSourceChanged));

        public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
            nameof(Glyph), typeof(string), typeof(PosterImage), new PropertyMetadata(FolderGlyph));

        public static readonly DependencyProperty GlyphSizeProperty = DependencyProperty.Register(
            nameof(GlyphSize), typeof(double), typeof(PosterImage), new PropertyMetadata(40.0));

        public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
            nameof(CornerRadius), typeof(CornerRadius), typeof(PosterImage), new PropertyMetadata(new CornerRadius(4)));

        public ImageSource Source
        {
            get => (ImageSource)GetValue(SourceProperty);
            set => SetValue(SourceProperty, value);
        }

        public string Glyph
        {
            get => (string)GetValue(GlyphProperty);
            set => SetValue(GlyphProperty, value);
        }

        public double GlyphSize
        {
            get => (double)GetValue(GlyphSizeProperty);
            set => SetValue(GlyphSizeProperty, value);
        }

        public CornerRadius CornerRadius
        {
            get => (CornerRadius)GetValue(CornerRadiusProperty);
            set => SetValue(CornerRadiusProperty, value);
        }

        public PosterImage()
        {
            InitializeComponent();
        }

        private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (PosterImage)d;
            var source = e.NewValue as ImageSource;
            control.ImageBorder.Background = source == null
                ? null
                : new ImageBrush(source) { Stretch = Stretch.UniformToFill };
        }
    }
}

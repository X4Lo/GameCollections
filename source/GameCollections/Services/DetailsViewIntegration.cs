using GameCollections.Settings;
using GameCollections.Views;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace GameCollections.Services
{
    /// <summary>
    /// Shows the collections list in themes that don't place the custom element. Playnite has no API for
    /// this, so the list is inserted into the theme's visual tree, below the game description. It stands
    /// aside when a theme places the element itself, and does nothing if it finds no suitable place.
    /// All members must be called on the UI thread.
    /// </summary>
    public class DetailsViewIntegration
    {
        // Named parts Playnite's details views use, in order of preference. The list goes below the first one found.
        private static readonly string[] AnchorNames = { "PART_ElemDescription", "PART_ElemNotes" };

        // Temporary, until these themes place the element themselves. Their description isn't in a plain panel,
        // so the list goes next to their Game Relations section instead, which they all bind to this proxy.
        private static readonly string[] ThemesUsingGameRelationsSection =
        {
            "Harmony_d49ef7bc-49de-4fd0-9a67-bd1f26b56047",
            "8b15c46a-90c2-4fe5-9ebb-1ab25ba7fcb1", // Helium
            "Nova_X_0a95b7a3-00e4-412d-b301-f2fa3f98dfad",
            "Stardust 2.0_1fb333b2-255b-43dd-aec1-8e2f2d5ea002"
        };
        private const string GameRelationsProxyName = "GameRelationsVisibilityProxy";

        private const int MaxSearchAttempts = 3;
        private const double MinTopSpacing = 10;
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly IPlayniteAPI playniteApi;
        private readonly GameCollectionsSettingsViewModel settings;
        private readonly Func<GameCollectionsControl> createControl;
        private readonly Func<Window> getWindow;
        private readonly DispatcherTimer timer;

        private GameCollectionsControl control;
        // What was added to the theme: the list itself, or the tab or box holding it.
        private FrameworkElement inserted;
        private RowDefinition insertedRow;
        private Game currentGame;
        private DesktopView? lastView;
        private int searchAttemptsLeft = MaxSearchAttempts;

        public DetailsViewIntegration(
            IPlayniteAPI playniteApi,
            GameCollectionsSettingsViewModel settings,
            Func<GameCollectionsControl> createControl,
            Func<Window> getWindow,
            Dispatcher dispatcher)
        {
            this.playniteApi = playniteApi;
            this.settings = settings;
            this.createControl = createControl;
            this.getWindow = getWindow;
            settings.SettingsChanged += Settings_SettingsChanged;

            // The overview of a library view only exists once that view is shown, and there is no event for
            // switching views. Reading the current view is cheap; the visual tree is only searched after a change.
            timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (s, e) => Update();
        }

        public void Start()
        {
            timer.Start();
        }

        public void Stop()
        {
            timer.Stop();
            settings.SettingsChanged -= Settings_SettingsChanged;
        }

        public void OnGameSelected(IList<Game> games)
        {
            currentGame = games != null && games.Count == 1 ? games[0] : null;
            if (control != null)
            {
                control.GameContext = currentGame;
            }

            searchAttemptsLeft = MaxSearchAttempts;
            Update();
        }

        /// <summary>
        /// Makes sure the list is where it should be right now. Never lets an error reach Playnite.
        /// </summary>
        public void Update()
        {
            try
            {
                var view = playniteApi.MainView.ActiveDesktopView;
                if (view != lastView)
                {
                    lastView = view;
                    searchAttemptsLeft = MaxSearchAttempts;
                }

                if (!settings.Settings.AddDetailsListToAnyTheme)
                {
                    Remove();
                    return;
                }

                // Already in a visible overview, which is the normal case, so no searching is needed.
                if (inserted?.Parent is FrameworkElement host && host.IsVisible)
                {
                    return;
                }

                if (searchAttemptsLeft > 0)
                {
                    searchAttemptsLeft--;
                    Search();
                }
            }
            catch (Exception e)
            {
                logger.Error(e, "Failed to add the collections list to the details view.");
                searchAttemptsLeft = 0;
            }
        }

        private void Settings_SettingsChanged(object sender, EventArgs e)
        {
            // The placement setting may have changed, so start again from nothing.
            Remove();
            searchAttemptsLeft = MaxSearchAttempts;
            Update();
        }

        private bool UsesGameRelationsSection()
        {
            return settings.Settings.UseThemeSpecificPlacement &&
                ThemesUsingGameRelationsSection.Contains(playniteApi.ApplicationSettings?.DesktopTheme, StringComparer.OrdinalIgnoreCase);
        }

        private void Search()
        {
            var window = getWindow();
            if (window == null)
            {
                return;
            }

            var result = new SearchResult();
            Walk(window, result);
            if (result.ThemeHasElement)
            {
                searchAttemptsLeft = 0;
                Remove();
                return;
            }

            if (UsesGameRelationsSection() && result.GameRelationsSection != null && InsertNextToSection(result.GameRelationsSection))
            {
                searchAttemptsLeft = 0;
                return;
            }

            foreach (var name in AnchorNames)
            {
                if (result.Anchors.TryGetValue(name, out var anchor) && InsertBelow(anchor))
                {
                    searchAttemptsLeft = 0;
                    return;
                }
            }
        }

        private void Walk(DependencyObject parent, SearchResult result)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent) && !result.ThemeHasElement; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                // The library's game lists hold most of the tree and never contain the overview.
                if (child is ListBoxItem)
                {
                    continue;
                }

                if (child is GameCollectionsControl existing)
                {
                    if (!existing.IsInjected)
                    {
                        result.ThemeHasElement = true;
                        return;
                    }

                    continue;
                }

                if (child is FrameworkElement element && element.Parent is FrameworkElement elementParent && elementParent.IsVisible)
                {
                    // An overview from a view that isn't showing can still be in the tree, so visibility of the parent counts.
                    if (Array.IndexOf(AnchorNames, element.Name) >= 0 && element.Parent is Panel && !result.Anchors.ContainsKey(element.Name))
                    {
                        result.Anchors[element.Name] = element;
                    }

                    if (result.GameRelationsSection == null &&
                        BindingOperations.GetBinding(element, UIElement.VisibilityProperty)?.ElementName == GameRelationsProxyName)
                    {
                        result.GameRelationsSection = element;
                    }
                }

                Walk(child, result);
            }
        }

        private GameCollectionsControl PrepareControl()
        {
            Remove();
            if (control == null)
            {
                control = createControl();
                control.IsInjected = true;

                // A tab can't stay selected once it's hidden, or the theme would show an empty panel.
                DependencyPropertyDescriptor.FromProperty(UIElement.VisibilityProperty, typeof(UIElement)).AddValueChanged(control, (s, e) =>
                {
                    if (inserted is TabItem tab && tab.IsSelected && control.Visibility != Visibility.Visible)
                    {
                        ((TabControl)tab.Parent).SelectedIndex = 0;
                    }
                });
            }

            control.Margin = new Thickness(0, 10, 0, 0);
            return control;
        }

        private bool InsertBelow(FrameworkElement anchor)
        {
            // A Grid would need a row of its own, and changing a theme's rows could break its layout.
            if (!(anchor.Parent is Panel panel) || panel is Grid || panel.IsItemsHost)
            {
                return false;
            }

            var list = PrepareControl();
            var title = FindTitle(anchor);
            list.SetHeader(BuildHeader(title, FindSeparator(anchor)));

            // Keeps the theme's spacing, while making sure the list doesn't touch the description.
            var top = Math.Max(anchor.Margin.Top, title == null || title.Margin.Top < MinTopSpacing ? MinTopSpacing : 0);
            list.Margin = new Thickness(anchor.Margin.Left, top, anchor.Margin.Right, anchor.Margin.Bottom);
            if (panel is DockPanel)
            {
                DockPanel.SetDock(list, DockPanel.GetDock(anchor));
            }

            panel.Children.Insert(panel.Children.IndexOf(anchor) + 1, list);
            inserted = list;
            list.GameContext = currentGame;
            return true;
        }

        /// <summary>
        /// Adds the list as a sibling of the theme's Game Relations section, in the same form as that section.
        /// That section's own visibility doesn't matter: only its parent needs to be showing.
        /// </summary>
        private bool InsertNextToSection(FrameworkElement section)
        {
            if (section.Parent is TabControl tabControl)
            {
                var list = PrepareControl();
                list.SetHeader();
                list.Margin = new Thickness(0, 15, 0, 0);
                var tab = new TabItem { Header = Loc.Get("LOCGameCollections_DetailsHeader"), Content = list };
                BindVisibilityToList(tab, list);
                tabControl.Items.Insert(tabControl.Items.IndexOf(section), tab);
                inserted = tab;
                list.GameContext = currentGame;
                return true;
            }

            if (section.Parent is Grid grid)
            {
                // The sections of a Grid view each have a row, so the list gets a new one at the end.
                var list = PrepareControl();
                FrameworkElement element = list;
                if (section is Expander sectionExpander)
                {
                    // Grid view sections are collapsible, with the title in the expander's header.
                    var separator = CloneSeparator(FindSeparator(section));
                    if (separator != null)
                    {
                        list.SetHeader(separator);
                    }
                    else
                    {
                        list.SetHeader();
                    }

                    list.Margin = new Thickness(0);
                    var expander = new Expander
                    {
                        Style = sectionExpander.Style,
                        Margin = sectionExpander.Margin,
                        IsExpanded = true,
                        Header = (object)CloneTitle(FindTitle(section)) ?? Loc.Get("LOCGameCollections_DetailsHeader"),
                        Content = list
                    };
                    BindVisibilityToList(expander, list);
                    element = expander;
                }
                else
                {
                    list.SetHeader(BuildHeader(FindTitle(section), FindSeparator(section)));
                    list.Margin = section.Margin;
                }

                insertedRow = new RowDefinition { Height = GridLength.Auto };
                grid.RowDefinitions.Add(insertedRow);
                Grid.SetRow(element, grid.RowDefinitions.Count - 1);
                grid.Children.Add(element);
                inserted = element;
                list.GameContext = currentGame;
                return true;
            }

            if (section.Parent is Panel panel && !panel.IsItemsHost)
            {
                var list = PrepareControl();
                list.SetHeader(BuildHeader(FindTitle(section), FindSeparator(section)));
                FrameworkElement element = list;
                if (section is Border sectionBorder)
                {
                    // Keeps the theme's rounded box around the section. Its padding comes from the style.
                    var box = new Border { Style = sectionBorder.Style, Margin = sectionBorder.Margin, Child = list };
                    BindVisibilityToList(box, list);
                    list.Margin = new Thickness(0);
                    element = box;
                }
                else
                {
                    list.Margin = section.Margin;
                }

                panel.Children.Insert(panel.Children.IndexOf(section), element);
                inserted = element;
                list.GameContext = currentGame;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Finds the title of a theme's section. Hidden text, such as these themes' visibility proxies, doesn't count.
        /// </summary>
        private static FrameworkElement FindTitle(object root)
        {
            if (root is Expander expander && expander.Header is FrameworkElement header)
            {
                return header;
            }

            return FindFirst(root, e => (e is TextBlock || e is Label) && e.Visibility != Visibility.Collapsed);
        }

        private static Separator FindSeparator(object root)
        {
            return FindFirst(root, e => e is Separator) as Separator;
        }

        private static FrameworkElement FindFirst(object root, Func<FrameworkElement, bool> predicate)
        {
            if (root is FrameworkElement element && predicate(element))
            {
                return element;
            }

            if (root is DependencyObject parent)
            {
                foreach (var child in LogicalTreeHelper.GetChildren(parent))
                {
                    var match = FindFirst(child, predicate);
                    if (match != null)
                    {
                        return match;
                    }
                }
            }

            return null;
        }

        private static UIElement[] BuildHeader(FrameworkElement title, Separator separator)
        {
            var header = new List<UIElement>();
            var titleClone = CloneTitle(title);
            if (titleClone != null)
            {
                header.Add(titleClone);
            }

            var separatorClone = CloneSeparator(separator);
            if (separatorClone != null)
            {
                header.Add(separatorClone);
            }

            // A theme with no title of its own to copy still gets one.
            if (titleClone == null)
            {
                header.Insert(0, new TextBlock
                {
                    Text = Loc.Get("LOCGameCollections_DetailsHeader"),
                    Style = ResourceProvider.GetResource("BaseTextBlockStyle") as Style,
                    FontWeight = FontWeights.SemiBold
                });
            }

            return header.ToArray();
        }

        /// <summary>
        /// Copies how a theme's section title looks, using its current values: a theme's own values
        /// can't be reused as they are, because many are set directly on the title rather than in a style.
        /// </summary>
        private static FrameworkElement CloneTitle(FrameworkElement title)
        {
            var text = Loc.Get("LOCGameCollections_DetailsHeader");
            switch (title)
            {
                case TextBlock block:
                    return new TextBlock
                    {
                        Text = text,
                        Style = block.Style,
                        FontSize = block.FontSize,
                        FontWeight = block.FontWeight,
                        FontFamily = block.FontFamily,
                        Foreground = block.Foreground,
                        Margin = block.Margin,
                        Cursor = block.Cursor
                    };
                case Label label:
                    return new Label
                    {
                        Content = text,
                        Style = label.Style,
                        FontSize = label.FontSize,
                        FontWeight = label.FontWeight,
                        FontFamily = label.FontFamily,
                        Foreground = label.Foreground,
                        Padding = label.Padding,
                        Margin = label.Margin,
                        HorizontalAlignment = label.HorizontalAlignment
                    };
                default:
                    return null;
            }
        }

        private static Separator CloneSeparator(Separator separator)
        {
            return separator == null
                ? null
                : new Separator { Style = separator.Style, Background = separator.Background, Margin = separator.Margin };
        }

        private static void BindVisibilityToList(FrameworkElement element, GameCollectionsControl list)
        {
            element.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(UIElement.Visibility)) { Source = list });
        }

        private void Remove()
        {
            if (inserted is TabItem tab)
            {
                tab.Content = null;
                (tab.Parent as TabControl)?.Items.Remove(tab);
            }
            else
            {
                if (inserted is Border box)
                {
                    box.Child = null;
                }
                else if (inserted is Expander expander)
                {
                    expander.Content = null;
                }

                var panel = inserted?.Parent as Panel;
                panel?.Children.Remove(inserted);
                if (insertedRow != null)
                {
                    (panel as Grid)?.RowDefinitions.Remove(insertedRow);
                }
            }

            inserted = null;
            insertedRow = null;
        }

        private class SearchResult
        {
            public Dictionary<string, FrameworkElement> Anchors { get; } = new Dictionary<string, FrameworkElement>();
            public FrameworkElement GameRelationsSection { get; set; }
            public bool ThemeHasElement { get; set; }
        }
    }
}

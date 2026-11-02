using GameCollections.Settings;
using GameCollections.Views;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
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
        private const int MaxSearchAttempts = 3;
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly IPlayniteAPI playniteApi;
        private readonly GameCollectionsSettingsViewModel settings;
        private readonly Func<GameCollectionsControl> createControl;
        private readonly Func<Window> getWindow;
        private readonly DispatcherTimer timer;

        private GameCollectionsControl control;
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
                if (control?.Parent is Panel host && host.IsVisible)
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
            searchAttemptsLeft = MaxSearchAttempts;
            Update();
        }

        private void Search()
        {
            var window = getWindow();
            if (window == null)
            {
                return;
            }

            var found = new Dictionary<string, FrameworkElement>();
            var themeHasElement = false;
            Walk(window, found, ref themeHasElement);
            if (themeHasElement)
            {
                searchAttemptsLeft = 0;
                Remove();
                return;
            }

            foreach (var name in AnchorNames)
            {
                if (found.TryGetValue(name, out var anchor) && Insert(anchor))
                {
                    searchAttemptsLeft = 0;
                    return;
                }
            }
        }

        private void Walk(DependencyObject parent, Dictionary<string, FrameworkElement> found, ref bool themeHasElement)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent) && !themeHasElement; i++)
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
                        themeHasElement = true;
                        return;
                    }

                    continue;
                }

                // An overview from a view that isn't showing can still be in the tree, so visibility of the parent counts.
                if (child is FrameworkElement element && Array.IndexOf(AnchorNames, element.Name) >= 0 &&
                    !found.ContainsKey(element.Name) && element.Parent is Panel panel && panel.IsVisible)
                {
                    found[element.Name] = element;
                }

                Walk(child, found, ref themeHasElement);
            }
        }

        private bool Insert(FrameworkElement anchor)
        {
            // A Grid would need a row of its own, and changing a theme's rows could break its layout.
            if (!(anchor.Parent is Panel panel) || panel is Grid || panel.IsItemsHost)
            {
                return false;
            }

            Remove();
            if (control == null)
            {
                control = createControl();
                control.IsInjected = true;
                control.Margin = new Thickness(0, 10, 0, 0);
            }

            if (panel is DockPanel)
            {
                DockPanel.SetDock(control, DockPanel.GetDock(anchor));
            }

            panel.Children.Insert(panel.Children.IndexOf(anchor) + 1, control);
            control.GameContext = currentGame;
            return true;
        }

        private void Remove()
        {
            (control?.Parent as Panel)?.Children.Remove(control);
        }
    }
}

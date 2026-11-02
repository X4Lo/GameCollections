using Playnite.SDK;
using Playnite.SDK.Plugins;
using System;
using System.Collections;
using System.Windows;
using System.Windows.Input;

namespace GameCollections.Services
{
    /// <summary>
    /// Switches Playnite to a plugin's sidebar view. The SDK has no API for this, so it does what Playnite's
    /// own SwitchToLibraryView does: run the Command of the entry in the main view model's SidebarItems.
    /// Those are Playnite internals that could change, so callers must handle this returning false.
    /// </summary>
    public static class SidebarNavigator
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        public static bool TryActivate(Window mainWindow, SidebarItem item)
        {
            try
            {
                var viewModel = mainWindow?.DataContext;
                var entries = viewModel?.GetType().GetProperty("SidebarItems")?.GetValue(viewModel) as IEnumerable;
                if (entries == null)
                {
                    logger.Warn("Could not find Playnite's sidebar items.");
                    return false;
                }

                foreach (var entry in entries)
                {
                    var type = entry.GetType();
                    var sideItem = type.GetProperty("SideItem")?.GetValue(entry) as SidebarItem;
                    if (sideItem == null || (!ReferenceEquals(sideItem, item) && !(sideItem.Type == item.Type && sideItem.Title == item.Title)))
                    {
                        continue;
                    }

                    if (type.GetProperty("Command")?.GetValue(entry) is ICommand command)
                    {
                        command.Execute(null);
                        return true;
                    }
                }

                logger.Warn("Could not find this extension's sidebar entry.");
            }
            catch (Exception e)
            {
                logger.Error(e, "Failed to switch to the sidebar view.");
            }

            return false;
        }
    }
}

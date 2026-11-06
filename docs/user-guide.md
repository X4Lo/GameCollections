# User guide

## Adding games to collections

1. Select one or more games in the library, right-click, and choose **Add to Collection…**.
2. Type in the search box to filter your collections, then tick or untick them.
   - With several games selected, a collection that contains only some of them shows a mixed (■) check.
     Clicking it cycles through *remove from all → add to all → leave as is*.
3. If no collection matches what you typed, press **Enter** or click **Create "…"** to create one with that name.
   It is ticked automatically. Names are unique, ignoring capitals and extra spaces.
4. Click **Save** (or press **Enter**) to apply. **Cancel** or **Esc** discards every change, including new collections.

Keyboard: **↓/↑** move between the search box and the list, **Space** toggles the highlighted collection.

## The Collections page

Click **Collections** in the sidebar. The page keeps its state (open collection, search, sort) while Playnite is running.

- **Search** filters by title.
- **Grid / List** toggle (top right) switches views; your choice is remembered between sessions.
- **New collection** opens the create dialog.
- Open a collection with a click (grid) or a double-click (list).
- Edit or delete from the right-click menu, the buttons on list rows, or the keyboard:
  **Enter** opens, **F2** edits, **Delete** deletes (after confirmation).

Deleting a collection never deletes or changes any games.

## Creating and editing a collection

| Field       | Notes |
|-------------|-------|
| Title       | Required, up to 100 characters, unique (case-insensitive). |
| Description | Optional, up to 2000 characters. |
| Rating      | 0–10 in steps of 0.5, shown as `8.5/10`. |
| Poster      | Optional PNG, JPG, BMP, GIF or TIFF. The image is copied into the extension's data, so moving the original later is fine. **Remove** goes back to the automatic poster. |

When there's no custom poster, the cover of the first game in the collection is used; with no cover either,
a placeholder is shown.

## Dynamic collections

A dynamic collection fills itself from rules. To make one, choose **Dynamic** under **Type** when creating or
editing a collection.

- A **block** is a group of conditions. A game must meet **every** condition in a block.
- Between blocks, choose **AND** or **OR**. Blocks combine from top to bottom, so
  *A OR B AND C* means *(A or B) and C*.
- The bar at the bottom shows how many games currently match, and some of their names.
- Hidden games are never included.

| You can filter by | Conditions |
|---|---|
| Name | includes, does not include |
| Platform, genres, developers, publishers, categories, features, tags, series | includes any of, all of or none of; is empty |
| Completion status | includes any of or none of; is empty |
| Release date, last played | after, before, between; in the last or older than a number of days; has no date |
| User, critic and community score | at least, at most, between; is empty |
| Time played (hours), play count | at least, at most, between |

Dates can be a year, a month or a day (`2005`, `2005-03` or `2005-03-14`). *After 2005* starts at 2006, and
*between 1995 and 1999* includes both years.

**Example:** Block 1: *Platform includes any of Windows PC* and *Categories includes any of Boomer shooter*.
OR Block 2: *Tags includes any of Action*.

**When it updates** (nothing you need to do):

| When | What is checked |
|---|---|
| You create or edit the collection | That collection, right away |
| A game is added or changed, including after playing it | Only those games, about a second later |
| A game is removed from Playnite | Removed from collections right away |
| Playnite starts (after 5 seconds) and after a library update | Every dynamic collection, in the background |
| Every hour, if a rule uses "in the last" or "older than" days | Every dynamic collection, in the background |
| You click **Refresh now** on its page | That collection, right away |

You can't add or remove a dynamic collection's games by hand, but you can reorder them; the order is kept when it
updates, and new games go at the end. Switching a dynamic collection to manual keeps its current games. Switching a
manual collection to dynamic replaces its games with the matches, and the dialog warns you first.

If a tag, genre or similar is deleted from Playnite, it stops counting in rules. If a condition is left with no
values, the rules match nothing until you edit them, so a broken rule can never pull in your whole library.

## Game details

The selected game's collections appear below the description in Playnite's game details, each with its poster.
Click one to open it in a window, where **Open in Collections** takes you to its page in the Collections section. The list is hidden when the game is in no collection or several games are selected.

In the extension's settings:

- **Show collections in details view** turns the automatic list on or off.
- **Use special placement for Stardust, Nova X, Helium and Harmony** (off by default) is a temporary fix for these
  themes, whose layouts don't have a normal spot for the list. It goes next to their Game Relations section: as a
  **Collections** tab in Harmony's Details view, a section in the others, and at the end of the panel in Grid view.
- **Display dynamic collections in details view** lists dynamic collections as well as manual ones.

The list is added to the theme's layout automatically, because Playnite has no official way to do this. It needs the
theme to keep Playnite's standard description or notes section in a simple panel, so a heavily customized theme may
not show it. List view has no details panel, so it doesn't appear there. Theme authors can see
[Theme integration](theme-integration.md).

## A collection's page

- Header with poster, title, rating, game count and description, plus **Edit**, **Select in library** and **Delete**.
  Dynamic collections also show their rules and a **Refresh now** button.
- The arrow button at the header's top right **collapses the header** to just the title, game count and Dynamic badge,
  leaving more room for games. Press it again to expand it. This is remembered for all collections.
- **Search** the collection's games by name.
- **Sort** by collection order (the order you arranged, or the order games were added), name, release date,
  last played, time played or date added, ascending or descending.
- **Group by** shows the games in labelled groups, such as by platform, series, genre or release year. Each collection
  remembers its own choice. A game with several values, such as two genres, appears in each group. Games with no
  value go in a **Not set** group at the end.
  - Click a group's header to collapse or expand it, or use **Collapse all** / **Expand all** next to Group by.
    Collapsed groups reset when you leave the page or change the grouping.
  - Collapsing a group deselects its games, unless they also appear in a group that's still open.
- **Card size**, the slider at the right of the toolbar, makes the game cards bigger or smaller. It's remembered for
  all collections.
- **Installed only** hides games that aren't installed (uninstalled games are dimmed otherwise).
- For each game (right-click, or keyboard):
  - **Play** / **Install** – double-click or **Enter**.
  - **Show in library** – selects the game in Playnite's library view.
  - **Remove from collection** – **Delete**. Works on several selected games at once (Ctrl/Shift-click).
    Not available in dynamic collections.
- **Reorder** while sorted by collection order, ascending, with no grouping: drag games (several at once if selected), use
  **Move to top / earlier / later / to end** in the right-click menu, or press **Alt+arrow keys**.

## Where data is stored

In Playnite's `ExtensionsData\3f6b8d2e-5c41-4a7e-9e0b-6d2f1a8c7b54` folder:

- `collections.json` – your collections. `collections.json.bak` holds the previous save.
- `Posters\` – copies of custom posters.
- `config.json` – extension settings.
- `collections.json.v<number>.bak` – a copy from before an update changed the file's format.

Back up this folder to back up your collections. If `collections.json` ever becomes unreadable, it is renamed to
`collections.unreadable-<date>.json`, the backup is loaded instead, and Playnite shows a notification.

## Known limitations

These come from Playnite's extension API:

- Extensions can't open a game's details page directly. **Show in library** selects the game and switches to
  the library view, where its details appear. If a library filter hides the game, Playnite can't select it –
  clear the filter and try again.
- Extensions have no official way to switch to their own sidebar view, so **Open in Collections** uses Playnite's
  internals. If that ever stops working, the page is still ready, and a notification tells you to click Collections.
- Extensions have no official way to add to a theme's details view, so the list is inserted into the theme's layout.
  It may not appear in heavily customized themes.
- Extensions can't embed Playnite's own library grid, so a collection's page uses its own game grid.
- The extension targets Desktop mode. Fullscreen mode doesn't show sidebar views or game context menus from extensions.

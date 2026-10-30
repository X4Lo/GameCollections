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

## A collection's page

- Header with poster, title, rating, game count and description, plus **Edit**, **Select in library** and **Delete**.
- **Search** the collection's games by name.
- **Sort** by collection order (the order games were added), name, release date, last played, time played
  or date added, ascending or descending.
- **Installed only** hides games that aren't installed (uninstalled games are dimmed otherwise).
- For each game (right-click, or keyboard):
  - **Play** / **Install** – double-click or **Enter**.
  - **Show in library** – selects the game in Playnite's library view.
  - **Remove from collection** – **Delete**. Works on several selected games at once (Ctrl/Shift-click).

## Where data is stored

In Playnite's `ExtensionsData\3f6b8d2e-5c41-4a7e-9e0b-6d2f1a8c7b54` folder:

- `collections.json` – your collections. `collections.json.bak` holds the previous save.
- `Posters\` – copies of custom posters.
- `config.json` – extension settings.

Back up this folder to back up your collections. If `collections.json` ever becomes unreadable, it is renamed to
`collections.unreadable-<date>.json`, the backup is loaded instead, and Playnite shows a notification.

## Known limitations

These come from Playnite's extension API:

- Extensions can't open a game's details page directly. **Show in library** selects the game and switches to
  the library view, where its details appear. If a library filter hides the game, Playnite can't select it –
  clear the filter and try again.
- Extensions can't embed Playnite's own library grid, so a collection's page uses its own game grid.
- The extension targets Desktop mode. Fullscreen mode doesn't show sidebar views or game context menus from extensions.

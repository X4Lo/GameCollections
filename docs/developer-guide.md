# Developer guide

How Game Collections works and how to change it. For a short overview of the layout and components, see
[architecture.md](architecture.md). For behaviour as users see it, see [user-guide.md](user-guide.md).

## Getting started

You need Windows and a .NET SDK (6.0 or later recommended). The .NET Framework 4.6.2 reference assemblies come from
NuGet, so no targeting pack is needed. The project targets `net462` with C# 7.3, matching Playnite extensions, and
references `PlayniteSDK` 6.11.0.

```
dotnet build source/GameCollections.sln -c Debug
```

To run it, add `source/GameCollections/bin/Debug` under Playnite's *Settings → For developers → Custom extension
folders* and restart Playnite after each build. A Release build also creates the `.pext` in `dist/`.

Logs go to Playnite's `extensions.log`. Data is in `ExtensionsData\3f6b8d2e-5c41-4a7e-9e0b-6d2f1a8c7b54`.

## How the pieces fit together

```
GameCollectionsPlugin            Playnite entry point and event wiring
 ├─ CollectionManager            the only code that changes collections
 │   ├─ CollectionRepository     collections.json: load, recover, atomic save
 │   ├─ PosterStorage            Posters\ folder
 │   └─ RuleEngine               rules -> predicate (static, no state)
 ├─ DynamicCollectionRefresher   when dynamic collections are re-evaluated
 ├─ DetailsViewIntegration       inserts the list into themes without the element
 ├─ DialogService                dialogs in Playnite-created windows
 └─ CollectionsRootViewModel     sidebar state; created on first use, kept for the session
     ├─ CollectionsHomeViewModel       -> CollectionCardViewModel
     └─ CollectionDetailsViewModel     -> CollectionGameViewModel
```

### Rules to keep

- **All changes go through `CollectionManager`.** Each change saves and then raises `CollectionsChanged` with the
  affected IDs. View models and `GameCollectionsControl` react to that event, and don't update themselves.
- **`CollectionManager` is UI-thread only.** Playnite's database events can arrive on other threads, so the plugin
  sends them through `RunOnUiThread`. Only the static `CollectionManager.FindMatches` and `RuleEngine` are safe on a
  background thread.
- **Collections store game IDs only.** Metadata is always read from Playnite's database.
- **Views use theme resources only**, such as `TextBrush`, `NormalBrush` and `FontIcoFont`, through `DynamicResource`,
  so light and dark themes both work. The one fixed colour is the dynamic badge in `Styles.xaml`.
- **No user-visible text in code or XAML.** Add a `LOCGameCollections_*` key to `Localization/en_US.xaml`, and read it
  with `Loc` in code or `DynamicResource` in XAML.

## Startup and lifecycle

1. **Constructor:** creates settings, services and the dialog host, and registers settings and custom element support.
   The game database isn't ready yet, so nothing is loaded.
2. **`OnApplicationStarted`:** loads collections, prunes games no longer in the library, deletes unreferenced posters,
   subscribes to database events, then starts the refresher and, in Desktop mode, the details integration.
3. **`GetSidebarItems` → `Opened`:** builds a new view each time around the same `CollectionsRootViewModel`. That is
   why the open page, search and sort survive leaving the sidebar page.
4. **`OnApplicationStopped`:** unsubscribes and stops the timers.

## Data

### collections.json

`CollectionsDataFile` holds `SchemaVersion` and a list of `GameCollection`. See the example in
[architecture.md](architecture.md#data-model).

**Saving** (`CollectionRepository.Save`) writes a temp file, then swaps it in with `File.Replace`, keeping the previous
version as `collections.json.bak`. It falls back to copying where `File.Replace` isn't supported.

**Loading** moves an unreadable file aside as `collections.unreadable-<timestamp>.json` so a later save can't overwrite
it, then tries the backup, and returns a warning that the plugin shows as a notification. `Sanitize` repairs damaged
records, such as missing IDs, duplicate IDs, or out-of-range ratings.

### Versioning and migrations

The file has a `SchemaVersion`, and `CollectionsDataMigrator` runs on every load, before the JSON becomes model objects:

1. It reads `SchemaVersion`, or assumes 1 if it's missing.
2. It applies each step from that version up to `CurrentVersion`, in order, so someone updating from 1 to 7 runs
   1→2, 2→3 and so on.
3. `CollectionRepository` copies the original to `collections.json.v<old version>.bak` and saves the migrated data.

If a step fails or is missing, the file counts as unreadable: it's moved aside and the normal backup is tried, also
through the migrator. A file from a **newer** version isn't changed by the migrator. It loads as well as it can, a copy
is kept, and the user gets a warning, because properties this version doesn't know about are lost on the next save.

**To change the format:**

1. Change the model, and repair or default the value in `Sanitize`.
2. Increase `CollectionsDataMigrator.CurrentVersion`.
3. Add a step to `Steps` with `FromVersion` set to the previous version. It edits the raw `JObject`:

   ```csharp
   // 2 -> 3 (extension 1.2.0): renamed Rating to Score.
   new DataMigrationStep(2, data =>
   {
       foreach (var collection in data["Collections"])
       {
           collection["Score"] = collection["Rating"];
           ((JObject)collection).Remove("Rating");
       }
   })
   ```

4. Add a line to the version history below.

Steps use raw JSON so they keep working after the model changes. Never edit a released step, rename a property without
a step, or renumber an enum value, because enums are stored as numbers. A new property with a safe default still needs
a version bump only if old files need converting, but bumping keeps the history clear.

Version history: **1** was the initial format (extension 1.0.0); **2** added `GameCollection.Rules` (extension 1.1.0).

`config.json` (settings) isn't versioned. Playnite reads it directly, and new settings simply take their defaults.

### Settings

`GameCollectionsSettings` is saved by Playnite to `config.json`. Add a property using `SetValue`, and bind to
`Settings.<Name>` in `GameCollectionsSettingsView.xaml`. Mark state that shouldn't be saved with `[DontSerialize]`.
Themes can read these properties through `{PluginSettings Plugin=GameCollections, Path=<Name>}`.
`SettingsChanged` fires after the settings dialog is saved or cancelled.

## Manual order

The order is simply the order of `GameCollection.GameIds`, which is also what the automatic poster uses (first game).
`CollectionManager.MoveGames(collectionId, gameIds, anchorId, after)` is the single move operation. Selected games keep
their relative order.

On the page, `CollectionDetailsViewModel.CanReorder` requires the *Collection order, ascending* sort with no grouping,
because a moved game wouldn't stay put otherwise. Moves relate to *visible* games, so with a filter active, "move earlier" passes the
previous visible game.

Dragging is in `CollectionDetailsView.xaml.cs`. A click on an already selected game in a multi-selection delays
collapsing the selection until the mouse button is released, otherwise several games couldn't be dragged.
`FindDropTarget` picks the closest tile on the pointer's row and which side of it to use.

## Grouping

`GameCollection.GroupBy` holds the choice, set through `CollectionManager.SetGroupBy`. It has a safe default, so old
files need no migration. `GameGroupDescription` returns a list of names for a multi-value field, which makes WPF put
the same tile in several groups. That's why `RequestVisibleCovers` reads tiles from the visual tree: with groups,
`ContainerFromItem` doesn't reach them. To add a grouping, add it to the end of `GameGroupField`, then to
`GroupNameFromItem` and `GroupOptions`.

## Dynamic collections

A collection is dynamic when `Rules` is not null. `GameIds` stores the latest matches, so no page has to search the
library.

### Evaluation

`RuleEngine.Compile(rules, now)` returns a `Func<Game, bool>`:

- Conditions in a block are ANDed.
- Blocks combine strictly from top to bottom using each block's `Join`, without AND taking priority over OR. This
  matches how they read in the editor.
- Hidden games never match.
- Rules that aren't complete compile to "match nothing", so damaged data can't fill a collection with the whole library.
- Compile once and reuse the predicate. It prepares `HashSet`s and parsed dates up front.

`MergeMatches` applies results: games still matching keep their position, which preserves manual order, and new ones go
at the end.

### When it runs

`DynamicCollectionRefresher` owns the timing:

| Trigger | Method | Scope and thread |
|---|---|---|
| Collection created or edited | `CreateCollection` / `UpdateCollection` | One collection, UI thread |
| Game added or updated | `NotifyGamesChanged` → `ApplyGameChanges` | Changed games only, 1 s debounce, UI thread |
| Startup | `Start` → `RefreshAllAsync` | All, after 5 s, background |
| Library update finished | `ScheduleFullRefresh` | All, after 1 s, background |
| Hourly, if a rule uses relative days | `RefreshAllAsync` | All, background |
| **Refresh now** button | `RefreshCollection` | One collection, UI thread |

Two details prevent wrong results from a background search:

- Each result carries the `DynamicRules` object it used. `ApplyMatches` skips it if the collection now has a different
  one, because every save of the editor creates a new object.
- Game changes arriving during a search stay queued and are applied after its results.

### Adding a rule field

1. Add it to the end of `RuleField` in `Models/DynamicRules.cs`.
2. In `RuleEngine`, update `GetValueKind` and add it to `GetItemIds` or `GetNumber` (or the date selection in
   `CompileDate`).
3. For a list field, return its items from `RuleText.LoadItems`.
4. Add it to `FieldOrder` in `RuleConditionViewModel`, plus `UnitText` if it has a unit.
5. Add `LOCGameCollections_RuleField<EnumName>` to the strings. The key is built from the enum name.

`RuleText.Describe` and the editor pick it up from there.

### Adding an operator

Add it to the end of `RuleOperator`, offer it in `RuleEngine.GetOperators`, handle it in the matching `Compile*` method
and in `IsComplete`, and add `LOCGameCollections_RuleOperator<EnumName>`. If it needs different inputs, adjust the
`Show*` properties of `RuleConditionViewModel`, `ToModel` and `RuleBuilderView.xaml`.

### Editor

`DynamicRulesViewModel` → `RuleBlockViewModel` → `RuleConditionViewModel`. Values stay as text while editing, and
`ToModel()` produces the saved form, so `RuleEngine.IsComplete` is the one source of validation. The preview searches
the library after a 250 ms pause, on the UI thread, which is fast enough at the sizes tested (see Performance).

## Game details

For theme authors, see [theme-integration.md](theme-integration.md). `GameCollectionsControl` is a `PluginUserControl`. It reads `GameContext`, uses the cached
`CollectionManager.GetCollectionsForGame` lookup, and collapses when there's nothing to show. It subscribes to events
only while loaded, so a discarded instance can be collected.

It reaches the screen in one of two ways:

1. **Custom element.** A theme adds `<ContentControl x:Name="GameCollections_CollectionsControl" />`, and Playnite
   calls `GetGameViewControl`. This is the supported route and works in Desktop mode only.
2. **`DetailsViewIntegration`.** Playnite has no API for adding to a theme, so this walks the main window's visual tree
   for `PART_ElemDescription`, or else `PART_ElemNotes`, and inserts one control after it.

The integration's safeguards:

- It stands aside when it finds a theme-placed control (`IsInjected` is false).
- It doesn't insert into a `Grid`, which would need a new row, or an items host.
- It searches only after a game selection, a view change or a settings change, up to 3 times each, and skips
  `ListBoxItem`s, which hold most of the tree. A 1 second timer only compares `ActiveDesktopView`.
- Every error is caught and logged, and stops further searching until the next trigger.

Behind the `UseThemeSpecificPlacement` setting (off by default), and only when `ApplicationSettings.DesktopTheme` is in
`ThemesUsingGameRelationsSection`, it instead goes next to the element whose `Visibility` binding names
`GameRelationsVisibilityProxy`, in that element's form: a new `TabItem` beside a tab, a new row in a `Grid`, a copy of a
`Border`'s style, or a plain sibling in a panel. It only needs that element's parent to be visible, so it works without
Game Relations installed. This is a temporary workaround that depends on these themes' XAML. The injected control stays
subscribed to events because a control in an unselected tab never loads.

The section title is copied from the theme, not defined here: `FindTitle` and `FindSeparator` look in the neighbouring
section (the anchor or Game Relations section), and `CloneTitle` copies its current style values. A section that's an
`Expander` gets an `Expander` of its own with the title in its header.

**This relies on Playnite's internal part names.** Check it after Playnite updates. The default layouts are
`Themes/Desktop/Default/Views/DetailsViewGameOverview.xaml` and `GridViewGameOverview.xaml` in Playnite's repository.

Clicking a collection calls `CollectionsRootViewModel.OpenCollectionInWindow`. The page uses `CloseWindow` to know it's
in a window, where **Open in Collections** replaces **Select in library**. That button opens the page in the sidebar
view's state, closes the window, then calls `SidebarNavigator.TryActivate`.

The SDK has no way to switch sidebar views, so `SidebarNavigator` uses reflection to run the `Command` of the matching
entry in `SidebarItems` on the main window's view model, as Playnite's `SwitchToLibraryView` does. **This relies on
Playnite's internals, so check it after Playnite updates.** If it fails, a notification tells the user to click
Collections, and the page is already open there.

## Images

`ImageLoader` decodes at the size needed, off the UI thread, with limited concurrency, and returns frozen bitmaps. Its
cache key includes the path, width, modified time and file size, so a changed file is reloaded. Pass the display width
to it rather than loading full-size artwork. Game covers on a collection's page load only near the viewport.

## Performance

Measured in the test harness, not in Playnite: 20 full searches of 20,000 synthetic games took about 0.85 s in total.
Keep these costs in mind:

- Nothing should scan the library on the UI thread when a game is selected.
- Writes happen only when something changed, because `collections.json` is rewritten in full each time.
- Don't subscribe to `ItemUpdated` for per-game work without a debounce. Library updates send many at once.

## Testing

**There's no test project in the repository yet.** The checks so far ran in a throwaway harness outside it, against a
fake Playnite API built with `RealProxy`. Its limits:

- `Playnite.SDK.Data.Serialization` and `API.Instance` are normally set by Playnite, so any test host has to supply
  them. The SDK alone throws `NullReferenceException`.
- It doesn't load a real theme, so screenshots have wrong glyphs and default button colours. Theming can only be judged
  in Playnite.
- Real mouse drag-and-drop and insertion into real theme layouts can't be tested there.

Before a release, check these by hand in Playnite:

1. Create, edit and delete manual and dynamic collections, in a light and a dark theme.
2. Open the value picker in the rule builder, search, and tick several values.
3. Drag one game and several games, including scrolling while dragging.
4. Select games in Details and Grid view, and switch between views; the list should appear once, below the description.
   Click a collection there, then **Open in Collections**.
5. Toggle both details view settings.
6. Change a game's tags, and play a game, and watch a dynamic collection update.

## Known issues to watch

- `ApplicationMode` other than Desktop is untested, and the views need Desktop theme styles.
- A search of the library in the rule preview runs on the UI thread. Very large libraries could feel it while editing.
- Translations come from Crowdin, so a release only includes the languages that were merged before it was built.

## Translations

`Localization/en_US.xaml` is the only file edited by hand. Everything else in `Localization/` comes from
[Crowdin](https://crowdin.com/project/game-collections) and is overwritten on every sync, so never edit it here.

- [`crowdin.yml`](../crowdin.yml) maps `en_US.xaml` to `Localization/<locale>.xaml`, using Crowdin's
  `%locale_with_underscore%` names (`de_DE`, `pt_BR`, `zh_CN`). These are the names Playnite looks for.
- Playnite loads `en_US.xaml` first, then the file for the user's language on top of it, so a missing string falls
  back to English. The plugin needs no code for this; the files only have to be copied to the output folder, which
  `GameCollections.csproj` does for every `Localization\*.xaml`.
- The [Crowdin workflow](../.github/workflows/crowdin.yml) uploads `en_US.xaml` whenever it changes on `master`, and
  every Monday (or when run by hand from the Actions tab) opens a pull request from `l10n_crowdin` with the latest
  translations. Merge it before building a release.
- It needs the `CROWDIN_PROJECT_ID` and `CROWDIN_PERSONAL_TOKEN` repository secrets.
- When you add or change a key, change only `en_US.xaml`. Crowdin marks changed strings for re-translation, and
  removed keys disappear from the translations on the next sync.

## Releasing

Update the version in both `GameCollections.csproj` and `extension.yaml`, add it to `CHANGELOG.md`, and add a new entry
at the top of `Packages` in `manifests/installer_manifest.yaml`. Pushing a `v*` tag makes the
[workflow](../.github/workflows/build.yml) build the `.pext` and create the release. Check the manifest with
`Toolbox.exe verify installer <url>`. `PackageUrl` must match the file name exactly, which includes the extension ID and
the version with underscores.

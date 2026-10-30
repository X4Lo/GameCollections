# Architecture and development

## Repository layout

```
GameCollections/
├─ source/
│  ├─ GameCollections.sln
│  └─ GameCollections/            the extension project
│     ├─ GameCollectionsPlugin.cs  entry point (GenericPlugin)
│     ├─ extension.yaml            Playnite extension manifest
│     ├─ icon.png                  extension, menu and sidebar icon
│     ├─ Localization/en_US.xaml   all user-visible strings (LOCGameCollections_*)
│     ├─ Models/                   persisted data types
│     ├─ Services/                 storage, business logic, dialogs, image loading
│     ├─ Settings/                 ISettings implementation and settings view
│     ├─ ViewModels/               MVVM view models
│     └─ Views/                    XAML views, shared styles, converters
├─ manifests/
│  ├─ installer_manifest.yaml      release list Playnite reads for installs and updates
│  └─ X4Lo_GameCollections.yaml    entry for the Playnite add-on database
├─ docs/                           this documentation
├─ screenshots/                    images for the README and add-on database
├─ .github/                        CI build/release workflow, issue template
└─ dist/                           build output: the .pext package (git-ignored)
```

## Building

Requirements: the .NET SDK (any version that supports SDK-style projects; 6.0 or later recommended).
The .NET Framework 4.6.2 reference assemblies are restored from NuGet automatically, so no targeting pack is needed.

```
dotnet build source/GameCollections.sln -c Release
```

This produces `source/GameCollections/bin/Release/` and the installable package
`dist/GameCollections_3f6b8d2e-5c41-4a7e-9e0b-6d2f1a8c7b54_<version>.pext`.

To try a build in Playnite, open the `.pext` file (Playnite installs it after a restart), or add
`source/GameCollections/bin/Debug` under *Settings → For developers → Custom extension folders*.

## Playnite conventions followed

- `Playnite.SDK` is referenced with `ExcludeAssets="runtime"`, so the SDK DLL is never shipped – Playnite provides it.
- All strings live in `Localization/en_US.xaml` and are read with `ResourceProvider.GetString`; Playnite loads the
  file matching the user's language, falling back to `en_US`.
- Views only use Playnite theme resources (`TextBrush`, `NormalBrush`, `FontIcoFont`, …) so every theme,
  light or dark, styles them.
- Dialogs are created with `PlayniteApi.Dialogs.CreateWindow` so they get the theme's window style.
- Logging goes through `LogManager.GetLogger()` into Playnite's `extensions.log`.
- Data lives in `GetPluginUserDataPath()`; settings use `LoadPluginSettings` / `SavePluginSettings`.
- Database events are unsubscribed in `OnApplicationStopped`, and UI updates are marshalled to the UI dispatcher.

## Main components

| Component | Responsibility |
|-----------|----------------|
| `GameCollectionsPlugin` | Wires everything together; game menu item, sidebar item, settings; reacts to library changes. |
| `CollectionRepository` | Reads/writes `collections.json`. Atomic save (temp file + `File.Replace` with a `.bak`), recovery from the backup, data sanitising. |
| `PosterStorage` | Copies custom posters into `Posters\`, deletes them, cleans up orphans. |
| `CollectionManager` | All collection rules: validation, unique titles, create/update/delete, membership changes in one save, poster resolution, pruning deleted games. Raises `CollectionsChanged`. |
| `ImageLoader` | Asynchronous, downscaled (`DecodePixelWidth`), frozen, cached image decoding with limited concurrency. |
| `DialogService` | Hosts the Add to Collection and create/edit dialogs in Playnite windows. |
| `CollectionsRootViewModel` | Sidebar page navigation (home ↔ collection) and shared open/edit/delete actions. Lives for the whole session so view state is kept. |
| `CollectionsHomeViewModel` / `CollectionDetailsViewModel` | The Collections page and a collection's page. |
| `AddToCollectionViewModel` / `EditCollectionViewModel` | The two dialogs. |

### Data model

```json
{
  "SchemaVersion": 1,
  "Collections": [
    {
      "Id": "guid",
      "Title": "Couch co-op",
      "Description": "",
      "Rating": 8.5,
      "PosterFileName": "guid_ticks.png",
      "GameIds": ["game guid", "..."],
      "CreatedAt": "2026-10-30T12:00:00Z",
      "ModifiedAt": "2026-10-30T12:00:00Z"
    }
  ]
}
```

Only game IDs are stored. Missing games are removed on startup and whenever Playnite deletes a game.

### Performance

Covers and posters are decoded at display size, off the UI thread, and cached. On a collection's page,
covers are only requested for tiles that are scrolled into view.

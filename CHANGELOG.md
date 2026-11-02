# Changelog

Every release must also be added to [manifests/installer_manifest.yaml](manifests/installer_manifest.yaml),
which is what Playnite reads to offer updates.

## 1.2.0 – 2026-11-02

- Added: Group games on a collection's page by platform, series, genre, developer, publisher, category, feature, tag,
  completion status, release year or install status. Each collection remembers its own choice.
- Added: Optional setting (off by default) that places the game details list next to the Game Relations section in
  Stardust, Nova X, Helium and Harmony, whose layouts don't have a normal spot for it.
- Improved: The game details list now uses the same title style as the theme's other sections.
- Fixed: Create Collection button overlapping the window buttons on the Collections page.

## 1.1.0 – 2026-11-02

- Added: Reorder games in a collection by dragging, from the right-click menu, or with Alt+arrow keys.
- Added: Dynamic collections, filled automatically by rules made of condition blocks combined with AND/OR.
- Added: A list of the selected game's collections in the game details view.
- Added: Settings to turn that list off, and to show or hide dynamic collections in it.
- Added: Automatic migrations of the data file, based on its `SchemaVersion`.

## 1.0.0 – 2026-10-30

- Initial release.

# Theme integration

For theme authors. Everything here is optional: Game Collections works in themes that do none of it.

## Collections list

Shows the collections of the selected game, each with its poster. Clicking one opens it. The list collapses itself when
the game is in no collection or no single game is selected.

By default, the extension inserts the list below the game description in any theme that keeps Playnite's standard
description or notes section. To choose the position yourself, add this anywhere in a game details view:

```xml
<ContentControl x:Name="GameCollections_CollectionsControl" />
```

When your theme has this element, the extension doesn't insert a second list, and your list stays visible even if the
user turns off **Show collections in details view**. The list shown there includes dynamic collections unless the user
turns off **Display dynamic collections in details view**.

## Temporary placement for specific themes

Stardust, Nova X, Helium and Harmony don't keep the description in a plain panel, so the default insertion can't work
well there. When the user turns on **Use special placement** and one of these is the active theme, the list goes next
to the element whose `Visibility` is bound to `GameRelationsVisibilityProxy`. Adding the `ContentControl` above to one
of these themes replaces this, because the theme's own element always takes priority.

## Settings available to themes

Bind to these with `PluginSettings`:

| Path | Type | Meaning |
|---|---|---|
| `IsDetailsControlVisible` | `bool` | The list currently has collections to show. Use it to hide a surrounding header or panel. |
| `ShowDynamicCollectionsInDetails` | `bool` | The user's choice for listing dynamic collections. |
| `AddDetailsListToAnyTheme` | `bool` | The user's choice for inserting the list automatically. |
| `UseThemeSpecificPlacement` | `bool` | The user's choice for the temporary placement above. |

```xml
<Border Visibility="{PluginSettings Plugin=GameCollections, Path=IsDetailsControlVisible, Converter={StaticResource BooleanToVisibilityConverter}}">
    <ContentControl x:Name="GameCollections_CollectionsControl" />
</Border>
```

## Compatibility

Keep the element name and these setting names stable across themes: they're treated as public, and any future
changes or new integration points will be listed in this file.

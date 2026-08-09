# Design system

A reusable design language, not a set of one-off styles. Every colour, size and radius used
by the shell comes from a token, so a future module never invents its own.

The old application's UI was deliberately not copied.

## Three-tier resource model

```
Palette.xaml            primitive colours      Color.Blue.600
      ↓
Theme.Light.xaml        semantic brush roles   Brush.Primary, Brush.Surface
Theme.Dark.xaml         (identical key sets)
      ↓
Styles/*.xaml           DynamicResource references
```

Styles never reference a palette colour directly — always a semantic brush, and always via
`DynamicResource`. That indirection is what lets the theme swap at runtime without a
restart: `ThemeService` replaces the theme dictionary in place and every consumer
re-resolves.

Because the two theme dictionaries must expose identical key sets, adding a brush means
adding it to both.

### Merge order in `App.xaml`

`Tokens` → `Theme` → `Icons`/`Assets` → `Styles` → `Controls`.

This is load-bearing. The theme occupies its own dictionary slot so `ThemeService` can find
and replace it by index. Within a dictionary, `StaticResource` resolves in document order,
so a style must be declared before whatever consumes it.

## Tokens

**Spacing** — an 8px system, with a 4px half-step for tight cases.

| Token | Value | | Token | Value |
| --- | --- | --- | --- | --- |
| `Space.0` | 0 | | `Space.5` | 24 |
| `Space.1` | 4 | | `Space.6` | 32 |
| `Space.2` | 8 | | `Space.7` | 40 |
| `Space.3` | 12 | | `Space.8` | 48 |
| `Space.4` | 16 | | | |

**Corner radius** — `Radius.SM` 6, `Radius.MD` 10 (the default for cards, buttons and
inputs), `Radius.LG` 14, `Radius.Pill` 999. `Radius.MD.Focus` is 13, because a focus ring
drawn 3px outside a `Radius.MD` element needs 3px more curve to stay concentric.

**Typography** — Segoe UI Variable, falling back to Segoe UI. Sizes run
`Caption` → `Small` → `Body` → `BodyLarge` → `Subtitle` → `Title` → `TitleLarge` →
`Display`. `Font.Icons` is Segoe Fluent Icons; `Font.Mono` is Cascadia Mono for weights and
tickets.

**Colour roles** — Primary, Neutral, Success, Warning, Danger, Information. Each exposes
default, hover, pressed and subtle variants, so an interactive element never hard-codes a
state colour.

## Buttons

`Button.Primary`, `Button.Secondary`, `Button.Danger`, `Button.Success`, `Button.Text`,
plus `Button.Icon` and `Button.WindowControl` for the title bar. All derive from
`Button.Base`, which owns sizing, focus ring and disabled treatment — a new variant only
supplies colours.

## Controls

Seven templated controls in `Controls/`, each with its default style keyed
`{x:Type controls:Foo}`:

| Control | Purpose |
| --- | --- |
| `ModernCard` | Elevated surface with soft shadow and optional header |
| `SectionHeader` | Title, optional description, optional action slot |
| `StatusBadge` | Pill-shaped state indicator, semantic colouring |
| `NavigationItem` | Left-panel entry. Derives from `RadioButton`, so panel-wide mutual exclusion is free |
| `SearchBox` | Text input with icon, placeholder and clear button |
| `EmptyState` | Icon, title, message and optional action for empty content areas |
| `LoadingSpinner` | Indeterminate progress indicator |

Default styles live in a dictionary merged from `App.xaml` rather than in
`Themes/Generic.xaml`, which keeps the whole design system in one visible merge order.

## Dialogs

All dialogs are custom WPF windows. **No `MessageBox` anywhere.** Confirmation,
Information, Warning, Error, Success, Progress and Loading are served through
`IDialogService`.

They use `WindowStyle.None` with `AllowsTransparency`, a null background and an outer
margin so the drop shadow is not clipped, plus a guarded `DragMove` helper.

`MainWindow` is different: it uses `WindowChrome`, so native dragging,
double-click-to-maximise, Aero Snap and correct work-area maximising all still work. Title
bar buttons call `SystemCommands` rather than assigning `WindowState`.

## Icons

Segoe Fluent Icons, referenced by resource key rather than by glyph. ViewModels expose a
key such as `Icon.Dashboard`, and `ResourceKeyConverter` resolves it — so no ViewModel ever
contains a private-use codepoint.

## Status indicators

The status bar dots are coloured with a `DataTrigger` plus a `DynamicResource` setter, not
an `IValueConverter`. A converter returning a `Brush` freezes that instance and would keep
the old theme's colour after a swap.

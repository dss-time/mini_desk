# MiniDesk Design System

The shared WPF design resources live in `Styles/DesignTokens.xaml` and `Styles/FluentControls.xaml`. Pages should consume these resources rather than defining local control templates or semantic colors. Translatable labels are stored in `Resources/Strings*.resx` and registered with `LocalizationService`.

## Foundations

| Token family | Values / behavior |
|---|---|
| Primary | `#1769F7` |
| Success / Warning / Danger | Green / amber / red semantic brushes; theme-aware values are applied by `AppThemeService` |
| Background / Surface / Border | Cool grey-blue canvas, white or dark neutral surface, subtle border |
| Text | Primary and secondary text plus a disabled tone; all switch at runtime with the application theme |
| Spacing | 4, 8, 12, 16, 24, 32 DIPs (`Space4` … `Space32`) |
| Radius | 8, 12, 16 DIPs (`Radius8`, `Radius12`, `Radius16`) |
| Type | Caption 10, body 12, subtitle 13, section 15, page title 28 DIPs; Segoe UI Variable with Segoe UI fallback |

The theme service owns palette changes. Use dynamic brushes for semantic colors so an open window updates immediately when the user changes theme or Windows appearance. Light and dark values are both assigned centrally in `AppThemeService`; system mode follows the Windows personalization setting.

## Shared controls

`FluentControls.xaml` defines the shared `PrimaryButton`, `SecondaryButton`, `DangerButton`, `IconButton`, `NavigationItem`, `Card` / `SettingCard`, `DialogCard`, `ToastCard`, `ToggleSwitch`, checkbox, choice-card radio, `TabItem`, `ComboBox`, `Slider`, and `ToolTip` styles. `App.xaml` defines the shared `TextBox` styling. Control templates cover normal, hover, pressed/selected, disabled, and keyboard-focus states where those states apply. Page titles, subtitles, section headings, card surfaces, and navigation use shared resources.

## Localization, DPI, and layout

- Keep user-facing text in `Strings.resx`, `Strings.zh-CN.resx`, and `Strings.en-US.resx`; add each new resource key to `LocalizationService.ResourceKeys` so live language switching updates it.
- Keep the UI language separate from the regional formatting culture. Both selectors update at runtime and persist in `WorkspaceConfig`.
- Use wrapping and flexible grid columns for translated content. Avoid fixed text widths; fixed dimensions are reserved for icons and small affordances.
- The app manifest declares Per-Monitor V2 once. WPF layout stays in DIPs; desktop group geometry converts to each display's physical pixels using its own DPI. The manager window clamps itself to the active work area and scrolls when the viewport is smaller than page content.
- Shared page shell keeps the navigation column stable; content pages scroll vertically instead of compressing text or controls at smaller logical display sizes.

## Visual QA record

- Reference: user-supplied `codex-clipboard-877fc3c8-55df-4bc8-b31b-db60c3292819.png` (Appearance page).
- Implementation capture: `implementation-appearance-redesign.png`; additional page captures use the `implementation-*-redesign.png` names in the project root.
- Captured at the active Windows display scale (150%); display cards are populated from `EnumDisplayMonitors`, not mock monitor data.
- Appearance page visual checklist: theme cards, live expanded/collapsed/icon-only preview, collapse-style cards, enumerated monitor resolution and scale, appearance sliders, language and region selectors, and runtime/architecture information are present. Long content remains scrollable at constrained work-area heights.
- Runtime verification: Release publishes for `win-x64` and `win-arm64`, followed by the app's `--self-test`; all 16 existing checks pass, including page navigation, theme/language switching, monitor-DPI coordinate conversion, and the three preview-mode buttons.

The self-test screenshot harness renders each page with WPF and drives interactive elements through UI Automation. It verifies rendering and handlers but is not a substitute for testing on native ARM64 hardware or physically moving the window across monitors with different DPI values.

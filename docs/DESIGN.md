# MPswift visual refresh

The owner supplied `play-button(1).png` on 2026-10-06. `Assets/MPswift.png` retains its original bytes. The ICO packages the same artwork at 16/24/32/48/64/128/256 pixels for Windows; the apphost, window, tray and empty artwork use this identity. The product, apphost/assembly, portable folder and development archive are named MPswift. Internal namespaces, IPC identity and the existing per-user storage path remain stable so updates retain existing sessions and single-instance behavior. Portable upgrades still require retaining the existing Data folder.

## Design direction

Graphite background, raised slate now-playing card, warm amber primary action, quiet secondary controls, 6–12 px corner radii, a clear title/body/caption hierarchy and consistent spacing. Segoe UI Variable Text uses the installed Windows fallback to Segoe UI. Amber is reserved for playback, selection and interactive focus. Track lists remain virtualized; no per-row artwork loading, blur, animation loop or new UI dependency is introduced.

The refresh covers buttons, checkboxes, dropdowns, text fields, sliders, scrollbars, tooltips and dark menu surfaces. Search has an actual localized placeholder; destructive removal uses a named, tooltip-equipped icon. Empty technical details no longer occupy a permanent row. Folder-drop guidance appears over the tab strip. Existing EN/RU labels, automation peers, keyboard navigation and system high-contrast brush overrides remain in use. Existing native window chrome/resizing is preserved.

Reviewed sources (retrieved 2026-10-06):

- [Fluent 2 design principles](https://fluent2.microsoft.design/design-principles): platform familiarity, adaptive layouts and a distinct signature experience.
- [Windows typography](https://learn.microsoft.com/en-us/windows/apps/design/style/typography): readable hierarchy and restrained type ramps, with ellipsis for bounded metadata.
- [Windows materials](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/materials): base/content layering and transient surfaces. This WPF implementation uses opaque layers; it does not claim native Mica/Acrylic effects.

## Verification

Existing desktop checks exercise actual EN/RU WPF layout at minimum window size, Tab navigation, automation range/toggle providers, list virtualization and 96/144/192-DPI visual renders. Integration additionally checks MPswift window/apphost metadata and icon availability; packaging requires the renamed EXE, assembly and RU satellite. Physical DPI/Narrator and audio-device acceptance remain separate Stage G gates.

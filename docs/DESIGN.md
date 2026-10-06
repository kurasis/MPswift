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

## Menus, settings and album sections

Context menus use explicit dark templates for root/submenu surfaces, highlights and separators, without the default WPF icon rail. Actions are organized into playlist, selected-track, queue and file/backup groups. Keyboard submenu handling and real nested actions remain native MenuItem behavior. A named settings button in the title bar opens language, album-heading and close-to-tray preferences plus the existing audio-output/EQ/ReplayGain/crossfade window. Language is explicitly applied on the next launch; heading and close behavior apply when saved. Cancel discards draft preferences.

Album headings are annotations on the first row of each contiguous visible folder/album run. Album tags take precedence over folder names; the last two folder components and visible run count clarify disc subfolders, with the full source folder in a tooltip. They do not sort, merge duplicate occurrences, change playback order or introduce selectable fake tracks. Filtering, tab changes, imports, sorting and row moves recompute boundaries. Standard recycling virtualization remains enabled. Existing settings default to headings on; the preference persists through normal settings and backup serialization without a schema bump.

Validation includes real folder-drop/render/filter/toggle/order checks, menu popup and submenu images/keyboard navigation/action execution, actual settings Apply persistence and Cancel behavior in EN/RU, plus the existing nearly-10k-row virtualization checks. New pure tests cover nested discs, same-name folders, interleaved album runs, missing tags, filtered boundaries and old-settings compatibility.

## Consistent action icons and live language

Main-window actions, window controls, search and playing-row indicator use the same 18-DIP vector component, with 1.5-DIP strokes for outline symbols. Button targets remain sized for their role; cover art is a separate artwork surface. EN/RU bindings refresh in place after Settings Apply, including virtualized templates and detached menus. Compiled build version is visible next to the product name.

## Playlist tab interaction and waveform hierarchy

Tabs share the compact dark menu styling; popup actions use the clicked target. Tab dragging shows an amber insertion marker without replacing selection/playback highlights. The waveform has a solid RMS envelope, subdued raw peak context, fixed linear amplitude, vertical breathing room and one-DIP column gaps. This separates transient extrema from sustained energy while retaining real analysis data.

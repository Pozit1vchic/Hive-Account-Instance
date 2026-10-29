# Hive workspace design

The default is a white account-management workspace with sparse honeycomb outlines above the toolbar. The accent is reserved for Add account, Launch selected and the active navigation state.

- Light: canvas #F4F6FA, surface #FFFFFF, quiet surface #F6F8FC, text #29313F, lines #E1E6EF, honey #DFA740. Dusk uses #151923, #212838 and #293347 with lighter text. Iris and Ocean share the same hierarchy.
- Segoe UI Variable Text keeps account data readable at compact sizes. Titles 22–23px, controls 12–13px, supporting labels 11px. No additional promotional labels.
- Main window starts at 900×560 with a three-column table and a compact launcher. Filters are local view changes, not blocking operations. Utilities holds secondary actions. Editing opens a separate dialog.
- Preferences starts at 800×600 with fixed-order vertical navigation: Appearance, Accounts, Launch. A StackPanel avoids TabPanel's automatic selected-row repositioning. Toggles reserve their full template width and do not clip.
- Theme changes update existing brushes over 180ms. Navigation reveal uses 180ms and 5px. Button hover lifts 1px; press feedback scales to 97%. App and Windows reduced-motion settings are respected.
- Lucide SVG files are bundled with their license. The local renderer handles paths, circles, ellipses, rectangles, lines and polygons at a consistent 1.8-unit stroke.
- Addition progress is neutral; only failed results use red. Manual login opens immediately. Credentials are filled once, with final sign-in and verification performed by the user.

Sources: [Anthropic frontend-design](https://github.com/anthropics/skills/blob/main/skills/frontend-design/SKILL.md), [Lucide](https://github.com/lucide-icons/lucide). The skill informs composition and hierarchy; Hive's account-manager workflows determine the layout.

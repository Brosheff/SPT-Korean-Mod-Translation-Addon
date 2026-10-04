# F12 Configuration Manager Korean profiles

This folder contains display-only Korean translation profiles for the SPT/BepInEx F12 Configuration Manager.

- Target: SPT 4.1.6
- Analyzed source packages with F12 settings: 59 mods / 61 plugin GUIDs
- Translation entries: 1,204
- Source descriptions translated where present; settings with no upstream description remain without a tooltip translation.
- `ConfigDefinition`, saved `.cfg` keys and setting values are never rewritten.
- `section: "*"` is a guarded fallback for mods that construct category strings dynamically. It matches only the same plugin GUID + unique key and still checks the source display name before applying the translation.
- Excluded from analysis by request: PeinBetterRearSights, Tarkov-1.0-Backport.

Custom in-game UIs and `CustomDrawer` content are tracked separately in `analysis/CUSTOM_UI_AUDIT.md`.

# Changelog

## 7.0.41.2

- Fixed account mixing: automatic discovery uses the active account's valid license packages instead of shared artwork/cache IDs.
- Fixed missing game names with account-scoped app mapping and independent metadata-entry handling.
- Improved live checking with explicit local stats refresh, account-matched callbacks, schema settling, and live ID/state reconciliation.
- Added Check games for selected games or all games when nothing is selected.
- Renamed bulk buttons to Unlock all and Lock all. Both apply to selected games after preview.
- Added ten regression tests for account isolation, package formats, names and live-check behavior.

## 7.0.41.1

- Added a dark availability dashboard, search, filters and multi-game selection.
- Added reviewed bulk unlock/lock with two parallel workers and per-game results.
- Added permission inheritance checks, unknown-state handling and save acknowledgments.
- Retained individual editors and manual App IDs / Store links.
- Added English documentation, GitHub templates, build workflow, start shortcuts and regression tests.

Based on the supplied upstream SteamAchievementManager-7.0.41 archive.

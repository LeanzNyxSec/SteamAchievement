# Validation — 7.0.41.2

- Release builds: zero warnings and errors.
- 29 synthetic tests cover account isolation, cancelled/foreign licenses, package formats, game names, protected and unknown permissions, and save outcomes.
- Read-only discovery against the current account's local cache succeeded; game names resolved from metadata.
- Read-only native Steam probe succeeded: 520 achievement IDs/states loaded with matching schema permissions.
- UI previews use synthetic data. No real account achievement writes were exercised.
- Another PC may have different or stale Steam cache files; missing account caches produce an error instead of a combined library.

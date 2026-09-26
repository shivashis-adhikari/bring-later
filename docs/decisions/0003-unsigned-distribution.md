# 0003 Unsigned releases

Releases are not code-signed. Consequences we design around:

- **Windows:** SmartScreen asks users to choose "More info" then "Run anyway" on first launch. PCs with Smart App Control on block unsigned apps outright. The README says both. SignPath Foundation offers free signing for open-source projects if this becomes a problem.
- **macOS:** builds are ad-hoc signed (Apple Silicon requires a signature to run). First launch needs System Settings > Privacy & Security > Open Anyway. The Accessibility grant is tied to the binary's hash, so it silently stops working after every update. The app detects this on launch, resets the stale entry, and asks again.

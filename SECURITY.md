# Security

Bring Later controls other apps' windows and stores their titles on your computer, so security reports matter.

Please report vulnerabilities privately through [GitHub's private reporting](https://github.com/shivashis-adhikari/bring-later/security/advisories/new) rather than a public issue. You'll get a reply within a week.

What Bring Later does, for reference:

- It makes no network requests except when you choose **Check for updates**, which reads the public GitHub releases list.
- It stores snoozes and settings in `%LOCALAPPDATA%\BringLater` (Windows) or `~/Library/Application Support/BringLater` and the app's preferences (macOS). Nothing leaves the device.
- Its log records window handles and app file names, never window titles.
- It never runs as administrator on Windows, and on macOS it uses Accessibility access only to move, minimize and restore windows.

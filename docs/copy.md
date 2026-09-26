# Writing for Bring Later

Every sentence a user sees lives in one file per platform:

- Windows: [`windows/src/BringLater/Copy.cs`](../windows/src/BringLater/Copy.cs), plus a few labels in the XAML views
- macOS: [`mac/Sources/BringLater/Copy.swift`](../mac/Sources/BringLater/Copy.swift), plus labels in the SwiftUI views

## Rules

- **Follow the platform.** macOS uses title case for menu items, buttons and window titles ("Bring Back Now"). Windows uses sentence case ("Bring back now").
- **Say what happened and what to do, in one sentence.** "Another app is using Win+Shift+Z. Choose a different shortcut in Settings."
- **Name things.** Use the window's title and the app's name rather than "this item" or "your content".
- **No exclamation marks, no emoji, no apologies.** Don't use "Oops", "Whoops", "Uh-oh" or "Something went wrong" on its own.
- **No marketing words inside the app.** Avoid "seamless", "effortless", "powerful", "supercharge", "unlock", "magic".
- **Times follow the user's locale.** Format through the platform (`Format` on both platforms). Never hard-code "PM" or a date order.
- **Empty states are one plain line**, with at most one line of help under it.

## The strings that matter most

| Where | Windows | macOS |
|---|---|---|
| Panel placeholder | Type a time, like 7pm or tomorrow 9 | same |
| Presets | In 1 hour · This evening · Tomorrow morning · Next week · Pick a date and time… | In 1 Hour · This Evening · Tomorrow Morning · Next Week · Pick a Date & Time… |
| Typed time not understood | Try 7pm, tomorrow 9, or 2h | same |
| Typed time in the past | That time has already passed | same |
| Notification, one window back | *Window title* / *App* · snoozed at 3:12 PM | same |
| Notification, window closed | The *App* window was closed while it was snoozed. | same |
| Tray or menu, nothing snoozed | No snoozed windows | No Snoozed Windows |
| Quit while snoozed | Quit Bring Later? Your 3 snoozed windows will come back now. | Windows come back without asking, as macOS apps quit silently |

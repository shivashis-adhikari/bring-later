import AppKit
import BringLaterCore
import ServiceManagement
import os
import SwiftUI

@MainActor
@Observable
final class SettingsModel {
    var shortcut = Preferences.shortcut
    var shortcutMessage = ""
    var morning = Preferences.morning { didSet { Preferences.morning = morning } }
    var evening = Preferences.evening { didSet { Preferences.evening = evening } }
    var bringToFront = Preferences.bringToFront { didSet { Preferences.bringToFront = bringToFront } }
    var launchAtLogin = SMAppService.mainApp.status == .enabled
    var accessGranted = AXIsProcessTrusted()
    var updateStatus = "Checks GitHub for a newer release when you ask. Nothing is sent automatically."
    var updateURL: URL?
    var checking = false

    /// Tries a new shortcut. False if another app already has it.
    var applyShortcut: (Shortcut) -> Bool = { _ in false }
    var pauseShortcut: (Bool) -> Void = { _ in }

    let times: [ClockTime] = {
        var times = (0..<48).map { ClockTime(hour: $0 / 2, minute: $0 % 2 * 30) }
        times.append(contentsOf: [Preferences.morning, Preferences.evening])
        return Array(Set(times)).sorted()
    }()

    func setLaunchAtLogin(_ enabled: Bool) {
        do {
            if enabled { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
        } catch {
            Logger.app.error("Launch at login: \(error.localizedDescription, privacy: .public)")
        }
        launchAtLogin = SMAppService.mainApp.status == .enabled
    }

    func checkForUpdates() async {
        checking = true
        updateStatus = "Checking…"
        switch await Updates.check() {
        case .upToDate:
            updateStatus = "You have the latest version."
            updateURL = nil
        case .available(let version, let url):
            updateStatus = "Version \(version) is available."
            updateURL = url
        case .failed:
            updateStatus = "Couldn't reach GitHub. Try again later."
            updateURL = nil
        }
        checking = false
    }
}

struct SettingsView: View {
    @Bindable var model: SettingsModel

    var body: some View {
        Form {
            Section {
                LabeledContent {
                    ShortcutRecorder(model: model)
                } label: {
                    Text("Keyboard Shortcut")
                    Text("Opens the snooze panel for the window you're using.")
                    if !model.shortcutMessage.isEmpty {
                        Text(model.shortcutMessage).foregroundStyle(.red)
                    }
                }
                Picker(selection: $model.evening) {
                    ForEach(model.times, id: \.self) { Text(Format.clock($0)).tag($0) }
                } label: {
                    Text("This Evening")
                    Text("What “this evening” and “tonight” mean.")
                }
                Picker(selection: $model.morning) {
                    ForEach(model.times, id: \.self) { Text(Format.clock($0)).tag($0) }
                } label: {
                    Text("Morning")
                    Text("Used by “tomorrow morning”, “next week” and dates without a time.")
                }
                Picker(selection: $model.bringToFront) {
                    Text("Show It Without Switching to It").tag(false)
                    Text("Bring It to the Front").tag(true)
                } label: {
                    Text("When a Window Comes Back")
                    Text("A notification tells you either way.")
                }
                .pickerStyle(.radioGroup)
            } header: {
                Text("Snoozing")
            }

            Section("General") {
                Toggle(isOn: Binding(get: { model.launchAtLogin }, set: { model.setLaunchAtLogin($0) })) {
                    Text("Launch at Login")
                    Text("Bring Later has to be running to bring windows back.")
                }
                LabeledContent {
                    if model.accessGranted {
                        Text("Allowed").foregroundStyle(.secondary)
                    } else {
                        Button("Open System Settings") { Access.openSettings() }
                    }
                } label: {
                    Text("Accessibility")
                    Text("Needed to hide and restore other apps' windows.")
                }
            }

            Section("About") {
                LabeledContent("Version", value: Updates.current)
                LabeledContent {
                    HStack {
                        if let url = model.updateURL {
                            Button("Download") { NSWorkspace.shared.open(url) }
                        }
                        Button("Check for Updates") { Task { await model.checkForUpdates() } }
                            .disabled(model.checking)
                    }
                } label: {
                    Text("Updates")
                    Text(model.updateStatus)
                }
                HStack(spacing: 16) {
                    Link("Source Code", destination: URL(string: "https://github.com/shivashis-adhikari/bring-later")!)
                    Link("Report a Problem", destination: URL(string: "https://github.com/shivashis-adhikari/bring-later/issues/new")!)
                }
            }
        }
        .formStyle(.grouped)
        .frame(width: 520)
        .fixedSize(horizontal: false, vertical: true)
    }
}

/// Click, then press the new shortcut. Esc cancels.
private struct ShortcutRecorder: View {
    @Bindable var model: SettingsModel
    @State private var recording = false
    @State private var monitor: Any?

    var body: some View {
        Button {
            recording ? stop() : start()
        } label: {
            Text(recording ? "Press a Shortcut" : model.shortcut.display)
                .frame(minWidth: 110)
                .monospacedDigit()
        }
        .accessibilityLabel(recording ? "Recording. Press the new shortcut." : "Keyboard shortcut \(model.shortcut.display). Click to change.")
        .onDisappear(perform: stop)
    }

    private func start() {
        model.shortcutMessage = ""
        recording = true
        model.pauseShortcut(true)
        monitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { event in
            if event.keyCode == 53 { // Esc
                stop()
                return nil
            }
            guard let shortcut = Shortcut(event: event), shortcut.isValid else {
                model.shortcutMessage = "Use ⌘ or ⌃ together with another key."
                return nil
            }
            guard model.applyShortcut(shortcut) else {
                model.shortcutMessage = "Another app is already using \(shortcut.display). Try a different one."
                return nil
            }
            model.shortcut = shortcut
            Preferences.shortcut = shortcut
            model.shortcutMessage = ""
            stop()
            return nil
        }
    }

    private func stop() {
        guard recording else { return }
        recording = false
        if let monitor { NSEvent.removeMonitor(monitor) }
        monitor = nil
        model.pauseShortcut(false)
    }
}

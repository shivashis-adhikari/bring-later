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
    var updateStatus = L("settings.updatesIdle")
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
        updateStatus = L("settings.updatesChecking")
        switch await Updates.check() {
        case .upToDate:
            updateStatus = L("settings.updatesCurrent")
            updateURL = nil
        case .available(let version, let url):
            updateStatus = L("settings.updatesAvailable", version)
            updateURL = url
        case .failed:
            updateStatus = L("settings.updatesFailed")
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
                    Text(L("settings.shortcut"))
                    Text(L("settings.shortcutHint"))
                    if !model.shortcutMessage.isEmpty {
                        Text(model.shortcutMessage).foregroundStyle(.red)
                    }
                }
                Picker(selection: $model.evening) {
                    ForEach(model.times, id: \.self) { Text(Format.clock($0)).tag($0) }
                } label: {
                    Text(L("settings.evening"))
                    Text(L("settings.eveningHint"))
                }
                Picker(selection: $model.morning) {
                    ForEach(model.times, id: \.self) { Text(Format.clock($0)).tag($0) }
                } label: {
                    Text(L("settings.morning"))
                    Text(L("settings.morningHint"))
                }
                Picker(selection: $model.bringToFront) {
                    Text(L("settings.returnQuiet")).tag(false)
                    Text(L("settings.returnFront")).tag(true)
                } label: {
                    Text(L("settings.return"))
                    Text(L("settings.returnHint"))
                }
                .pickerStyle(.radioGroup)
            } header: {
                Text(L("settings.snoozing"))
            }

            Section(L("settings.general")) {
                Toggle(isOn: Binding(get: { model.launchAtLogin }, set: { model.setLaunchAtLogin($0) })) {
                    Text(L("settings.launchAtLogin"))
                    Text(L("settings.launchHint"))
                }
                LabeledContent {
                    if model.accessGranted {
                        Text(L("settings.allowed")).foregroundStyle(.secondary)
                    } else {
                        Button(L("settings.openSystemSettings")) { Access.openSettings() }
                    }
                } label: {
                    Text(L("settings.accessibility"))
                    Text(L("settings.accessibilityHint"))
                }
            }

            Section(L("settings.about")) {
                LabeledContent(L("settings.version"), value: Updates.current)
                LabeledContent {
                    HStack {
                        if let url = model.updateURL {
                            Button(L("settings.download")) { NSWorkspace.shared.open(url) }
                        }
                        Button(L("settings.checkUpdates")) { Task { await model.checkForUpdates() } }
                            .disabled(model.checking)
                    }
                } label: {
                    Text(L("settings.updates"))
                    Text(model.updateStatus)
                }
                HStack(spacing: 16) {
                    Link(L("settings.source"), destination: URL(string: "https://github.com/shivashis-adhikari/bring-later")!)
                    Link(L("settings.report"), destination: URL(string: "https://github.com/shivashis-adhikari/bring-later/issues/new")!)
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
            Text(recording ? L("settings.shortcutRecord") : model.shortcut.display)
                .frame(minWidth: 110)
                .monospacedDigit()
        }
        .accessibilityLabel(recording ? L("settings.shortcutRecording") : L("settings.shortcutLabel", model.shortcut.display))
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
                model.shortcutMessage = L("settings.shortcutInvalid")
                return nil
            }
            guard model.applyShortcut(shortcut) else {
                model.shortcutMessage = L("settings.shortcutTaken", shortcut.display)
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

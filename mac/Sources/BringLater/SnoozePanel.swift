import AppKit
import BringLaterCore
import SwiftUI

struct PresetRow: Identifiable {
    let id: Int
    let label: String
    let time: String
    /// Nil for "Pick a Date & Time…".
    let date: Date?
}

/// State behind the snooze panel: presets, typed input and the date picker.
@MainActor
@Observable
final class PanelModel {
    let now: Date
    let title: String
    let subtitle: String
    let icon: NSImage?
    /// When set, the panel explains why the window can't be snoozed instead of offering times.
    let refusal: (title: String, message: String)?
    let rows: [PresetRow]
    private let prefs: TimePrefs

    var query = "" { didSet { reparse() } }
    private(set) var result = ""
    private(set) var resultIsError = false
    private var parsed: Date?
    var selected: Int?
    var isPicking = false
    var pickedDate: Date
    var error = ""

    init(now: Date, prefs: TimePrefs, title: String, subtitle: String, icon: NSImage?, refusal: (title: String, message: String)? = nil) {
        self.now = now
        self.prefs = prefs
        self.title = title
        self.subtitle = subtitle
        self.icon = icon
        self.refusal = refusal
        var rows = Presets.compute(now: now, zone: .current, prefs: prefs).enumerated().map { index, preset in
            PresetRow(id: index, label: Copy.preset(preset.kind), time: Format.short(preset.date, now: now), date: preset.date)
        }
        rows.append(PresetRow(id: rows.count, label: Copy.pickDateAndTime, time: "", date: nil))
        self.rows = rows
        let tomorrow = Calendar.current.date(byAdding: .day, value: 1, to: now) ?? now
        pickedDate = Calendar.current.date(bySettingHour: prefs.morning.hour, minute: prefs.morning.minute, second: 0, of: tomorrow) ?? tomorrow
    }

    var showsResult: Bool { !query.trimmingCharacters(in: .whitespaces).isEmpty }
    var pickRange: ClosedRange<Date> { now...now.addingTimeInterval(TimeGrammar.horizon) }
    var pickIsValid: Bool { pickedDate > now && pickedDate.timeIntervalSince(now) <= TimeGrammar.horizon }

    /// What Return would choose right now, or nil if nothing is chosen yet.
    var committable: Date? {
        if isPicking { return pickIsValid ? LocalTime.floorToMinute(pickedDate) : nil }
        if showsResult { return parsed }
        if let selected { return rows[selected].date }
        return nil
    }

    func move(_ step: Int) {
        let count = rows.count
        guard let current = selected else {
            selected = step > 0 ? 0 : count - 1
            return
        }
        selected = ((current + step) % count + count) % count
    }

    private func reparse() {
        switch TimeGrammar.parse(query, now: now, zone: .current, prefs: prefs) {
        case .success(let date):
            parsed = date
            result = Format.when(date, now: now)
            resultIsError = false
        case .failure(let error):
            parsed = nil
            result = error == .empty ? "" : Copy.parseError(error)
            resultIsError = true
        }
    }
}

/// A borderless panel that takes the keyboard without activating Bring Later, so the app you were
/// using stays frontmost, the way Spotlight does.
final class KeyPanel: NSPanel {
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { false }
}

@MainActor
final class SnoozePanel: NSObject, NSWindowDelegate {
    private let panel: KeyPanel
    private let model: PanelModel
    private var chosen: ((Date) -> Void)?
    var onClose: (() -> Void)?

    init(model: PanelModel, onChoose: ((Date) -> Void)?) {
        self.model = model
        chosen = onChoose
        panel = KeyPanel(contentRect: .zero, styleMask: [.borderless, .nonactivatingPanel, .fullSizeContentView], backing: .buffered, defer: true)
        super.init()

        panel.isFloatingPanel = true
        panel.level = .floating
        panel.backgroundColor = .clear
        panel.isOpaque = false
        panel.hasShadow = true
        panel.hidesOnDeactivate = false
        panel.isReleasedWhenClosed = false
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .transient]
        panel.delegate = self

        let background = NSVisualEffectView()
        background.material = .popover
        background.state = .active
        background.blendingMode = .behindWindow
        background.wantsLayer = true
        background.layer?.cornerRadius = 12
        background.layer?.masksToBounds = true

        let content = PanelView(model: model, commit: { [weak self] in self?.commit() }, cancel: { [weak self] in self?.dismiss() })
        let hosting = NSHostingView(rootView: content)
        hosting.translatesAutoresizingMaskIntoConstraints = false
        background.addSubview(hosting)
        NSLayoutConstraint.activate([
            hosting.leadingAnchor.constraint(equalTo: background.leadingAnchor),
            hosting.trailingAnchor.constraint(equalTo: background.trailingAnchor),
            hosting.topAnchor.constraint(equalTo: background.topAnchor),
            hosting.bottomAnchor.constraint(equalTo: background.bottomAnchor),
        ])
        panel.contentView = background
        panel.setContentSize(hosting.fittingSize)
    }

    /// Shows the panel centered on `frame` (Cocoa coordinates), kept inside that display.
    func present(over frame: CGRect) {
        let size = panel.frame.size
        let screen = NSScreen.screens.first { $0.frame.contains(CGPoint(x: frame.midX, y: frame.midY)) } ?? NSScreen.main
        let visible = screen?.visibleFrame ?? frame
        var origin = CGPoint(x: frame.midX - size.width / 2, y: frame.midY - size.height / 2)
        origin.x = min(max(origin.x, visible.minX + 8), visible.maxX - size.width - 8)
        origin.y = min(max(origin.y, visible.minY + 8), visible.maxY - size.height - 8)
        panel.setFrameOrigin(origin)
        panel.makeKeyAndOrderFront(nil)
    }

    func showError(_ message: String) {
        model.error = message
    }

    func dismiss() {
        guard panel.isVisible else { return }
        panel.orderOut(nil)
        panel.close()
        onClose?()
    }

    private func commit() {
        guard let date = model.committable else { return }
        model.error = ""
        chosen?(date)
    }

    func windowDidResignKey(_ notification: Notification) {
        dismiss()
    }
}

private struct PanelView: View {
    @Bindable var model: PanelModel
    let commit: () -> Void
    let cancel: () -> Void
    @FocusState private var inputFocused: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            header
                .padding(.bottom, 12)

            if let refusal = model.refusal {
                Text(refusal.message)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                HStack {
                    Spacer()
                    Button(L("panel.close"), action: cancel).keyboardShortcut(.defaultAction)
                }
                .padding(.top, 14)
            } else if model.isPicking {
                picker
            } else {
                input
                if model.showsResult { resultRow } else { presetList }
            }

            if !model.error.isEmpty {
                Text(model.error)
                    .foregroundStyle(.red)
                    .font(.callout)
                    .fixedSize(horizontal: false, vertical: true)
                    .padding(.top, 10)
            }
        }
        .padding(14)
        .frame(width: 380)
        .onKeyPress(.escape) {
            if model.isPicking { model.isPicking = false; inputFocused = true } else { cancel() }
            return .handled
        }
    }

    private var header: some View {
        HStack(spacing: 10) {
            Group {
                if model.refusal != nil {
                    Image(systemName: "exclamationmark.triangle").font(.title2).foregroundStyle(.secondary)
                } else if let icon = model.icon {
                    Image(nsImage: icon).resizable().interpolation(.high)
                }
            }
            .frame(width: 28, height: 28)
            VStack(alignment: .leading, spacing: 1) {
                Text(model.refusal?.title ?? model.title).font(.headline).lineLimit(1).truncationMode(.middle)
                if model.refusal == nil {
                    Text(model.subtitle).font(.subheadline).foregroundStyle(.secondary).lineLimit(1)
                }
            }
        }
    }

    private var input: some View {
        TextField(Copy.inputPlaceholder, text: $model.query)
            .textFieldStyle(.roundedBorder)
            .controlSize(.large)
            .focused($inputFocused)
            .onAppear { inputFocused = true }
            .onSubmit(commit)
            .onKeyPress(.downArrow) { model.showsResult ? .ignored : { model.move(1); return .handled }() }
            .onKeyPress(.upArrow) { model.showsResult ? .ignored : { model.move(-1); return .handled }() }
            .onKeyPress(characters: .decimalDigits, phases: .down) { press in
                guard press.modifiers == .command, let digit = Int(press.characters), digit >= 1, digit <= model.rows.count else { return .ignored }
                choose(model.rows[digit - 1])
                return .handled
            }
            .accessibilityLabel(L("panel.inputName"))
    }

    private var resultRow: some View {
        HStack {
            Text(model.result).foregroundStyle(model.resultIsError ? .secondary : .primary)
            Spacer()
            if !model.resultIsError {
                Text("↩").font(.callout).foregroundStyle(.secondary)
            }
        }
        .padding(.horizontal, 10)
        .frame(height: 32)
        .background(model.resultIsError ? Color.clear : Color.accentColor.opacity(0.14), in: RoundedRectangle(cornerRadius: 6))
        .padding(.top, 8)
    }

    private var presetList: some View {
        VStack(spacing: 2) {
            ForEach(model.rows) { row in
                Button { choose(row) } label: {
                    HStack {
                        Text(row.label)
                        Spacer()
                        Text(row.time).foregroundStyle(.secondary)
                        Text("⌘\(row.id + 1)").font(.callout).foregroundStyle(.tertiary).frame(width: 30, alignment: .trailing)
                    }
                    .contentShape(Rectangle())
                    .padding(.horizontal, 10)
                    .frame(height: 30)
                    .background(model.selected == row.id ? Color.accentColor.opacity(0.18) : .clear, in: RoundedRectangle(cornerRadius: 6))
                }
                .buttonStyle(.plain)
            }
        }
        .padding(.top, 8)
    }

    private var picker: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                Button { model.isPicking = false; inputFocused = true } label: { Image(systemName: "chevron.left") }
                    .buttonStyle(.borderless)
                    .accessibilityLabel(L("panel.back"))
                Text(L("panel.pickTitle")).font(.headline)
            }
            DatePicker(L("panel.date"), selection: $model.pickedDate, in: model.pickRange, displayedComponents: .date)
                .datePickerStyle(.graphical)
                .labelsHidden()
            HStack {
                DatePicker(L("panel.time"), selection: $model.pickedDate, displayedComponents: .hourAndMinute)
                    .datePickerStyle(.stepperField)
                    .labelsHidden()
                Spacer()
                Button(L("panel.snooze"), action: commit)
                    .keyboardShortcut(.defaultAction)
                    .disabled(!model.pickIsValid)
            }
            Text(model.pickIsValid ? Format.when(model.pickedDate, now: model.now) : Copy.parseError(.past))
                .font(.callout)
                .foregroundStyle(model.pickIsValid ? .secondary : Color.red)
        }
    }

    private func choose(_ row: PresetRow) {
        model.selected = row.id
        if row.date == nil {
            model.isPicking = true
        } else {
            commit()
        }
    }
}

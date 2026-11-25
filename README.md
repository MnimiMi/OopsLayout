# OopsLayout

<p align="center">
  <img src="OopsLayout.Windows/Resources/icon.ico" width="96" alt="OopsLayout icon"><br>
  <em>Type <code>ghbdtn</code>, get <strong>привет</strong>. Automatic keyboard-layout fixer for Windows/Mac.</em>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet&logoColor=white" alt=".NET 8">
  <img src="https://img.shields.io/badge/C%23-239120?style=flat-square&logo=csharp&logoColor=white" alt="C#">
  <img src="https://img.shields.io/badge/Swift-FA7343?style=flat-square&logo=swift&logoColor=white" alt="Swift">
  <img src="https://img.shields.io/badge/Windows-10%20%2F%2011-0078D6?style=flat-square&logo=windows&logoColor=white" alt="Windows 10/11">
  <img src="https://img.shields.io/badge/macOS-12%2B-000000?style=flat-square&logo=apple&logoColor=white" alt="macOS 12+">
  <img src="https://img.shields.io/badge/UI-WinForms%20tray-5C2D91?style=flat-square" alt="WinForms tray">
  <img src="https://img.shields.io/badge/license-MIT-3DA639?style=flat-square" alt="MIT license">
  <img src="https://img.shields.io/badge/status-working-success?style=flat-square" alt="Status: working">
</p>

---

Forgot to switch layout and typed `ghbdtn` instead of `привет`? OopsLayout catches it and fixes it as you type — free, open source, and cross-platform.

## How it works

It sits in the system tray and watches your typing through a global keyboard hook. When you finish a word it asks one question: **does this look more like a real word in the other layout?** If yes, it deletes the word, switches the layout, and retypes it correctly.

The clever bit is *how* it decides. Instead of a giant dictionary it uses **character-bigram language models** (tiny frequency tables of which letter-pairs are common). `ghbdtn` is gibberish in English but its Russian twin `привет` is perfectly ordinary — so it flips. A real English word like `hello` stays put, because its twin `руддщ` is nonsense.

The Cyrillic side is **English ↔ Russian or Ukrainian** — pick which one in the tray (see [Russian / Ukrainian](#russian--ukrainian)).

Works both ways:

| You typed (wrong layout) | OopsLayout gives you |
|---|---|
| `ghbdtn` (EN) | `привет` + switches to RU |
| `руддщ` (RU) | `hello` + switches to EN |
| `ghbdsn` (EN, Ukrainian mode) | `привіт` + switches to Ukrainian |

### Smart touches

- 🧠 **Bigram detection** — no dictionary, handles any word, rarely false-positives.
- 🇺🇦 **Russian *or* Ukrainian** — choose the Cyrillic target in the tray; the choice is remembered.
- 🔤 **Short words & single letters** — uses surrounding context, so a lone `z` becomes `я` and re-fixes the previous word when the next one reveals the language.
- 📋 **Your own exceptions** — a words-list you edit (tray → *Exceptions…*) for anything it keeps changing on you.
- 🔠 **ALL-CAPS left alone** — acronyms and constants (`XML`, `API`) aren't touched.
- ⌨️ **Punctuation-as-letter aware** — Cyrillic letters typed via `' [ ] ; , .` (э х ъ ж б ю / є х ї ж б ю) are handled, not treated as word breaks.
- 🔐 **Password fields are off-limits** — a focused password box is detected via UI Automation and nothing typed there is buffered, analysed, or touched. *(Windows; macOS hides password typing from apps at the system level anyway.)*
- ↩️ **Backspace = undo** — hit Backspace right after an auto-fix to roll it back: your original text returns and the layout switches back to what it was. Any other key keeps the fix. *(Windows)*
- 🧑‍💻 **Stays out of code** — dormant in editors and terminals (VS Code, Rider, Windows Terminal, MobaXterm, …); the tray icon greys out.
- 🎮 **Your own app list** — tray → *Apps…*: name any app (pick a running one or browse to its `.exe`) and OopsLayout neither reads nor types anything while it's in front. *(Windows)*
- ⏸️ **Pause means off** — pausing removes the keyboard hook entirely, not just ignores keys. *(Windows)*
- 🌍 **Respects your layouts** — switches to *your* installed English (e.g. UK), Russian and Ukrainian, never forces US English on you.
- 💀 **Dead-key friendly** — works on layouts where `'` is a dead key (US-International, UK Extended).
- 😴 **Survives sleep** — re-arms the keyboard hook after the machine resumes.
- 🎯 **Layout-independent typing** — replacements use Unicode injection, so the corrected text lands in any app.

## Privacy

OopsLayout runs entirely on your machine — and since it's open source, you don't have to take that on faith.

- **No network, no telemetry, no accounts.** There is no networking code in the app at all; nothing you type can leave your computer.
- **Keystrokes are never recorded.** Only the *current* word is held in memory to score it, then discarded the moment the word ends. There is no keystroke log, anywhere.
- **Password fields are skipped entirely** (Windows). While a password box has focus, keystrokes aren't even buffered for analysis — nothing to score, nothing to replace.
- **No file ever contains what you type.** Besides your settings, two logs are written (Windows):
  - a crash log (`%TEMP%\oopslayout-error.log`), only if something actually throws — exception details for debugging;
  - an activity log (`%AppData%\OopsLayout\activity.log`) — events only: keyboard hook installed/removed, paused/resumed, and entering/leaving an app from your *Apps…* list (by process name). It's there so you can check what the app did, e.g. that the hook really was off while you played.

A layout fixer *has* to watch your typing, which unavoidably looks like a keylogger to your OS and antivirus (see [First run on Windows](#first-run-on-windows)). The honest answer to *"is it spying on me?"* is: the keyboard hook is a couple dozen lines — read them.

## Requirements

- Windows 10 / 11
- [.NET 8 Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) (Desktop)
- English + Russian (and/or Ukrainian) keyboard layouts installed in Windows

## Installation

1. Download the latest build (or build from source, below).
2. Run `OopsLayout.exe`.
3. It appears in the system tray — start typing.

### First run on Windows

The build is **not code-signed** (a signing certificate costs money), so a brand-new unknown app gets a cautious welcome — normal for indie tools:

- **SmartScreen** — *"Windows protected your PC."* Click **More info → Run anyway**.
- **Antivirus may flag it.** A layout fixer installs a global keyboard hook, which trips "keylogger" heuristics even though it logs nothing (see [Privacy](#privacy)). If it gets quarantined, allow it — or build from source and run your own binary.

Nothing is wrong with the download; it's simply the cost of an unsigned binary. Prefer to compile it yourself? The source is right here.

To run on startup: tray → **Launch at startup**.

> ⚠️ **Playing a game?** Pause OopsLayout before launching games with anti-cheat — see [Games & anti-cheat](#games--anti-cheat).

## Tray menu

| Action | Result |
|---|---|
| Double-click icon | Pause / resume |
| **Enabled** | Pause / resume — paused means the keyboard hook is removed |
| **Cyrillic target → Russian / Ukrainian** | Pick which Cyrillic language to switch to |
| **Launch at startup** | Start with Windows |
| **Exceptions…** | Edit your never-switch word list |
| **Apps…** | Apps where OopsLayout does nothing (games, etc.) |
| **About** | Project link |
| **Exit** | Quit |

The icon **greys out** while the switcher is paused or dormant (in a code editor / terminal, or an app from your list); the tooltip says why.

### Russian / Ukrainian

The English side is always English; the "other" side is **Russian or Ukrainian**, whichever you pick under **Cyrillic target**. It's a manual choice (remembered across restarts), not auto-detection — Russian and Ukrainian share too much for bigrams to tell them apart reliably. With Ukrainian selected, corrections use the Ukrainian ЙЦУКЕН layout and switch to your installed Ukrainian keyboard.

## Games & anti-cheat

OopsLayout works by installing a **global keyboard hook** and **injecting keystrokes** — exactly the behaviours that anti-cheat systems (Vanguard, BattlEye, EAC, and many games' own protection) treat as cheating. Running it alongside such a game can make the game **crash, refuse to launch, or flag your account**.

There is **no reliable way to auto-detect anti-cheat and step aside.** By the time any tool could notice the game, its hook is already in the system, and anti-cheats scan at startup — there's always a window where the hook exists. So OopsLayout deliberately does **not** try to guess.

What you can do (Windows):

- **Pause before launching the game** — double-click the tray icon. Pausing removes the keyboard hook and the password-field watcher entirely; for the hook, that's the same as quitting. Resume when you're done. **Exit** works too.
- **Add the game to *Apps…*** — OopsLayout then never buffers or injects anything while the game is in front, so your keys reach it untouched and nothing is typed on your behalf. The hook itself **stays installed**, so this only helps with games whose anti-cheat doesn't react to the mere presence of a keyboard hook. If a game crashes or complains with OopsLayout running, pause before launching it instead.

Want to double-check? `%AppData%\OopsLayout\activity.log` shows when the hook was installed and removed, and when a listed app was in front. No guarantees either way — anti-cheat rules are the game developer's, and they change.

## Building from source

```bash
git clone https://github.com/MnimiMi/OopsLayout
cd OopsLayout
dotnet build
dotnet test
dotnet run --project OopsLayout.Windows
```

## Project structure

```
OopsLayout.Core/            # Platform-agnostic logic
├── KeyMap.cs                 # EN ↔ Russian / Ukrainian character mapping
├── Bigrams.cs                # Generated EN/RU/Ukrainian bigram models (do not hand-edit)
├── WordBuffer.cs             # Collects chars, decides if/how a word is mis-typed
├── SwitcherEngine.cs         # Wires buffer + backend together
└── IKeyboardBackend.cs       # Interface for the platform backend

OopsLayout.Windows/         # Windows app
├── WindowsKeyboardBackend.cs   # Win32 hook + SendInput + layout switch + undo
├── PasswordFocusWatcher.cs     # UIA watcher: skip password fields
├── NativeMethods.cs            # P/Invoke declarations
├── TrayApp.cs                  # WinForms tray (menu, grey-when-dormant icon)
├── ExceptionsForm.cs           # "Exceptions…" settings window
├── UserExceptions.cs           # User keep-words  → exceptions.json
├── AppsForm.cs                 # "Apps…" settings window
├── UserApps.cs                 # User app list    → apps.json
├── Settings.cs                 # Cyrillic-target choice → settings.json
├── AutoStart.cs                # "Launch at startup" (HKCU Run key)
├── ActivityLog.cs              # Event log (activity.log) — never typed text
├── CrashLog.cs                 # Error logging (%TEMP%\oopslayout-error.log)
└── Program.cs                  # Entry point

OopsLayout.Core.Tests/      # xUnit tests for the engine
OopsLayout.Windows.Tests/   # xUnit tests: hook lifecycle, app list, …

OopsLayout.Mac/             # macOS app (Swift)
├── Sources/OopsLayoutCore/     # Core logic, ported from OopsLayout.Core
├── Sources/OopsLayout/         # CGEventTap backend + NSStatusItem menu bar
├── Sources/OopsLayoutSelfTest/ # Headless logic tests (no XCTest needed)
└── build-app.sh                # Assembles OopsLayout.app

tools/
└── gen-bigrams.ps1           # Regenerates Bigrams.cs from frequency lists
```

> This tree covers both platforms. The shared logic (`KeyMap`, `Bigrams`, `WordBuffer`) is duplicated between the C# and Swift ports and kept in sync by hand.

On Windows, user data lives in `%AppData%\OopsLayout\` — `exceptions.json` (your keep-words), `apps.json` (your app list), `settings.json` (Russian/Ukrainian choice) and `activity.log`.

The bigram tables are built from [OpenSubtitles frequency lists](https://github.com/hermitdave/FrequencyWords). To rebuild them:

```powershell
powershell -ExecutionPolicy Bypass -File tools/gen-bigrams.ps1
```

## macOS

A native macOS port lives in [`OopsLayout.Mac/`](OopsLayout.Mac) — a Swift menu-bar
app that mirrors the Windows behaviour one-to-one. The platform-agnostic logic
(`KeyMap`, `Bigrams`, `WordBuffer`, `SwitcherEngine`) is ported to Swift; the
platform layer uses **`CGEventTap`** for global key monitoring, **`CGEvent`**
injection for the text fix, and **`TISSelectInputSource`** for layout switching.
The tray becomes an **`NSStatusItem`** menu-bar item.

### Install (for users)

1. Download `OopsLayout-x.y.z.dmg` from the [latest release](https://github.com/MnimiMi/OopsLayout/releases).
2. Open the DMG and drag **OopsLayout** into **Applications**.
3. **Get past Gatekeeper** (one-time — the app is signed but not notarized):
   - Double-click **OopsLayout**. macOS shows *"OopsLayout can't be opened"* with
     only an **OK** button — click OK.
   - Open **System Settings → Privacy & Security**, scroll to the **Security**
     section. A line now says *"OopsLayout was blocked…"* with an **Open Anyway**
     button — click it and confirm with Touch ID / password.
   - The app opens and the choice is remembered.

   <details><summary>…or do it in one Terminal command</summary>

   ```bash
   xattr -dr com.apple.quarantine /Applications/OopsLayout.app
   ```
   This strips the "downloaded from the internet" flag, after which the app opens
   on a normal double-click. (This is what Gatekeeper is reacting to.)
   </details>
4. Grant **Accessibility** when asked (*Privacy & Security → Accessibility →
   enable OopsLayout*). It starts working the instant you flip the switch.

That's it — no runtime to install (the Swift runtime ships with macOS), and the
app is **universal** so it runs on both Apple Silicon and Intel Macs.

> **Why the Gatekeeper step?** Apps open with zero warnings only if they're
> *notarized* by Apple, which needs a paid Apple Developer account ($99/yr). As a
> free open-source build, OopsLayout is code-signed but not notarized, so macOS
> makes you confirm once. This is normal for indie Mac apps — nothing is wrong
> with the download.

### Build from source

Requirements: macOS 12+, the Swift toolchain (Xcode or Command Line Tools —
`xcode-select --install`), and EN + RU (and/or Ukrainian) layouts enabled in
*System Settings → Keyboard*.

```bash
cd OopsLayout.Mac
./build-app.sh          # produces OopsLayout.app (release, ad-hoc signed)
open OopsLayout.app
```

On first launch macOS asks for **Accessibility** permission (required for the
global keyboard tap). The app starts working the moment you flip the switch (it
polls for the grant — no relaunch needed) and lives in the menu bar.

> **Permission keeps getting asked on every rebuild?** An *ad-hoc* signature
> changes identity each build, so macOS forgets the grant. Create a stable local
> signing identity once:
>
> ```bash
> ./tools/make-signing-cert.sh   # one-time; may prompt for your keychain password
> ./build-app.sh                 # now signed with a stable identity
> ```
>
> Grant Accessibility one last time and it sticks across future rebuilds.

Verify the core logic headlessly (no permissions needed):

```bash
cd OopsLayout.Mac && swift run OopsLayoutSelfTest
```

Build a universal release DMG:

```bash
cd OopsLayout.Mac
./build-dmg.sh          # universal (arm64 + x86_64) OopsLayout-x.y.z.dmg
```

## Known limitations

- Backspacing before a word ends clears the buffer (we can't track what got deleted).
- Layout switching in Chrome can be unreliable (`PostMessage` is sometimes ignored); the text fix still works.
- Mixed-language tokens (e.g. `Wi-Fi`) won't trigger — by design.
- **Undo trusts the caret.** Moving the caret with the *mouse* between a fix and pressing Backspace isn't visible to a keyboard hook, so the restored text would land at the new position. Pressing any other key or switching windows safely cancels the undo instead.
- **Password detection is event-driven.** Clicking into a password box and typing within the first ~100 ms can let a keystroke or two reach the analyzer before the field is recognised (they still aren't logged — nothing ever is). A password typed at human speed is fully covered.

## License

[MIT](LICENSE)

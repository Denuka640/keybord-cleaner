# KeyShield — Keyboard Cleaner

**Designed by Denuka Jayasundara**

KeyShield is a premium, secure Windows application that completely locks your keyboard so you can safely clean it without accidental inputs. It blocks all key presses system-wide, including the Windows key, Alt+Tab, and other system shortcuts, while keeping your touchpad and mouse fully functional.

## Features
- **Deep System Lock**: Uses a low-level Windows keyboard hook (`WH_KEYBOARD_LL`) to intercept and block all keyboard input at the OS level.
- **Emergency Unlock**: Press `Win + 1 + 2 + 3` simultaneously to force-unlock the keyboard in case your mouse/touchpad is unavailable.
- **System Tray Integration**: Minimizes to the system tray for a clean desktop experience.
- **Live Statistics**: Displays a live counter of blocked key presses and a session timer.
- **Modern Glass-morphism UI**: Beautiful, dark, animated UI built with WPF.

## How to Run
1. Double-click the `run.bat` file to quickly launch the application without compiling a standalone executable.
2. To build a standalone, self-contained `.exe` that you can copy anywhere (no .NET installation required):
   - Double-click the `publish.bat` file.
   - The compiled executable will be located in the `publish` folder as `KeyShield.exe`.

## How to Use
1. Launch the application.
2. Click the **Lock Keyboard** button.
3. Clean your keyboard freely.
4. When finished, use your mouse or touchpad to click **Unlock Keyboard**.
5. Alternatively, use the emergency unlock combo: `Win + 1 + 2 + 3`.

## Security & Privacy
This software runs entirely locally and uses standard Windows APIs for low-level hooks. It does not log keystrokes to disk or send any data over the network. The keyboard hook is only active when the lock state is engaged.

> **Note on Special Function Keys**: Some hardware-level keys on laptops (like a physical touchpad toggle button or `Fn+F6` depending on the manufacturer) operate at the driver/hardware level *before* the Windows OS sees the key press. These specific hardware combos might still trigger even when locked because they bypass the OS keyboard event queue.

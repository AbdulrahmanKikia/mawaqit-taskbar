Mawaqit Widget — portable install
=================================

For people who prefer not to use a Setup wizard:

1. Make sure this folder contains:
     Mawaqit Widget.exe
     Install.bat
     Uninstall.bat
     (plus the bundled Windows App SDK / WinUI files)

2. Double-click Install.bat

3. Allow Windows / SmartScreen if it asks (More info → Run anyway).
   Tip: right-click Install.bat or "Mawaqit Widget.exe" → Properties → Unblock.

4. A prayer chip should appear on the taskbar (next to the clock).
   Right-click → Change mosque… to pick your mosque.
   If inject fails, use the tray icon near the clock.

If Windows says "Application Control policy has blocked this file"
(error 4551): the app is unsigned. Turn off Smart App Control
(Windows Security → App & browser control), or ask IT on a work PC.

To remove: double-click Uninstall.bat

Requirements: Windows 11 (64-bit, build 22000+), internet, WebView2 Runtime
(WebView2 is usually already installed on Windows 11).
Windows App SDK 1.5 is bundled (self-contained); no extra runtime install.

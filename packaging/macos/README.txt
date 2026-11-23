Juicebar for macOS (not signed with an Apple Developer account)

1. Copy Juicebar.app to the Applications folder.
2. Open Terminal and run:

   chmod +x /Applications/Juicebar.app/Contents/MacOS/Juicebar
   xattr -dr com.apple.quarantine /Applications/Juicebar.app
   codesign --force --deep --sign - /Applications/Juicebar.app

   (the last line signs the app locally; it is required on Apple silicon Macs.
    Packages built by the GitHub release workflow are already signed this way.)
3. Open Juicebar. The icon appears in the menu bar, top right.

If macOS asks for "Input Monitoring" permission to read the devices,
allow it in System Settings > Privacy & Security.

Use Juicebar-osx-arm64 on Apple silicon Macs (M1 or later) and Juicebar-osx-x64 on Intel Macs.
Keyboard shortcut: assign "/Applications/Juicebar.app/Contents/MacOS/Juicebar --show" to a shortcut
(for example with the Shortcuts app).

# Aj179PStat 🖱️🔋

A lightweight Windows System Tray application written in C# to display the battery percentage of your **Ajazz AJ179 Pro** wireless mouse.

![License](https://img.shields.io/badge/License-MIT-blue.svg)
![Platform](https://img.shields.io/badge/Platform-Windows-lightgrey.svg)
![Framework](https://img.shields.io/badge/.NET-10.0-purple.svg)

---

## 🌟 Features

- 📌 **Dynamic System Tray Icon**: Renders the exact battery percentage number directly onto the notification area icon with dynamic color badges (Green > 50%, Yellow 21-50%, Red <= 20%).
- ⚡ **Single-Click Refresh**: Left-click the system tray icon anytime for instant battery status updates.
- 🖼️ **Double-Click Dashboard**: Double-click the system tray icon to open the full diagnostic Dashboard Window.
- 🔔 **Customizable Low Battery Notifications**: Get Windows notifications when the battery drops below your chosen percentage (e.g. 20%).
- 🔄 **Instant Auto-Reconnect**: Uses native Windows `WM_DEVICECHANGE` hardware events + 10s fast-polling to reconnect instantly when the mouse wakes up or is plugged in.
- 🚀 **Start with Windows**: Built-in toggle to run automatically on Windows startup via Registry.
- 📦 **Standalone Single Executable**: Compiled into a single `.exe` with zero external runtime or DLL dependencies.

---

## 🛠️ Hardware Compatibility

- **Mouse Model**: Ajazz AJ179 Pro Wireless Mouse
- **Vendor ID (VID)**: `0x3151` (`12625`)
- **Product ID (PID)**: `0x402D` (`16429`)
- **Report Details**:
  - Request: 64-byte payload starting with `0xF7` (`247`).
  - Response: 64-byte payload parsing `data[2]` for battery level percentage (0 - 100%).

---

## 📥 Installation & Running

### Download
Download the latest single-file `Aj179PStat.exe` release from the [Releases](https://github.com/GetTheNya/Aj179PStat/releases) page.

### Run
Double click `Aj179PStat.exe`. It will start silently in your Windows system tray.

---

## 🔨 Building & Publishing Single EXE

### Prerequisites
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download) or [.NET 10.0 SDK](https://dotnet.microsoft.com/download)

### Build Standalone Single File EXE
Execute the following command:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o ./publish
```

The compiled standalone executable will be located in `./publish/Aj179PStat.exe`.

---

## 🖥️ Usage & Controls

- **Left Click Tray Icon**: Refreshes battery status.
- **Double-Click Tray Icon**: Opens Dashboard window.
- **Right-Click Tray Icon**: Opens Context Menu (Refresh Now, Open Dashboard, Exit).

---

## 📄 License
This project is licensed under the MIT License.

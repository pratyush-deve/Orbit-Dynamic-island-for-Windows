# PROJECT ORBIT 2.0 — Native Windows Desktop Companion

> **High-Performance, Always-On-Top Windows Companion built with .NET 10, C# 14, WPF, and Native Win32 APIs.**

### 🚀 Completed Features & Modes

- ✅ **CTRL Mode (System Controls)**: Compact, equal-sized horizontal card strip providing one-click toggles and live status detection for Microphone (global Windows mute), Wi-Fi (adapter state & connected network SSID), Bluetooth (radio state & connected device names), System Volume (master mute), and native Windows Settings access (`ms-settings:`).
- ✅ **DEV Mode (Developer Workspace Launcher)**: Streamlined developer workspace launcher with automatic tool discovery, independent detection and branding for both **Antigravity** and **Antigravity IDE**, Android Studio, and Google Chrome (with ChatGPT). Includes Manage Apps configuration with collapsible search, invisible smooth scrolling, selection persistence, and one-click workspace activation.
- ✅ **Dynamic Responsive Sizing**: Downward-expanding popup anchored to the top screen edge, smoothly animating height according to active screen content.
- ⏳ **AI Mode (Agent Hub)**: In active preparation to observe and monitor Antigravity AI agent activities.

All source code, solution files, and test projects are located in [`ORBIT-WPF/`](ORBIT-WPF/).

See the comprehensive [ORBIT-WPF README](ORBIT-WPF/README.md) for architecture, design specifications, and API documentation.

### Quick Commands

Build solution:
```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build ORBIT-WPF\Orbit.sln
```

Run test suite:
```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" test ORBIT-WPF\Orbit.sln
```

Run application:
```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" run --project ORBIT-WPF\Orbit\Orbit.csproj
```

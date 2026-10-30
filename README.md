# PROJECT ORBIT 2.0 — Native Windows Desktop Companion

> **High-Performance, Always-On-Top Windows Companion built with .NET 10, C# 14, WPF, and Native Win32 APIs.**

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

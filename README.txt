AudioPopFix Tray App (C# + NAudio)
===================================

What it does
------------
- Small tray app that keeps selected audio devices "awake" by playing silence directly
  to each device (no Volume Mixer routing needed).
- Lets the user pick exact devices to target.
- Stores settings in %AppData%\AudioPopFix\config.json.
- Optional "Start with Windows" toggle (per-user, no admin required).

Build requirements
------------------
- Windows 10/11
- .NET 6 SDK
- Visual Studio 2022 (or `dotnet` CLI)
- NuGet package: NAudio (restores automatically on build)

How to build
------------
1) Open `src\AudioPopFixTray\AudioPopFixTray.csproj` in Visual Studio, or run:
   dotnet build -c Release src\AudioPopFixTray\AudioPopFixTray.csproj

2) The build output will be in:
   src\AudioPopFixTray\bin\Release\net6.0-windows\

3) Run `AudioPopFixTray.exe`. Right-click the tray icon to select devices and enable "Start with Windows".

Inno Setup installer
--------------------
- Use the provided `installer\AudioPopFix_Inno.iss` to package the compiled EXE into a single installer.
- The installer lets the user choose any install directory (no fixed C:\Program Files\AudioFix requirement).
- The installer can optionally add a Start Menu shortcut and "Start with Windows" entry.

Notes
-----
- Because the app writes silence directly to device endpoints, it doesn't rely on
  per-app audio routing; your selection is exact.
- You can run multiple devices simultaneously.

Code Signing (optional but recommended)
======================================

To sign the EXE and installer, you need:
- A code signing certificate (PFX file)
- Its password
- A timestamp server URL (e.g., http://timestamp.digicert.com)

How to enable signing:
1) Open "installer\build_all.bat" and set these env vars BEFORE running it, e.g.:
   set CERT_PATH=C:\certs\yourcert.pfx
   set CERT_PASS=yourpassword
   set TIMESTAMP_URL=http://timestamp.digicert.com

2) Run build_all.bat. If signtool is found and vars are set, it will sign:
   - AudioPopFixTray.exe
   - AudioPopFixTray_Setup.exe

Notes:
- signtool.exe comes with Windows SDK. Typical path:
  "C:\Program Files (x86)\Windows Kits\10\bin\x64\signtool.exe"
- If you don't have it, install the Windows 10/11 SDK.

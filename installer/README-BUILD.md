# Building the Bapala Media Server Installer

## Prerequisites
- .NET 10 SDK
- [Inno Setup 6.3+](https://jrsoftware.org/isinfo.php)

---

## Step 1 — Publish a self-contained Windows executable

Open PowerShell in the repo root and run:

```powershell
cd "F:\2026\code\repos\Bapala Media Manager\BapalaServer"

dotnet publish -c Release -r win-x64 --self-contained true `
  /p:PublishSingleFile=true `
  /p:EnableCompressionInSingleFile=true `
  -o "..\publish\win-x64"
```

This produces a **single `.exe`** (~60-80 MB) with the .NET runtime bundled — users do **not** need .NET installed.

---

## Step 2 — Copy the browser helper into the publish folder

```powershell
Copy-Item "F:\2026\code\repos\Bapala Media Manager\installer\open-browser.bat" `
          "F:\2026\code\repos\Bapala Media Manager\publish\win-x64\"
```

---

## Step 3 — Build the installer

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" `
  "F:\2026\code\repos\Bapala Media Manager\installer\BapalaServer.iss"
```

Output: `installer\Output\BapalaServer-Setup-1.0.0.exe`

---

## What the installer does

1. Copies all files to `C:\Program Files\EllenJoe Software\Bapala Media Server\`
2. Registers **BapalaMediaServer** as a Windows Service (auto-start)
3. Optionally starts the service immediately
4. Optionally adds a Windows Firewall inbound rule for TCP port 8484
5. Adds Start Menu shortcuts

## What the uninstaller does

1. Stops and deletes the Windows Service
2. Removes the Firewall rule
3. Deletes all installed files

---

## Service management (manual)

```powershell
# Check status
sc.exe query BapalaMediaServer

# Stop / Start / Restart
sc.exe stop  BapalaMediaServer
sc.exe start BapalaMediaServer

# View logs (Windows Event Viewer or)
Get-EventLog -LogName Application -Source BapalaMediaServer -Newest 20
```

---

## Configuring the server after install

Edit `appsettings.json` in the install directory, then restart the service:

| Setting | Default | Description |
|---|---|---|
| `Bapala:Username` | `admin` | Login username |
| `Bapala:Password` | `changeme` | Login password — **change this!** |
| `Bapala:Port` | `8484` | HTTP port |
| `Bapala:ServerName` | `My Bapala Server` | Name shown in the mobile app |
| `Bapala:MediaFolders` | `[]` | Paths to scan for media files |
| `Jwt:Secret` | *(default)* | **Change this in production** |

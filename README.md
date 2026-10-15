# ECO-WORTHY Battery Tray Monitor — Community Edition (prototype)

**Separate project.** Derived from a personal Windows BLE tray monitor. Does not include the original owner’s battery identifiers or Bluetooth addresses.

## Features
- Discovers nearby advertisements with ECO-WORTHY, OB_, or 0B_ naming and four-character hex suffixes.
- Right-click tray icon → **Choose batteries...** to select the bank members; selections persist locally.
- Shows an overall SOC only when **all selected batteries** have fresh readings (within 45 seconds).
- Hover to see individual readings; colored tray badge indicates overall charge.
- Right-click → **Pause Bluetooth** to release connections for the manufacturer's mobile app; **Resume Bluetooth** reconnects.
- Does not write BMS settings or send FFF2 commands. It does write the standard BLE notification subscription descriptor (CCCD) when connecting/disconnecting.

## Install / build
Windows 10/11 with Bluetooth LE; .NET 8 Windows target. Run GitHub Actions → Build ECO-WORTHY Tray Monitor → Run workflow, then download the Windows artifact. Alternatively: `dotnet publish EcoWorthyCommunityEdition.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish`.

## First use
Start program; wait for nearby batteries to advertise. Right-click the taskbar icon → **Choose batteries...**, select devices, then **Save and resume**. If no batteries appear, ensure Bluetooth is on, the batteries are in range, and the manufacturer's app is disconnected. Selection screen intentionally pauses monitoring.

## Limitations and safety
**Prototype — not yet tested on a Windows host in this environment.** Only one known ECO-WORTHY notification format is parsed; other models may be incompatible. BLE disconnect may take a few seconds after pausing. If the official app cannot connect immediately, wait and retry. This is informational software, not a safety or battery-protection device. SOC bank average is an unweighted arithmetic mean, not a capacity-weighted measurement. The program does not change BMS settings. Do not publish or rely on it until compiled and tested on a real Windows BLE system.

## Privacy
Battery identifiers selected by the user are stored only in `%LOCALAPPDATA%\EcoWorthyCommunityEdition\batteries.json`; no telemetry or internet transmission is implemented.

# ECO-WORTHY Bluetooth Battery Tray Monitor

A free, lightweight Windows application that monitors multiple ECO-WORTHY Bluetooth LiFePO4 batteries and displays their combined state of charge (SOC) directly in the Windows system tray.

Designed for off-grid solar systems, RVs, camper vans, and anyone managing multiple compatible ECO-WORTHY batteries.

**Independent community project — not affiliated with or endorsed by ECO-WORTHY.**

## Features

- **Automatic Bluetooth discovery** — Finds nearby compatible ECO-WORTHY batteries without manually entering Bluetooth addresses.
- **Flexible battery selection** — Choose which batteries to monitor, whether you have one battery or an entire bank.
- **Taskbar SOC display** — Shows the average charge percentage of your selected batteries directly in the Windows system tray.
- **Individual battery readings** — Hover over the tray icon to view each battery's SOC and monitoring status.
- **Incomplete data indicators** — Displays `--` until all selected batteries have reported fresh readings, rather than showing a potentially misleading partial average.
- **Saved battery selections** — Remembers your chosen batteries between sessions.
- **Pause/Resume Bluetooth** — Temporarily releases Bluetooth connections so you can use the official ECO-WORTHY mobile app.
- **Local monitoring** — Reads battery information without sending BMS configuration commands or changing battery settings.
- **No internet required** — Operates locally using your computer's Bluetooth connection.
- **No subscription or additional monitoring hardware required.**

## Compatibility

**Supports multiple ECO-WORTHY Bluetooth batteries. Tested with eight; larger banks have not yet been verified.**

The application has been successfully built and tested on Windows with eight ECO-WORTHY Bluetooth LiFePO4 batteries.

The following functionality has been verified with this setup:

- Automatic battery discovery
- Selection and deselection of batteries
- SOC readings and bank-average calculations
- Correct recalculation when changing the number of selected batteries
- Saved selections after restarting
- Bluetooth pause/resume functionality
- Compatibility with the official ECO-WORTHY mobile app when monitoring is paused
- Stable taskbar hover display

### Important compatibility limitations

Only one known ECO-WORTHY Bluetooth notification format is currently supported. Other ECO-WORTHY battery models may use different Bluetooth protocols and may not be compatible.

Although the software is designed to support different numbers of batteries, Bluetooth adapter limitations, signal strength, and connection reliability may affect performance with larger battery banks.

**Larger installations, including banks of 12, 16, 24, or more batteries, have not yet been verified.**

## System requirements

- Windows 10 or Windows 11
- Bluetooth Low Energy (BLE) support
- Compatible ECO-WORTHY Bluetooth LiFePO4 batteries
- Batteries within Bluetooth range of the computer

The downloadable self-contained Windows build does not require users to install the .NET runtime separately.

## Installation

1. Visit the project's GitHub **Releases** page.
2. Download the latest Windows executable ZIP.
3. Extract the ZIP to a folder of your choice.
4. Run `EcoWorthyCommunityEdition.exe`.
5. If the application does not appear immediately, check the system tray near the Windows clock, including the hidden icons menu (`^`).

The application does not require an internet connection, account registration, or subscription.

## First use

1. Start the application.
2. Allow a short time for nearby ECO-WORTHY batteries to advertise over Bluetooth.
3. The battery-selection window should appear automatically during initial setup.
4. Select the batteries you want included in your monitored bank.
5. Click **Save and resume** to begin monitoring.
6. Wait for the selected batteries to report their SOC percentages.
7. The overall bank percentage will appear in the Windows system tray once all selected batteries have fresh readings.
8. If the percentage icon is hidden, click the ^ arrow near the Windows clock and drag the ECO-WORTHY icon onto the taskbar beside the clock. Windows normally remembers this preference, so you should only need to do this once.

To change your selection later, right-click the tray icon and choose **Choose batteries...**

The selection screen intentionally pauses Bluetooth monitoring while you make changes.

**Note:** Windows controls which notification icons remain visible on the taskbar. The application cannot automatically override these settings.

### If no batteries appear

- Confirm that Bluetooth is enabled on your Windows computer.
- Make sure the batteries are powered on and within Bluetooth range.
- Disconnect the official ECO-WORTHY mobile app from the batteries.
- Allow additional time for Bluetooth discovery.
- Close and reopen the battery-selection window to see newly discovered devices.

## Using the taskbar monitor

The system tray icon displays the average SOC of your selected batteries.

Hover over the icon to view individual battery percentages and connection information.

### Understanding the display

- **Percentage shown:** All selected batteries have supplied fresh readings, and the average is available.
- **`--` shown:** One or more selected batteries have not yet supplied fresh readings.
- **Individual battery information:** The hover panel allows you to identify which batteries have reported and which are still pending.

The application intentionally avoids calculating an incomplete bank average.

## Pause and resume Bluetooth

Some ECO-WORTHY Bluetooth battery modules support only one active Bluetooth connection at a time.

This means the official ECO-WORTHY phone app may be unable to connect while the Windows monitor is actively connected.

To use the official app:

1. Right-click the monitor's system tray icon.
2. Select **Pause Bluetooth**.
3. Open the ECO-WORTHY mobile app and connect to your batteries.
4. When finished, disconnect or close the mobile app.
5. Right-click the tray icon and select **Resume Bluetooth**.

Bluetooth disconnections may take several seconds to complete. If the mobile app cannot connect immediately after pausing, wait briefly and try again.

## How the bank average is calculated

The displayed bank percentage is the **unweighted arithmetic mean** of the selected batteries' SOC readings.

For example, if four selected batteries report:

| Battery | SOC |
| --- | ---: |
| Battery 1 | 80% |
| Battery 2 | 90% |
| Battery 3 | 70% |
| Battery 4 | 100% |

The displayed bank average is **85%**.

This method is most meaningful when the selected batteries have equal usable capacities.

It is **not** a capacity-weighted measurement of the total energy remaining in the battery bank. It does not replace a properly configured battery shunt or dedicated battery monitoring system.

## Privacy and data storage

Selected battery identifiers are stored locally on your computer at:

`%LOCALAPPDATA%\EcoWorthyCommunityEdition\batteries.json`

The application does not implement telemetry, analytics, cloud synchronization, or internet transmission of battery readings.

Bluetooth communication takes place locally between your Windows computer and the selected batteries. No online account is required.

## Limitations and safety

**Tested on Windows with eight ECO-WORTHY Bluetooth LiFePO4 batteries.** Only one known ECO-WORTHY notification format is parsed; other models may be incompatible. Larger battery banks have not yet been verified. BLE disconnect may take a few seconds after pausing. If the official app cannot connect immediately, wait and retry.

This is informational software, **not a safety or battery-protection device**. The SOC bank average is an unweighted arithmetic mean, not a capacity-weighted measurement. The program does not send BMS configuration commands or intentionally change BMS settings. Bluetooth notification subscriptions are used to receive readings.

- Battery readings depend on the information reported by the battery's BMS.
- Bluetooth connectivity may be affected by range, interference, hardware capabilities, and the number of connected batteries.
- Do not use this program as the sole source of information for safety-critical decisions.
- Continue using appropriate battery protection equipment and the manufacturer's recommended installation and operating procedures.

## Development and testing status

**Community Edition — initial release candidate**

Successfully compiled and tested on a Windows computer with eight compatible ECO-WORTHY Bluetooth batteries.

Core functionality has been verified, including automatic discovery, battery selection, saved preferences, SOC calculations, hover display, and Bluetooth pause/resume.

Additional testing with other battery models, Windows computers, Bluetooth adapters, and larger battery banks is welcome.

## Contributing and feedback

Feedback from other ECO-WORTHY battery owners is welcome, particularly regarding:

- Compatible and incompatible battery models
- Performance with larger battery banks
- Bluetooth connection reliability
- Windows and Bluetooth adapter compatibility
- Bugs or unexpected behavior

Please use the GitHub **Issues** section to report problems or suggest improvements.

When reporting an issue, include your Windows version, battery model, number of batteries, and a description of the problem.

**Please avoid posting private Bluetooth addresses, serial numbers, or other sensitive information in public issue reports.**

## Disclaimer

This is an independent community-developed utility and is not an official ECO-WORTHY product.

The software is provided without guarantees of compatibility, accuracy, availability, or suitability for any particular battery installation.

Always follow the battery manufacturer's safety guidance.

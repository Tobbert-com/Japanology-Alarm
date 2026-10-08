Japanology Alarm v3 — LAN controls

1. Extract this ZIP on the Windows 8.1 tablet.
2. Double-click StartJapanologyAlarm.bat.
3. Accept the Windows administrator permission prompt on the first run.
4. Connect the tablet and the other PC to the same Wi-Fi or Ethernet network.
5. The clock screen and its control page show an address such as:
   http://192.168.1.25:8123
6. Type that address into a web browser on the other PC.

Security:
- The firewall rule permits port 8123 only on Windows Private networks.
- Do not configure router port forwarding for port 8123.
- Set the Wi-Fi network to Private on the Windows tablet if the other PC cannot connect.
- Anyone on the same local network can open this control page. Use only a trusted home/school network.

The batch file builds JapanologyAlarm.exe automatically on its first run using the .NET Framework compiler included with .NET Framework 4.8. Later starts simply launch the existing application.

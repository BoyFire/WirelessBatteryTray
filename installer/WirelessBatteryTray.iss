#ifndef AppSource
  #error AppSource must point to the published app directory
#endif

[Setup]
AppId={{D5AB709E-AE48-4A89-8DD9-7AE05F6257B9}
AppName=Wireless Battery Tray
AppVersion=0.1.0
AppPublisher=Wireless Battery Tray
DefaultDirName={localappdata}\Programs\WirelessBatteryTray
DefaultGroupName=Wireless Battery Tray
PrivilegesRequired=lowest
OutputBaseFilename=WirelessBatteryTray-Setup-UNSIGNED
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\WirelessBatteryTray.exe
DisableProgramGroupPage=yes
ChangesAssociations=no
CloseApplications=yes

[Files]
Source: "{#AppSource}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Wireless Battery Tray"; Filename: "{app}\WirelessBatteryTray.exe"

[Run]
Filename: "{app}\WirelessBatteryTray.exe"; Description: "启动 Wireless Battery Tray"; Flags: nowait postinstall skipifsilent

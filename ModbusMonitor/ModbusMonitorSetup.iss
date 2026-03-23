[Setup]
; 基础信息
AppName=Modbus Monitor 工业温度监控系统
AppVersion=1.0.0
AppPublisher=西安斯克达机械制造有限公司
AppPublisherURL=http://www.yourcompany.com
DefaultDirName={pf}\ModbusMonitor
DefaultGroupName=Modbus Monitor
OutputBaseFilename=Modbus监控系统安装包_v1.0
SetupIconFile=app.ico
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64

[Tasks]
Name: "desktopicon"; Description: "在桌面创建快捷方式"; GroupDescription: "附加任务:"; Flags: unchecked

[Files]
; 打包 publish 文件夹下的所有文件（这个目录由 dotnet publish 命令生成）
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; 创建开始菜单和桌面快捷方式，指向被打包进去的执行文件
Name: "{group}\Modbus Monitor"; Filename: "{app}\ModbusMonitor.exe"
Name: "{group}\卸载 Modbus Monitor"; Filename: "{uninstallexe}"
Name: "{commondesktop}\Modbus Monitor"; Filename: "{app}\ModbusMonitor.exe"; Tasks: desktopicon

[Run]
; 安装完成后提供立即运行的选项
Filename: "{app}\ModbusMonitor.exe"; Description: "启动 Modbus Monitor"; Flags: nowait postinstall skipifsilent

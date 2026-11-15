; Установочный пакет серверной части Picket302 (Inno Setup 6).
; Отмена и неуспешная установка не оставляют наполовину созданную службу:
; регистрация службы выполняется самоустановкой исполняемого файла
; (Picket302.Service.exe --install), а удаление — Picket302.Service.exe
; --uninstall в секции [UninstallRun]. Пользовательские данные (каталог
; данных службы и копии) не удаляются — удаление объясняет это явно.

#define AppName "Picket302 Server"
#define ServiceName "Picket302Service"
#define AppVersion "1.0"

[Setup]
AppId={{8A1C2F4E-6B7D-4E3A-9C21-PICKET302SRV}
AppName={#AppName}
AppVersion={#AppVersion}
DefaultDirName={autopf}\Picket302 Service
DefaultGroupName=Picket302
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=picket302-server-setup
Compression=lzma
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
Uninstallable=yes
CreateAppDir=yes

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[Files]
; Сборка службы и всех её зависимостей публикуется заранее:
; dotnet publish src/Picket302.Service -c Release -r win-x64
Source: "..\src\Picket302.Service\bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Dirs]
; Каталог данных службы: ProgramData, не профиль сотрудника и не рядом с EXE.
; Права группам пользователей НЕ выдаются: при каждом старте служба
; ограничивает права каталога и всех вложенных объектов (SYSTEM,
; Администраторы, фактическая учётная запись службы) и отказывается
; запускаться (код выхода 4), если ограничить или проверить права не
; удалось. Право users-modify, выданное прежней версией установщика,
; убирается при первом старте службы.
Name: "{commonappdata}\Picket302\Service"
Name: "{commonappdata}\Picket302\Service\backups"

[Run]
; Регистрация службы под учётной записью с минимальными правами.
Filename: "{app}\Picket302.Service.exe"; Parameters: "--install"; Flags: runhidden waituntilterminated
; Правило брандмауэра для локальной сети: входящий TCP на порт 30242.
; Правило создаётся для порта по умолчанию. Нестандартный порт
; (service.json, ServiceOptions.Port) требует отдельного ручного правила —
; автоматическая настройка брандмауэра под кастомный порт не поддерживается.
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""Picket302 Service"" dir=in action=allow protocol=TCP localport=30242 profile=private"; Flags: runhidden
; Первый запуск службы.
Filename: "{sys}\sc.exe"; Parameters: "start {#ServiceName}"; Flags: runhidden

[UninstallRun]
; Остановка и снятие регистрации службы. Данные в
; {commonappdata}\Picket302\Service сохраняются.
Filename: "{sys}\sc.exe"; Parameters: "stop {#ServiceName}"; Flags: runhidden; RunOnceId: "StopService"
Filename: "{app}\Picket302.Service.exe"; Parameters: "--uninstall"; Flags: runhidden waituntilterminated; RunOnceId: "DeleteService"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""Picket302 Service"""; Flags: runhidden; RunOnceId: "DeleteFirewall"

[UninstallDelete]
; Ничего в {commonappdata} не удаляется: база, шаблоны и копии остаются.

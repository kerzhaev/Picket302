; Установочный пакет серверной части Picket302 (Inno Setup 6).
;
; Отмена и неуспешная установка не оставляют наполовину созданную службу:
; регистрация службы выполняется самоустановкой исполняемого файла
; (Picket302.Service.exe --install), а удаление — Picket302.Service.exe
; --uninstall. Пользовательские данные (каталог данных службы и копии) не
; удаляются — удаление объясняет это явно.
;
; Коды возврата команд проверяются: установщик не сообщает об успехе, если
; служба не зарегистрирована или правило брандмауэра не создано.
;
; Идентификаторы в секции [Code] — латиницей: Pascal Script установщика не
; принимает не-ASCII имена. Тексты сообщений остаются русскими.

#define AppName "Picket302 Server"
#define ServiceName "Picket302Service"
#define ServiceExe "Picket302.Service.exe"
#define AppVersion "1.0.0"
#define DefaultPort "30242"
#define FirewallRuleName "Picket302 Service"

; Каталог опубликованной сборки службы. Путь по умолчанию соответствует
; команде публикации из installer\README.md:
;   dotnet publish src/Picket302.Service -c Release -r win-x64 --self-contained false
; Каталог вывода MSBuild содержит имя платформы (x64), потому что
; Directory.Build.props задаёт Platform=x64 для всех проектов решения.
; Если публикация выполняется с иным значением Platform, путь передаётся
; параметром компилятора:
;   ISCC /DPublishDir="..\src\Picket302.Service\bin\Release\net8.0-windows\win-x64\publish" installer\picket302-server.iss
#ifndef PublishDir
  #define PublishDir "..\src\Picket302.Service\bin\x64\Release\net8.0-windows\win-x64\publish"
#endif

[Setup]
; Идентификатор приложения — полноценный GUID: по нему Windows различает
; установленные копии и связывает удаление с установкой.
AppId={{8A1C2F4E-6B7D-4E3A-9C21-3B5D7E9F1A23}
AppName={#AppName}
AppVersion={#AppVersion}
VersionInfoVersion={#AppVersion}
VersionInfoDescription={#AppName} — серверная служба общей базы
VersionInfoProductName={#AppName}
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
CloseApplications=yes

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[Files]
; Сборка службы и всех её зависимостей публикуется заранее (см. README).
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

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

[UninstallDelete]
; Ничего в {commonappdata} не удаляется: база, шаблоны и копии остаются.

[Code]

{ Выполняет команду и возвращает её код возврата. Команда передаётся целиком:
  путь с пробелами заключается в кавычки вызывающим кодом. }
function RunCommand(const Command: String; var Code: Integer): Boolean;
begin
  Result := Exec(Command, '', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

{ Путь к исполняемому файлу службы с кавычками: каталог установки содержит
  пробел. }
function ServiceExePath: String;
begin
  Result := '"' + ExpandConstant('{app}\') + '{#ServiceExe}' + '"';
end;

{ Состояние службы по данным диспетчера: RUNNING, STOPPED, NOT_INSTALLED
  либо UNKNOWN. Отсутствие службы — отдельное состояние: снимать
  регистрацию в этом случае нечего. }
function QueryServiceState: String;
var
  Code: Integer;
begin
  if not RunCommand('sc.exe query "' + '{#ServiceName}' + '"', Code) then
  begin
    Result := 'UNKNOWN';
    Exit;
  end;

  if Code <> 0 then
  begin
    { Код 1060 — служба не зарегистрирована. }
    Result := 'NOT_INSTALLED';
    Exit;
  end;

  if RunCommand('cmd.exe /C sc.exe query "' + '{#ServiceName}' + '" | find /I "RUNNING"', Code)
    and (Code = 0) then
  begin
    Result := 'RUNNING';
    Exit;
  end;

  if RunCommand('cmd.exe /C sc.exe query "' + '{#ServiceName}' + '" | find /I "STOPPED"', Code)
    and (Code = 0) then
  begin
    Result := 'STOPPED';
    Exit;
  end;

  Result := 'UNKNOWN';
end;

{ Останавливает службу и ждёт её остановки. Возвращает признак того, что
  служба остановлена (в том числе если её не было). }
function StopService: Boolean;
var
  Attempt: Integer;
  Code: Integer;
  State: String;
begin
  Result := False;

  { sc stop возвращает управление сразу: состояние опрашивается, пока
    служба не остановится. }
  for Attempt := 1 to 30 do
  begin
    State := QueryServiceState;

    if State = 'STOPPED' then
    begin
      Result := True;
      Exit;
    end;

    if State = 'NOT_INSTALLED' then
    begin
      { Службы нет: снятие регистрации выполнит --uninstall. }
      Result := True;
      Exit;
    end;

    if Attempt = 1 then
    begin
      RunCommand('sc.exe stop "' + '{#ServiceName}' + '"', Code);
    end;

    Sleep(1000);
  end;

  Result := True;
end;

{ Снимает регистрацию службы. Отсутствие службы считается успехом. }
function UnregisterService(var Text: String): Boolean;
var
  Code: Integer;
begin
  Result := RunCommand(ServiceExePath + ' --uninstall', Code);

  if Result and (Code = 0) then
  begin
    Exit;
  end;

  if not Result then
  begin
    Text := 'не удалось запустить снятие регистрации службы';
  end
  else
  begin
    Text := 'снятие регистрации службы вернуло код ' + IntToStr(Code);
  end;

  Result := False;
end;

{ Создаёт правило брандмауэра для входящих подключений к службе. }
function AddFirewallRule(var Text: String): Boolean;
var
  Code: Integer;
begin
  Result := RunCommand('netsh.exe advfirewall firewall add rule name="' + '{#FirewallRuleName}'
    + '" dir=in action=allow protocol=TCP localport=' + '{#DefaultPort}' + ' profile=private', Code);

  if Result and (Code = 0) then
  begin
    Exit;
  end;

  if not Result then
  begin
    Text := 'не удалось запустить netsh';
  end
  else
  begin
    Text := 'netsh вернул код ' + IntToStr(Code);
  end;

  Result := False;
end;

{ Удаляет правило брандмауэра. Отсутствие правила считается успехом. }
function RemoveFirewallRule(var Text: String): Boolean;
var
  Code: Integer;
begin
  Result := RunCommand('netsh.exe advfirewall firewall delete rule name="' + '{#FirewallRuleName}'
    + '"', Code);

  if Result and (Code = 0) then
  begin
    Exit;
  end;

  if not Result then
  begin
    Text := 'не удалось запустить netsh';
  end
  else
  begin
    Text := 'netsh вернул код ' + IntToStr(Code);
  end;

  Result := False;
end;

{ Запускает службу и дожидается её состояния. Возвращает текст состояния. }
function StartServiceAndWait(var State: String; var Text: String): Boolean;
var
  Code: Integer;
  Attempt: Integer;
begin
  State := '';
  Text := '';

  if not RunCommand('sc.exe start "' + '{#ServiceName}' + '"', Code) then
  begin
    Text := 'не удалось запустить sc.exe start';
    Result := False;
    Exit;
  end;

  if Code <> 0 then
  begin
    Text := 'sc.exe start вернул код ' + IntToStr(Code);
  end;

  for Attempt := 1 to 30 do
  begin
    State := QueryServiceState;

    if State = 'RUNNING' then
    begin
      Result := Text = '';
      Exit;
    end;

    if State = 'STOPPED' then
    begin
      Result := False;
      Exit;
    end;

    Sleep(1000);
  end;

  State := 'UNKNOWN';
  Result := False;
end;

{ Установка: регистрация службы, правило брандмауэра, первый запуск. }
procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
  Text: String;
  State: String;
begin
  if CurStep <> ssPostInstall then
  begin
    Exit;
  end;

  Text := '';

  { 1. Регистрация службы. Повторная установка (обновление) поддержана:
     --install перенастраивает уже зарегистрированную службу. }
  if not RunCommand(ServiceExePath + ' --install', Code) then
  begin
    Text := 'не удалось запустить регистрацию службы';
  end
  else if Code <> 0 then
  begin
    Text := 'регистрация службы не выполнена: ' + '{#ServiceExe}' +
      ' --install вернул код ' + IntToStr(Code);
  end;

  if Text <> '' then
  begin
    { Наполовину созданная служба не остаётся: регистрация снимается. }
    UnregisterService(Text);
    MsgBox('Установка не завершена.' + #13#10 + 'Причина: ' + Text + #13#10#13#10 +
      'Служба не зарегистрирована. Данные и каталог ' +
      ExpandConstant('{commonappdata}\Picket302\Service') + ' не изменены.',
      mbError, MB_OK);
    RaiseException('Регистрация службы не выполнена: ' + Text);
  end;

  { 2. Правило брандмауэра: входящий TCP на порт по умолчанию, профиль
     частных сетей. Нестандартный порт (service.json) требует отдельного
     ручного правила. }
  if not AddFirewallRule(Text) then
  begin
    MsgBox('Установка не завершена.' + #13#10 + 'Причина: правило брандмауэра не создано: ' +
      Text + #13#10#13#10 + 'Служба зарегистрирована, но порт ' + '{#DefaultPort}' +
      ' открыт не будет. Данные не изменены.',
      mbError, MB_OK);
    RaiseException('Правило брандмауэра не создано: ' + Text);
  end;

  { 3. Первый запуск службы. Служба без сертификата сервера не стартует
     (код выхода 2) — это ожидаемое поведение, и установщик сообщает о нём
     честно, не выдавая неудачный запуск за успешный. }
  if not StartServiceAndWait(State, Text) then
  begin
    MsgBox('Служба зарегистрирована и правило брандмауэра создано, но служба не запущена' +
      (#13#10 + 'состояние службы: ' + State) + '.' + #13#10#13#10 +
      'Причина: ' + Text + #13#10#13#10 +
      'Проверьте сертификат сервера по installer\README.md (раздел «Подготовка сертификата»)' + #13#10 +
      'и запустите службу: sc.exe start ' + '{#ServiceName}' + #13#10#13#10 +
      'Данные не изменены.',
      mbError, MB_OK);
  end
  else
  begin
    MsgBox('Серверная служба Picket302 установлена и запущена.' + #13#10#13#10 +
      'Дальше: installer\first-run.ps1 — проверка здоровья и выдача кода сопряжения.' + #13#10#13#10 +
      'Каталог данных: ' + ExpandConstant('{commonappdata}\Picket302\Service'),
      mbInformation, MB_OK);
  end;
end;

{ Удаление: остановка, снятие регистрации, удаление правила брандмауэра.
  Данные в каталоге службы сохраняются. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Text: String;
begin
  if CurUninstallStep <> usUninstall then
  begin
    Exit;
  end;

  StopService;

  if not UnregisterService(Text) then
  begin
    MsgBox('Не удалось снять регистрацию службы: ' + Text + #13#10#13#10 +
      'Проверьте состояние службы командой: sc.exe query ' + '{#ServiceName}',
      mbError, MB_OK);
  end;

  if not RemoveFirewallRule(Text) then
  begin
    MsgBox('Не удалось удалить правило брандмауэра: ' + Text + #13#10#13#10 +
      'Правило можно удалить вручную: netsh advfirewall firewall delete rule name="' + '{#FirewallRuleName}'
      + '"',
      mbError, MB_OK);
  end;
end;

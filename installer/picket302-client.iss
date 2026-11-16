; Установочный пакет клиентской части Picket302 (Inno Setup 6).
;
; Это обычное пользовательское приложение, а не серверная служба. Пакет:
;   * устанавливает опубликованную сборку оболочки Picket302.App в
;     {autopf}\Picket302 (обычный Program Files системного диска);
;   * создаёт ярлык в меню «Пуск» (и, по желанию, на рабочем столе);
;   * НЕ регистрирует службу, НЕ открывает порт, НЕ создаёт правило
;     брандмауэра, НЕ меняет сертификаты и НЕ регистрирует COM-сервер
;     автоматически: регистрация COM выполняется по желанию оператора из
;     меню «Сервис → Настройки интеграции (COM)…» и только в ветке HKCU;
;   * НЕ трогает пользовательские данные: база, секрет сопряжения и журнал
;     незавершённых операций остаются в %AppData%\Picket302 при установке,
;     при обновлении и при удалении.
;
; Отдельная идентичность приложения: серверная служба имеет AppId
; {8A1C2F4E-6B7D-4E3A-9C21-3B5D7E9F1A23} и устанавливается в
; {autopf}\Picket302 Service, клиент — свой AppId и свой каталог. Обе части
; могут быть установлены на одной машине и не путаются между собой.
;
; Идентификаторы в секции [Code] — латиницей: Pascal Script установщика не
; принимает не-ASCII имена. Тексты сообщений остаются русскими.

#define AppName "Picket302"
#define AppVersion "1.0.0"
#define AppExe "Picket302.App.exe"
#define AppPublisher "Picket302"

; Каталог опубликованной сборки оболочки. Задаётся параметром компилятора
; вида:
;   ISCC /DPublishDir="C:\путь\к\публикации" installer\picket302-client.iss
; Параметр обязателен намеренно: молча собрать пакет из устаревшего каталога
; публикации хуже, чем не собрать его вовсе. Сборку выполняет
; installer\build-client.ps1 — он проверяет состав каталога, код возврата
; компилятора, размер и SHA-256 итогового пакета.
#ifndef PublishDir
  #error PublishDir is not defined. Build the package with installer\build-client.ps1 or pass /DPublishDir="<publish dir>".
#endif

[Setup]
; Идентификатор приложения — полноценный GUID: по нему Windows различает
; установленные копии и связывает удаление с установкой. Этот GUID не
; совпадает с AppId серверной службы.
AppId={{56C1FDF3-7563-4737-9E33-219A7B4E00C4}
AppName={#AppName}
AppVersion={#AppVersion}
VersionInfoVersion={#AppVersion}
VersionInfoDescription={#AppName} — приложение оператора (клиент общей базы)
VersionInfoProductName={#AppName}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\Picket302
DefaultGroupName=Picket302
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=picket302-client-setup
Compression=lzma
SolidCompression=yes
; Сборка опубликована для win-x64: 64-разрядная Windows 10/11. На ARM64
; Windows приложение запускается в режиме совместимости x64.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
; Program Files доступен только администратору: установщик запрашивает UAC.
; Сама программа после установки запускается от обычного пользователя
; (см. флаг runascurrentuser в секции [Run]).
PrivilegesRequired=admin
Uninstallable=yes
CreateAppDir=yes
; При обновлении уже запущенная программа закрывается: иначе заблокированный
; исполняемый файл делает копирование невозможным.
CloseApplications=yes
RestartApplications=no
WizardStyle=modern
SetupLogging=yes
; Напоминание после установки: что установлено, что не установлено и как
; подключиться к общей базе.
InfoAfterFile=client-after-install.txt

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
; Ярлык на рабочем столе — по желанию: по умолчанию не создаётся.
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
; Запуск после установки — по желанию и только при первой установке на
; машине: при обновлении программа запускается вручную.
Name: "postinstall"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: checkedonce

[Files]
; Всё содержимое опубликованной сборки оболочки. Отладка (PDB) и исходные
; файлы интерфейса (IDL) в поставку не входят.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs; Excludes: "*.pdb,*.idl"

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
; Запуск от имени обычного пользователя (runascurrentuser), даже когда
; установщик работал с правами администратора. В тихом режиме (silent)
; программа не запускается: skipifsilent.
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent runascurrentuser; Check: ShouldLaunchProgram

[UninstallDelete]
; Ничего в %AppData%\Picket302 не удаляется: локальная база, параметры
; сопряжения, секрет устройства и журнал незавершённых операций остаются
; у пользователя. Программа удаляется, ярлыки — тоже.

[Code]

{ Профиль пользователя: %AppData%\Picket302. }
function UserProfileDirectory: String;
begin
  Result := ExpandConstant('{userappdata}\Picket302');
end;

{ Автоматический запуск после установки пропускается, если профиль уже
  существует: оператор сначала проверяет копию данных и только потом
  открывает программу. Профиль удалять не нужно — установщик его не
  трогает. }
function ShouldLaunchProgram: Boolean;
begin
  Result := True;

  if DirExists(UserProfileDirectory) then
  begin
    Result := False;

    if not WizardSilent then
      MsgBox('Программа установлена.' + #13#10#13#10 +
        'Профиль пользователя уже существует: ' + UserProfileDirectory + #13#10#13#10 +
        'Автоматический запуск отменён. Сначала проверьте копию данных профиля, ' +
        'затем откройте Picket302 в меню «Пуск». Удалять профиль не нужно.',
        mbInformation, MB_OK);
  end;
end;

{ Удаление: программа и ярлыки удаляются, пользовательские данные —
  сохраняются. Сообщение показывается после завершения удаления и только
  при удалении вручную: при обновлении старый установщик выполняется молча. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep <> usPostUninstall then
  begin
    Exit;
  end;

  if UninstallSilent then
  begin
    Exit;
  end;

  if DirExists(UserProfileDirectory) then
  begin
    MsgBox('Picket302 удалена.' + #13#10#13#10 +
      'Пользовательские данные сохранены в ' + UserProfileDirectory + ':' + #13#10 +
      'база, параметры сопряжения с общей базой и журнал незавершённых операций.' + #13#10#13#10 +
      'Если программа больше не понадобится, этот каталог можно удалить вручную.',
      mbInformation, MB_OK);
  end;
end;

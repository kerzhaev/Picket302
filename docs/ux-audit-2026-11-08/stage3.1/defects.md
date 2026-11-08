# Defect register (stage3.1, 2026-11-08)

Формат §19. `ENV31-*` — окружение; `LAY3-1` — раскладка; `DRV31-*` —
исправленные дефекты инфраструктуры приёмки этой серии.

---

## LAY3-01

- **ID:** LAY3-01
- **Severity:** P2
- **File/Method:** `src/Picket302.App/MainShell.cs` — панель конверта:
  `SplitContainer<SplitterPanel<TableLayoutPanel<Panel<GroupBox<
  TableLayoutPanel<FlowLayoutPanel` (точное место — по цепочке
  LAYOUTCHECK; кандидат — ряд логотипа/клипарта в редакторе конверта)
- **Reproduction:** LAYOUTCHECK (harness): ребёнок FlowLayoutPanel
  137×74 при Y=130 в клиенте родителя 138×127 — низ ребёнка на 77 px ниже
  клиентской области родителя.
- **Expected:** ребёнок помещается в отведённую строку TLP.
- **Actual:** переполнение строки на 77 px.
- **Root cause:** не определён в этой серии (нужен разбор RowStyles/
  AutoSize-логики конкретного ряда).
- **Evidence:** LAYOUTCHECK на ТРЁХ масштабах: dpi=96 (137×74 в 138×127),
  dpi=120 (206×74 в 218×127), dpi=144 (чисто по другим формам; главный —
  вне=1 сохраняется) — дефект DPI-НЕЗАВИСИМЫЙ (есть и на 100 %).
- **Regression test:** условия драйвера `LAYOUTCHECK` (примеры с полной
  цепочкой родителей печатаются в каждом прогоне dpi-драйвера).
- **Runtime evidence:** `dpi-happy-evidence.md` / `dpi-failure-evidence.md`,
  строки LAYOUTCHECK 2026-11-08 20:13–20:16 UTC.
- **Status:** OPEN (визуальное влияние: возможное перекрытие нижнего блока
  на вкладке конверта; на скриншотах main-*.png явного перекрытия не видно —
  требуется прицельная проверка)
- **Next:** owner/next wave — идентифицировать конкретный ряд и исправить.

## ENV31-01 (не код; наследие ENV3-01)

- **ID:** ENV31-01
- **Severity:** P1 (окружение)
- **Screen/flow:** инкрементальные сборки MSBuild на SMB Z:\
- **Evidence:** методика этой серии: все сборки — ЯВНЫЕ (`dotnet build
  <проект>`), каждый бинарник проверен маркером (`rg -a -c`): App Debug/
  Release `реестрОтправителяИзолирован`=1, harness
  `ПустойМенеджерПринтеров`=1, тесты `ContainmentIsolationTests`=2 (обе
  конфигурации); хэш App.dll в bin источника и в bin harness совпал
  (`c29be1ce6185af25…`).
- **Status:** MITIGATED процессом (см. stage3 ENV3-01); владельцу —
  локальный диск или CI.

## ENV31-02 (внешнее; продолжение ENV3-02)

- **ID:** ENV31-02
- **Severity:** P2 (внешнее)
- **Screen/flow:** параллельная активность владельца в %TEMP% (сотни новых
  BackupTests/Restore/Schedule-каталогов с mtime в ходе серии).
- **Evidence:** baseline.md; финальный census (newest 17:50:43 UTC).
- **Status:** EXTERNAL — источник не серия; production-профиль не менялся
  с 15:51/16:17 UTC (см. env3-02.md §2).

## DRV31-01 (исправлено)

- **ID:** DRV31-01
- **Severity:** P1 (инфраструктура приёмки)
- **File/Method:** `AppServices.СоздатьAsync` / `CompositeCurrentSenderStore`
- **Reproduction:** любой acceptance-сеев отправителя писал SenderId в
  РЕАЛЬНЫЙ `HKCU\Software\PEPro\CurrentSender` (вне профиля).
- **Fix:** `NullCurrentSenderStore` + параметр `реестрОтправителяИзолирован`;
  harness передаёт true во всех режимах.
- **Regression test:** `ContainmentIsolationTests.ИзолированныйРеестрОтправителя_НеПишетВРеальныйРеестр`
  + батарейка: SenderId ABSENT до/после всех прогонов.
- **Status:** FIXED

## DRV31-02 (исправлено)

- **ID:** DRV31-02
- **Severity:** P1 (инфраструктура приёмки)
- **File/Method:** `tools/ui-acceptance/UiAcceptance.csproj`
- **Reproduction:** harness SystemAware → WM_DPICHANGED применял только
  suggested-rect без рескала (DeviceDpi 96), DPI-симуляция не работала.
- **Fix:** `<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>`.
- **Regression test:** run-dpi.ps1: DeviceDpi 120/144/96 отслеживается.
- **Status:** FIXED

## DRV31-03 (исправлено)

- **ID:** DRV31-03
- **Severity:** P2 (драйверы)
- **File/Method:** `run-noprinter.ps1` / `run-screens2.ps1` / `run-dpi.ps1`
- **Reproduction:** три независимых дефекта первых прогонов: (а) evidence-
  каталог не создавался до первого скриншота → GDI+ «ошибка общего вида»;
  (б) scriptblock-делегат не видит локальную переменную функции (PS 5.1) →
  текст MessageBox читался пустым; (в) LAYOUTCHECK-строки накапливались из
  прошлых вызовов (ложные «чужие» формы/DPI).
- **Fix:** раннее `New-Item` evidence-корня + функция `Захват` с settle;
  `$script:`-переменные для делегатов; окно чтения «только новые строки».
- **Regression test:** сами драйверы (fail-closed условия).
- **Status:** FIXED

## Открытые кодовые дефекты: LAY3-01 (P2)

Остальные подтверждённые дефекты серии исправлены. Внешние пункты —
ENV31-01/02 (владельцу).

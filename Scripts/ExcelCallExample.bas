Attribute VB_Name = "ExcelCallExample"
Option Explicit

' Пример вызова COM-сервера автоматизации Picket302 из Excel
' (ТЗ §8.9, функция 115; ТЗ §9.1 — контракт IPEProApplication).
'
' Порядок использования:
'   1. Зарегистрировать сервер: Picket302.ComAutomation.exe --register
'   2. В Excel: ALT+F11 -> File -> Import File... -> ExcelCallExample.bas
'   3. Запустить макрос PrintEnvelopeFromRegistry
'
' Сервер запускается в «слепом» режиме: модальных окон нет, флаг
' ShowFormsAsModal по умолчанию равен False (ТЗ §9.2).

Private Const REG_KEY As String = "HKCU\Software\Picket302\PrintEnvelope"

' Печать конверта по настройкам, прочитанным из реестра.
Public Sub PrintEnvelopeFromRegistry()
    Dim App As Object
    Dim fromName As String
    Dim fromAddr As String
    Dim fromIndex As String
    Dim toName As String
    Dim toAddr As String
    Dim toIndex As String
    Dim warning As Long

    fromName = ReadSetting("FromName")
    fromAddr = ReadSetting("FromAddr")
    fromIndex = ReadSetting("FromIndex")
    toName = ReadSetting("ToName")
    toAddr = ReadSetting("ToAddr")
    toIndex = ReadSetting("ToIndex")

    Set App = CreateObject("PEPro.Application")

    ' Перехват фокуса перед печатью (ТЗ §9.2, метод 10).
    App.OpenDummyFormToSetAsForeground

    App.PrintEnvelope fromName, fromAddr, fromIndex, toName, toAddr, toIndex

    ' Возврат фокуса вызывающему процессу — Excel (ТЗ §9.2, метод 11).
    App.CloseDummyFormAndSetAsForeground "EXCEL.EXE"

    warning = App.GetWarningType

    MsgBox "Код результата (GetWarningType): " & DescribeResult(warning), _
           vbInformation, "Picket302 — автоматизация"

    Set App = Nothing
End Sub

' Печать одного конверта с явными параметрами.
Public Sub PrintEnvelopeSample()
    Dim App As Object
    Dim warning As Long

    Set App = CreateObject("PEPro.Application")

    App.PrintEnvelope _
        "Иванов Иван Иванович", _
        "125009, г. Москва, ул. Тверская, д. 1", _
        "125009", _
        "ООО «Ромашка»", _
        "101000, г. Москва, ул. Пушкина, д. 5", _
        "101000"

    warning = App.GetWarningType

    MsgBox "Код результата (GetWarningType): " & DescribeResult(warning), _
           vbInformation, "Picket302 — автоматизация"

    Set App = Nothing
End Sub

' Печать почтового реестра из XML без показа формы с сохранением результата
' по указанному пути (ТЗ §9.1, метод 21 PrintFromXmlRegistersWithoutForm;
' ТЗ §8.9, функция 115: «макрос Excel формирует файл реестра по указанному пути»).
'
' Порядок вызова: после загрузки базы (SafeLoadDatabase) макрос формирует
' XML в формате RegistersData и передаёт его серверу вместе с именем
' шаблона реестра и путём итогового файла.
Public Sub PrintRegisterToFile()
    Dim App As Object
    Dim xml As String
    Dim templateName As String
    Dim resultPath As String
    Dim warning As Long

    ' Имя пользовательского шаблона реестра и путь итогового файла задаются
    ' здесь либо вводятся оператором. Пустой шаблон означает шаблон по
    ' умолчанию, пустой путь — файл рядом с макросом.
    templateName = "Почтовый реестр"
    resultPath = "C:\Temp\register.xlsx"

    If Len(resultPath) = 0 Then
        MsgBox "Задайте путь итогового файла реестра.", vbExclamation, "Picket302 — автоматизация"
        Exit Sub
    End If

    xml = BuildRegisterXml()

    Set App = CreateObject("PEPro.Application")

    ' Загрузка базы обязательна для пользовательских шаблонов реестра.
    App.SafeLoadDatabase

    ' Печать реестра без показа формы: результат сохраняется по resultPath.
    App.PrintFromXmlRegistersWithoutForm xml, templateName, resultPath

    warning = App.GetWarningType

    If warning = 0 Then
        MsgBox "Файл реестра сформирован: " & resultPath, vbInformation, "Picket302 — автоматизация"
    Else
        MsgBox "Файл реестра не сформирован." & vbCrLf & _
               "Код результата (GetWarningType): " & DescribeResult(warning), _
               vbExclamation, "Picket302 — автоматизация"
    End If

    Set App = Nothing
End Sub

' Формирование данных реестра в формате RegistersData
' (EVIDENCE/automation_data_formats.txt).
Private Function BuildRegisterXml() As String
    BuildRegisterXml = _
        "<RegistersData>" & _
        "<RegisterData>" & _
        "<Number>1</Number>" & _
        "<From>" & _
        "<Name>Отправитель</Name>" & _
        "<Address>г. Москва, ул. Тверская, д. 1</Address>" & _
        "<ZipCode>125009</ZipCode>" & _
        "</From>" & _
        "<Packages>" & _
        "<Package>" & _
        "<Number>1</Number>" & _
        "<To>" & _
        "<Name>Получатель</Name>" & _
        "<Address>г. Москва, ул. Пушкина, д. 5</Address>" & _
        "<ZipCode>101000</ZipCode>" & _
        "</To>" & _
        "<Note>Особые отметки отправителя</Note>" & _
        "</Package>" & _
        "</Packages>" & _
        "</RegisterData>" & _
        "</RegistersData>"
End Function

' Отказоустойчивое чтение параметра реестра: отсутствующий параметр даёт
' пустую строку, а не исключение (ТЗ §9.2).
Private Function ReadSetting(Name As String) As String
    Dim Shell As Object
    Dim Value As String

    Value = ""

    On Error Resume Next

    Set Shell = CreateObject("WScript.Shell")

    Value = CStr(Shell.RegRead(REG_KEY & "\" & Name))

    If Err.Number <> 0 Then
        Value = ""
        Err.Clear
    End If

    Set Shell = Nothing

    On Error GoTo 0

    ReadSetting = Value
End Function

' Формирование текста результата операции: значение, возвращённое
' интерфейсом, и его расшифровка (ТЗ §9.2, таблица 9.2).
Private Function DescribeResult(Code As Variant) As String
    If IsEmpty(Code) Or Code = "" Then
        DescribeResult = "значение не возвращено интерфейсом"
        Exit Function
    End If

    DescribeResult = CStr(Code) & vbCrLf & DescribeWarning(CLng(Code))
End Function

' Описание кода результата GetWarningType (ТЗ §9.2, таблица 9.2).
Private Function DescribeWarning(Code As Long) As String
    Select Case Code
        Case 0
            DescribeWarning = "успех"
        Case 1
            DescribeWarning = "база данных не загружена"
        Case 2
            DescribeWarning = "ошибка чтения XML"
        Case 3
            DescribeWarning = "ошибка подготовки документа"
        Case 4
            DescribeWarning = "файл шаблона не найден"
        Case Else
            DescribeWarning = "неизвестный код"
    End Select
End Function

Option Explicit

Dim WshShell, fso, chromePath, scriptDir, debugPort, profileDir
Set WshShell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

' --- PHAN 1: TIM DUONG DAN CHROME ---
scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
chromePath = ""

' 1. Thu doc tu file chrome_path.txt
If fso.FileExists(scriptDir & "\chrome_path.txt") Then
    Dim objFile, strLine
    Set objFile = fso.OpenTextFile(scriptDir & "\chrome_path.txt", 1)
    If Not objFile.AtEndOfStream Then
        strLine = Trim(objFile.ReadLine())
        If fso.FileExists(strLine) Then chromePath = strLine
    End If
    objFile.Close
End If

' 2. Tim mac dinh
If chromePath = "" Then
    If fso.FileExists("C:\Program Files\Google\Chrome\Application\chrome.exe") Then
        chromePath = "C:\Program Files\Google\Chrome\Application\chrome.exe"
    ElseIf fso.FileExists("C:\Program Files (x86)\Google\Chrome\Application\chrome.exe") Then
        chromePath = "C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"
    Else
        ' Thu tim trong LocalAppData
        Dim localAppData
        localAppData = WshShell.ExpandEnvironmentStrings("%LOCALAPPDATA%")
        If fso.FileExists(localAppData & "\Google\Chrome\Application\chrome.exe") Then
            chromePath = localAppData & "\Google\Chrome\Application\chrome.exe"
        End If
    End If
End If

If chromePath = "" Then
    MsgBox "Khong tim thay Google Chrome! Vui long tao file chrome_path.txt va dan duong dan Chrome vao do.", 16, "Loi Khoi Dong"
    WScript.Quit
End If

' --- PHAN 2: DOC DEBUG PORT TU config.json ---
debugPort = 9222
profileDir = "C:\selenium\ChromeProfile"
If fso.FileExists(scriptDir & "\config.json") Then
    Dim cfgFile, cfgText, cfgRegex, cfgMatches
    Set cfgFile = fso.OpenTextFile(scriptDir & "\config.json", 1)
    cfgText = cfgFile.ReadAll
    cfgFile.Close
    Set cfgRegex = New RegExp
    cfgRegex.Pattern = """debug_port""\s*:\s*(\d+)"
    cfgRegex.IgnoreCase = True
    If cfgRegex.Test(cfgText) Then
        Set cfgMatches = cfgRegex.Execute(cfgText)
        debugPort = CLng(cfgMatches(0).SubMatches(0))
    End If
    cfgRegex.Pattern = """profile_dir""\s*:\s*""([^""]+)"""
    If cfgRegex.Test(cfgText) Then
        Set cfgMatches = cfgRegex.Execute(cfgText)
        profileDir = Replace(cfgMatches(0).SubMatches(0), "\\", "\")
    End If
End If

' --- PHAN 3: KHOI DONG CHROME AN (BACKGROUND) ---
' Tham so khoi dong Chrome Debug + Anti-Sleep
Dim chromeArgs
chromeArgs = " --remote-debugging-port=" & debugPort & " --user-data-dir=""" & profileDir & """ --disable-background-timer-throttling --disable-backgrounding-occluded-windows --disable-renderer-backgrounding"

' Chay Chrome an (WindowStyle = 0)
WshShell.Run chr(34) & chromePath & chr(34) & chromeArgs, 0
WScript.Sleep 3000 ' Cho 3 giay de Chrome len

' --- PHAN 4: KHOI DONG TOOL (.exe) ---
' Tool co co che chong chay trung nen chay lai nhieu lan cung an toan.
Dim toolExe, toolScript
toolExe = scriptDir & "\GeminiQuickOverlay.exe"
toolScript = scriptDir & "\GeminiQuickOverlay.py"
If fso.FileExists(toolExe) Then
    WshShell.CurrentDirectory = scriptDir
    WshShell.Run chr(34) & toolExe & chr(34), 0
ElseIf fso.FileExists(toolScript) Then
    WshShell.CurrentDirectory = scriptDir
    WshShell.Run "pythonw " & chr(34) & toolScript & chr(34), 0
Else
    MsgBox "Chrome da bat nhung khong tim thay GeminiQuickOverlay.exe canh START_TOOL.vbs.", 48, "Thieu file"
End If

Set WshShell = Nothing
Set fso = Nothing


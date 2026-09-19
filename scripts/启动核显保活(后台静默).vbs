Set fso = CreateObject("Scripting.FileSystemObject")
Set ws = CreateObject("WScript.Shell")

scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
exePath = scriptDir & "\GpuKeepAlive.exe"

If Not fso.FileExists(exePath) Then
    exePath = fso.GetParentFolderName(scriptDir) & "\GpuKeepAlive.exe"
End If

If fso.FileExists(exePath) Then
    ws.Run chr(34) & exePath & chr(34) & " -a intel -f 15 -i 1 --hide", 0, False
End If

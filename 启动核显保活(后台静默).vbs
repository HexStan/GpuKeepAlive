Set fso = CreateObject("Scripting.FileSystemObject")
Set ws = CreateObject("WScript.Shell")

scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
exePath = scriptDir & "\GpuKeepAlive.exe"

If fso.FileExists(exePath) Then
    ws.Run chr(34) & exePath & chr(34) & " -a intel -f 15 -i 1 --hide", 0, False
End If

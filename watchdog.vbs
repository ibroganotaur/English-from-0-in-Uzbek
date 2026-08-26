' Starts the bot if it is not already running. Safe to run as often as you like:
' if EnglishBot.exe is alive it does nothing at all.
'
' The point is that run-bot.cmd only survives crashes. It cannot help if the loop
' itself is killed - Ctrl+C, Task Manager, a closed window. This can, because it
' checks the actual process and does not depend on anything staying alive.

Set fso = CreateObject("Scripting.FileSystemObject")
Set shell = CreateObject("WScript.Shell")
here = fso.GetParentFolderName(WScript.ScriptFullName)

Set wmi = GetObject("winmgmts:\\.\root\cimv2")
Set running = wmi.ExecQuery("SELECT ProcessId FROM Win32_Process WHERE Name = 'EnglishBot.exe'")

If running.Count = 0 Then
    shell.Run """" & here & "\run-bot.cmd""", 0, False
End If

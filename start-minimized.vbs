' Launches run-bot.cmd with NO window at all (style 0 = hidden).
'
' Deliberately invisible. A visible console window is a hazard: clicking it and
' pressing Ctrl+C, or closing it, kills the bot AND the restart loop with it -
' Ctrl+C is delivered to the whole console process group. That has taken the bot
' down more than once.
'
' To stop the bot on purpose, run stop-bot.cmd.
' To see what it is doing, open bot-log.txt.

Set fso = CreateObject("Scripting.FileSystemObject")
Set shell = CreateObject("WScript.Shell")
here = fso.GetParentFolderName(WScript.ScriptFullName)
shell.Run """" & here & "\run-bot.cmd""", 0, False

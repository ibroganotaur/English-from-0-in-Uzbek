' Launches run-bot.cmd minimised, so logon does not throw a console window in your face.
' Window style 7 = minimised and not activated. The window stays in the taskbar on
' purpose: you can see the bot is alive, and closing it stops the bot.
Set shell = CreateObject("WScript.Shell")
shell.Run """" & CreateObject("Scripting.FileSystemObject") _
    .GetParentFolderName(WScript.ScriptFullName) & "\run-bot.cmd""", 7, False

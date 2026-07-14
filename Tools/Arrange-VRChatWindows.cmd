@echo off
setlocal

rem Arrange visible VRChat windows on the first window's display.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Arrange-VRChatWindows.ps1" %*
set "EXIT_CODE=%ERRORLEVEL%"

if not "%EXIT_CODE%"=="0" pause
exit /b %EXIT_CODE%

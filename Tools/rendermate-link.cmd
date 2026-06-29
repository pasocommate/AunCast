@echo off
rem ============================================================
rem RenderMate: swap to sibling repo (link)
rem   Calls rendermate-dev-link.ps1 "link".
rem   ASCII-only wrapper to avoid CP932 corruption in cmd.exe.
rem   Close Unity before running.
rem ============================================================
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0rendermate-dev-link.ps1" link
set "RC=%ERRORLEVEL%"
rem Pause only when launched by double-click (do not block scripts).
echo %cmdcmdline% | find /i "%~nx0" >nul && pause
exit /b %RC%

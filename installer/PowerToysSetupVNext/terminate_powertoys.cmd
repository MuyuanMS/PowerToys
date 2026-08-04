@echo off
setlocal ENABLEDELAYEDEXPANSION

@REM Check if PowerToys.exe is running before trying to kill it.
@REM This avoids hanging if taskkill behaves unexpectedly when the process doesn't exist.
tasklist /FI "IMAGENAME eq PowerToys.exe" 2>NUL | find /I "PowerToys.exe" >NUL
if errorlevel 1 exit /b 0

@REM We loop here until PowerToys.exe is no longer running. We can't use the /F flag inside the loop,
@REM because a forced kill does not let PowerToys.exe clean up first. Instead we send WM_CLOSE
@REM (taskkill without /F), which is caught by the message loops in PowerToys.exe, closing its windows
@REM one by one. We re-check with tasklist each iteration rather than trusting taskkill's exit code,
@REM so a transient failure (e.g. "Access is denied") is not mistaken for "process not found".
for /l %%x in (1, 1, 100) do (
    tasklist /FI "IMAGENAME eq PowerToys.exe" 2>NUL | find /I "PowerToys.exe" >NUL
    if errorlevel 1 exit /b 0
    taskkill /IM PowerToys.exe 1>NUL 2>NUL
    @REM ping -n 2 waits about one second, giving the app time to shut down.
    ping -n 2 127.0.0.1 >NUL 2>NUL
)

@REM Force kill if graceful close failed after all attempts, then report the actual outcome.
taskkill /F /IM PowerToys.exe 1>NUL 2>NUL
tasklist /FI "IMAGENAME eq PowerToys.exe" 2>NUL | find /I "PowerToys.exe" >NUL
if errorlevel 1 exit /b 0
exit /b 1
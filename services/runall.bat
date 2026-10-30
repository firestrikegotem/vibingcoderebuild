@echo off
rem Starts every service in its own window. Works from any location and any folder name.
cd /d "%~dp0"

start "Roblox 2016 website" cmd /k cd /d "%~dp0Roblox\Roblox.Website" ^&^& call run.bat
start "game server" cmd /k cd /d "%~dp0game-server" ^&^& call run.bat
start "asset validation" cmd /k cd /d "%~dp0AssetValidationServiceV2" ^&^& call run.bat
start "frontend" cmd /k cd /d "%~dp02016-roblox-main" ^&^& call run.bat

if exist "%~dp0RCCService\RCCService.exe" (
	echo RCC found. The game server starts it automatically when someone joins a game.
) else (
	echo.
	echo WARNING: services\RCCService\RCCService.exe was not found.
	echo Players cannot join games until the RCC folder is in place.
	echo.
)

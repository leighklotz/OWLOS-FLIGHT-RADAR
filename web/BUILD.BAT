@echo off
rem the radar in a web page: core\ built for WebAssembly with Emscripten, into twin.js and twin.wasm here
rem   BUILD.BAT      Emscripten from C:\emsdk, or set EMSDK=path
rem what goes in is in emcc.args, the site's workflow builds from the same file
rem then serve this folder with any web server: a page opened from a file can't load the wasm
setlocal
if "%EMSDK%"=="" set EMSDK=C:\emsdk
if not exist "%EMSDK%\upstream\emscripten\emcc.exe" (
  echo Emscripten not found in %EMSDK%
  exit /b 1
)
rem its own python and settings, not whatever else is on this PC
set "PATH=%EMSDK%\upstream\emscripten;%PATH%"
set "EM_CONFIG=%EMSDK%\.emscripten"
for /d %%P in ("%EMSDK%\python\*") do set "EMSDK_PYTHON=%%P\python.exe"
cd /d "%~dp0"
rem the version, as the firmware takes it: the first "## " line of CHANGES.md
set VER=
for /f "tokens=2" %%v in ('findstr /b /c:"## " ..\CHANGES.md') do if not defined VER set VER=%%v
rem one radar for each size of screen: 800x480, 320x240, 480x320, the round one, 480x272
rem each with its own letters: the 5" has its own set, in the glass's pixels
set FONTS=../core/font_small.c ../core/font_medium.c ../core/font_large.c ../core/font_huge.c
set GLASS=../core/font_glass_small.c ../core/font_glass_medium.c ../core/font_glass_large.c ../core/font_glass_huge.c ../core/font_glass_tiny.c
call :one 10 Twin twin "%GLASS%" || exit /b 1
call :one 1 Twin1 twin1 "%FONTS%" || exit /b 1
call :one 2 Twin2 twin2 "%FONTS%" || exit /b 1
call :one 4 Twin4 twin4 "%FONTS%" || exit /b 1
call :one 13 Twin5 twin5 "%FONTS%" || exit /b 1
echo built the radars, version %VER%
exit /b 0

:one
call emcc @emcc.args %~4 -DFLIGHT_VERSION=\"%VER%\" -DRADAR_DISPLAY=%1 -sEXPORT_NAME=%2 -o %3.js
exit /b %errorlevel%

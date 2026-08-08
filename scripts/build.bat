@echo off
setlocal
chcp 65001 >nul
rem Plain javac build. Requires JDK 17 or newer.
cd /d %~dp0\..
if exist build\classes rmdir /s /q build\classes
if exist build\test-classes rmdir /s /q build\test-classes
mkdir build\classes
mkdir build\test-classes
dir /s /b src\main\java\*.java > build\sources.txt
javac --release 17 -encoding UTF-8 -d build\classes @build\sources.txt
if errorlevel 1 exit /b 1
dir /s /b src\test\java\*.java > build\test-sources.txt
javac --release 17 -encoding UTF-8 -d build\test-classes @build\sources.txt @build\test-sources.txt
if errorlevel 1 exit /b 1
echo Build complete. Run: scripts\run.bat

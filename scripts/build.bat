@echo off
chcp 65001 >nul
rem Plain javac build. Requires JDK 17 or newer.
cd /d %~dp0\..
if not exist build\classes mkdir build\classes
if not exist build\test-classes mkdir build\test-classes
dir /s /b src\main\java\*.java > build\sources.txt
javac --release 17 -encoding UTF-8 -d build\classes @build\sources.txt
dir /s /b src\test\java\*.java > build\test-sources.txt
javac --release 17 -encoding UTF-8 -cp build\classes -d build\test-classes @build\test-sources.txt
echo Build complete. Run: scripts\run.bat

@echo off
setlocal
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvarsall.bat" x64 >nul
if errorlevel 1 exit /b 1
cd /d "%~dp0"
cl /nologo /EHsc /O2 /MT /I "..\x86\source\libde265-1.1.3" /I "build" "..\x86\de265-native-test.cpp" /link "build\libde265\Release\de265.lib" /out:de265-native-test.exe
exit /b %errorlevel%

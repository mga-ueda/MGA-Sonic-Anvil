@echo off
setlocal
set VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe
for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set VSDIR=%%i
if not defined VSDIR (
  echo Visual C++ toolset not found.
  exit /b 1
)
call "%VSDIR%\VC\Auxiliary\Build\vcvars64.bat"
set ROOT=%~dp0
set OUT=%ROOT%win-x64
if not exist "%OUT%" mkdir "%OUT%"
set SRC=%ROOT%signalsmith_stretch_c.cpp
set INC_STRETCH=%ROOT%..\signalsmith-stretch
set INC_LINEAR=%ROOT%..\signalsmith-linear\include
cl /nologo /O2 /EHsc /std:c++17 /W3 /wd4244 /wd4305 /DSIGNALSMITH_STRETCH_EXPORTS /LD /I "%ROOT%" /I "%INC_STRETCH%" /I "%INC_LINEAR%" "%SRC%" /Fe:"%OUT%\SignalsmithStretch.dll" /Fo:"%OUT%\signalsmith_stretch_c.obj"
if errorlevel 1 exit /b 1
del /q "%OUT%\signalsmith_stretch_c.obj" "%OUT%\SignalsmithStretch.exp" "%OUT%\SignalsmithStretch.lib" 2>nul
echo Built %OUT%\SignalsmithStretch.dll

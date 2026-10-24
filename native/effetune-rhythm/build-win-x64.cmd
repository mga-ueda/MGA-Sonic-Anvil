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
set SRC=%ROOT%effetune_rhythm_c.cpp
set INC=%ROOT%include
set GEN=%ROOT%generated
set OBJ_LOW=%GEN%\rhythm_d_low.obj
set OBJ_MID=%GEN%\rhythm_d_mid.obj
set OBJ_HIGH=%GEN%\rhythm_d_high.obj
if not exist "%OBJ_LOW%" (
  echo Missing %OBJ_LOW% — regenerate with embed_models.py
  exit /b 1
)
cl /nologo /O2 /EHsc /std:c++20 /W3 /wd4244 /wd4305 /DEFFETUNE_RHYTHM_EXPORTS /LD /I "%ROOT%." /I "%INC%" /I "%GEN%" "%SRC%" "%OBJ_LOW%" "%OBJ_MID%" "%OBJ_HIGH%" /Fe:"%OUT%\EffeTuneRhythm.dll" /Fo:"%OUT%\effetune_rhythm_c.obj"
if errorlevel 1 exit /b 1
del /q "%OUT%\effetune_rhythm_c.obj" "%OUT%\EffeTuneRhythm.exp" "%OUT%\EffeTuneRhythm.lib" 2>nul
echo Built %OUT%\EffeTuneRhythm.dll

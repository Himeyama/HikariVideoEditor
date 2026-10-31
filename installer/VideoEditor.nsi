Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
!include "WinVer.nsh"
!ifndef APP_VERSION
  !error "APP_VERSION is required"
!endif
!ifndef SOURCE_DIR
  !error "SOURCE_DIR is required"
!endif
!ifndef OUTPUT_FILE
  !error "OUTPUT_FILE is required"
!endif

Name "VideoEditor"
OutFile "${OUTPUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\VideoEditor"
InstallDirRegKey HKCU "Software\VideoEditor" "InstallDir"
RequestExecutionLevel user
SetCompressor /SOLID lzma
VIProductVersion "${APP_VERSION}.0"
VIAddVersionKey /LANG=1033 "ProductName" "VideoEditor"
VIAddVersionKey /LANG=1033 "FileDescription" "VideoEditor Setup"
VIAddVersionKey /LANG=1033 "FileVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=1033 "LegalCopyright" "VideoEditor contributors"

!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\app\VideoEditor.exe"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "Japanese"

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "VideoEditor requires 64-bit Windows."
    Abort
  ${EndIf}
  ${If} ${AtLeastBuild} 19041
  ${Else}
    MessageBox MB_ICONSTOP "VideoEditor requires Windows 10 version 2004 or later."
    Abort
  ${EndIf}
FunctionEnd

Section "VideoEditor"
  SetShellVarContext current
  SetOutPath "$INSTDIR\app"
  SetOverwrite on
  File /r "${SOURCE_DIR}\*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateDirectory "$SMPROGRAMS\VideoEditor"
  CreateShortcut "$SMPROGRAMS\VideoEditor\VideoEditor.lnk" "$INSTDIR\app\VideoEditor.exe"
  WriteRegStr HKCU "Software\VideoEditor" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\VideoEditor" "DisplayName" "VideoEditor"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\VideoEditor" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\VideoEditor" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\VideoEditor" "QuietUninstallString" '$\"$INSTDIR\Uninstall.exe$\" /S'
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\VideoEditor" "DisplayIcon" "$INSTDIR\app\VideoEditor.exe"
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\VideoEditor" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\VideoEditor" "NoRepair" 1
SectionEnd

Section "Uninstall"
  SetShellVarContext current
  RMDir /r "$INSTDIR\app"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  Delete "$SMPROGRAMS\VideoEditor\VideoEditor.lnk"
  RMDir "$SMPROGRAMS\VideoEditor"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\VideoEditor"
  DeleteRegKey HKCU "Software\VideoEditor"
SectionEnd

Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"

Name "Jemur ${VERSION}"
OutFile "${OUTFILE}"
InstallDir "$LOCALAPPDATA\Programs\Jemur"
InstallDirRegKey HKCU "Software\Jemur" "InstallLocation"
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetCompressorDictSize 32
BrandingText "Jemur"
VIProductVersion "${VERSION}.0"
VIAddVersionKey /LANG=1033 "ProductName" "Jemur"
VIAddVersionKey /LANG=1033 "CompanyName" "M. Revan Apriyandi"
VIAddVersionKey /LANG=1033 "FileDescription" "Jemur installer (${RID})"
VIAddVersionKey /LANG=1033 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=1033 "LegalCopyright" "M. Revan Apriyandi"

!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\Jemur"
!define MUI_ICON "../src/Assets/jemur.ico"
!define MUI_UNICON "../src/Assets/jemur.ico"
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\Jemur.exe"
!define MUI_FINISHPAGE_RUN_NOTCHECKED
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "../LICENSE"
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "Indonesian"

!macro CheckAppClosed
  System::Call 'kernel32::OpenMutexW(i 0x100000, i 0, w "Jemur.SingleInstance.v1") p .r0'
  ${If} $0 != 0
    System::Call 'kernel32::CloseHandle(p r0)'
    MessageBox MB_OK|MB_ICONEXCLAMATION "Close Jemur from its tray menu, then run this installer again." /SD IDOK
    SetErrorLevel 1618
    Abort
  ${EndIf}
!macroend

Function .onInit
  SetShellVarContext current
  SetRegView 64
  !if "${RID}" == "win-arm64"
    ${IfNot} ${IsNativeARM64}
      MessageBox MB_OK|MB_ICONSTOP "This package requires Windows on ARM64. Download the x64 package for Intel or AMD PCs." /SD IDOK
      SetErrorLevel 1633
      Abort
    ${EndIf}
  !else
    ${IfNot} ${RunningX64}
      MessageBox MB_OK|MB_ICONSTOP "This package requires 64-bit Windows." /SD IDOK
      SetErrorLevel 1633
      Abort
    ${EndIf}
  !endif
  !insertmacro CheckAppClosed
FunctionEnd

Function un.onInit
  SetShellVarContext current
  SetRegView 64
  !insertmacro CheckAppClosed
FunctionEnd

Section "Jemur (required)" SEC_APP
  SectionIn RO
  SetOutPath "$INSTDIR"
  File /oname=Jemur.exe "${SOURCE_EXE}"
  File /oname=LICENSE.txt "../LICENSE"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\Jemur" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "Jemur"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "Publisher" "M. Revan Apriyandi"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\Jemur.exe"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "URLInfoAbout" "https://github.com/revanapriyandi/jemur"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegStr HKCU "${UNINSTALL_KEY}" "QuietUninstallString" '$\"$INSTDIR\Uninstall.exe$\" /S'
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "EstimatedSize" ${PAYLOAD_KB}
  CreateDirectory "$SMPROGRAMS\Jemur"
  CreateShortcut "$SMPROGRAMS\Jemur\Jemur.lnk" "$INSTDIR\Jemur.exe"
  CreateShortcut "$SMPROGRAMS\Jemur\Uninstall Jemur.lnk" "$INSTDIR\Uninstall.exe"
SectionEnd

Section /o "Desktop shortcut" SEC_DESKTOP
  CreateShortcut "$DESKTOP\Jemur.lnk" "$INSTDIR\Jemur.exe"
  WriteRegDWORD HKCU "Software\Jemur" "DesktopShortcut" 1
SectionEnd

Section "Uninstall"
  Delete "$INSTDIR\Jemur.exe"
  Delete "$INSTDIR\LICENSE.txt"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  Delete "$SMPROGRAMS\Jemur\Jemur.lnk"
  Delete "$SMPROGRAMS\Jemur\Uninstall Jemur.lnk"
  RMDir "$SMPROGRAMS\Jemur"
  ReadRegDWORD $0 HKCU "Software\Jemur" "DesktopShortcut"
  ${If} $0 == 1
    Delete "$DESKTOP\Jemur.lnk"
  ${EndIf}
  ReadRegStr $0 HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "Jemur"
  ${If} $0 == '$\"$INSTDIR\Jemur.exe$\" --background'
    DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "Jemur"
  ${EndIf}
  DeleteRegKey HKCU "${UNINSTALL_KEY}"
  DeleteRegValue HKCU "Software\Jemur" "InstallLocation"
  DeleteRegValue HKCU "Software\Jemur" "DesktopShortcut"
  DeleteRegKey /ifempty HKCU "Software\Jemur"
SectionEnd

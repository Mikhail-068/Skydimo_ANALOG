Unicode true
Name "Контур"
OutFile "${OUTPUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\Kontur"
RequestExecutionLevel user
SetCompressor /SOLID lzma

!include "MUI2.nsh"
!include "WinVer.nsh"
!include "x64.nsh"

!define ARP_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\KonturSK0127"
InstallDirRegKey HKCU "${ARP_KEY}" "InstallLocation"
!define MUI_ICON "..\Kontur.ico"
!define MUI_UNICON "..\Kontur.ico"
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\Kontur.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Запустить «Контур»"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "Russian"

VIProductVersion "${APP_VERSION}.0"
VIAddVersionKey /LANG=1049 "ProductName" "Контур"
VIAddVersionKey /LANG=1049 "ProductVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=1049 "FileVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=1049 "FileDescription" "Установщик «Контур» для Windows x64"

Function .onInit
    ${If} ${AtLeastWin10}
    ${AndIf} ${RunningX64}
        SetRegView 64
        SetShellVarContext current
    ${Else}
        MessageBox MB_OK|MB_ICONSTOP "Для установки нужна 64-разрядная Windows 10 или 11." /SD IDOK
        SetErrorLevel 2
        Abort
    ${EndIf}
FunctionEnd

Function un.onInit
    SetRegView 64
    SetShellVarContext current
FunctionEnd

Section "Контур"
    SetOutPath "$INSTDIR"
    File /oname=Kontur.new "${APP_FILE}"
    System::Call 'kernel32::MoveFileExW(w "$INSTDIR\Kontur.new", w "$INSTDIR\Kontur.exe", i 9)i.r0'
    ${If} $0 == 0
        Delete "$INSTDIR\Kontur.new"
        MessageBox MB_OK|MB_ICONSTOP "Закройте «Контур» через меню рядом с часами и повторите установку." /SD IDOK
        SetErrorLevel 3
        Abort
    ${EndIf}

    SetOutPath "$INSTDIR\licenses"
    File "${LICENSE_DIR}\dotnet-runtime-license.txt"
    File "${LICENSE_DIR}\dotnet-desktop-license.txt"
    File "${LICENSE_DIR}\dotnet-third-party-notices.txt"
    File /oname=nsis-licenses.txt "${NSIS_NOTICE}"
    SetOutPath "$INSTDIR"
    WriteUninstaller "$INSTDIR\Uninstall.exe"
    CreateShortcut "$SMPROGRAMS\Контур.lnk" "$INSTDIR\Kontur.exe"
    WriteRegStr HKCU "${ARP_KEY}" "DisplayName" "Контур"
    WriteRegStr HKCU "${ARP_KEY}" "DisplayVersion" "${APP_VERSION}"
    WriteRegStr HKCU "${ARP_KEY}" "DisplayIcon" "$INSTDIR\Kontur.exe"
    WriteRegStr HKCU "${ARP_KEY}" "InstallLocation" "$INSTDIR"
    WriteRegStr HKCU "${ARP_KEY}" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
    WriteRegStr HKCU "${ARP_KEY}" "QuietUninstallString" '$\"$INSTDIR\Uninstall.exe$\" /S'
    WriteRegDWORD HKCU "${ARP_KEY}" "NoModify" 1
    WriteRegDWORD HKCU "${ARP_KEY}" "NoRepair" 1
SectionEnd

Section "Uninstall"
    ClearErrors
    Delete "$INSTDIR\Kontur.exe"
    ${If} ${Errors}
        MessageBox MB_OK|MB_ICONSTOP "Закройте «Контур» через меню рядом с часами и повторите удаление." /SD IDOK
        SetErrorLevel 3
        Abort
    ${EndIf}
    Delete "$INSTDIR\licenses\dotnet-runtime-license.txt"
    Delete "$INSTDIR\licenses\dotnet-desktop-license.txt"
    Delete "$INSTDIR\licenses\dotnet-third-party-notices.txt"
    Delete "$INSTDIR\licenses\nsis-licenses.txt"
    RMDir "$INSTDIR\licenses"
    Delete "$SMPROGRAMS\Контур.lnk"
    ReadRegStr $0 HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "KonturSK0127"
    StrCmp $0 '$\"$INSTDIR\Kontur.exe$\"' 0 +2
    DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "KonturSK0127"
    DeleteRegKey HKCU "${ARP_KEY}"
    Delete "$INSTDIR\Uninstall.exe"
    RMDir "$INSTDIR"
SectionEnd

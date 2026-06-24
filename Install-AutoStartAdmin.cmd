@echo off
chcp 65001 >nul
title HanEngIndicator - 부팅 시 관리자 자동 실행 등록
setlocal

rem ============================================================
rem  이 파일을 HanEngIndicator.exe 와 "같은 폴더"에 두고
rem  더블클릭하세요. 로그인할 때마다 관리자 권한으로 자동
rem  실행되도록 Windows 작업 스케줄러에 등록합니다.
rem  (작업 스케줄러의 "가장 높은 권한" 작업은 로그인 시 UAC
rem   창 없이 조용히 관리자 권한으로 실행됩니다.)
rem ============================================================

rem --- 관리자 권한으로 자기 자신을 다시 실행 ---
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo 관리자 권한이 필요합니다. 권한 상승 창에서 "예"를 눌러주세요...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

set "EXE=%~dp0HanEngIndicator.exe"
set "TASKNAME=HanEngIndicator"

if not exist "%EXE%" (
    echo.
    echo [오류] 같은 폴더에서 HanEngIndicator.exe 를 찾을 수 없습니다.
    echo 이 스크립트를 HanEngIndicator.exe 와 같은 폴더에 두고 실행하세요.
    echo 현재 위치: %~dp0
    echo.
    pause
    exit /b 1
)

echo 등록할 프로그램: "%EXE%"
echo 작업 이름      : %TASKNAME%
echo.

rem --- 기존 작업이 있으면 교체(/F) ---
schtasks /Create /TN "%TASKNAME%" /TR "\"%EXE%\"" /SC ONLOGON /RL HIGHEST /F
if %errorlevel% neq 0 (
    echo.
    echo [오류] 작업 등록에 실패했습니다.
    pause
    exit /b 1
)

echo.
echo [완료] 다음 로그인부터 관리자 권한으로 자동 실행됩니다.
echo.

rem --- 지금 바로 한 번 실행 (이미 떠 있으면 교체) ---
taskkill /IM HanEngIndicator.exe /F >nul 2>&1
start "" "%EXE%"
echo 지금도 한 번 실행했습니다. (트레이 아이콘 확인)
echo.
pause
endlocal

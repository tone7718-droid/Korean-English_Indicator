@echo off
chcp 65001 >nul
title HanEngIndicator - 자동 실행 등록 제거
setlocal

rem 부팅 시 관리자 자동 실행 등록을 제거하고, 실행 중이면 종료합니다.

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo 관리자 권한이 필요합니다. 권한 상승 창에서 "예"를 눌러주세요...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

set "TASKNAME=HanEngIndicator"

schtasks /Delete /TN "%TASKNAME%" /F
taskkill /IM HanEngIndicator.exe /F >nul 2>&1

echo.
echo [완료] 자동 실행 등록이 제거되었습니다.
echo.
pause
endlocal

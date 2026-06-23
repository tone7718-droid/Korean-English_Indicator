# HanEngIndicator — 한·영 입력 상태 표시기

Windows에서 현재 입력 모드가 **한글(가)** 인지 **영문(A)** 인지를 글자 입력
커서(캐럿) 옆에 작은 배지로 즉시 보여 주는 데스크톱 프로그램입니다.

병원 차트처럼 입력칸마다 한/영 기본 상태가 다른 환경에서, 모드를 착각해
한글·영문이 뒤섞인 오타가 나는 것을 방지하기 위해 만들었습니다.

- **가** (파란 배지): 한국어 IME가 **한글 입력** 상태
- **A** (주황 배지): 한국어 IME가 **영문 입력** 상태이거나, 키보드가 영문 레이아웃

작업표시줄의 `한/ENG`(키보드 레이아웃)가 아니라, **한국어 IME 내부의 실제
`A / 가` 상태**를 감지합니다.

---

## 1. 비개발자용 사용 방법 (가장 빠른 길)

1. 배포된 **`HanEngIndicator-win-x64.zip`** 파일의 압축을 풉니다.
2. 안에 들어 있는 **`HanEngIndicator.exe`** 를 더블클릭해 실행합니다.
3. Windows 보안 경고("Windows의 PC 보호")가 나오면, 파일을 신뢰할 수 있을 때
   **추가 정보 → 실행**을 누릅니다. (아래 "문제 해결" 참고. 파일 속성 →
   디지털 서명/출처를 먼저 확인하세요.)
4. 실행되면 화면에 창은 뜨지 않고, **작업표시줄 오른쪽 알림 영역(트레이)** 에
   동그란 아이콘이 생깁니다. 글자를 입력하는 곳 근처에 **가 / A** 배지가
   나타납니다.
5. 설정을 바꾸려면 트레이 아이콘을 **오른쪽 클릭** 합니다.
6. **종료**하려면 트레이 아이콘 오른쪽 클릭 → **종료 (Exit)** 를 누릅니다.

> 관리자 권한이 필요 없고, 인터넷에 접속하지 않으며, 설치 과정도 없습니다.
> 그냥 EXE 하나만 실행하면 됩니다.

---

## 2. 무엇을 하고, 무엇을 하지 않는가 (보안)

이 프로그램은 병원 차트 입력에 사용될 수 있다는 점을 전제로, 다음을
**하지 않습니다**:

- ❌ 네트워크 요청 / 원격 서버 통신 / 텔레메트리 — **전혀 없음**
- ❌ 키 입력(타이핑한 글자) 저장·전송 — **하지 않음**
- ❌ 클립보드 읽기 — **하지 않음**
- ❌ 화면 캡처 — **하지 않음**
- ❌ 환자정보·차트 내용 읽기 / 차트 DB 접근 — **하지 않음**
- ❌ 키보드 후킹(키로깅) — **사용하지 않음**
- ❌ 관리자 권한 요구 — **하지 않음** (`asInvoker`)

이 프로그램이 **하는 일**은 다음뿐입니다:

- 현재 활성 창과 입력 컨트롤의 **상태(메타데이터)** 만 읽습니다:
  키보드 레이아웃, IME의 한/영 변환 상태, 캐럿의 화면 좌표.
- 그 결과를 작은 배지로 화면에 그립니다.

설정 파일은 오직 사용자 환경설정만 저장하며, 위치는 다음과 같습니다:

```
%LOCALAPPDATA%\HanEngIndicator\settings.json
```

환자 데이터나 입력한 문장은 **절대** 저장하지 않습니다.

### 사용하는 Windows API

| API | 용도 |
|-----|------|
| `GetForegroundWindow`, `GetWindowThreadProcessId`, `GetClassName` | 활성 창/스레드 식별 |
| `GetKeyboardLayout` | 키보드 레이아웃(한국어 여부) 확인 |
| `ImmGetDefaultIMEWnd` + `SendMessageTimeout(WM_IME_CONTROL, IMC_GETCONVERSIONMODE/IMC_GETOPENSTATUS)` | 한국어 IME의 실제 한/영 상태 조회 (주 방식) |
| `ImmGetContext`, `ImmGetConversionStatus`, `ImmGetOpenStatus`, `ImmReleaseContext` | IME 상태 조회 (보조 방식) |
| `GetGUIThreadInfo` (`rcCaret`), `ClientToScreen` | 캐럿 좌표 감지 |
| UI Automation (`TextPattern`, `BoundingRectangle`) | 캐럿 좌표 보조 감지 (브라우저·커스텀 컨트롤) |
| `GetCursorPos` | 마우스 위치 fallback |
| `MonitorFromPoint`, `GetDpiForMonitor` | 다중 모니터 / DPI 배율 보정 |
| `WS_EX_LAYERED`, `WS_EX_TRANSPARENT`, `WS_EX_NOACTIVATE`, `WS_EX_TOOLWINDOW` | 클릭 통과·비활성·항상 위 오버레이 |

**키 입력 내용(어떤 글자를 눌렀는지)** 은 어떤 API로도 읽지 않습니다.

---

## 3. 기능

- 한국어 IME의 실제 **가 / A** 상태 감지 (레이아웃이 아닌 IME 내부 상태)
- 캐럿 옆 오버레이 배지 (한글=파랑 `가`, 영문=주황 `A`)
- 위치 우선순위: **캐럿 → 마우스 → 화면 고정 위치**
- 항상 위(topmost), **클릭 통과**(아래 창 클릭 방해 안 함), 포커스 뺏지 않음
- 작업표시줄/Alt-Tab에 표시되지 않음, 투명도 조절
- DPI 100% / 125% / 150% 및 **다중 모니터** 대응, 화면 밖으로 안 나가도록 보정
- 트레이 메뉴: 표시 on/off, 한글만/모두 표시, 위치 모드, 글자 크기, 투명도,
  오프셋, **Windows 시작 시 자동 실행**, 진단 로그, 종료
- **진단 로그 모드**(선택): 호환성 문제 해결용. 타이핑 내용은 절대 기록 안 함.

---

## 4. 개발자용 — 빌드 방법

### 요구 사항
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 10/11 (권장) 또는 .NET 8 SDK가 설치된 Linux/macOS(크로스 빌드)

### 빌드 + 테스트
```powershell
# 저장소 루트에서
powershell -ExecutionPolicy Bypass -File .\build.ps1
```
또는 직접:
```bash
dotnet restore HanEngIndicator.sln
dotnet build  HanEngIndicator.sln -c Release
dotnet test   tests/HanEngIndicator.Tests/HanEngIndicator.Tests.csproj -c Release
```

### 단일 EXE 만들기 (배포)
```powershell
powershell -ExecutionPolicy Bypass -File .\publish-win-x64.ps1
```
또는 직접:
```powershell
dotnet publish src/HanEngIndicator/HanEngIndicator.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:PublishTrimmed=false `
  -p:IncludeNativeLibrariesForSelfExtract=true
```
결과물:
```
artifacts\publish\HanEngIndicator.exe        # 단일 실행 파일
artifacts\HanEngIndicator-win-x64.zip         # EXE + README
artifacts\SHA256SUMS.txt                       # 무결성 해시
```

> **참고(크로스 빌드):** 이 프로젝트는 Windows에서는 표준 `UseWindowsForms`
> 방식으로 빌드되고, Windows가 아닌 호스트(예: Linux CI)에서는
> `EnableWindowsTargeting` + `Microsoft.WindowsDesktop.App` FrameworkReference로
> 동일한 win-x64 EXE를 교차 컴파일합니다. `csproj`에 두 경로가 모두 들어 있어
> 별도 설정 없이 어느 쪽에서도 빌드됩니다.

---

## 5. 프로젝트 구조

```
HanEngIndicator.sln
src/HanEngIndicator/
  Program.cs                 # 진입점, 단일 인스턴스, DPI 설정
  app.manifest               # asInvoker(관리자 불필요), PerMonitorV2 DPI
  Resources/app.ico
  Native/NativeMethods.cs    # 모든 P/Invoke (상태 읽기 전용)
  Models/InputState.cs       # 입력 모드 / 상태 스냅샷
  Models/CaretLocation.cs    # 캐럿 앵커 / 감지 출처
  Settings/AppSettings.cs    # JSON 설정 (환경설정만 저장)
  Services/ImeDetector.cs    # 한/영 상태 감지 핵심 로직
  Services/CaretLocator.cs   # 캐럿 좌표 감지 (GUIThreadInfo → UIA → 마우스)
  Services/PositionCalculator.cs  # DPI/다중모니터/가장자리 보정 (순수 로직)
  Services/AutoStartManager.cs    # HKCU Run 키 자동 실행
  Services/DiagnosticLogger.cs    # 진단 로그 (타이핑 내용 미기록)
  Forms/OverlayForm.cs       # 클릭통과·항상위 배지 창
  Forms/TrayApplicationContext.cs # 트레이 아이콘·메뉴·폴링 루프
tests/HanEngIndicator.Tests/ # xUnit 단위 테스트 (순수 로직)
build.ps1
publish-win-x64.ps1
```

---

## 6. 한/영 상태를 어떻게 감지하는가

1. `GetForegroundWindow` → `GetWindowThreadProcessId` 로 활성 창과 UI 스레드 식별
2. `GetKeyboardLayout(threadId)` 로 레이아웃 확인 — 한국어(0x0412)가 아니면 영문
3. 한국어 레이아웃이면 `ImmGetDefaultIMEWnd` 로 IME 창을 얻고
   `SendMessageTimeout(WM_IME_CONTROL, IMC_GETCONVERSIONMODE)` 로 변환 모드를
   조회 — `IME_CMODE_NATIVE` 비트가 켜져 있으면 **가**, 아니면 **A**.
   (브라우저·일반 Win32 입력창·다수의 커스텀 컨트롤에서 동작)
4. 위 방식이 응답하지 않으면 포커스 컨트롤에 `ImmGetContext` +
   `ImmGetConversionStatus` 로 보조 조회.

> 일부 특수 입력 컨트롤(직접 그린 차트 입력칸 등)은 표준 IME 상태를 노출하지
> 않을 수 있습니다. 그런 경우를 위해 **진단 로그**(트레이 메뉴)를 켜고
> `%LOCALAPPDATA%\HanEngIndicator\diagnostics.log` 를 확인하면, 감지된 창
> 클래스/스레드/레이아웃/IME 상태/캐럿 감지 방식이 기록됩니다(타이핑 내용은
> 절대 기록되지 않음).

---

## 7. 문제 해결 (Troubleshooting)

**Q. 실행했는데 아무 것도 안 보여요.**
창이 없는 트레이 앱입니다. 작업표시줄 오른쪽 끝의 **∧(숨겨진 아이콘 표시)** 를
눌러 동그란 아이콘이 있는지 확인하세요. 글자를 입력하는 칸에 커서를 두면 배지가
나타납니다. 트레이 메뉴에서 "표시 활성화"가 켜져 있는지도 확인하세요.

**Q. Windows 보안 경고("Windows의 PC를 보호했습니다")가 떠요.**
코드 서명이 없는 새 EXE라서 SmartScreen 경고가 날 수 있습니다. 파일을 신뢰할 수
있을 때만 **추가 정보 → 실행**을 누르세요. 먼저 파일을 오른쪽 클릭 → **속성**
에서 출처/차단 해제(있으면 "차단 해제" 체크)를 확인하세요. `SHA256SUMS.txt` 의
해시와 다운로드한 EXE의 해시(`Get-FileHash`)가 일치하는지 비교할 수 있습니다.

**Q. 배지 위치가 캐럿과 안 맞아요 / 어떤 프로그램에서는 마우스만 따라가요.**
해당 입력 컨트롤이 표준 캐럿 정보를 노출하지 않는 경우입니다. 트레이 메뉴에서
**마우스 옆 표시** 또는 **화면 고정 위치 표시**로 바꿔 사용하세요.

**Q. 가/A가 실제와 반대이거나 안 바뀌어요.**
일부 앱은 IME 상태를 비표준 방식으로 다룹니다. **진단 로그**를 켜고 로그를
확인해 주세요. (위 6번 참고)

**Q. 글자가 너무 작아요/커요, 다른 화면에서 위치가 이상해요.**
트레이 메뉴의 **글자 크기 / 오프셋 / 투명도**를 조절하세요. DPI 배율과 다중
모니터는 자동 보정되지만, 환경별 미세 조정은 메뉴로 가능합니다.

---

## 8. 라이선스 / 면책

내부 도구용 예제 코드입니다. 의료 환경에서 사용하기 전 반드시 자체 검증을
거치세요. 이 프로그램은 입력 보조용 표시기일 뿐이며 차트 데이터의 정확성을
보장하지 않습니다.

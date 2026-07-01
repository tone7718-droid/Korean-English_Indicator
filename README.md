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

> 일반적인 프로그램에서는 관리자 권한이 필요 없고, 인터넷에 접속하지 않으며,
> 설치 과정도 없습니다. 그냥 EXE 하나만 실행하면 됩니다.

### 1-1. 관리자 권한 프로그램(SeeChart 등) 안에서 배지가 안 뜰 때

일부 진료 차트 프로그램(예: **SeeChart**)은 **관리자 권한으로 실행**됩니다.
이 경우 Windows 보안 정책(UIPI) 때문에, 권한이 낮은 이 표시기는 그 프로그램의
한/영 상태를 읽지 못해 **배지가 안 뜹니다**. 해결 방법:

- **그때만:** `HanEngIndicator.exe` 를 **오른쪽 클릭 → "관리자 권한으로 실행"**.
- **부팅 시 자동으로 관리자 실행:** 함께 들어 있는
  **`Install-AutoStartAdmin.cmd`** 를 (HanEngIndicator.exe 와 같은 폴더에 둔 채)
  더블클릭하세요. 권한 상승 창에서 "예"를 누르면, **로그인할 때마다 관리자
  권한으로 자동 실행**되도록 Windows 작업 스케줄러에 등록됩니다(로그인 시 UAC
  창은 뜨지 않습니다). 해제하려면 **`Uninstall-AutoStartAdmin.cmd`** 를 실행합니다.

> 참고: 트레이 메뉴의 "Windows 시작 시 자동 실행"은 **일반(낮은) 권한**으로
> 실행되므로, SeeChart처럼 관리자 권한 프로그램 안에서는 동작하지 않습니다.
> 그런 경우에는 위의 `Install-AutoStartAdmin.cmd` 방식을 사용하세요.

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
- 자체 **설정 파일**과, 켜둔 경우에 한해 **진단 로그**를 로컬에 저장합니다.
- "Windows 시작 시 자동 실행"을 켜면 **레지스트리(HKCU Run)** 를, 관리자 자동 실행
  스크립트를 쓰면 **작업 스케줄러** 항목을 만듭니다. (선택 사항이며 사용자 동의 하에만)

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
| `AttachThreadInput` + `GetKeyState(VK_CAPITAL)` | 영문 입력 시 Caps Lock 상태(A/a) 확인 (전경 스레드 입력 상태 공유) |
| `ImmGetDefaultIMEWnd` + `SendMessageTimeout(WM_IME_CONTROL, IMC_GETCONVERSIONMODE)` | 한국어 IME의 실제 한/영 상태 조회 (주 방식) |
| `ImmGetContext`, `ImmGetConversionStatus`, `ImmGetOpenStatus`, `ImmReleaseContext` | IME 상태 조회 (보조 방식) |
| `GetGUIThreadInfo` (`rcCaret`), `ClientToScreen` | 캐럿 좌표 감지 |
| UI Automation (`TextPattern`, `BoundingRectangle`) | 캐럿 좌표 보조 감지 (브라우저·커스텀 컨트롤) |
| `GetCursorPos` | 마우스 위치 fallback |
| `GetDpiForWindow`, `MonitorFromPoint`, `GetDpiForMonitor` | 다중 모니터 / DPI 배율 보정 |
| `SetWindowPos` (`HWND_TOPMOST`) | 오버레이를 항상 위로 유지 |
| `WS_EX_LAYERED`, `WS_EX_TRANSPARENT`, `WS_EX_NOACTIVATE`, `WS_EX_TOOLWINDOW` | 클릭 통과·비활성·항상 위 오버레이 |

**키 입력 내용(어떤 글자를 눌렀는지)** 은 어떤 API로도 읽지 않습니다.

---

## 3. 기능

- 한국어 IME의 실제 **가 / A / a** 상태 감지 (레이아웃이 아닌 IME 내부 상태)
  - `가` = 한글, `A` = 영문 + Caps Lock 켜짐(대문자), `a` = 영문 + Caps Lock 꺼짐(소문자)
- 캐럿 옆 오버레이 배지 (한글=파랑 `가`, 영문=주황 `A`/`a`)
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
결과물 (`publish-win-x64.ps1` 실행 시 `dist\` 폴더에 생성):
```
dist\HanEngIndicator.exe                    # 단일 실행 파일
dist\HanEngIndicator-win-x64.zip             # 단일 EXE + README + 자동실행 스크립트
dist\HanEngIndicator-win-x64-folder.zip      # 폴더형(자기추출 안 함) + 위 파일들
dist\SHA256SUMS.txt                          # 무결성 해시
```

> **참고(크로스 빌드):** 이 프로젝트는 **Windows·Linux 모두 동일한 방식**으로
> 빌드됩니다. `UseWindowsForms` 대신 `Microsoft.WindowsDesktop.App`
> FrameworkReference를 직접 참조하고(WinForms·WPF·UI Automation 모두 포함),
> `EnableWindowsTargeting`으로 비Windows 호스트에서도 win-x64 EXE를 교차
> 컴파일합니다. 따라서 Windows 개발 PC와 Linux CI 러너에서 `csproj` 분기 없이
> 똑같이 빌드됩니다. (이전의 OS별 `UseWindowsForms` 분기는 Windows에서 UI
> Automation 참조가 해석되지 않아 제거했습니다.)

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
  Services/CaretLocator.cs   # 캐럿 좌표 감지 (GUIThreadInfo → UIA → 마우스), UIA 스로틀/서킷브레이커
  Services/PositionCalculator.cs  # DPI/다중모니터/가장자리 보정 (순수 로직)
  Services/OverlayPolicy.cs  # 표시 여부·유예(hysteresis) 판정 (순수 로직, 테스트됨)
  Services/AutoStartManager.cs    # HKCU Run 키 자동 실행
  Services/DiagnosticLogger.cs    # 진단 로그 (버퍼링, 타이핑 내용 미기록)
  Forms/OverlayForm.cs       # 클릭통과·항상위 배지 창
  Forms/TrayApplicationContext.cs # 트레이 아이콘·메뉴 + 백그라운드 감지 워커(UI 스레드 비차단)
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

**Q. AhnLab(V3) 등 백신이 차단해서 실행이 안 돼요.**
이는 바이러스가 아니라 **오탐(false positive)** 입니다. 원인은 (1) 코드 서명이
없는 새 EXE, (2) 단일 EXE 방식이 실행 시 자기 자신을 임시폴더에 풀기 때문에
백신 휴리스틱이 의심하는 것입니다. 이 프로그램은 네트워크·키로깅을 하지 않으며,
환자 데이터·차트 파일·문서는 읽거나 변경하지 않습니다(소스 전체 공개). 다만 자체
설정(settings.json)과 선택적 진단 로그는 로컬에 저장하고, 자동 실행을 켜면 레지스트리
(HKCU Run) 또는 작업 스케줄러를 변경합니다. 해결 방법은 다음과 같습니다.

1. **폴더형 버전을 쓰세요(권장).** 단일 EXE 대신
   `HanEngIndicator-win-x64-folder.zip` 를 받아 압축을 풀고 그 안의
   `HanEngIndicator.exe` 를 실행하면, 자기 추출을 하지 않아 차단 확률이 크게
   낮아집니다. (기능은 단일 EXE와 동일)
2. **백신에 예외(허용) 등록.** AhnLab V3 기준:
   `V3 환경 설정 → 검사 → 검사 예외 설정(예외 폴더/파일)` 에서
   `HanEngIndicator.exe`(또는 풀어 놓은 폴더)를 예외로 추가합니다.
   회사/병원 PC라면 IT 관리자나 보안 정책상 관리자에게 요청해야 할 수 있습니다.
3. **오탐 신고.** AhnLab 오탐 신고 페이지에 파일을 제출하면 분석 후 탐지가
   해제됩니다: <https://www.ahnlab.com/site/securityinfo/falsepositive/> .
   제출 시 `SHA256SUMS.txt` 의 해시를 함께 적어 무결성을 증명할 수 있습니다.
4. **근본 해결(선택): 코드 서명.** Authenticode 코드 서명 인증서로 EXE에
   서명하면 SmartScreen·백신 경고가 사라집니다. 인증서는 유료(연 단위)이며,
   병원에서 인증서를 보유했다면 `signtool` 로 서명해 배포할 수 있습니다.


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

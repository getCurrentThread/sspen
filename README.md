# SS Pen

[![CI](https://github.com/getCurrentThread/sspen/actions/workflows/ci.yml/badge.svg)](https://github.com/getCurrentThread/sspen/actions/workflows/ci.yml)

화면 위에 바로 판서하는 윈도우용 한국어 주석 도구. 모니터마다 투명 오버레이를 띄워 펜·형광펜·도형·텍스트를 그리고,
전역 단축키로 도구를 바꾸며, 화면을 캡처해 복사·저장·핀 고정한다.

- **WPF / .NET 10 (`net10.0-windows`)**, 앱 프로젝트 **NuGet 의존성 0개**
- 모든 OS 연동은 직접 작성한 **Win32 P/Invoke** (`src/SSPen/Interop/`)
- 멀티 모니터 · PerMonitorV2 DPI 인식 · 음수 가상 화면 원점 지원

## 주요 기능

| 기능 | 설명 |
| --- | --- |
| 판서 | 펜 · 형광펜 · 지우개, 굵기 6단계, 바로가기 색상 6칸 |
| 도형 | 선 · 화살표 · 사각형 · 타원 · 텍스트, Shift 스냅(수평/수직/정비율) |
| 선택 | 필기내용 선택 후 이동 · 크기 조절 · 회전 · 삭제, 모니터 간 이동 |
| 보드 | 화이트보드 / 블랙보드 토글, 슬라이드 전환 |
| 페이딩 잉크 | 0.1~5초 후 스스로 사라지는 잉크 (도구와 조합되는 토글) |
| 캡처 | 영역 캡처 → 클립보드 복사 · PNG 저장 · 화면에 핀 고정(클릭 통과·확대) |
| 클릭 통과 | 오버레이를 켠 채로 아래 창을 그대로 조작 |
| 트레이 | 트레이 아이콘으로 판서 켜기/끄기 · 설정 · 종료 |

## 단축키

기본값이며 설정 창에서 재지정할 수 있다.

| 조합 | 동작 | | 조합 | 동작 |
| --- | --- | --- | --- | --- |
| `Alt+Shift+1` | 표시 토글 | | `Alt+Shift+L` | 선 |
| `Alt+Shift+2` | 클릭 통과 | | `Alt+Shift+A` | 화살표 |
| `Alt+Shift+3` | 펜 | | `Alt+Shift+R` | 사각형 |
| `Alt+Shift+4` | 형광펜 | | `Alt+Shift+E` | 타원 |
| `Alt+Shift+5` | 지우개 | | `Alt+Shift+T` | 텍스트 |
| `Alt+Shift+6` | 실행 취소 | | `Alt+Shift+W` | 화이트보드 |
| `Alt+Shift+7` | 전체 지우기 | | `Alt+Shift+B` | 블랙보드 |
| `Alt+Shift+0` | 툴바 토글 | | `Alt+Shift+F` | 페이딩 잉크 |
| `Alt+Shift+[` | 굵기 감소 | | `Alt+Shift+S` | 캡처 |
| `Alt+Shift+]` | 굵기 증가 | | `Alt+Shift+V` | 필기내용 선택 |
| `Ctrl+Shift+1~6` | 바로가기 색상 | | `Alt+Shift+D` | 선택 삭제 |

## 요구 사항

- Windows 10 1809 이상 (x64)
- 개발 시: [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0)
- 설치 프로그램 빌드 시: Inno Setup 6 (`winget install JRSoftware.InnoSetup`)

## 빌드 · 실행

```powershell
dotnet build SSPen.sln -c Debug
dotnet run --project src/SSPen
```

## 테스트

```powershell
# 단위 테스트 — 헤드리스 안전, 어디서나 실행 가능
dotnet test tests/SSPen.Tests/SSPen.Tests.csproj

# 통합 테스트 — 3x1920x1080 토폴로지 + 대화형 데스크톱이 필요한 머신 전용
dotnet test tests/SSPen.IntegrationTests/SSPen.IntegrationTests.csproj
```

`dotnet test SSPen.sln` 은 통합 테스트까지 같이 돌리므로 일반 환경에서는 단위 테스트 프로젝트만 지정한다.

## 배포

```powershell
# self-contained win-x64 게시
dotnet publish src/SSPen/SSPen.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64

# 게시 + 자체 포함 검증 + Inno Setup 설치 프로그램까지 한 번에
powershell -ExecutionPolicy Bypass -File build/publish.ps1
```

`v*` 태그를 밀면 [Release 워크플로](.github/workflows/release.yml)가 self-contained 게시 · 설치 프로그램 ·
포터블 zip 을 만들어 GitHub 릴리스에 올린다.

## 프로젝트 구조

```
src/SSPen/
  Annotation/   도구 상태(AppState), 요소 모델, 모니터별 오버레이, 입력 상태 기계, 선택/변형, 언두, 페이딩
  Shell/        툴바 · 설정 창 · 트레이 · 전역 핫키 · 문자열 테이블
  Interop/      Win32 P/Invoke 와 정책 래퍼(좌표계 · 모니터 토폴로지 · 창 스타일 · 캡처)
  Settings/     설정 POCO · JSON 영속화 · AppState 양방향 동기화 · 로그인 시 시작
  Capture/      캡처 세션 상태 기계 · BitBlt · 영역 오버레이 · 클립보드/PNG 출력
  Pin/          핀 고정 창 (클릭 통과 · 확대)
  Diagnostics/  일자별 롤링 파일 로그
tests/          단위 테스트 · 머신 바인딩 통합 테스트
build/          게시 · 아이콘 생성 스크립트
installer/      Inno Setup 6 스크립트
```

설계 규칙과 코딩 컨벤션은 [AGENTS.md](AGENTS.md) 에 정리되어 있다.

## 사용자 데이터 위치

| 경로 | 내용 |
| --- | --- |
| `%APPDATA%\SS Pen\settings.json` | 설정 (손상 시 `.bad` 로 격리 후 기본값 재생성) |
| `%APPDATA%\SS Pen\logs\sspen-yyyyMMdd.log` | 일자별 로그 |
| `사진\SS Pen\` | 캡처 저장 기본 폴더 (설정에서 변경 가능) |

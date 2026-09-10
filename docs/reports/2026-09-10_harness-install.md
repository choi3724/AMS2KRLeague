# 개발 하네스 설치 및 현재 코드 기준선 검증 — 작업 보고서

- 날짜: 2026-09-10
- 에이전트: Codex
- 기준 커밋: v0.7.0 / `48b2881f1f6f4b6f758763ef9556e383db086e70`
- 작업 후 HEAD: 동일. 미커밋, 작업 트리에 있음.
- 기준 저장소: 사용자 지정 AMS2KRLeague 작업 폴더.

## 1. 한 줄 요약

하네스 초안을 실제 Windows 환경에 맞게 설치·수정했습니다. 최종 결과는 아래 실행 출력으로 확인합니다.

## 2. 검증 게이트

```text
=== VERIFY (20260910-121541) ===  branch=main head=48b2881f1f6f4b6f758763ef9556e383db086e70 dirty=True
dotnet: C:\Users\User\Documents\Codex\2026-08-25\files-pasted-by-the-user-2026\outputs\AMS2KRLeague\work\dotnet8\dotnet.exe (8.0.424)
[PASS] versions — canonical version: 0.7.0  (source: Directory.Build.props)
[PASS] secrets 
[PASS] restore 
[PASS] build — errors=0 warnings=0 log=build-20260910-121541.log
[PASS] test:Client — exit=0 passed=135 total=135 failed=0 log=test-Client-20260910-121541.log
[PASS] test:Activity — exit=0 passed=110 total=110 failed=0 log=test-Activity-20260910-121541.log

=== GATE: PASS (47.9s) ===
report: harness\reports\verify-20260910-121541.md
```

증거 구분:

- 실제/라이브: 이 PC에서 SDK 8.0.424로 restore, Release 빌드 및 두 테스트 프로세스를 실행했습니다.
- fixture/데모: 제품 테스트 245개, 하네스 자체 검사 10개, 원본 DLL과 동일한 실행 사본에서 두 러너의 의도된 실패 종료 코드를 검증했습니다.
- NOT RUN: 실게임·실서버·VR 기기 검증, 원격 GitHub CI 실행. 이번 작업의 대상은 개발 하네스입니다.
- 첫 전체 실행도 통과: `verify-20260910-120606.md`, 60.9초, Client 135/135, Activity 110/110, 오류 0·경고 0.

### 실제 빌드·테스트 요약

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:00.72
RESULT: 135 passed, 0 failed, 135 total
RESULT: 110 passed, 0 failed, 110 total (2738 ms)
```

### 테스트가 실패했을 때의 종료 코드

테스트 소스와 원본 빌드 출력은 수정하지 않았습니다. 별도 사본에서 Client는 OpenVR 라이선스 파일, Activity는 fixture JSON 하나의 복사를 생략했습니다. Client는 해당 테스트 1개를 필터 실행했고 Activity는 전체 110개를 실행했습니다. 두 사본의 테스트 DLL은 원본과 SHA-256이 같았습니다. 운영 게임/서버에 연결하지 않았습니다.

```text
Client: RESULT: 0 passed, 1 failed, 1 total exit=1 originalAssemblyUnchanged=True
Activity: RESULT: 109 passed, 1 failed, 110 total (2729 ms) exit=1 originalAssemblyUnchanged=True
TEST_FAILURE_EXIT_CHECK: PASS (2 runners returned 1)
```

Client의 `Program.cs:245`는 전체 통과 시 0, 그 외 1을 반환합니다. Activity의 `Program.cs:47`도 실패 수가 0일 때만 0입니다. 실패를 종료 코드 0으로 숨기는 문제는 확인되지 않았으며 테스트 러너를 고치지 않았습니다.

### 하네스 자체 검사

```text
SUMMARY_CHECK: PASS (8 cases)
SECRET_BLOCK_CHECK: PASS (exit=1 hits=1 redacted=True untracked=True)
SECRET_ALLOW_CHECK: PASS (exit=0 hits=0)
HARNESS_SELF_CHECK: PASS (10 cases)
```

제품 테스트 코드 대신 `harness/scripts/test-harness.ps1`에 검사 10개를 남겼습니다. 실제 verifier의 정규식과 판정식을 읽어 정상 요약 2종, 실패 요약, 요약 없음, 전체 0개, 비정상 종료 등을 검사합니다. 별도 임시 Git 저장소에서는 미추적 가짜 토큰 검출, 출력 가림, 같은 줄의 허용 경로가 토큰을 숨기지 않는지, 정확한 예외가 허용되는지를 검사합니다.

## 3. 한 것

### 설치 및 환경

- 저장소에는 처음 `harness/`가 없었습니다. 상위 폴더에 있는 사용자가 제공한 `harness/` 초안을 통째로 복사했습니다. 원본 폴더와 ZIP은 그대로 보존했습니다.
- 작업 시작 시 Git 작업 트리는 clean이었습니다. 복사 이후 나타난 `?? harness/`와 이번 설정·보고서 변경은 모두 미커밋으로 남겼습니다. 기존 미커밋 변경을 되돌린 작업은 없습니다.
- 요청된 읽기 순서에 따라 AGENTS의 규칙 11개, 하네스 README·스크립트·설정, 기존 verify 2종·build-release·CI 파일 1개, 두 Program.cs를 확인했습니다. 보고서는 제공된 REPORT.md의 8개 항목을 따릅니다.
- 최초 preflight는 `"$tag: present"`의 PowerShell 변수 문법 오류로 종료 코드 1이었습니다. `"${tag}: present"`로 수정했습니다.
- preflight의 대상 태그를 요청한 `v0.7.0`으로 변경하고 HEAD 전체 해시를 기록합니다.
- PowerShell 5.1에서 기본 매개변수 식의 `$PSScriptRoot`가 빈 값으로 평가되는 문제를 수정했습니다. check-versions와 check-secrets는 본문에서 기본 경로를 구성합니다.
- 한글 깨짐을 막기 위해 하네스 PowerShell 파일은 UTF-8 BOM, JSON·텍스트 읽기는 명시적 UTF-8을 사용합니다.
- 기존 root `global.json`은 없어서 요청한 내용으로 생성했습니다.

```json
{
  "sdk": {
    "version": "8.0.424",
    "rollForward": "latestPatch"
  }
}
```

- 시스템 PATH의 dotnet에는 SDK 3.1.426만 있어 이번 빌드에 사용할 수 없었습니다. 기존에 준비된 8.0.424 SDK를 프로세스 환경변수 `AMS2_DOTNET`으로 지정했습니다. 실제 전체 경로는 preflight 원문에 있습니다. 새 SDK 설치나 시스템 설정 변경은 하지 않았습니다.
- Git 소유권 확인에 필요한 safe.directory도 실행 프로세스의 `GIT_CONFIG_COUNT / GIT_CONFIG_KEY_0 / GIT_CONFIG_VALUE_0`로만 지정했습니다. 전역 Git 설정은 변경하지 않았습니다.
- `.gitignore`에 `harness/reports/`를 추가했습니다. 하네스 실행 로그와 사본 검증 파일은 모두 이 무시 경로에 있습니다.

### 게이트 판정 수정

- 실제 두 RESULT 형식을 파싱하여 `passed`, `failed`, `total`을 기록합니다.
- 종료 코드 0, 전체 > 0, 실패 0, 통과 = 전체를 모두 만족해야 테스트 PASS입니다. 파싱 실패의 -1은 PASS가 되지 않습니다.
- Release 구성만 허용하며 검사 생략 옵션을 사용하면 전체 GATE는 FAIL입니다.
- restore/build/test 종료 코드는 명령 직후 보존합니다. restore 로그도 저장합니다.
- 빌드 언어를 프로세스 안에서 영어로 고정하여 오류·경고 숫자를 확정합니다. 각각 정확히 0이어야 PASS입니다. 기존 초안의 errors=-1 허용을 제거했습니다.
- 보고서 파일 기록 실패는 오류로 종료되도록 했습니다.
- 0 warnings 정책의 출처는 AGENTS의 명시 문구가 아니라 제공된 `harness/README.md`임을 바로잡았습니다.

### 버전 검사 결과

```text
canonical version: 0.7.0  (source: Directory.Build.props)
  [OK]   VersionPrefix: 0.7.0
  [OK]   README 현재 버전: 0.7.0
  [OK]   README Setup.exe 파일명: 0.7.0
  [OK]   VERSIONING SemVer 기준: 0.7.0
  [OK]   CHANGELOG 최상단 항목: 0.7.0
  [OK]   Inno Setup AppVersion: 0.7.0
  [OK]   build-release.ps1 기본 Version: 0.7.0
  [OK]   릴리스 노트 최상단: 0.7.0
  [OK]   ClientStatusViewModel 기본 버전 (02번 작업에서 단일화): 0.7.0
  [OK]   build-release.ps1 기본 DisplayVersion: 0.7.0
  [OK]   ClientStatusViewModel 빈 버전 대체값: 0.7.0
VERSION CHECK: PASS
```

required 항목 11개 모두 실제 선언에서 매치됐으며 0.7.0으로 일치합니다. 패턴 매치 실패나 SKIP은 없습니다.

실제 선언은 다음과 같습니다.

- `installer/AMS2LeagueOverlay.iss:8`: 들여쓰기 두 칸 뒤 `#define AppVersion "0.7.0"`.
- `scripts/build-release.ps1:4`: `[string]$Version = '0.7.0',`.
- `scripts/build-release.ps1:6`: `[string]$DisplayVersion = '0.7.0',`.
- `src/AMS2LeagueClient/Presentation/ClientStatusViewModel.cs:23`: 생성자 기본 인수에 0.7.0 하드코딩.
- 같은 파일 25행: 빈 문자열 대체값에도 0.7.0 하드코딩. **두 곳 모두 수정하지 않았습니다.**
- `release/RELEASE_NOTES_KO.md`는 존재하며 1행에 0.7.0이 있습니다. 항목을 유지하고 첫 제목에 고정했습니다.
- VersionPrefix, 릴리스 노트, ClientStatus 기본값은 실제 존재를 확인하고 required로 설정했습니다. DisplayVersion과 빈 버전 대체값 검사 2개를 추가했습니다.

### 정규식 전후 비교

아래는 JSON 이스케이프를 해제한 실제 정규식입니다.

테스트 요약 변경 전:

```regex
(\d+)\s*/\s*(\d+)
```

테스트 요약 변경 후:

```regex
^RESULT:\s+(\d+) passed, (?<failed>\d+) failed, (\d+) total(?: \(\d+ ms\))?\s*$
```

.NET 정규식의 이름 없는 그룹 1은 passed, 2는 total이며 이름 있는 failed 그룹으로 실패 수를 확인합니다. Activity에만 있는 시간 접미사는 선택적으로 받습니다.

### Inno Setup AppVersion

경로: `installer/AMS2LeagueOverlay.iss`

변경 전:
```regex
(?im)^#define\s+\w*Version\w*\s+"([0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.]+)?)"
```
변경 후:
```regex
(?m)^\s*#define\s+AppVersion\s+"([0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.]+)?)"\s*$
```

### build-release.ps1 기본 Version

경로: `scripts/build-release.ps1`

변경 전:
```regex
(?im)\$Version\s*=\s*["']([0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.]+)?)["']
```
변경 후:
```regex
(?m)^\s*\[string\]\$Version\s*=\s*'([0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.]+)?)',\s*$
```

### 릴리스 노트 최상단

경로: `release/RELEASE_NOTES_KO.md`

변경 전:
```regex
([0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.]+)?)
```
변경 후:
```regex
(?m)^# AMS2 리그 오버레이 ([0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.]+)?) 업데이트\s*$
```

### ClientStatusViewModel 기본 버전 (02번 작업에서 단일화)

경로: `src/AMS2LeagueClient/Presentation/ClientStatusViewModel.cs`

변경 전:
```regex
"([0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.]+)?)"
```
변경 후:
```regex
public ClientStatusViewModel\(string version = "([0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.]+)?)"\)
```

### 추가한 검사

기본 DisplayVersion:
```regex
(?m)^\s*\[string\]\$DisplayVersion\s*=\s*'([0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.]+)?)',\s*$
```
빈 버전 대체값:
```regex
string\.IsNullOrWhiteSpace\(version\)\s*\?\s*"([0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.]+)?)"
```


비밀 검사 경로 정규식도 Markdown의 백틱·표 구분자를 경로에 섞지 않고 드라이브 대소문자를 모두 받도록 조정했습니다.

변경 전:

```regex
E:\\[^\s"'<>]+
(?i)C:\\Users\\[^\\\s"'<>]+
```

변경 후:

```regex
(?i)E:\\[^\s"'<>\x60|]+
(?i)C:\\Users\\[^\\\s"'<>\x60|]+
```

위 코드 블록의 `\x60`은 읽기 편의를 위해 표시한 백틱 문자입니다. 실제 PowerShell 정규식에는 리터럴 백틱이 들어 있습니다.

### 비밀 검사 HIT 전수 검토

검출값을 그대로 출력하던 초안은 실행 전에 파일·줄·종류만 출력하도록 수정했습니다. 최초 끝까지 실행된 검사 결과는 다음과 같습니다.

```text
  [HIT] docs/CLAUDE_CODE_HANDOFF_2026-09-05_KO.md:217  <user profile path>  [value redacted]
  [HIT] docs/DATA_RECORDING_TRANSMISSION_SPEC_0.5.1_2026-09-09_KO.md:445  <dev drive path E:>  [value redacted]
  [HIT] docs/DATA_RECORDING_TRANSMISSION_SPEC_0.5.1_2026-09-09_KO.md:471  <user profile path>  [value redacted]
  [HIT] docs/DRIVING_HUD_RESTART_2026-09-09_KO.md:7  <dev drive path E:>  [value redacted]
  [HIT] docs/EDIT_PREVIEW_2026-09-08_KO.md:4  <dev drive path E:>  [value redacted]
  [HIT] docs/GZIP_JSON_UPLOAD_IMPLEMENTATION_2026-09-09_KO.md:3  <dev drive path E:>  [value redacted]
  [HIT] docs/MAIN_OVERLAY_GALLERY_2026-09-10_KO.md:56  <dev drive path E:>  [value redacted]
  [HIT] docs/OFFLINE_LAYOUT_AND_UI_CAPABILITIES_2026-09-09_KO.md:3  <dev drive path E:>  [value redacted]
  [HIT] docs/RELEASE_0.4.4_2026-09-09_KO.md:4  <dev drive path E:>  [value redacted]
  [HIT] docs/SHM_FIELD_INVENTORY.md:9  <dev drive path E:>  [value redacted]
  [HIT] docs/UPDATE_0.4.1_2026-09-08_KO.md:3  <dev drive path E:>  [value redacted]
  [HIT] docs/UPDATE_0.4.2_2026-09-08_KO.md:3  <dev drive path E:>  [value redacted]
  [HIT] docs/UPDATE_0.4.3_2026-09-08_KO.md:4  <dev drive path E:>  [value redacted]
  [HIT] harness/scripts/check-secrets.ps1:30  <dev drive path E:>  [value redacted]
  [HIT] PROJECT.md:3  <dev drive path E:>  [value redacted]
  [HIT] scripts/Test-PublicPackage.ps1:40  <user profile path>  [value redacted]
  [HIT] tests/AMS2LeagueClient.Tests/Program.cs:1844  <token assignment>  [value redacted]
  [HIT] tests/AMS2LeagueClient.Tests/Program.cs:1869  <token assignment>  [value redacted]
  [HIT] tests/AMS2LeagueClient.Tests/Program.cs:1899  <token assignment>  [value redacted]
  [HIT] tests/AMS2LeagueClient.Tests/Program.cs:1986  <token assignment>  [value redacted]
  [HIT] tests/AMS2LeagueClient.Tests/Program.cs:2037  <token assignment>  [value redacted]
scanned=317 allowed=0 hits=21
SECRET CHECK: FAIL (21 hit)
```

21건 모두 검토했습니다.

| 대상 | 건수 | 확인 내용 / 처리 |
|---|---:|---|
| docs의 경로 | 13 | 작업 폴더·설치 SDK 근거·서버 소스 위치 등 의도된 문서 경로. 파일별 정확한 검출값 예외 |
| PROJECT.md | 1 | 사용자가 지정한 작업 경로. 정확한 예외 |
| scripts/Test-PublicPackage.ps1 | 1 | 공개 패키지에 들어가면 안 되는 경로의 탐지 문자열. 정확한 예외 |
| harness/scripts/check-secrets.ps1 | 1 | 검사기 자신의 경로 정규식. 정확한 예외 |
| tests/AMS2LeagueClient.Tests/Program.cs | 5 | Pairing DPAPI/가짜 HTTP 전송/403 진단 테스트의 고정 가짜 토큰. fixture.invalid 핸들러 또는 임시 저장소에서만 쓰이는 값. 정확한 예외 |

실제 운영 비밀은 이 검사와 검토에서 발견되지 않았습니다. 토큰 검출값은 보고서에 기록하지 않았습니다.

예외는 기존의 줄 전체 부분 문자열 방식에서 **정확한 파일 + 검출 종류 + 검출된 문자열** 방식으로 바꿨습니다. 허용 경로가 있다는 이유로 같은 줄의 다른 토큰을 통과시키지 않습니다. 검사 범위는 추적 파일과 Git에서 무시하지 않는 미추적 파일입니다. allowlist 자체는 검토한 예외 설정으로 취급해 내용 스캔에서 제외합니다. 지정된 패턴만 검사하므로 미검출을 모든 종류의 비밀 부재 보증으로 해석하지 않습니다.

최종 보고서에 복사한 preflight의 의도된 PC 경로도 이 보고서 파일에 한정해 예외로 등록합니다.

```text
scanned=319 allowed=28 hits=0
SECRET CHECK: PASS
```

### 기존 검증 / 하네스 / CI 비교

| 검사 | scripts/verify.ps1 (verify.sh 포함) | harness/scripts/verify.ps1 | 현재 CI build.yml |
|---|---|---|---|
| restore | build에 포함, --ignore-failed-sources | 별도 restore 후 --no-restore build | build-release에서 별도 restore |
| Release 빌드 | 실행 | 실행, 구성 Release만 허용 | 실행 |
| 빌드 경고 0 강제 | 없음, 종료 코드만 확인 | 있음, 오류·경고 숫자 모두 0 | 없음, 종료 코드만 확인 |
| Client 테스트 | 실행, 종료 코드 확인 | 실행, 종료 코드와 RESULT 숫자 확인 | 실행, 종료 코드 확인 |
| Activity 테스트 | 실행, 종료 코드 확인 | 실행, 종료 코드와 RESULT 숫자 확인 | 실행, 종료 코드 확인 |
| ApplyUpdate.ps1 문법 검사 | 있음 | 없음 | 명시적 Parser 검사 없음 |
| UI 캡처 | -CaptureLayouts 선택 가능 | 없음 | 없음 |
| 버전 일치 검사 | 없음 | 11개 검사 | build-release의 Version / InformationalVersion 두 요청값 대조 |
| 저장소 비밀·개발 경로 검사 | 없음 | 있음 | 없음 |
| 구조화된 게이트 JSON/Markdown | 없음, 실행 로그 | 있음, last-verify.json + 시각별 MD/로그 | 없음, GitHub 단계 로그 |
| self-contained publish / ZIP / 패키지 검사 | 없음 | 없음 | 있음, build-release 및 Test-PublicPackage 호출 |
| SDK 지정 | dotnet 인수 사용 | 인수/환경변수/로컬 SDK/PATH + global.json | setup-dotnet 8.0.x; 이번 global.json을 반영하는 경우 8.0.4xx 선택 가능 여부 주의 |

`scripts/verify.sh`는 Windows PowerShell의 기존 verify.ps1을 호출하는 래퍼이며 독자적인 추가 검사는 없습니다. 기존 verify.ps1의 기본 증거 폴더명은 `work/validation-0.4.1`로 남아 있습니다.

CI 파일은 `.github/workflows/build.yml` 1개입니다. `scripts/build-release.ps1`를 통해 **restore, Release build, Client/Activity 두 테스트는 모두 수행**합니다. 하네스와 완전히 같은 게이트는 아니며 위 차이가 있습니다. CI 파일과 기존 scripts 파일은 수정하지 않았고 원격 CI를 실행하지 않았습니다.

## 4. 하지 않은 것 / 범위 밖으로 남긴 것

- commit / tag / push / GitHub Release를 하지 않았습니다.
- src, 제품 테스트, 기존 scripts의 PowerShell 및 verify.sh, .github/workflows는 수정하지 않았습니다.
- reset / clean / force-checkout을 하지 않았습니다.
- 버전 단일화(02), 문서 정리(03), 테스트 러너 교체(04)는 진행하지 않았습니다.
- 기존 verifier와 하네스의 통합 및 CI 교체는 진행하지 않았습니다.
- 필요한 하네스 검증 중 미실행 항목은 없습니다.

## 5. 발견한 문제 (이번 작업 범위 밖)

- ClientStatusViewModel에 버전 문자열이 두 번 하드코딩돼 있습니다. 현재 값은 맞지만 02번 작업에서 단일화할 대상입니다.
- 기본 PATH SDK와 프로젝트 SDK 요구가 다릅니다. 이 PC에서는 기존 SDK 경로를 환경변수/명시 인수로 넘겨야 합니다.
- CI의 8.0.x 설정과 새 global.json의 8.0.4xx 선택 조건은 원격 환경에서 별도 확인이 필요합니다. 이번 작업은 CI 수정·실행을 포함하지 않습니다.

## 6. 사용자 결정이 필요한 항목

| 선택 | 장점 | 남는 점 |
|---|---|---|
| 기존 verifier와 하네스 유지 | 기존 UI 캡처 흐름을 그대로 사용 | 개발자가 서로 다른 검증 경로를 선택할 수 있음 |
| 하네스로 통합하고 기존 verifier를 래퍼로 변경 | 공통 게이트 한 곳 관리 | ApplyUpdate 문법 검사와 UI 캡처 옵션을 먼저 이관해야 함 |

통합 여부는 사용자 결정으로 남깁니다. 현재 작업 완료를 위해 추가 승인을 요구하지 않습니다.

## 7. 다음 단계 제안

1. 다음 변경부터 하네스 verify를 실행하고 생성된 게이트와 테스트 수를 보고합니다.
2. 02번 작업에서 버전 기본값·대체값을 단일화합니다.
3. 통합 결정 후 기존 검증의 추가 기능을 보존하면서 CI와 로컬 게이트를 맞춥니다.

## 8. preflight 출력 (원문)

### 최초 실행 — 초안 오류, 종료 코드 1

아래는 요청한 명령을 저장소에 설치한 초안에 실행한 전체 출력입니다. 상위 호출의 PowerShell 오류 레코드도 그대로 포함했습니다.

```text
powershell : At E:\AMS2 KRLEAGUE\AMS2KRLeague\harness\scripts\preflight.ps1:42 char:53
위치 줄:6 문자:1
+ powershell -NoProfile -ExecutionPolicy Bypass -File .\harness\scripts ...
+ ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
    + CategoryInfo          : NotSpecified: (At E:\AMS2 KRLE....ps1:42 char:53:String) [], RemoteException
    + FullyQualifiedErrorId : NativeCommandError
 
+     if (git tag -l $tag) { Write-Host "baseline tag $tag: present" }
+                                                     ~~~~~
Variable reference is not valid. ':' was not followed by a valid variable name character. Consider using ${} to delimit
 the name.
    + CategoryInfo          : ParserError: (:) [], ParentContainsErrorRecordException
    + FullyQualifiedErrorId : InvalidVariableReferenceWithDrive
```

### 수정 후 실행 — 종료 코드 0

```text
=== PREFLIGHT (2026-09-10 12:15:40) ===
repo: E:\AMS2 KRLEAGUE\AMS2KRLeague
branch: main  HEAD: 48b2881f1f6f4b6f758763ef9556e383db086e70
WARN: 작업 트리에 미커밋 변경이 있습니다. 다른 에이전트의 작업일 수 있으니 되돌리지 마십시오.
   M .gitignore
  ?? docs/reports/
  ?? global.json
  ?? harness/
baseline tag v0.7.0: present
latest tag: v0.7.0   Directory.Build.props Version: 0.7.0
dotnet: C:\Users\User\Documents\Codex\2026-08-25\files-pasted-by-the-user-2026\outputs\AMS2KRLeague\work\dotnet8\dotnet.exe (8.0.424)
=== PREFLIGHT DONE ===
```

실행 환경은 AMS2_DOTNET으로 기존 SDK를 지정한 PowerShell 프로세스입니다. 저장소 외의 SDK 경로를 코드에 하드코딩하거나 전역 설정으로 저장하지 않았습니다.


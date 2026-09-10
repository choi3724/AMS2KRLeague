# AMS2KRLeague 개발 하네스

Codex(또는 다른 AI 에이전트)가 이 저장소에서 작업할 때 **매번 같은 방식으로 검증하고 보고하게** 만드는 도구 묶음입니다.
저장소 루트에 `harness/` 폴더째로 넣습니다.

## 폴더 구성

```
harness/
  README.md                  ← 이 파일
  scripts/
    preflight.ps1            ← 작업 전: git 상태, 기준 커밋, dotnet 버전 확인
    verify.ps1               ← 작업 후: 버전 일치 → 비밀 검사 → restore → build → 테스트 2종 → 보고서
    check-versions.ps1       ← 실제 버전 선언 11개 항목의 일치 검사
    check-secrets.ps1        ← 추적 파일과 무시되지 않은 미추적 파일의 토큰/비밀번호/개발 PC 경로 검사
    version-config.json      ← check-versions.ps1이 보는 파일·패턴 목록 (Codex가 조정)
    secret-allowlist.txt     ← 비밀 검사에서 검토한 파일·검출 종류·검출값의 정확한 예외
  templates/
    TASK_PROMPT.md           ← Codex에 줄 작업 프롬프트 틀
    REPORT.md                ← Codex가 작업 끝에 써야 하는 보고서 틀
  prompts/
    01-install-harness.md    ← 하네스 설치 + 기준선 검증 (가장 먼저)
    02-version-single-source.md
    03-docs-consolidation.md
    04-test-runner.md
    05-updater-and-architecture-audit.md
  ci/
    verify.yml.example       ← GitHub Actions에서 같은 게이트를 도는 예시
  reports/                   ← verify.ps1이 결과를 남기는 곳 (Git 제외 권장)
```

## 사용 순서 (사용자)

1. `harness/` 폴더를 저장소 루트에 복사합니다.
2. `harness/prompts/01-install-harness.md` 내용을 Codex에 붙여넣습니다.
   Codex가 스크립트를 실제 환경에 맞게 고치고, 현재 코드가 게이트를 통과하는지 확인합니다.
3. 이후 작업은 `templates/TASK_PROMPT.md`를 채워서 Codex에 줍니다.
   Codex는 작업 끝에 반드시 `verify.ps1`을 돌리고 `templates/REPORT.md` 형식으로 보고해야 합니다.
4. 보고서에서 볼 것은 세 줄입니다:
   - `GATE: PASS` 인지 `FAIL` 인지
   - 테스트 숫자 (예: Client 113/113, Activity 102/102)
   - "하지 않은 것" 항목이 비어 있는지

## 검증 게이트가 보는 것

| 단계 | 통과 조건 |
|---|---|
| 버전 일치 | Directory.Build.props 버전이 다른 모든 곳과 같음 |
| 비밀 검사 | 검토된 예외 외에 토큰·비밀번호·개발 PC 경로의 검출 없음 |
| restore/build | Release 빌드 0 error, 0 warning |
| 테스트 | 두 프로젝트 모두 종료 코드 0, 요약 파싱 성공, 전체 > 0, 실패 0, 통과 = 전체 |

하나라도 실패하면 `GATE: FAIL`이고 스크립트 종료 코드가 0이 아닙니다.
결과는 `harness/reports/verify-<시각>.md`와 `harness/reports/last-verify.json`에 남습니다.

## 주의

- 이 스크립트들은 제공된 초안을 실제 Windows/.NET 환경에서 검증하여 수정한 버전입니다.
  설치 검증 결과는 docs/reports/2026-09-10_harness-install.md에 있습니다.
- `harness/reports/`는 `.gitignore`에 추가되어 있습니다.

## 현재 환경에서 실행

Windows PowerShell 5.1 이상과 .NET SDK 8.0.424가 필요합니다.
`global.json`은 같은 8.0.4xx 기능 대역의 최신 패치를 허용합니다.
SDK 선택 순서는 `-DotnetExe`, `AMS2_DOTNET` 환경변수, `work\dotnet8\dotnet.exe`, PATH입니다.
별도 폴더의 SDK는 `AMS2_DOTNET`을 해당 dotnet.exe 경로로 설정하거나 `-DotnetExe`로 지정합니다.
설치 경로와 전역 Git/PowerShell 설정은 하네스가 변경하지 않습니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\harness\scripts\preflight.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\harness\scripts\test-harness.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\harness\scripts\verify.ps1
```

- 검증은 Release만 허용합니다. `-SkipTests`, `-SkipSecrets`, `-SkipVersions`는 부분 실행용이며 최종 게이트는 FAIL/종료 코드 1입니다.
- 테스트 요약을 읽지 못하면 FAIL입니다. 빌드 출력은 프로세스 안에서 영어로 고정하여 경고·오류 숫자를 집계합니다.
- `secret-allowlist.txt`는 한 줄에 JSON 객체 하나(`file`, `label`, `value`)를 사용합니다. 세 값이 정확히 일치해야 허용하며, 같은 줄의 다른 비밀까지 허용하지 않습니다. 실제 비밀은 등록하면 안 됩니다.
- 비밀 검사 HIT는 파일·줄·종류만 출력합니다. allowlist 자체는 검토 대상 설정으로 취급하여 내용 검사는 제외합니다. 자동 검사는 지정된 패턴만 찾으며 완전한 비밀 탐지를 보장하지 않습니다.
- `test-harness.ps1`은 요약 판정과 미추적 파일 검사·예외 범위·민감값 비출력을 10개 경우로 확인합니다. 제품 테스트 코드를 바꾸지 않습니다.
- 보고서는 UTF-8이며 `harness/reports/` 전체가 Git에서 제외됩니다. 기존 `scripts/verify.ps1`과 CI는 그대로 유지합니다.
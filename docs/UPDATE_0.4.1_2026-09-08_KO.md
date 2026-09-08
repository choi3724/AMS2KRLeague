# AMS2KRLeague 0.4.1 작업·검증 보고서

작성일: 2026-09-08 KST. 작업 기준 경로: `E:\AMS2 KRLEAGUE\AMS2KRLeague`.

## 기준선

- 사용자 요청에 따라 E: 저장소에서 GitHub `fetch --tags` 및 `merge --ff-only origin/main`을 실행했다. 기존 E: 체크아웃은 변경 없는 v0.1.0 / `8107398`이었다.
- 시작 기준선은 공개 v0.4.0 / `ce8e084e43c3fb37667d10e00f3f63077bea2600`. GitHub Latest API와 설치 파일 메타데이터도 직접 확인했다.
- 원래 C: 저장소, 사용자의 실제 설치본과 데이터, 서버, 대화 기록은 수정하지 않았다. E: 저장소에는 이번 작업 이외의 미커밋 변경이 없었다.
- 작업 전 `scripts/verify.sh`는 없었다. 동등한 .NET Release 빌드 및 두 스위트를 먼저 실행했다: 경고 0 / 오류 0, Client 102/102, Activity 102/102.
- 작업 전 화면 캡처: `work/validation-0.4.1/before/`. 사용자가 Ponytail 사용 중단을 지시했으며 이 작업에서 사용하지 않았다.

## 요구사항과 구현

### REQ-KO-01 — 구현·화면 검증 완료

- 레이스 컨트롤 `FINAL`과 타워 `FIN`의 표시를 **완주**로 변경했다.
- 타워 상태는 피트·완주·중도 포기·미완주·실격·최고·의무 피트·수리·미확인으로 표시한다. 내부 상태 코드는 기존 전이·애니메이션 처리를 위해 유지한다.
- 랩 안내, 플레이어 고정 행 설명, 데모 표시, 이벤트/게임 상태 진단, 시작·실행·공유 메모리 오류, 업데이트 안내와 설치 프로그램을 한글화했다.
- 일반 사용자 안내에 운영체제의 영어 오류를 그대로 노출하지 않는다. 세부 오류는 기술 로그에 남긴다.
- 차량/트랙/클래스 고유 명칭, P/S1 등의 순위·섹터 기호, 단위와 프로토콜/기술 로그 코드는 유지한다. 영어 카탈로그는 선택 가능한 내부 호환 자료로 남지만 기본 실행은 한국어다.

### REQ-PEN-02 — 구현·화면 검증 완료, 실게임 신규 페널티 관측 미실행

- 타워 우측에 112px 페널티 열과 구분선을 추가했다. 기본 타워는 520×586 → **648×608px**이며 15개 행을 유지한다.
- 참가자별 원본 `PitSchedule.DriveThrough`, `StopGo`, 실격 race state 또는 black flag를 표시한다. 의무 피트/수리 요청은 페널티로 취급하지 않는다.
- 페널티와 완주·피트·최고기록은 서로 독립적으로 갱신한다. 페널티 해제도 기존 행에 반영하며 행 전체를 흐리게 만들지 않는다.
- 미지원 schedule은 `미확인`, 지원되는 상태에서 확인된 페널티가 없으면 `—`다. 제공되지 않는 가산 초나 구체적인 사유는 만들지 않는다.
- 이전 저장 프로필은 타워 폭과 헤더 높이를 보정해 행 수를 유지한다. 다시 저장한 프로필은 중복 확대하지 않으며 다른 패널의 저장 배치는 바꾸지 않는다.
- 실제 WPF 렌더에서 칸 폭·텍스트 경계·동시 표시·해제 갱신을 검사했다. 신규 실게임에서 페널티를 발생시킨 검수는 하지 않았다.

### REQ-UPD-03 — 구현·다운로드·설치/재실행 검증 완료

- 시작 시 및 6시간마다 공개 GitHub Latest API를 조회한다. 새로운 SemVer만 적용하며 draft/prerelease, 다운그레이드, 잘못된 버전/주소/크기/해시는 거부한다.
- 지정 저장소의 해당 태그 설치 파일만 다운로드한다. 크기와 SHA-256을 검증하고, 설치 직전 다시 검증한 뒤 읽기 핸들을 유지해 검증 파일의 교체를 막는다.
- 조회는 30초, 다운로드는 최대 15분 제한이다. 부분 다운로드는 제거하며 실패 시 현재 앱이 계속 작동한다. 완성된 임시 설치 파일도 실패 또는 완료 후 정리한다.
- AMS2 실행 중에는 설치를 기다린다. 별도 숨김 업데이트 도구의 준비 확인 후 기록을 정상 저장·종료한다. 업데이트 도구는 부모 프로세스의 ID/시작 시각과 종료를 확인한 뒤 설치한다.
- 설치 직전 게임 및 다른 오버레이를 다시 확인한다. 앱 강제 종료나 Windows 자동 재부팅을 하지 않는다. 설치 후 같은 실행 인자를 보존해 재실행하고 결과를 한글로 표시한다.
- 설치판은 기존 경로/제거 항목을 갱신한다. ZIP 실행본은 포터블 설치 옵션으로 같은 폴더를 갱신하며 제거 항목을 새로 만들지 않는다. 설정/활동 데이터 경로는 유지한다.
- 중복 설치 잠금, 설치 실패/취소, 재시작 반복 방지를 위한 6시간 재확인 간격이 있다. 데모·캡처·자동 종료 실행은 업데이트를 생략한다. `--updates-disabled`로 이번 실행만 끌 수 있다.
- 인증/텔레메트리 토큰은 GitHub 요청에 사용하지 않는다. 새 의존성은 추가하지 않았다.

**배포 전제:** 0.4.0 이하에는 업데이트 기능이 없다. 최초 0.4.1은 수동 설치가 필요하다. 0.4.1 이후에는 공개 GitHub Release에 새로운 설치 파일이 게시되어야 자동 업데이트된다. 이번 작업은 로컬 커밋 범위이며 공개 게시를 하지 않았다.

### REQ-VER-04 / REQ-PATH-05

- 제품/어셈블리/설치판/패키지 기본값 및 문서를 0.4.1 / 0.4.1.0으로 맞췄다.
- 사용자 승인 범위는 **E: 저장소에서 0.4.1 로컬 커밋**이다. tag/push/Release는 실행하지 않는다.
- `PROJECT.md`, `docs/TASK.md`, 통합 검증 진입점을 추가해 새 경로의 기준과 요청 범위를 기록했다.

## 최종 검증

| 항목 | 결과 | 증거 |
|---|---|---|
| Release build | 경고 0 / 오류 0 | `work/validation-0.4.1/shell-gate/build.log` |
| Client tests | **107 passed / 0 failed** | `work/validation-0.4.1/shell-gate/client.log` |
| Activity tests | **102 passed / 0 failed** | `work/validation-0.4.1/shell-gate/activity.log` |
| `verify.sh` → PowerShell/.NET 게이트 | PASS | `work/validation-0.4.1/shell-gate.log` |
| 최종 패키지 재빌드·두 스위트 | PASS, 동일 107/102 | `work/validation-0.4.1/verified-package.log` |
| 공개 패키지 검사 | 폴더 466 / ZIP 466 / Installer 1, 금지 항목 0 | 같은 패키지 로그 |
| 실제 GitHub 설치 파일 다운로드 | v0.4.0, 51,256,722 bytes, SHA-256 일치 | `work/validation-0.4.1/github-download.log` |
| 실제 공개 ZIP 기준 자동 설치 | **0.4.0 → 0.4.1**, helper exit 0 | `work/validation-0.4.1/verified-install-proof.log` |
| 설치 후 재실행 | 새 0.4.1 앱의 화면 PNG **18개**, 예외 없음 | 아래 별도 설치 시험 폴더 |
| 사용자 파일/포터블 보존 | 모두 PASS | 같은 설치 시험 |
| 실제 게임 주행·새 페널티·GPU 출력 FPS | **NOT RUN** | 이번 요청에서 게임 실행/조작하지 않음 |

설치 시험 폴더: `work/update-install-proof-1cf0f326e2aa448bae398ccae1cffef8/`.

시험은 실제 공개 v0.4.0 ZIP을 새 작업 하위 폴더에 풀고, 이번에 만든 설치 파일과 제품 업데이트 도구를 적용했다. 사용자의 실제 설치본을 교체하지 않았다. 다운로드 경로는 제품 HTTP 코드로 별도 검증했고 설치 설정은 시험 스크립트가 제품과 같은 형식으로 준비했다. **아직 미게시인 0.4.1을 GitHub에서 받는 전체 운영 흐름을 실행했다는 뜻은 아니다.** 설치판의 기존 제거 항목 갱신은 코드/설치 컴파일 검증이며 실제 시험은 포터블 경로다.

신규 테스트는 버전 순서, 배포 메타데이터 거부, 주소 제한, 해시 불일치, 중단·초과 다운로드, HTTP 오류, 취소/재시도, 명령줄 공백/따옴표 보존, 업데이트 도구의 위변조/취소 거부와 화면 표시/폭 이행을 검사한다. 기존 테스트를 삭제하거나 생략하지 않았다.

화면 증거: `work/validation-0.4.1/after/korean-penalties.png`, `korean-update-status.png`, `korean-update-failure.png`. 모두 테스트 데이터의 WPF 실제 렌더이며 실게임 캡처가 아니다.

## 패키지

| 파일 | bytes | SHA-256 |
|---|---:|---|
| AMS2-League-Overlay-0.4.1-Setup.exe | 51,261,045 | `9b24e8963724fca1357da440cebe86f09ae2afe7b3d91f9ac2b7fa8965f171b5` |
| AMS2-League-Overlay-0.4.1-win-x64.zip | 72,356,488 | `185db84c5a211ae3f124d463fd50502394d1e05845375755229244bc73d039e4` |

`artifacts/`에 설치판·ZIP·manifest·SHA256SUMS를 생성했다. win-x64, self-contained이며 별도 .NET 설치가 필요 없다.

## 하지 않은 것과 남은 제한

- 서버·DB·계정·Compact 계약·관측 cadence·전송량 정책 변경 0건. 기록/설정 삭제, 게임 조작, 실제 사용자 설치본 교체 0건.
- 요청 없는 별도 사용자 기능/입력란/메뉴 추가 0건. 업데이트 진행/결과 한 줄은 REQ-UPD-03에 필요한 표시다.
- 공개 게시, 두 독립 PC witness 검증, 실제 주행 중 페널티/표시/FPS 검수는 미실행이다.
- 이전의 세션 종료 추가 capture, 세션 전환 분류 혼입, cadence 손실과 용량 gate 재측정은 범위 밖 후속 과제로 유지한다.
- 최종 판정: 구현·자동/화면/별도 설치 시험은 PASS. **실게임 운영 QA는 PARTIAL**로 남긴다.

## 재검증 명령

```powershell
# .NET 8 SDK 경로를 지정할 수도 있다.
.\scripts\verify.ps1 -DotnetExecutable dotnet -CaptureLayouts

# 실제 GitHub 파일을 다운로드·검증만 하며 설치하지 않는다.
dotnet run --project tests/AMS2LeagueClient.Tests -c Release --no-build -- --verify-live-update work/github-update-proof

# 공식 v0.4.0 ZIP을 준비한 후 새 격리 폴더에서 설치/재실행을 검사한다.
.\scripts\verify-update-install.ps1 -BaselineZip <v0.4.0-ZIP-경로> -BaselineSha256 393f1b7f8af66040a6f5a20a4df75c474a2b9d86b62a86b9693b8034a588ac3b
```

구현 근거: [GitHub Release API](https://docs.github.com/en/rest/releases/releases#get-the-latest-release), [Inno Setup 명령행 옵션](https://jrsoftware.org/ishelp/topic_setupcmdline.htm), [포터블 설치 옵션](https://jrsoftware.org/ishelp/topic_setup_uninstallable.htm).

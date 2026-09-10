<BEGIN_PROMPT>
# 작업: 자동 업데이터 신뢰 체계 + 코드 구조 감사 (측정과 보고만, 코드 변경 없음)

## 0. 먼저 읽을 것
1. `AGENTS.md` — "정책에 영향을 주는 변경은 먼저 측정 결과와 옵션을 보고하고 사용자 결정을 받는다"
2. 자동 업데이트 코드 전체: GitHub Latest 조회, 다운로드, 크기/SHA-256 검증, 설치 실행, 재실행. (`src/AMS2LeagueClient/` 아래에서 Update/Updater 이름으로 찾는다)
3. `scripts/publish-github-release.ps1`, `scripts/build-release.ps1`
4. `src/AMS2LeagueClient/Runtime/PlayerOverlayCoordinator.cs`, `src/AMS2LeagueClient.Core/Presentation/OverlayViewModel.cs`, `src/AMS2LeagueClient/Presentation/ClientStatusWindow.xaml.cs`, `src/AMS2LeagueClient/Overlay/OverlayWindow.xaml.cs`, `src/AMS2LeagueClient.Core/Presentation/OverlayLayoutProfile.cs`

## 1. 시작 절차
- `preflight.ps1` 실행.

## 2. 목표
두 가지를 **측정해서 보고**한다. 이번 작업에서 코드는 바꾸지 않는다. 결과를 보고 사용자가 다음 작업을 정한다.

## 3. MUST — 파트 A: 자동 업데이터
1. 현재 흐름을 순서도(텍스트)로 적는다: 어디서 버전을 읽고, 어디서 SHA-256 기대값을 얻고, 무엇과 비교하고, 실패 시 어떻게 되는지.
2. 다음 질문에 코드 근거(파일:줄)로 답한다:
   - SHA-256 기대값은 어디서 오는가? (릴리스 본문? 별도 asset? 코드 내장?) 설치 파일과 같은 출처면 "전송 오류 방어만 되고 출처 위조 방어는 안 됨"으로 분류한다.
   - 다운로드 URL이 `choi3724/AMS2KRLeague` 릴리스로 제한되는 검사가 문자열 비교인지 URL 파싱인지. 리디렉션은 차단되는가.
   - 다운로드한 파일을 실행하기 전에 실행 파일 서명(Authenticode) 확인을 시도하는가.
   - GitHub 계정이 탈취되어 악성 Setup.exe가 Latest로 올라오면 현재 클라이언트가 이를 막을 수 있는가. (예/아니오 + 근거)
3. 개선 옵션 3개를 각각 "구현 난이도 / 사용자 체감 / 막는 위협"으로 표를 만든다:
   - 옵션 1: 릴리스에 서명된 매니페스트(ed25519, minisign 형식) 추가. 개인키는 사용자 PC 밖(오프라인) 보관, 공개키는 클라이언트 내장. 클라이언트는 매니페스트 서명 검증 후에만 설치.
   - 옵션 2: 코드 서명 인증서 구매 + Authenticode 검증 강제.
   - 옵션 3: 릴리스 채널 분리 — `beta` 태그는 자동 업데이트 대상에서 제외하고 사용자가 상태창에서 "beta 받기"를 켠 사람만 받음. 현재 "모든 릴리스는 Latest" 정책과 충돌하므로 사용자 결정 필요.
4. 각 옵션이 AGENTS.md 절대 규칙 2번("모든 Release는 Latest")과 충돌하는지 명시한다.

## 3. MUST — 파트 B: 코드 구조
1. 다음을 측정해 표로 만든다 (도구를 만들어 측정, 추측 금지): `src/` 아래 모든 `.cs`/`.xaml.cs` 파일의 줄 수 상위 15개, 각 파일의 public 메서드 수, 가장 긴 메서드 5개(파일:메서드:줄 수).
2. `PlayerOverlayCoordinator`, `OverlayViewModel`, `ClientStatusWindow.xaml.cs`, `OverlayWindow.xaml.cs` 각각에 대해: 담당하는 책임을 목록으로 적고(예: SHM 폴링, 세션 판정, 업로드 스케줄, VR 출력, 미리보기, 레이아웃 저장…), 5개를 넘으면 분리 후보로 표시한다.
3. `overlay-layout.json` 호환 처리: 버전별 마이그레이션 로직(예: 타워 폭 520→648 확장)이 어디에 있고 몇 개인지. 스키마 버전 필드가 있는지. 없으면 "스키마 버전 도입" 을 권고 항목에 넣는다.
4. 0.4.0~0.7.0 사이에 추가된 기능(VR, 계기판, 페달 게이지, 미리보기, 디자인 선택)이 어느 파일에 들어갔는지 `git log --stat v0.4.0..HEAD -- src/` 로 집계해 파일별 변경 줄 수 상위 10개를 적는다.
5. 위 측정을 근거로 리팩터링 후보를 우선순위 3개까지만 제안한다. 각 후보에 "왜 지금", "안 하면 생기는 문제", "예상 변경 범위(파일 수)"를 적는다.

## 4. MUST NOT
- commit / tag / push / Release 금지.
- `src/`, `tests/`, `scripts/`, `installer/`, 문서를 수정하지 않는다. 측정 도구는 `work/audit-2026-09-10/` 아래에만 만들고, 이 폴더가 `.gitignore`에 포함되는지 확인한다(안 되면 보고).
- 추측으로 답하지 않는다. 확인 못 한 항목은 "확인 못 함 + 이유".
- 개선안을 바로 구현하지 않는다.

## 5. 범위 밖
- 실제 리팩터링, 서명 키 생성, 릴리스 정책 변경.

## 6. 완료 조건
- [ ] 파트 A 질문 4개에 파일:줄 근거로 답함
- [ ] 파트 A 옵션 표 3개
- [ ] 파트 B 측정표 4개 (줄 수, 책임 목록, 레이아웃 마이그레이션, 변경 집계)
- [ ] 리팩터링 후보 ≤3개
- [ ] `verify.ps1` GATE: PASS (코드 변경 없으므로 작업 전과 동일해야 함)
- [ ] 보고서: `docs/reports/2026-09-10_updater-architecture-audit.md`. 마지막 절 "사용자 결정이 필요한 항목"에 옵션 1/2/3과 리팩터링 후보를 선택지로 정리.

## 7. 보고
한국어. 숫자는 측정 도구 출력을 그대로 붙인다.
<END_PROMPT>

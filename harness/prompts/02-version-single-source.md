<BEGIN_PROMPT>
# 작업: 버전 문자열 단일 출처화 (Directory.Build.props 하나만 고치면 되게)

## 0. 먼저 읽을 것
1. `AGENTS.md`
2. `VERSIONING.md` — "버전을 올릴 때 함께 변경하는 항목" 목록
3. `Directory.Build.props`, `installer/AMS2LeagueOverlay.iss`, `scripts/build-release.ps1`, `scripts/publish-github-release.ps1`
4. `ClientStatusViewModel`(01번 보고서에서 확인한 경로)과 자동 업데이터가 현재 버전을 읽는 코드
5. `harness/scripts/version-config.json`
6. `docs/reports/2026-09-10_harness-install.md`

## 1. 시작 절차
- `preflight.ps1` 실행, 출력 보고.
- 기준 커밋: 01번 작업 완료 후 HEAD.

## 2. 목표
현재 버전은 8곳에 흩어져 있다. 이 중 **코드와 빌드 산출물이 읽는 곳**은 전부 `Directory.Build.props` 하나에서 자동으로 오게 만든다. 사람이 읽는 문서(README, CHANGELOG, VERSIONING)는 손으로 고치되 `check-versions.ps1`가 불일치를 잡는다.

## 3. MUST
1. `ClientStatusViewModel`(및 다른 어떤 C# 파일이든)에 하드코딩된 버전 문자열을 제거하고 어셈블리의 `InformationalVersion` 또는 `FileVersion`에서 읽도록 바꾼다. 자동 업데이터의 버전 비교도 같은 소스를 쓰는지 확인하고, 다르면 통일한다.
2. `scripts/build-release.ps1`의 기본 `-Version`을 하드코딩 대신 `Directory.Build.props`에서 읽도록 바꾼다. 명시적 `-Version` 인자는 계속 허용하되, props와 다르면 경고 후 중단한다.
3. `installer/AMS2LeagueOverlay.iss`의 버전을 `build-release.ps1`이 빌드 시 `/D` 정의로 넘기거나, 생성된 `.iss`에 치환해 넣도록 바꾼다. `.iss` 파일 안에 버전 리터럴이 남지 않게 한다.
4. `VERSIONING.md`의 "함께 변경하는 항목" 목록을 실제 남은 항목(props + README + CHANGELOG)으로 갱신한다.
5. `harness/scripts/version-config.json`을 새 구조에 맞게 갱신한다. 제거된 하드코딩 위치는 "있으면 FAIL" 검사로 바꾼다(즉 그 파일에서 semver 리터럴이 발견되면 FAIL).
6. 이 변경을 검증하는 테스트를 추가한다: 상태창 ViewModel이 표시하는 버전 == 어셈블리 InformationalVersion.
7. `verify.ps1` GATE: PASS.

## 4. MUST NOT
- commit / tag / push / Release 금지.
- 실제 버전 번호를 올리지 않는다. 0.7.0 그대로.
- 자동 업데이터의 다운로드·검증·설치 로직은 건드리지 않는다. 버전 "읽는" 부분만.
- Compact 프로토콜, 업로드 gate, 서버 계약 변경 금지.
- reset/clean/force-checkout 금지.

## 5. 범위 밖
- 릴리스 채널(beta/stable) 분리는 05번 작업의 사용자 결정 항목이다.

## 6. 완료 조건
- [ ] `grep -rn "0\.7\.0" src/` 결과가 0건 (props 제외)
- [ ] `.iss`와 `build-release.ps1`에 semver 리터럴 없음
- [ ] 새 테스트가 Client 테스트 스위트에 포함되고 통과
- [ ] `check-versions.ps1` PASS
- [ ] `verify.ps1` GATE: PASS
- [ ] 보고서: `docs/reports/2026-09-10_version-single-source.md`

## 7. 보고
한국어. 제거한 하드코딩 위치를 파일:줄 목록으로 적는다. 자동 업데이터 버전 비교 소스가 무엇이었는지, 바뀌었는지 명시한다.
<END_PROMPT>

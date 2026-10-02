# 2026-10-02 오버레이 문구·배치·타워 표시 검증

## 기준선

- 작업 브랜치 `claude/jolly-hypatia-euwyty`, HEAD `7d7d997`, 공개 `main`은 변경하지 않았다.
- 기존 0.8.4 Windows 수정 4개 파일은 보존했다. 작업 전 Windows 검증은 Client 192/192, Activity 111/111이었다.
- 기존 주행 HUD, 레이스 컨트롤 알림 전환, 순위/상태/페널티, 설정 저장 동작을 보호 대상으로 삼았다.

## 요구사항

REQ-DRIVING-SESSION-LABEL-090 DONE
- 계기판 오른쪽 아래에 세션 정보와 같은 `남은 시간 14:03` 또는 `남은 랩 7 / 10`을 표시한다.
- 값/화면 증거: `tests/AMS2LeagueClient.Tests/DrivingDashboardTests.cs`, `../validation-20261002-ui-layout/combined-dashboard*.png`.

REQ-RACE-CONTROL-FIT-090 DONE
- 카드의 짧은 축에 맞춰 글자 크기를 제한하고 세로 카드에서는 위쪽부터 배치한다. 288×66, 416×152, 600×55 및 세로 카드의 글자 범위를 확인했다.
- 증거: `tests/AMS2LeagueClient.Tests/Program.cs`의 `RaceControlReflowsWithoutClipping`, `../validation-20261002-ui-layout/race-control-fit-*.png`.

REQ-DRIVING-FONT-PREVIEW-090 DONE
- 속도계와 기어 글꼴 선택 바로 아래에 실제 선택 글꼴의 예시를 표시하고 선택 변경 시 즉시 바꾼다.
- 증거: `tests/AMS2LeagueClient.Tests/DrivingHudTests.cs`, `../validation-20261002-ui-layout/driving-settings-menu.png`.

REQ-TOWER-LEADER-GAP-090 DONE
- 레이스에서만 순위 1위 차량의 최고 랩과 각 차량의 최고 랩 차이를 표시한다. 1위 `0.000`, 뒤차 `+3.375`, 1위보다 빠른 최고 랩 `-0.500`; 기록이 없으면 기존 주행 상태나 `—`를 쓴다. 연습·예선·테스트·타임어택은 기존 최고 랩 표시를 유지한다.
- 기본형과 레이싱형의 제목/값을 확인했다. 증거: `tests/AMS2LeagueClient.Tests/Program.cs`의 `TowerShowsLeaderBestLapDifference`, `../validation-20261002-tower-final2/{leader-best-gap,practice-best-lap}-{legacy,racing}.png`.
- Shared Memory에는 모든 차량의 실시간 1위 대비 초 단위 간격이 없다. 따라서 이 값은 실시간 레이스 간격이 아닌 **최고 랩 차이**로 표시한다.

## 요청하지 않은 변경

- 없음. 기존 0.8.4 작업 중이던 네 파일의 수정은 유지했다.

## 승인 대기

- 공개 커밋·태그·push·GitHub Release는 요청받지 않아 실행하지 않았다.

## 발견했지만 고치지 않은 문제

- NuGet 취약성 데이터 서버 접근 실패로 빌드에 `NU1900` 경고가 2건 남았다. 코드 빌드 오류는 0건이다.
- 실제 AMS2 주행, VR, 물리 트리플 모니터 화면은 이번 검증 환경에서 재현하지 못했다.

## 최종 게이트

- Windows `scripts/verify.ps1`: Client 193/193, Activity 111/111, 종료 코드 0.
- `git diff --check`: 통과.
- 휴대용 시험 ZIP: `../../outputs/0.8.4-ui-preview-20261002/AMS2-League-Overlay-0.8.4-ui-preview-win-x64.zip`. 폴더/ZIP 공개 패키지 감사 각각 481파일, 금지 파일 0건.
- 실제 게임 화면 검증은 사용자가 시험 빌드를 주행 중 실행해야 남은 게이트다.

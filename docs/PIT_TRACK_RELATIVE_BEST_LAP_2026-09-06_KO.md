# 피트/트랙 전후방 분리 · 최고 랩 타워 · 아웃랩 — 2026-09-06

## 요청과 결과

- 내가 피트에 있으면 피트 차량끼리, 트랙에 있으면 트랙 차량끼리 전후방 탐지.
- 첫 랩 `인터벌 랩` 문구를 `아웃랩`으로 변경.
- 순위 타워의 시간은 실시간 경과 시간이 아니라 참가자별 최고 랩 기록으로 변경. 내 현재·섹터 타임 패널은 별도 유지.

수정본: `src/AMS2LeagueClient/bin/PitBestLapHotfix/AMS2LeagueClient.exe`.
현재 버전 번호 0.3.1은 유지했다. 자동으로 실행/교체하거나 릴리스하지 않았다.

## 기존 거리 표시가 달랐던 원인

기본 알고리즘은 월드 직선거리나 순위 차이가 아니라 `CurrentLapDistance`의 차이를 트랙 길이로 순환시켜 가장 작은 전방/후방 값을 고른다. 피트/트랙 영역 구분이 없었고, `InGarage` 차량은 무조건 제외했다. 랩 거리가 0인 첫 랩 참가자도 정상 트랙 거리 후보에서 제외했다.

월드 좌표 보조 계산은 자기 첫 랩 거리가 미설정이거나 시작선 근처 150m 이내에서 일부 참가자의 진행거리가 미설정일 때만 작동했다. 따라서 피트가 트랙 진행거리상 시작점 근처가 아니면 가까운 피트 차량을 제외하고 주행 트랙의 다른 참가자를 선택할 수 있었다.

기존 빌드를 그대로 로드한 재현 fixture:

| 조건 | 값 |
|---|---|
| 나 / 피트 앞차 / 피트 뒷차 | 모두 `InPit`, 첫 랩 |
| 내 트랙 진행거리 | 3,800m / 트랙 길이 4,000m |
| 실제 피트 앞뒤 월드 거리 | 10m / 20m |
| 피트 앞뒤 랩 거리 | 0 / 0 |
| 기존 선택 결과 | 트랙의 다른 차량 142m / 34m |
| 수정 후 같은 조건 | 피트 앞차 `~10m` / 피트 뒷차 `~20m` |

이는 제공된 화면과 같은 유형을 재현한 **인공 fixture**다. 스크린샷 순간의 SHM을 저장한 것은 아니므로 그 장면의 모든 raw 값을 확정한 것으로 보지 않는다. 재현 결과는 Git 제외 `work/pit-best-lap-audit/before-fixture.json`에 있다.

15:04:32–35 KST에는 실제 SHM을 읽기 전용으로 20회 확인했다(읽기 성공 20, drop 0). 이미 `Practice / InGamePaused / PitMode.None`인 다른 장면이었다. 이때 기존 판정 앞차 101m는 월드 거리 99.62m, 뒷차 155m는 월드 거리 153.27m였다. 도로 진행거리 방식 자체와 월드 직선거리는 다르다는 확인이며, 피트 문제의 실게임 재현 PASS는 아니다. 요약 원본: `work/pit-best-lap-audit/live-readonly-150432.json`.

## 현재 거리 계산 규칙

1. SHM participant `PitMode`로 같은 영역의 후보만 먼저 선택한다.
   - `None`: 트랙.
   - `DrivingIntoPits`, `InPit`, `DrivingOutOfPits`, `InGarage`, `DrivingOutOfGarage`: 피트.
   - 알 수 없는 값: 추측하지 않음. 피트 예약 `PitSchedule`만으로는 영역을 바꾸지 않음.
2. 트랙에서는 기존 트랙 진행거리 순환 계산을 유지한다. 피트 차량은 섞지 않는다. 출발 전 진행거리 미설정 보조 계산도 트랙 차량끼리만 한다.
3. 피트에서는 첫 랩/진행거리 초기화 여부에 상관없이 월드 좌표 보조 계산을 사용한다. 자기 차량 방향에 대한 전후 투영으로 앞뒤를 나누고 각 방향의 가까운 차량을 고른다. 거리 자체는 3D 좌표 간 직선거리이며 `~Nm`로 구별한다. 범퍼 간격은 아니다.
4. 차고 차량도 피트 후보에 포함한다. 차고에 비스듬히 주차된 차량의 방향이 다르다는 이유로 제외하지 않는다. 순위가 0인 활동 참가자도 탐지하며 순위만 `P—`로 표시한다. Safety Car는 기존 역할 판정으로 제외한다.
5. 같은 영역의 유효 후보가 없으면 차량 없음/`—`다. 반대 영역의 차량으로 대체하지 않는다. 피트에서는 트랙 누적거리로 LAP 차이를 생성하지 않는다.
6. 시간차는 이전과 동일하게 실제 선택 상대가 게임 split의 순위상 상대와 일치하며 게임 값이 유효할 때만 표시한다. 미터를 시간으로 환산하지 않는다. 거리 원본이 월드↔트랙으로 바뀔 때 증감 화살표 기준은 초기화한다.
7. 공통 판정기를 사용하는 Battle 이벤트도 검토했다. 피트의 가까운 차량을 경합으로 오인하는 이벤트는 발생시키지 않는다.

### 한계

월드 보조 계산은 가까운 차량용이며 150m 이내, 좌우 ±15m, 높이 ±6m, 전후 투영 0.5m 초과 조건을 유지한다. 좌표 미설정/NaN/Infinity, 나란히 겹친 위치는 앞뒤를 단정하지 않는다. 피트 전 구간의 곡선/분기 topology를 복원하는 시스템은 아니다. PitMode 전환 시점의 실게임 정확도와 곡선 피트는 후속 실게임 검증이 필요하다. 게임이 잘못된 mode나 위치를 제공하면 화면만 보고 보정하지 않는다.

## 타워 표시 규칙

- 원본은 각 참가자 `BestLapTime`(SHM `mFastestLapTimes[index]`) 하나다. 양의 유한 값만 사용한다.
- 내 root current/best, 상대 섹터 합계, 직전 랩, 관측 타이머로 최고 기록을 대신 만들지 않는다.
- 유효한 최고 기록이 없고 활동 중 첫 랩(`Racing`, `lapsCompleted=0`, `currentLap<=1`, Race/Practice/Qualify/Test)이면 `아웃랩`, 그 외는 `--`다. 최고 기록이 이미 있으면 기록을 우선 표시한다.
- Finish/RET/DNF/DSQ는 별도 상태 칸에 유지한다. 완료 후에도 최고 기록은 그대로 남으며 live timer가 증가하지 않는다.
- 현재 랩 무효 배지는 현재 랩의 상태를 나타낸다. 이미 유효한 최고 기록 숫자를 동결하거나 빨갛게 바꾸지 않는다. 개인 현재·섹터 패널의 기존 무효 처리/타이머 동결은 유지한다.
- 기존 타워 바인딩 이름 `CurrentTime`은 호환을 위해 유지했지만 내용은 이제 최고 랩이다. 타워 실시간 섹터 합산/직전 랩 대체 코드를 제거하고, 실행 클라이언트의 타워용 관측 랩 시계 갱신도 제거했다. 독립 진단용 `ParticipantLapClock`과 그 검증은 유지했다.

## 검증

| 항목 | 결과 |
|---|---|
| Release 전체 빌드 | 경고 0 / 오류 0 |
| Client 전체 + WPF 별도 렌더링 | 94/94 PASS |
| Activity 전체 | 102/102 PASS |
| 3개 세션 × 자기/상대 피트 모드 5종 × 첫/이후 랩 | PASS |
| 피트↔트랙 분리, 가까운 반대 영역 차량 제외 | PASS |
| 차고·순위 미설정 참가자, Safety Car 제외 | PASS |
| 진행거리 0/track length 없음, 잘못된 좌표/영역 | PASS |
| 원본 교체 시 증감 화살표 초기화, 피트 Battle 억제 | PASS |
| 참가자별 최고 기록, 잘못된 best fallback, 기록 갱신 | PASS |
| 현재 랩 무효와 과거 최고 기록 분리 | PASS |
| 기존 대기/애니메이션/글꼴/개인 타임 관련 테스트 | PASS |
| 피트 진입/주행/출구의 수정본 실게임 확인 | NOT RUN |

사용자 요청으로 타워 표시 규칙이 바뀌었으므로 종전 current timing을 기대하던 테스트는 best lap과 별도 개인 타임을 검증하도록 갱신했다. 단순히 실패 테스트를 제거한 것이 아니다.

시각 검수한 fixture 이미지: `work/pit-best-lap-hotfix-layouts/participant-best-lap-tower.png`, `opening-out-lap-tower.png`, `pit-relative-only.png`. 실게임 캡처와 구분한다.

```powershell
.\work\dotnet8\dotnet.exe build AMS2KRLeague.sln -c Release --no-restore -p:OutputPath=bin/PitBestLapHotfix/
.\work\dotnet8\dotnet.exe tests/AMS2LeagueClient.Tests/bin/PitBestLapHotfix/AMS2LeagueClient.Tests.dll --capture-layout work/pit-best-lap-hotfix-layouts
.\work\dotnet8\dotnet.exe tests/AMS2LeagueActivity.Tests/bin/PitBestLapHotfix/AMS2LeagueActivity.Tests.dll
```

게임 입력·실행본 강제 종료·서버 배포·Compact/API/DB 변경·commit/tag/release는 하지 않았다. 이전 미커밋 변경사항은 유지했다. Ponytail 원칙에 따라 기존 공통 근접 판정기와 월드 좌표 계산, 테스트 도구를 재사용했다. 별도 레이더 엔진이나 의존성은 추가하지 않았다.

다음 확인: 녹화/업로드가 끝난 뒤 기존 오버레이를 정상 종료하고 수정본을 실행하여 피트에서의 앞뒤 이름/거리, 피트 출구 전후 후보 교체, 최고 기록 갱신을 확인한다. 이전 `SessionLabelHotfix` 실행 파일을 다시 실행하면 이번 수정은 반영되지 않는다.

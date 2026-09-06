# 출발선 통과 전 전후방 거리 표시 수정 — 2026-09-06

## 원인과 확인 범위

기존 `TrackProximityResolver`는 `CurrentLapDistance`만 사용했다. 여러 참가자의 랩 거리가 0이면 모두 같은 트랙 위치로 간주하고, 동률의 가장 작은 participant index를 앞/뒤에 각각 선택했다. 따라서 같은 P1 / 같은 이름 / 양쪽 `0m`가 만들어질 수 있었다. 실제 트랙 위치가 없을 때 ViewModel이 순위상 앞뒤 참가자를 대신 넣는 경로도 있었다.

설치된 AMS2 공식 `Support/SharedMemory/AMS2_SharedMemoryExampleApp/SharedMemory.h`의 `mCurrentLapDistance`는 `UNSET = 0.0f`로 명시돼 있다. 제공된 출발 장면과 일치하는 all-zero raw fixture로 문제를 재현했다. 사용자 스크린샷 순간의 SHM 원본을 확보한 것은 아니므로, 그 순간의 모든 raw 값까지 실측했다고 주장하지 않는다.

이미 파싱 중인 participant `WorldPosition`과 `Orientation`을 활용했다. 11:47:43 KST의 읽기 전용 SHM 관측은 출발선을 이미 지난 주행 장면이며, 74개 snapshot을 확보했다. 0.1m 이상 이동하고 속도 5m/s를 넘는 연속 차량 관측 1,752개에서 실제 XZ 이동 방향과 `(-sin(yaw), -cos(yaw))`의 내적은 평균 0.9993403, 최솟값 0.9829035였다. 방향 축 부호는 이 실측으로 확인했다.

관측 원본은 Git 제외 경로 `work/ai-timing-probe/bin/PrestartProbe/observed-20260906-024743.jsonl`이다. 수정 후 11:59:36의 추가 읽기는 SHM read 성공 55회였지만 유효 local participant를 얻지 못해 모델 기록은 0건이었다. 이를 실게임 PASS로 계산하지 않는다.

## 수정

- 첫 랩의 랩 진행거리가 미설정이면 가까운 차량의 월드 좌표와 방향으로 앞/뒤를 구분한다. 속도나 출발선 통과를 활성화 조건으로 요구하지 않으므로 정지 그리드에서도 계산한다.
- 월드 좌표 기반 보조 거리는 `~10m`처럼 표시한다. 이는 실제 좌표 사이의 직선 거리이며 트랙을 따라 측정한 거리와 구별한다.
- 첫 랩 시작점 근처에서 일부 차량만 랩 거리 초기화를 마친 경우에도 보조 계산을 유지한다. 이후 유효한 랩 진행거리가 준비되면 기존 트랙 거리 계산으로 돌아간다.
- 보조 계산 범위는 150m, 좌우 폭은 ±15m, 높이 차이는 ±6m, 방향 차이는 60도 이내다. 전후 투영 거리 0.5m 이하는 나란히 있거나 겹친 경우로 보고 앞/뒤를 단정하지 않는다.
- 유효 좌표가 없거나 후보가 모호하면 `—`/차량 없음이다. 순위 참가자로 대체하거나 미설정 값을 실제 `0m`로 표시하지 않는다.
- Safety Car 제외는 기존 League Classification 경로를 유지한다. 미활성/차고 차량은 후보에서 제외하며 그리드 보조 계산은 NotStarted/Racing 차량만 사용한다.
- 월드 거리로 `LAP N`이나 시간차를 추정하지 않는다. 시간차는 기존처럼 물리적 상대가 게임 split의 순위상 상대와 일치하고 유효한 게임 값이 있을 때만 표시한다. 원래 위치의 LAP/시간차 UI는 유지했다.
- 기존 거리 증감 색상/안정화는 그대로 재사용하므로 출발선 이전에도 이동에 반응한다.

Ponytail 원칙에 따라 기존 공통 판정기에 제한된 보조 경로만 추가했다. 새 캡처, 트랙 맵 매칭 엔진, 타이머, 의존성은 추가하지 않았다. 보조 계산은 근처 그리드용이며, 15m 이내의 인접 평행 도로나 곡선 트랙의 연결 관계까지 식별하는 기능은 아니다. 그 구분이 실제로 필요한 경우에만 트랙 topology 기반 확장을 검토한다.

## 검증

| 항목 | 결과 |
|---|---|
| Release build | 경고 0 / 오류 0 |
| Client 전체 | 88/88 PASS |
| Activity 전체 | 97/97 PASS |
| 출발 전 모든 lap distance=0, 서로 다른 실제 앞/뒤 선택 | PASS |
| 출발선 이전 차량 이동에 거리/색상 갱신 | PASS |
| 일부 차량만 선 통과 → 전체 progress 준비 → native 복귀 | PASS |
| 순위 재정렬, Safety Car 제외, split 상대 일치 여부 | PASS |
| 좌표 미설정/NaN/Infinity, 겹침/나란히/원거리/높이/역방향 | PASS |
| yaw 회전, track length 없음, inactive/RET/차고 | PASS |
| 기존 LAP·시간차·이벤트 플래시·글꼴·첫 랩 무효 억제 | PASS |
| 실게임 새 레이스 출발 장면 재검수 | NOT RUN |

게임 입력이나 세션 재시작은 하지 않았다. 자동 테스트는 실제 SHM parser → 분류 → production ViewModel/거리 판정 경로를 사용한다.

## 실행 및 다음 확인

```powershell
.\work\dotnet8\dotnet.exe build AMS2KRLeague.sln -c Release --no-restore -p:OutputPath=bin/PrestartProximityHotfix/
.\work\dotnet8\dotnet.exe tests/AMS2LeagueClient.Tests/bin/PrestartProximityHotfix/AMS2LeagueClient.Tests.dll
.\work\dotnet8\dotnet.exe tests/AMS2LeagueActivity.Tests/bin/PrestartProximityHotfix/AMS2LeagueActivity.Tests.dll
```

수정본: `src/AMS2LeagueClient/bin/PrestartProximityHotfix/AMS2LeagueClient.exe`.

실행 중인 오버레이를 종료/교체하지 않았다. 이 빌드로 재실행한 뒤 새로운 레이스에서 출발선 이전의 앞뒤 이름/거리 갱신, 선 통과 때 `~Nm`에서 일반 `Nm`로의 전환을 확인하면 된다. 게임이 split을 제공하지 않는 구간의 시간차 `—`는 의도된 표시다.

이번 변경 파일은 `TrackProgressDistance.cs`, `OverlayViewModel.cs`, Client 테스트와 이 문서다. Git 제외 진단 probe에 근접 데이터 관측 옵션만 추가했다. 기존 미커밋 UI 수정은 모두 보존·포함했다. 버전 변경/commit/tag/push/Release는 하지 않았고 서버/API/DB/Compact/업로드/운영 데이터도 변경하지 않았다.

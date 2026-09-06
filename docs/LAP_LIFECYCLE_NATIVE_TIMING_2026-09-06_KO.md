# 아웃랩 → 계측 주행 → 인정 기록 표시 수정

작성: 2026-09-06 KST

## 결과

연습/예선의 첫 계측 시작을 랩 번호 증가만으로 기다리지 않도록 기존 참가자별 표시 판정을 수정했다. 표기는 **아웃랩**이며, `오픈랩`이 아니다.

| 상황 | 순위 타워 시간 칸 |
|---|---|
| 차고/피트 대기, 인정 기록 없음 | `--`, 별도 `PIT` 배지 |
| 출차 후 아직 계측 시작 전 | `아웃랩` |
| 계측 시작, 아직 인정된 최고 기록 없음 | `랩 타임 주행 중` |
| AMS2 participant BestLapTime이 유효한 양수 | 해당 최고 기록 |

이전에 사용자가 선택한 “최고 기록 유지, 기록이 없을 때만 상태 문구 표시”는 유지했다. 따라서 이미 인정된 최고 기록이 있으면 다음 출차/주행 중에도 그 기록을 표시한다. 레이스 첫 바퀴/두 번째 바퀴 표시 규칙은 이번에 바꾸지 않았다.

## 실제 데이터에서 확인한 원인

실행 중인 `LapStartHotfix` 후보와 AMS2를 종료하거나 조작하지 않고, 16:07:49–16:08:49 KST의 SHM 표시 관련 데이터를 읽기 전용으로 관측했다.

1. 첫 계측 시작 시 `CurrentLap=1`, `LapsCompleted=0`이 그대로인 참가자가 있었다. 이때 participant S1은 `-1`에서 작은 실제 시간으로 바뀌었다. 이전 코드는 랩 카운터 증가/기존 양수 S1의 감소를 주로 기다려 전환을 놓쳤다.
2. 아웃랩 동안 `RaceState=NotStarted`였다가 계측 시작 시 `Racing`으로 바뀌는 참가자가 있었다. 기존 이전 상태 조건과 표시 순서가 이를 놓쳐 `--` 또는 늦은 전환을 만들었다.
3. 한 사례의 출발선 직전 거리 `3999.8115m`는 SHM trackLength `3999.7273m`를 조금 초과했다. 이 경우 기존 기하학적 wrap 조건은 성립하지 않았다. 출발선 직후 `4.410772m`, S1 `0.07910156`, 랩 카운터 `1/0`이 확인됐다. 임의 거리 보정으로 시간을 만들지 않고 native timing 시작 신호를 사용했다.
4. 기존 세션 추적기의 generation은 일시정지/메뉴 전환에도 증가한다. 실행 로그에서도 Playing → Paused → Playing에 generation이 바뀌었다. 이때 표시 추적기가 출차 이력까지 지워 버렸다.

## 변경 범위

- `InvalidLapDisplayTracker.cs`: 첫 섹터에서 participant S1이 미제공 → 유효 값으로 바뀌는 전환을 계측 시작으로 인정한다. 이후 섹터에서 누락 값이 복구되는 경우에는 이 조건을 적용하지 않는다.
- 같은 추적기에서 `NotStarted` 아웃랩, 관측 도중 연결한 첫 아웃랩을 처리하고, Playing/Paused/MenuTimeTicking 사이의 전환으로 출차 이력이 소실되지 않도록 했다. 실제 세션/트랙 변경, 카운터 되돌림 등 기존 초기화는 유지한다.
- `OverlayViewModel.cs`: 유효 최고 기록 우선 표시를 유지하되, 일반적인 비주행 fallback보다 아웃랩 판정을 먼저 적용한다.
- Client 테스트 3개와 네 단계 화면 fixture를 추가했다. 기존 추적기와 fixture 도구를 재사용했으며 새 의존성은 없다.

공용 세션 추적기, Compact protocol/schema, 업로드, 서버 API/DB는 이번 수정에서 변경하지 않았다. 작업 시작 전에 존재하던 다른 미커밋 변경은 보존했다.

## 검증

| 항목 | 결과 |
|---|---|
| Release solution build | PASS, warning 0 / error 0 |
| Client 전체 테스트 | PASS, 102/102 |
| Activity 전체 테스트 | PASS, 102/102 |
| 실제 표시 데이터 재생 | PASS, 946 snapshots / 검사 대상 27,309행 관측 |
| 수정 전 표시 불일치 | 5,943행 관측 — 반복 관측 수이며 참가자 수가 아님 |
| 수정 후 표시 불일치 | 0 |
| 아웃랩 → 계측 주행 전환 | 10회 |
| 계측 주행 → 인정된 최고 기록 전환 | 12회 |
| WPF 네 단계 렌더 | PASS, PIT / 아웃랩 / 랩 타임 주행 중 / 0:55.235 확인 |
| 수정본의 실제 인게임 화면 검수 | NOT RUN — 실행 중인 기존 오버레이를 교체하지 않음 |

실제 데이터 재생은 저장한 **표시 필드 projection**을 기존 fixture/production 판정으로 재생한 것이다. 전체 raw SHM, 전체 세션, 공식 경기 결과 또는 서버 무결성 검증이 아니다. 저장하지 않은 메타데이터는 fixture 기본값을 사용한다. 이 관측의 sequence-drop 진단 값 19는 읽기 진단이며 서버 데이터 손실로 판정하지 않는다.

검증 명령(저장소 루트):

```powershell
.\work\dotnet8\dotnet.exe build AMS2KRLeague.sln -c Release --no-restore -p:OutputPath=bin/LapLifecycleHotfix/
.\work\dotnet8\dotnet.exe tests/AMS2LeagueClient.Tests/bin/LapLifecycleHotfix/AMS2LeagueClient.Tests.dll --capture-layout work/lap-lifecycle-hotfix-layouts
.\work\dotnet8\dotnet.exe tests/AMS2LeagueActivity.Tests/bin/LapLifecycleHotfix/AMS2LeagueActivity.Tests.dll
.\work\dotnet8\dotnet.exe work/ai-timing-probe/bin/LapLifecycleHotfix/ai-timing-probe.dll --replay-lap-phase work/ai-timing-probe/bin/LapLifecycleHotfix/observed-20260906-070749.jsonl
```

로컬 관측/재생 파일과 렌더 이미지는 ignored `work/`에 있으며 공개 배포하지 않는다.

- 관측 SHA256: `2C6B55DBA0B515129FB38F27E03D976EE4D7B25EB03F0B927BA19F62646D0134`
- 수정 Core DLL SHA256: `2770ACDF7FB318DF066629CE40C010E5F06D9C570E7C5D65359CA74F51D7038D`
- 렌더: `work/lap-lifecycle-hotfix-layouts/lap-lifecycle-four-stages.png`

## 실행/후속 확인

수정 후보: `src/AMS2LeagueClient/bin/LapLifecycleHotfix/AMS2LeagueClient.exe`

기존 오버레이를 종료한 뒤 위 후보를 실행한다. EXE만 다른 폴더로 복사하지 말고 빌드 폴더의 DLL과 함께 사용한다. 버전 문자열은 0.3.1 그대로이므로 실행 경로로 후보를 구분한다.

실게임에서 아직 최고 기록이 없는 참가자의 출차 → 첫 계측 → 유효 기록 인정 순서를 확인하면 된다. 이번 작업은 commit/tag/push/Release, 게임 조작, 운영 데이터 변경, 실행 중 프로세스 교체를 하지 않았다.

# 세션별 랩 상태 / 차고 PIT 배지 수정

작성: 2026-09-06 KST. 기존 0.3.1 작업 트리에 추가한 로컬 후보이며 commit/tag/release는 하지 않았다.

## 사용자 결정과 적용

| 조건 | 순위 타워 시간 칸 |
|---|---|
| 연습·예선: 유효한 참가자 최고 기록 존재 | 다음 랩 및 재출차 중에도 최고 기록 유지 (사용자 확인) |
| 연습·예선: 최고 기록 없음, 차고/피트 정차 | `--`, 별도 `PIT` 배지 |
| 연습·예선: 최고 기록 없음, 피트 출차 후 계측선 통과 전 | `아웃랩` |
| 연습·예선: 최고 기록 없음, 계측 주행 | `랩 타임 주행 중` |
| 레이스: 완료 랩 0 | `아웃랩` |
| 레이스: 완료 랩 1, 두 번째 바퀴 주행 | `레이스 중` |
| 레이스: 완료 랩 2 이상 | AMS2 참가자 최고 기록, 없으면 `레이스 중` |
| FIN/RET/DNF/DSQ | 기존 종료 상태 배지와 유효 최고 기록 유지; 가짜 실시간 타이머 없음 |

개인 랩타임 패널의 현재 시간은 별개다. 타워의 숫자는 SHM 참가자 `BestLapTime`만 사용한다. 레이스 두 번째 바퀴 완료 후 노출을 시작하지만, AMS2의 최고 기록 숫자 자체를 임의 재계산하거나 첫 랩을 제외한 별도 기록을 만들어 내지는 않는다.

## 원인과 수정 범위

- 기존 아웃랩 판정은 연습·예선에서도 `LapsCompleted == 0`이었다. 피트 재출차와 계측 주행을 구분할 수 없었다.
- 기존 `InvalidLapDisplayTracker`에 참가자별 출차 상태 관측을 추가했다. 차고/피트에서 출차한 뒤 실제 트랙의 완료 랩 증가 또는 연속된 유효 거리 wrap을 확인하면 계측 주행으로 전환한다. 기존 `ParticipantLapClock`과 동일한 전진 wrap/이동량 제한을 적용하며 타이머를 새로 계산하지 않는다.
- 대기 화면 중에도 관측한다. 세션/트랙/generation 변경, 참가자 교체·이탈·종료, 카운터 되감기, 게임 종료·리플레이에서 이전 상태를 제거한다.
- 동일 아웃랩 관측 집합을 타워, 개인 무효 표시, 무효 이벤트 판정에 전달한다. 피트·아웃랩 중 원본 무효 비트로 불필요한 이벤트를 새로 만들지 않는다. 원본 비트는 수정하지 않는다.
- 긴 문구는 WPF에서 두 줄로 표시하고 기존 Uniform/DownOnly 크기 조절을 유지한다.
- PIT 누락은 `RaceControlAnalyzer.IsPitActive`에서 `InGarage(4)`, `DrivingOutOfGarage(5)`를 제외한 코드가 원인이었다. 기존 공통 함수를 보완하여 알려진 피트 상태 5종을 모두 포함했다. `None(0)`과 알 수 없는 값은 PIT로 추정하지 않는다. DT/SG/RET/DNF/DSQ 우선순위와 활성 행의 밝기는 유지한다.
- Ponytail 원칙에 따라 기존 표시 추적기와 공통 PIT 판정을 보완했다. 새 패키지·서비스·원본 데이터 스키마는 추가하지 않았다.

한계: 프로그램이 출차 이후에 연결되어 과거 피트 관측이 없으면 해당 출차를 소급 복원하지 않는다. 단순 첫 랩 번호만으로 아웃랩이라고 추정하지 않고, 트랙에서 Racing 상태이면 계측 주행 문구를 사용한다. 실제 계측 상태를 보증하는 새 SHM 플래그를 만든 것은 아니다.

## 검증

| 검증 | 결과 |
|---|---|
| 최종 Release solution build | PASS, 경고 0 / 오류 0 |
| Client 테스트 | PASS, 97/97 |
| Activity/Compact 테스트 | PASS, 102/102 |
| WPF 전체 layout capture 실행 | PASS, 97/97 (마지막 카운터 순서 보호 추가 전 실행; 추가 후 전체 테스트 재실행 PASS) |
| WPF 이미지 직접 확인 | 상태 두 줄 / 차고 PIT 배지, 잘림 없음 |
| 피트 상태 5종 × 29명 | 각 fixture 29/29 PIT 표시 |
| 연습·예선·Test, 최초/후속 stint | 출차 → wrap → 계측 → 최고 기록 → 재출차 유지 PASS |
| 첫 레이스 랩 raw invalid / 카운터 갱신 순서 | 무효 표시 억제, 참가자별 표시 전환 PASS |
| git diff --check | PASS |
| 실게임 SHM 재검증 | NOT RUN: 5초 read-only probe에서 MappingUnavailable, 수신 snapshot 0 |

초기 테스트에서 이전 표시 규칙을 기대하던 5개 테스트가 실패하여 새 사용자 규칙으로 갱신하고 출차·PIT 검증을 추가했다. 렌더 테스트의 자연 TextBlock 높이 대신 실제 Viewbox 변환 후 높이를 검사하도록 수정했다. 최종 실패는 0개다.

실행 명령 (저장소 루트):

```powershell
.\work\dotnet8\dotnet.exe build AMS2KRLeague.sln -c Release --no-restore -p:OutputPath=bin/LapPhaseHotfix/
.\work\dotnet8\dotnet.exe tests/AMS2LeagueClient.Tests/bin/LapPhaseHotfix/AMS2LeagueClient.Tests.dll --capture-layout work/lap-phase-hotfix-layouts
.\work\dotnet8\dotnet.exe tests/AMS2LeagueActivity.Tests/bin/LapPhaseHotfix/AMS2LeagueActivity.Tests.dll
```

이미지: `work/lap-phase-hotfix-layouts/lap-phase-Practice-stint0.png`, `garage-pit-badges.png` 등. 실게임 캡처가 아니라 WPF fixture 렌더다.

## 실행 후보 / 다음 확인

`src/AMS2LeagueClient/bin/LapPhaseHotfix/AMS2LeagueClient.exe`

같은 폴더의 DLL·설정 파일을 함께 유지해야 한다. exe만 이전 폴더로 복사하면 수정 내용이 반영되지 않는다.

SHA256:

```text
166461365B702DB1D9DF010D199F003DE05C0D0951BEE3EA8D74A0C391EC944A  AMS2LeagueClient.exe
F8677D6FB2238EBE36CC9AF9F4E4B8755D7D93756E3FA4535A7B91CE7AADB32F  AMS2LeagueClient.Core.dll
```

다음 실게임 확인: 연습·예선에서 차고 PIT → 출차 아웃랩 → 계측선 이후 상태 → 유효 랩 완주 후 최고 기록 유지, 재피트에서도 최고 기록 유지 및 PIT 배지. 레이스는 참가자별 첫/두 번째/두 번째 완주 후 타워 표시를 확인한다.

이번 작업에서는 게임·기존 오버레이 실행/종료/재시작, Cafe24 변경/업로드, 기록 원본 변경, commit/push/release를 하지 않았다. 이전 턴의 별도 수정들은 작업 트리에 보존했다.

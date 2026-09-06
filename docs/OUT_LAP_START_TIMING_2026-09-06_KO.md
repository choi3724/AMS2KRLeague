# 아웃랩 → 계측 주행 전환 지연 보완

작성: 2026-09-06 KST. 직전 `LapPhaseHotfix` 후보의 후속 로컬 수정이며 공개 릴리스는 하지 않았다.

## 조사 결과

기존 `InvalidLapDisplayTracker.Observe`에는 전환이 늦어질 수 있는 조건이 있었다.

- `LapsCompleted` 증가 또는 거리 `trackLength → 0` wrap만 사용하고, AMS2 참가자의 `CurrentLap` 증가와 새 첫 섹터 시작을 보지 않았다.
- 직전/현재 피트 모드가 모두 `None`이어야 전환했다. 계측 시작 때 `DrivingOutOfPits`가 남아 있으면 신호를 무시했다.
- `DrivingOutOfPits`인 매 프레임마다 아웃랩 상태를 다시 설정했다.

추가한 재현 테스트는 수정 전 2/2 실패했다. 단, 이것이 사용자가 보았던 바로 그 순간의 원인이라고 실측 확정한 것은 아니다.

## 읽기 전용 실게임 관측

- 게임과 기존 오버레이를 조작·종료하지 않고 기존 SHM reader/probe로 60초 관측했다.
- 2026-09-06 15:55:12~15:56:12 KST, Practice, 트랙 길이 약 3999.73m.
- 기록 957개, 참가자 랩 전환 13회. 이 13회는 현재 랩/완료 랩/섹터/거리 wrap이 같은 표본에서 함께 바뀌었다.
- 로컬 플레이어는 관측 내내 현재 랩 1이었다. 따라서 문제의 아웃랩 종료 순간과 수정 전후 실화면 지연은 미확인이다. 별도의 잘못된 트랙 기준점이 있다는 사실도 확인되지 않았다.
- probe 출력 `SequenceDrops=4`는 진단 reader의 값이며 서버 전송 손실 판정이 아니다.
- 기록 중인 파일을 중간에 읽었을 때 불완전한 마지막 JSON 줄이 있었지만, 종료·flush 후 957줄 전체를 오류 없이 다시 파싱했다.

로컬 증거: `work/ai-timing-probe/bin/LapStartHotfix/observed-20260906-065512.jsonl` (Git 제외).

SHA256: `4AEBACCACA3D4E2D4F4BF844578909EAABDBECB5D18B5A622611722155814960`.

## 수정

기존 추적기를 재사용하는 Ponytail 방식으로 조건만 보완했다.

- 참가자 `CurrentLap`가 1 증가하면 같은 snapshot에서 아웃랩 해제. 차고 초기화 `0 → 1`은 제외한다.
- 마지막 섹터 → 첫 섹터 전환과 유효한 S1 타이머 감소를 함께 관측하면 같은 snapshot에서 해제한다. 단순 시간 증가, 누락/음수 값, 중간 섹터 전환만으로는 해제하지 않는다.
- 기존 완료 랩 증가/유효 전진 거리 wrap은 fallback으로 유지한다. 추정 시계를 만들거나 타이밍 값을 계산하지 않는다.
- 출차 상태가 남아 있어도 계측 시작을 인정한다. 이미 시작한 계측을 늦은 출차 플래그가 아웃랩으로 되돌리지 않는다. 실제 새 피트 진입·정차·차고 이력은 다음 아웃랩을 다시 준비한다.
- 타워는 계측 시작이 확인되면 출차 플래그가 남은 동안에도 `--`로 떨어지지 않고 `랩 타임 주행 중`을 표시한다. PIT 배지는 별도 유지한다.
- Race 첫 랩 규칙, 유효 최고 기록 우선 정책, 기존 초기화·참가자 교체·무효 표시 동작은 유지한다.

변경 소스: `InvalidLapDisplayTracker.cs`, `OverlayViewModel.cs`, Client 테스트. 진단 probe와 이 문서 외 다른 구현 범위는 이번 턴에서 변경하지 않았다.

## 검증 및 후보

- Release solution build: PASS, 경고 0 / 오류 0.
- Client: 99/99 PASS (새 재현 테스트 2개, 연습·예선·Test 및 출차 상태 조합 포함).
- Activity: 102/102 PASS.
- `git diff --check`: PASS.
- 게임 입력, 오버레이 교체/재시작, 서버/업로드 변경, commit/tag/push/release: 하지 않음.
- 문제 순간의 실게임 전환 재확인: NOT RUN. 실행 중인 PID 26912는 기존 `bin/LapPhaseHotfix/AMS2LeagueClient.exe` 그대로였다.

새 실행 후보: `src/AMS2LeagueClient/bin/LapStartHotfix/AMS2LeagueClient.exe`. 기존 오버레이를 사용자가 종료한 뒤 이 폴더의 실행파일로 검사한다. exe만 복사하지 말고 같은 폴더 DLL을 함께 유지한다.

```text
SHA256 AMS2LeagueClient.exe
166461365B702DB1D9DF010D199F003DE05C0D0951BEE3EA8D74A0C391EC944A
SHA256 AMS2LeagueClient.Core.dll
D4C57BFDB8CF402731A926275F07151C741DDA6E0838DC9C51C7CC89DE593D24
```

재실행:

```powershell
.\work\dotnet8\dotnet.exe build AMS2KRLeague.sln -c Release --no-restore -p:OutputPath=bin/LapStartHotfix/
.\work\dotnet8\dotnet.exe tests/AMS2LeagueClient.Tests/bin/LapStartHotfix/AMS2LeagueClient.Tests.dll
.\work\dotnet8\dotnet.exe tests/AMS2LeagueActivity.Tests/bin/LapStartHotfix/AMS2LeagueActivity.Tests.dll
```

다음 확인: 연습/예선에서 유효 최고 기록이 없는 참가자의 출차 후 첫 계측 시작 때 문구가 게임 타이머와 함께 전환되는지, 출차 상태가 늦게 사라져도 아웃랩으로 복귀하지 않는지 실주행 비교한다.

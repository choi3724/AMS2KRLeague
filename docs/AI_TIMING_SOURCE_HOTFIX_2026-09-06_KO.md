# AI 참가자 현재 랩타임 누락 수정 — 2026-09-06

> 후속 요청 반영: Race 첫 랩의 타워 타이밍은 이제 참가자 모두 의도적으로 숨긴다. 이 문서는 당시 AI timing source 복구 검증 기록이며, 최신 UI 정책은 [전 패널 글꼴/무효 랩 후속 보고서](OVERLAY_READABILITY_INVALID_LAP_2026-09-06_KO.md)를 따른다. 참가자별 native timing source 복구는 유지된다.

## 결론

0.3.1에서 참가자별 섹터 데이터를 현재 랩타임 계산에서 제외하고, 로컬에서 Start/Finish 통과를 관측한 이후에만 관측 타이머를 표시하도록 바꾼 것이 회귀 원인이다. 첫 랩에는 관측한 랩 시작과 직전 랩이 모두 없어 AI 행이 `--`가 되었다. 게임이 AI 데이터를 제공하지 않는 문제가 아니었다.

Overlay 표시 계층에서 AMS2 참가자별 현재 섹터 데이터를 다시 우선 사용하도록 수정했다. 실제 실행 중인 게임의 Shared Memory를 읽어 수정된 production ViewModel을 검증한 결과, 30초 / 470개 유효 표본 모두 AI 23/23명의 현재 시간이 표시되었다. 게임 조작, 실행 중인 오버레이 교체, 서버 전송은 하지 않았다.

이 변경은 로컬 수정본이다. 버전은 아직 0.3.1이며 새 commit/tag/release를 만들거나 기존 v0.3.1 배포 자산을 덮어쓰지 않았다. 실행 중인 설치본에는 아직 적용되지 않았다.

## 정확한 원인과 데이터 의미

원래 모든 참가자 섹터를 무조건 합산하는 방식에도 문제가 있었다. AMS2는 새 랩에서 현재 섹터 뒤쪽 값에 이전 랩의 섹터 시간을 남겨둘 수 있다. 따라서 세 칸을 항상 합산하면 현재 랩에 이전 랩 시간이 더해진다. 반대로 0.3.1처럼 섹터 전체를 버리면 유효한 현재 타이밍을 놓친다.

`CurrentSector`는 0부터 시작한다. 게임이 제공하는 참가자별 현재 랩 경과 시간은 다음처럼 계산한다.

| CurrentSector | 사용 값 | 제외하는 값 |
| --- | --- | --- |
| 0 | S1 | S2, S3 |
| 1 | S1 + S2 | S3 |
| 2 | S1 + S2 + S3 | 없음 |

완료된 섹터는 유한한 양수여야 한다. 진행 중인 섹터는 0부터 허용한다. 음수 sentinel(-1, -123), NaN, Infinity, 범위 밖 섹터는 사용할 수 없다. InGarage / DrivingOutOfGarage에서는 이 현재 타이밍 경로를 사용하지 않는다.

AMS2 설치본의 공식 `Support/SharedMemory/AMS2_SharedMemoryExampleApp/SharedMemory.h`에 있는 0-based 섹터 정의와 실제 연속 표본을 함께 확인했다. 이전 섹터가 남는 동작은 문서만 보고 가정한 것이 아니라 아래 실제 데이터로 확인했다.

첫 랩의 동시 출발 직후 여러 참가자의 경과 시간이 같은 것은 가능하다. 숫자가 같다는 이유만으로 원본을 버리거나, 다르게 보이도록 임의의 시간을 만들지 않는다.

## 실제 관측

수정 전 읽기 전용 probe에서는 AI에 서로 다른 직전 랩과 현재 섹터 값이 존재했지만, 관측한 라인 통과가 없는 상태에서는 직전 랩 `L...`만 표시되었다. 첫 랩처럼 직전 랩도 없으면 `--`가 되는 동일 경로다.

수정 후에는 관측 타이머가 0개인 첫 표본부터 다음 값이 표시되었다.

| KST 시각 / 참가자 | 현재 섹터 | 원본 값 | 실제 ViewModel 표시 |
| --- | --- | --- | --- |
| 10:29:17 / index 1 | 1 | S1 11.259216 + S2 13.185425; S3 15.857727은 제외 | 0:24.445 |
| 10:29:17 / index 2 | 1 | S1 11.0997925 + S2 18.513428 | 0:29.613 |
| 10:29:17 / index 3 | 1 | S1 11.175415 + S2 15.31311 | 0:26.489 |
| 10:29:37 / index 2, 다음 랩 | 0 | S1 4.522644; 이전 S2/S3 제외 | 0:04.523 |

전체 AI 개수를 추가 확인한 최종 probe:

- 구간: 2026-09-06 10:31:31.512–10:32:01.447 KST.
- Raw 참가자 25명 중 플레이어 1명, Safety Car 1명을 제외한 AI 23명 검사.
- 유효 표본: 470개. 모든 표본에서 AI 현재 시간 23/23, 누락 표본 0개.
- 세 섹터 및 다음 랩으로 넘어가는 구간 포함.
- Shared Memory 읽기에 실패해 채택하지 않은 시도: 12회. 유효 표본에 포함하거나 타이밍 성공으로 세지 않았다.
- 이 수치는 데이터/표시 모델 검증이며 렌더링 FPS 측정이 아니다.
- 첫 랩 시작 상황은 자동 fixture로 검증했다. 실게임을 재시작하거나 출발 상태로 되돌리지 않았다.

기존 `SharedMemoryReader`, 참가자/League resolver, `ParticipantLapClock`, `OverlayViewModel.Build`를 재사용한 읽기 전용 probe이며 WPF 창이나 업로드 런타임을 실행하지 않았다. 수정된 표시 문자열의 실제 게임 데이터 검증이지, 실행 중인 설치본 화면이 수정되었다는 뜻은 아니다.

로컬 증거는 git 제외 경로에 보존했다.

- 수정 전: `work/ai-timing-probe/bin/Release/net8.0-windows/observed.jsonl`
- 수정 후 개별 값: `work/ai-timing-probe/bin/Release/net8.0-windows/observed-20260906-012917.jsonl`
- 수정 후 전체 개수: `work/ai-timing-probe/bin/Release/net8.0-windows/observed-20260906-013131.jsonl`
- 전체 개수 증거 SHA256: `ddca293d2b71602a237e9b6cc06640ee19a535ff2a43dd3edda8fd2368945510`

## 수정 범위

1. `OverlayViewModel`: terminal / Practice·Qualifying 완료 상태를 먼저 처리하는 기존 규칙 유지. 주행 중에는 본인 root timing → 해당 참가자의 유효한 현재 섹터 합계 → 기존 관측 시간(`~`) → 직전 랩(`L`) / `--` 순서. 본인 root timing을 다른 참가자에게 복사하지 않는다.
2. `PlayerOverlayCoordinator`: 본인 시계가 멈추거나 관측한 랩 시작이 없어도 새로운 Shared Memory sequence에 따라 참가자 시간이 갱신되도록 표시 키 수정. 기존 UI 갱신 제한은 유지한다.
3. 테스트: 기존 누락 데이터 fixture에 현재 섹터를 명시했다. 기존 테스트가 기본 섹터값과 빠진 현재 섹터를 사용하면서 정상적인 첫 랩 데이터까지 배제하는 방향으로 검증했던 빈틈을 보완했다.

Ponytail 원칙에 따라 별도 추정 타이머나 의존성을 추가하지 않고 기존 참가자 원본 데이터 경로를 재사용했다. Capture Schema, Compact Protocol, 업로드, 서버 API/DB는 변경하지 않았다.

## 검증 결과

- Release solution build: PASS, 경고 0 / 오류 0.
- Client: 78/78 PASS.
- Activity: 97/97 PASS.
- `git diff --check`: PASS.
- Practice / Qualifying / Race별 첫 랩 현재 섹터, 다른 참가자의 서로 다른 시간, 후속 섹터의 이전 랩 잔존값 제외, 0초/invalid 값 fallback: PASS.
- 참가자별 Finish 처리, Practice/Qualifying 완료 Best Lap, 선두 Finish 후 후속 참가자 계속 진행: PASS.
- 기존 Waiting / Position Animation / Race Finish / Capture·Transport 회귀 테스트: PASS 유지.

재실행 명령:

```powershell
.\work\dotnet8\dotnet.exe build AMS2KRLeague.sln -c Release --no-restore
.\work\dotnet8\dotnet.exe run --project tests/AMS2LeagueClient.Tests/AMS2LeagueClient.Tests.csproj -c Release --no-build
.\work\dotnet8\dotnet.exe run --project tests/AMS2LeagueActivity.Tests/AMS2LeagueActivity.Tests.csproj -c Release --no-build
```

수정된 개발 실행 파일: `src/AMS2LeagueClient/bin/Release/net8.0-windows/AMS2LeagueClient.exe`. 테스트할 때 기존 설치본 오버레이를 종료한 뒤 이 파일을 실행해야 한다. 기존 공개 Installer/ZIP에는 이번 수정이 들어 있지 않다.

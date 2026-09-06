# 전후방 GAP 원위치 복원 / 최고 랩·선두 변경 플래시 — 2026-09-06

## 요청과 원인

사용자가 지적한 위치는 이름 아래가 아니라 거리(m) 바로 왼쪽의 기존 GAP 칸이다. 이전 변경에서 `LAP N`을 이름 아래로 분리하고 GAP 칸에는 시간만 남겼다. 실제 트랙 인접 차량이 순위 인접 차량과 다르면 게임 split을 사용할 수 없어, 이 칸에는 `—`만 남는 표시 회귀가 있었다.

시간이 LAP 숫자로 변환된 것은 아니다. 랩 차이와 시간차는 별도 데이터이고, 잘못된 차량의 split을 가져오지 않는 기존 조건은 유지한다.

## 수정

### 기존 오른쪽 GAP 칸 복원

- 이름 아래 `LAP` 줄을 제거했다.
- `LAP 1/2`는 다시 거리(m) 왼쪽 GAP 칸에 표시한다.
- 랩 차이와 유효한 시간차가 모두 있으면 같은 칸에 두 줄로 함께 표시한다.
- 랩 차이만 있으면 `LAP N`을 크게 표시하고 불필요한 `—`는 숨긴다.
- 시간차만 있으면 `+0.842s`처럼 기존 위치에 표시한다.
- 둘 다 근거가 없을 때만 `—`를 표시한다.
- 기존 트랙 진행거리/2회 확인, 참가자 교체 시 초기화, RED=불리/BLUE=유리 규칙은 변경하지 않았다.
- 자유 크기 조절 및 글꼴 균등 비율은 유지한다. 긴 숫자/두 줄은 GAP 칸 안에서 균등 축소한다.

게임이 제공하는 시간차는 표시 차량과 원본 split 대상이 맞을 때만 사용한다. 임의 거리÷속도, 다른 참가자 split, Current/Best Lap 차이로 없는 시간차를 만들지 않았다. 따라서 LAP 표시는 복원했지만 모든 차량 조합에 초 단위 시간이 항상 제공된다는 의미는 아니다.

### 이벤트 플래시

- `RaceFastestLap`, `LeaderChange` 이벤트가 새로 표시될 때 색상 스윕을 1회 재생한다.
- 기존 Race Control의 `HudMotion.Sweep`를 그대로 재사용한다: 최대 opacity 0.42, 스윕 360 ms + 페이드 700 ms.
- 기존 카드의 진입/종료 애니메이션과 메시지는 유지한다.
- 동일 EventId의 반복 ViewModel 갱신은 재생을 시작하지 않는다.
- 새로운 별개 이벤트는 다시 재생하고, 카드 종료/다른 이벤트 선점/미리보기에서는 이전 플래시를 해제한다.
- 무한 깜빡임, 새로운 애니메이션 라이브러리, 서버 이벤트 변경은 추가하지 않았다.

## 검증

최종 별도 출력 Release 빌드: 경고 0 / 오류 0. Client 85/85 PASS, Activity 97/97 PASS. `git diff --check` PASS.

화면 밖 WPF 테스트 창에서 500 ms 진행 후 두 이벤트의 스윕 opacity가 각각 0.403으로 측정됐고, 실제 플래시 중간 프레임도 캡처했다. 앞뒤 GAP의 LAP 전용/시간 동시 표시, 기본/세로 확장 이미지를 직접 열어 확인했다.

중간 검증에서는 새 브러시 바인딩이 갱신되기 전 색상을 단언한 테스트를 Dispatcher 처리 후 검사하도록 수정했다. 기존 업로드 fixture의 5초 대기 timeout도 1회 발생했으며, 업로드 제품 코드를 변경하지 않고 이후 직렬 전체 검증 2회에서 해당 항목을 포함해 모두 통과했다. timeout의 정확한 원인은 이번 UI 작업에서 확정하지 않았다.

자동 검증 내용:

- 시간/랩 있음·없음 4가지 × 520×104, 416×166, 780×83 = 12개 상대 GAP 렌더링.
- LAP/시간이 실제 오른쪽 열에 속하는지, 이름 아래가 아닌지, 이름/시간/거리 간 겹침 및 경계 잘림 검사.
- 최고 랩/선두 변경만 flash 대상으로 지정되는지, WPF animation clock 부착, 같은 이벤트 120회 갱신 중 재생 억제, 다음 이벤트/종료/미리보기 초기화 검사.
- 옵션 캡처에서는 게임 포커스를 가져오지 않는 화면 밖 테스트 창으로 WPF 애니메이션을 구동한다.
- 기존 타워/글꼴/무효 랩/첫 랩/플래그/업로드·Compact 회귀 테스트 유지.

증거 이미지는 Git 제외 `work/relative-event-hotfix-20260906`에 있다. 일반 창 fixture이며 실제 게임 위의 수정본 검증 또는 120 FPS 측정으로 간주하지 않는다.

## 빌드·실행 위치

사용자가 이전 개발 빌드를 실행 중이어서 기본 Release 출력의 Core DLL이 잠겼다(MSB3027/MSB3021). 게임이나 오버레이 프로세스를 종료하지 않고 별도 출력 경로를 사용했다. 프로젝트 설정 파일은 바꾸지 않았다.

```powershell
.\work\dotnet8\dotnet.exe build AMS2KRLeague.sln -c Release --no-restore -p:OutputPath=bin/RelativeEventHotfix/
.\work\dotnet8\dotnet.exe tests/AMS2LeagueClient.Tests/bin/RelativeEventHotfix/AMS2LeagueClient.Tests.dll --capture-layout work/relative-event-hotfix-20260906
.\work\dotnet8\dotnet.exe tests/AMS2LeagueActivity.Tests/bin/RelativeEventHotfix/AMS2LeagueActivity.Tests.dll
```

별도 출력 빌드는 `dotnet run --no-build`가 기존 경로의 테스트를 실행하지 않도록 위처럼 생성된 DLL을 직접 지정한다.

수정 실행파일: `src/AMS2LeagueClient/bin/RelativeEventHotfix/AMS2LeagueClient.exe`.

실행 중인 이전 개발 빌드에는 자동 적용되지 않는다. 사용자가 현재 오버레이를 종료한 후 위 파일을 실행해야 한다. Compact/캡처/업로드/서버/API/DB 변경, 운영 데이터 수정, commit/tag/push/Release 및 PC 종료는 하지 않았다. 이전 턴의 미커밋 수정은 보존했다.

# 전후방 시간 차이 + 거리 동시 표시 — 2026-09-06

> 후속 사용자 요청으로 LAP 차이 위치를 이름 아래에서 기존 오른쪽 GAP 칸으로 복원했다. 최신 배치와 별도 빌드 위치는 [GAP 원위치·이벤트 플래시 보고서](RELATIVE_GAP_SLOT_EVENT_FLASH_2026-09-06_KO.md)를 따른다.

## 변경

- 전후방 시간 차이에 초 단위를 붙였다: `+0.842s`, `+1.127s`.
- 기존 `LAP N` 확인 로직이 시간 차이 필드를 덮어쓰지 않도록 분리했다. 랩 차이는 참가자 이름 아래, 시간 차이는 거리 왼쪽에 표시한다.
- 시간 글씨를 18 → 24 design px로 확대했다. 시간/거리 열을 확보하고 긴 시간은 시간 셀 안에서만 축소해 겹침을 방지한다.
- 랩 차이가 없으면 보조 줄은 접히므로 기존 두 행 높이와 전체 520×104 크기는 유지한다.
- 거리 방향/RED=불리·BLUE=유리, deadband, 랩 차이 연속 확인 및 참가자 변경 시 초기화는 유지한다.

Ponytail 원칙에 따라 이미 존재하는 게임 split 경로를 재사용했다. 새 추정 시계나 거리÷속도 계산은 추가하지 않았다.

## 데이터 의미와 제한

`mSplitTimeAhead` / `mSplitTimeBehind`는 게임 제공 초 값이다. 현재 표시하는 실제 전후방 차량이 해당 게임 split의 순위 인접 차량과 일치할 때만 사용한다. Safety Car 제외로 대상이 바뀌거나 실제 트랙 인접 차량과 순위 인접 차량이 다르면 다른 차량의 split을 복사하지 않는다.

유효한 데이터가 없으면 시간은 `—`이고, 가능한 거리(m)와 랩 차이는 계속 표시한다. 따라서 모든 차량 조합에서 시간 차이가 항상 제공되는 기능은 아니다. 임의 차량 사이의 트랙 통과 시각 기반 간격 측정은 이번 변경에 포함하지 않았다. Current Lap/Best Lap 두 값의 차이 역시 실제 차량 간 시간 간격이 아니므로 사용하지 않는다.

## 검증

- Release solution build: 경고 0 / 오류 0.
- Client 80/80 PASS, Activity 97/97 PASS.
- 게임 split 양쪽 표시 및 0초: PASS.
- 음수, NaN, Infinity: 시간 `—`, 거리 유지 PASS.
- 물리적으로 더 가까운 다른 차량에 순위 차량 split 잘못 적용하지 않음: PASS.
- LAP 1 / LAP 2 확정 이후 양쪽 시간 차이를 덮어쓰지 않음: PASS.
- Start/Finish false LAP 및 참가자/세션 변경 시 랩 확인 초기화 회귀: PASS.
- WPF 실제 렌더링 520×104, 416×166, 780×83의 랩 차이 있음/없음 총 6종에서 표시 범위와 시간/거리 겹침 자동 검사: PASS.
- 기본 및 세로 확장 렌더링 이미지를 직접 확인했다. 게임 위의 실행 중인 설치본 화면 검증과는 구분한다.

스크린샷은 git 제외 경로 `work/relative-gap-layout-20260906/relative-gap-*.png`에 있다. 일반 예시와 123초 이상의 긴 갭/4자리 거리/시간 없음 예시를 포함한다.

이전 AI 타임 수정은 `AI_TIMING_SOURCE_HOTFIX_2026-09-06_KO.md`에 원인과 실제 게임 23/23 검증을 기록했다. 최종 80개 Client 테스트에는 해당 회귀 검증도 포함한다.

검증 재실행:

```powershell
.\work\dotnet8\dotnet.exe build AMS2KRLeague.sln -c Release --no-restore
.\work\dotnet8\dotnet.exe run --project tests/AMS2LeagueClient.Tests/AMS2LeagueClient.Tests.csproj -c Release --no-build -- --capture-layout work/relative-gap-layout-20260906
.\work\dotnet8\dotnet.exe run --project tests/AMS2LeagueActivity.Tests/AMS2LeagueActivity.Tests.csproj -c Release --no-build
```

## 적용 상태

게임 조작, 설치본 종료/교체, 서버/API/DB/Compact 변경은 하지 않았다. commit/tag/push/release도 하지 않았다.

두 수정이 포함된 개발 실행 파일은 `src/AMS2LeagueClient/bin/Release/net8.0-windows/AMS2LeagueClient.exe`다. 기존 오버레이를 종료하고 이 파일을 실행해야 수정본이 적용된다. 공개 v0.3.1 Installer/ZIP과 실행 중인 설치본은 그대로다.

# v0.3.1 영상 분석 / Race Control / 참가자별 타이밍 인수인계

작성: 2026-09-05 KST. 기준 `v0.3.0` / `d506a93`. 이전 두 레이아웃 보고서의 후속이며 최신 구현을 설명한다.

## 영상에서 확인한 것

사용자 MP4를 로컬에서만 읽고 프레임을 추출했다. 원본은 변경하거나 업로드하지 않았다.

- 39.43초, 1080×1920, HEVC, 30fps, 1,183프레임, 20,734,305 bytes.
- SHA256: `0133eaa3514316ec369bbf582c3659a0551504ec7573a135f4ec77ab4ce87d2f`.
- 약 12초: 타워 여러 행에 `0:07.440`, 약 36초: 여러 행에 `0:31.460`가 동시에 표시된다. 로컬 현재 시간과 같은 값이다. 표시 오류는 확인했지만 당시 SHM 배열 원본까지 확보한 것은 아니다.
- 약 5.8~7초 / 14~15.2초: 순위 이동·숫자 롤·플래시가 겹치는 연속 프레임을 확인했다. 카메라 이동과 30fps 샘플링이 포함되므로 오버레이 120/144Hz present 수치를 영상에서 역산하지 않았다.
- 약 33~39초: Race Control 제목이 세로로 늘어나고 우측 아래 상태가 작아지는 문제가 보인다. 독립 비율 창에 `Stretch.Fill`을 적용해 글자까지 비등방 확대하던 경로와 일치한다.
- 기본 AMS2 HUD가 투명 타워/세션 카드 뒤로 비쳐 숫자와 문구가 겹친다. 오버레이가 같은 TextBlock을 두 번 그린다고 판정하지 않았다. 게임 설정은 변경하지 않았다.

프레임 추출 도구는 별도 work 진단 폴더에만 설치했으며 제품/패키지 의존성에는 추가하지 않았다. 영상 프레임과 UI 캡처는 Git 제외 work 폴더에 보존한다.

## Race Control 수정

창의 가로·세로 독립 크기를 유지하면서 이 카드만 외부 Fill 확대에서 제외했다. 배경은 실제 창 전체를 채우고 텍스트는 실제 폭으로 줄바꿈한다. 넓고 낮은 창은 제목·상태와 본문을 나란히 배치한다. 낮은 창에서는 보조 history를 숨기고 현재 driver/message/state를 보존한다. 자연 높이가 넘을 때만 내부 텍스트를 균등 축소한다. Window 비율은 고정하지 않고 글자 가로/세로 배율만 동일하게 유지한다.

- 72×48, 288×66, 160×260, 600×55, 416×152, 832×304, 240×120 각각 compact/expanded 14조합 PASS.
- 긴 제목·드라이버·알림·이력의 변환된 TextBlock bounds, 줄바꿈 높이, 숨겨진 부모, 등방 배율 검사.
- compact/tall/wide/expanded PNG 시각 확인: 핵심 문구 잘림 없음. 극단적인 창에서는 글자가 작아지는 물리적 한계가 있다.
- ID/history 갱신 및 같은 ongoing flag로 축약될 때 재등장하지 않는 수정 유지. 실제 새 메시지는 정상 표시한다.

## 타워 타이밍 원인과 변경

기존 원격 참가자 표시 경로는 양수 `CurrentSector1Time + CurrentSector2Time + CurrentSector3Time` 합계를 현재 시간으로 사용했다. 각 차량의 랩 시작을 확인하지 않은 합계이므로 배열들이 같은 세션 경과값을 주면 모든 행에 같은 시간이 나온다. 이것은 UI 표시 경로의 확인된 결함이다. 영상 당시 AMS2가 왜 동일 배열을 제공했는지는 raw 증거가 없어 단정하지 않는다.

`ParticipantLapClock`은 Presentation 내부에서만 동작한다.

1. 슬롯 + 이름/차량/클래스 identity를 유지한다. 순위는 identity가 아니다.
2. 트랙 길이/거리 유효성을 검사하고 연속 snapshot에서 트랙 끝→시작의 전진 wrap을 관측한 때만 해당 차량의 랩 시계를 시작한다. 단순 currentLap 차이나 속도 역산으로 시작 시각을 만들지 않는다.
3. 유효한 세션 타이머의 관측 차이, 그 외에는 신선한 playing snapshot 사이의 시간 차이를 누적한다. pause/frozen sequence는 누적하지 않는다. 남은 시간 0은 개별 완주가 아니므로 이후 신선한 playing snapshot으로 계속 측정한다.
4. 연결 누락/identity 교체/1초 초과 running sample gap/비정상 거리 이동/역방향 wrap/카운터 초기화 시 시작점을 버린다. 새 유효 라인 통과 전에는 fallback한다.
5. 참가자별 FIN/RET/DNF/DSQ에서 즉시 제거한다. 선두 완주나 root 종료를 다른 참가자의 종료로 대신하지 않는다.

| 표시 | 출처 / 의미 |
|---|---|
| 접두사 없는 현재 시간 | 로컬 참가자이며 viewed index와 일치할 때 AMS2 root CurrentTime |
| `~0:06.400` | 해당 참가자의 라인 통과 이후 UI 관측 진행 시간 |
| `L1:10.125` | 현재 랩 시작 불명확 시 AMS2 participant LastLapTime |
| `--` | 유효 source 없음 |
| P/Q 완료 시 시간 | AMS2 participant BestLapTime |
| `FIN`/`RET`/`DNF`/`DSQ` | 참가자 개별 terminal state |

관측값은 공식 시간이나 정밀한 밀리초 기록이 아니다. 약 20Hz UI 관측으로 라인 통과 시각 오차가 생긴다. `~`로 구별하며 업로드/아카이브/공식 결과로 사용하지 않는다. 중간 접속/첫 랩에서 시작을 보지 못했으면 공통 race-start clock을 대신 표시하지 않는다. 타임 열은 접두사 포함 문자열을 자르지 않고 필요한 만큼 축소한다.

독립 라인 통과 fixture에서 A는 0.4초, B는 0.1초로 분리되며, 공통 sector=12초 fixture도 각 참가자의 last/observed 값으로 분리된다. frozen sequence, pause/resume, slot 교체, lap counter 지연, 순위 재정렬, 시간제 종료 후 개별 FIN까지 검증했다.

## 모션 변경 및 실측

게임 FPS를 오버레이 FPS로 대체하지 않았다. 로그의 UI 약 18~20Hz도 데이터 투영 횟수이지 모션 프레임 수가 아니다.

확인한 UI 병목은 시간 갱신 시 행 Replace/템플릿 재생성/강제 UpdateLayout과 Render 우선순위 데이터 투영이었다. 현재 행/컨테이너 유지, 변경 속성만 통지, 강제 레이아웃 제거, Background 투영으로 수정했다. 실제 이동 때만 재정렬 모션을 호출한다. 용량 리사이즈는 추월이 아니다. 공통 모션에 144Hz 힌트를 적용했지만 힌트 자체의 성능 개선을 주장하지 않는다.

`--motion-probe`는 제품 OverlayWindow를 데스크톱에 표시하고 20Hz 합성 갱신 중 **실제 순위 행 재정렬**을 700ms마다 발생시킨다. 340ms 애니메이션 중 처음 300ms에서 참가자 ContentPresenter의 실제 Y 위치를 측정한다. 워밍업 1초와 동일 RenderingTime 중복을 제외한다. SHM/업로드/게임 조작은 없다.

| 측정 | 좌표 변경/초 | Rendering 간격 p95 | 샘플 간 최대 이동 |
|---|---:|---:|---:|
| 전체 타워 왕복 fixture / system | 139.3 | 7.79ms | 0.84 DIP |
| 전체 타워 왕복 fixture / 144 요청 | 137.2 | 8.26ms | 0.83 DIP |
| 실제 순위 행 / 144 요청 / 9번 재정렬 | 139.9 | 9.42ms | 2.24 DIP |

이전 재실행의 실제 행은 142.0회/초, p95 9.64ms였다. 행이 20Hz에서만 이동하거나 38 DIP 한 행씩 즉시 튀는 현상은 이 프로브에서 나타나지 않았다. 이것은 CPU측 UI 좌표 관측이며 GPU/DWM present 계측이 아니다. p95가 120Hz의 8.33ms보다 길고 실제 게임 부하도 없으므로 **실게임 120 FPS 유지/체감 끊김 완전 해소는 NOT VERIFIED**다. 실제 사용 환경의 오버레이 모션 구간 고속 영상 또는 ETW/present 계측과 시각 확인이 필요하다.

## 검증과 경계

- Release build: 경고 0 / 오류 0.
- Client/UI/SHM/Transport: 77/77 PASS.
- Activity/Archive/Compact: 97/97 PASS.
- Waiting clipping, position up/down, active style, Practice/Qualifying Best Lap, per-driver race finish 회귀 PASS.
- 타워 마지막 행/가변 행/미리보기/저장 복원/같은 황색기 반복 억제 PASS.
- 게임/게임 설정/원본 동영상 변경 없음. Cafe24/Compact/capture/upload/API/DB 변경 없음.
- 소스 버전 0.3.1, 요청 게시 형식 Latest. 게시 완료 증거(commit/tag/asset hash)는 실제 GitHub 게시 후 릴리스 결과로 확인해야 한다.
- 실게임 모션 미검증을 전체 완료로 바꾸어 PC를 종료하지 않는다.

## 릴리스 준비 결과 / 보류 조건

2026-09-05 KST 로컬 0.3.1 후보 패키지를 생성했다. 패키징 스크립트에서 Release build 0/0, Client 77/77, Activity 97/97을 다시 통과했고 폴더/ZIP/Installer 공개 감사 forbidden=0이다. 실행 파일 ProductVersion=0.3.1, FileVersion=0.3.1.0과 manifest의 크기/해시 일치를 확인했다.

| 자산 | bytes | SHA256 |
|---|---:|---|
| AMS2-League-Overlay-0.3.1-win-x64.zip | 70,138,524 | ca8e2738b207f893749109c6362cded3075fb70be9e96d0ef7c84773ef7b1623 |
| AMS2-League-Overlay-0.3.1-Setup.exe | 51,251,375 | 040d5ee5c5bb605506ff8fd1ce1d22495eb55964cbeb9d6a85e7bbd1be0c9c5d |

`artifacts/`에 위 파일과 SHA256SUMS-0.3.1.txt, release-manifest-0.3.1.json을 보존한다. 설치 실행/사용자 설치본 교체는 하지 않았다.

**최초 보류 시점:** 커밋 / 태그 / push / GitHub Latest / PC 종료 미실행. 당시 HEAD는 `d506a93453a49c5728d873e4cefddbc4916b0483`이며 실제 게임 부하의 모션 확인이 남아 있었다.

**후속 사용자 결정:** 2026-09-05 사용자가 미검증 제한을 안내받은 뒤 “그러면 그냥 0.31로 릴리즈 해”라고 요청했다. 문맥상 준비한 `0.3.1`을 의미하므로 이 버전의 commit/tag/push/Latest 게시 보류를 해제한다. 제품 코드는 후보 생성 이후 바꾸지 않고 전체 build/test와 기존 자산 SHA256을 재검증한다. 실게임 120 FPS 항목은 여전히 NOT VERIFIED이며 릴리스 노트에도 공개한다. PC 종료는 이번 “릴리즈” 요청에 포함된 것으로 확대 해석하지 않는다.

게시 대상: <https://github.com/choi3724/AMS2KRLeague/releases/tag/v0.3.1>. 실제 게시 완료 여부와 commit SHA는 GitHub 및 별도 실행 결과로 확인한다.

## 재현 명령

```powershell
.\work\dotnet8\dotnet.exe build .\AMS2KRLeague.sln -c Release --no-restore
.\work\dotnet8\dotnet.exe run --project .\tests\AMS2LeagueClient.Tests\AMS2LeagueClient.Tests.csproj -c Release --no-build
.\work\dotnet8\dotnet.exe run --project .\tests\AMS2LeagueActivity.Tests\AMS2LeagueActivity.Tests.csproj -c Release --no-build
.\work\dotnet8\dotnet.exe run --project .\tests\AMS2LeagueClient.Tests\AMS2LeagueClient.Tests.csproj -c Release --no-build -- --capture-layout work/ui-video-hotfix
.\work\dotnet8\dotnet.exe run --project .\tests\AMS2LeagueClient.Tests\AMS2LeagueClient.Tests.csproj -c Release --no-build -- --motion-probe
```

설치본은 소스 빌드만으로 자동 교체되지 않는다. 이전 오버레이를 정상 종료하고 수정 실행 파일을 실행해야 새 화면을 볼 수 있다.

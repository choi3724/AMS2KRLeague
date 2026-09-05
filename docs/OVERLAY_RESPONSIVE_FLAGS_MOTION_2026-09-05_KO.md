# 오버레이 독립 리사이즈 / 황색기 반복 / 애니메이션 후속 수정

최신 후속: [0.3.1 영상·타이밍 보고서](OVERLAY_VIDEO_TIMING_HOTFIX_0.3.1_KO.md). 아래는 중간 작업 기록이며 Race Control Fill은 실제 크기 reflow로 교체됐다. 최신 테스트 수와 실제 순위 행 모션 측정은 후속 보고서를 따른다.

- 작성: 2026-09-05 KST
- 기준: v0.3.0 / d506a93 이후 로컬 변경. 버전·커밋·릴리스 변경 없음.
- 이전 타워 수정 보고서의 후속이다. 실게임의 끊김 해소 또는 120 FPS 달성을 확정한 보고서가 아니다.

## 수정 결과와 원인

### 미리보기와 실제 적용

이전에는 실제 게임 데이터 갱신 경로에서 창 크기로 행 수를 계산했다. 미리보기에서 창만 바꾸면 즉시 재계산되지 않았다. 또 표시 행 수 변경으로 마지막 고정 플레이어 행의 인덱스가 바뀌면 실제 추월과 같은 재정렬 애니메이션을 적용했다. 축소한 영역 밖의 옛 위치에서 플레이어가 들어오는 문제가 자동 테스트로 재현되었다.

- Window SizeChanged에서 최신 참가자 목록으로 표시 행 수를 즉시 재계산한다. 새 SHM snapshot을 기다리지 않는다.
- 높이 증가 시 추가 참가자를 표시하고, 축소 시 상위 순위 우선 + 범위 밖 플레이어 마지막 고정을 유지한다.
- 표시 용량 변경은 실제 추월로 애니메이션하지 않는다. 실제 순위 변경 효과는 유지한다.
- 편집용 불투명 30px 상단 바를 없앴다. 녹색 테두리 안을 드래그하고 오른쪽 아래 16px 그립으로 크기를 조절한다. 조작 설명은 툴팁이다.
- 10 → 20 → 8 → 20행, 저장 및 새 창 재로드에서 즉시 반영/하단 bounds를 확인했다.

### 다른 카드 크기와 글씨

기존 공통 AuxiliaryOverlayWindow의 Viewbox.Stretch=Uniform 때문에 비율을 다르게 조절해도 작은 축에 맞춰 내용이 고정 비율로 축소되고 빈 공간이 남았다.

- 전후방 거리, 현재/섹터 타임, 세션, 이벤트, Race Control, 멀티 대기 카드 모두 기존 WPF Viewbox의 Fill을 사용한다. 별도 레이아웃 엔진이나 의존성은 추가하지 않았다.
- 가로·세로 배율이 독립적이며 글자·배지도 각각 그 배율로 늘거나 줄어든다. 극단적인 비율에서는 글자가 길쭉하거나 납작해질 수 있다.
- 타워는 폭에 따른 글자 크기와 높이에 따른 행 수 증가를 유지한다.
- Race Control은 compact 288×66 / expanded 416×152 설계 크기를 표시 상태에 맞춰 선택한다. compact 내용을 expanded 설계 공간에 넣어 불필요하게 축소하지 않는다.
- 전후방 카드 내부 높이를 공통 설계 104px과 일치시키고 두 행을 각각 44px로 정렬했다.

### 황색기 반복 등장

RaceControlAnalyzer.BuildUpdate의 Version은 표시 중인 깃발뿐 아니라 참가자의 generation/compact state 변경에도 증가한다. 기존 state-only EventId가 `STATE:<Version>`이었고 RaceControlView가 EventId 변경을 등장 효과 조건으로 사용했다. 따라서 같은 황색기여도 다른 참가자의 상태 갱신이 새 등장 효과를 만들 수 있었다.

- state-only UI 식별자는 현재 표시 상태를 사용한다. 원본 분석기/캡처 이벤트는 변경하지 않았다.
- 등장 조건은 처음 표시되거나 실제 확장 메시지 내용(제목·대상·내용·강조색)이 달라진 경우다.
- ID/history 갱신만으로 다시 등장하지 않는다. 확장 알림 만료 후 같은 ongoing flag로 축약되어도 재등장하지 않는다.
- 같은 메시지가 유지되면 이전 메시지를 기억한다. 실제 state label 변경에는 작은 상태 효과만 적용하며, 숨겨졌다 다시 나타나는 새 알림 및 다른 메시지는 정상 애니메이션한다.
- 120회 ID/확장 상태 갱신과 서로 다른 Version의 동일 황색기 ID를 자동 검증했다.

### 끊김 대응과 측정의 한계

- 이전 수정의 행 객체/컨테이너 유지, timing-only CollectionChanged 0회, 강제 UpdateLayout 제거, 데이터 투영 Background 우선순위를 유지했다.
- 일반 타이머 갱신에서는 행의 모든 바인딩을 다시 알리지 않고 CurrentTime만 알린다. 120회 변경에서 알림 120개 모두 CurrentTime이며 값이 같으면 알림 0개다.
- 연속 SlideIn/Pop은 진행 중 위치·배율·opacity에서 이어진다.
- DoubleAnimation / DoubleAnimationUsingKeyFrames에 144Hz DesiredFrameRate 힌트를 공통 적용했다. SHM 30Hz / UI 데이터 최대 20Hz는 변경하지 않았다.
- 테스트용 App(startRuntime:false)로 WPF 리소스와 Dispatcher만 시작한다. 합성 검증에서 실제 recorder/자격증명/업로드를 시작하지 않는다.

Microsoft는 DesiredFrameRate를 보장값이 아닌 지침으로 설명한다. [공식 문서](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.animation.timeline.desiredframerate?view=windowsdesktop-10.0)

## 자동 검증 / 실측

- Release build: 경고 0 / 오류 0
- Client/UI/SHM/Transport: **73 / 73 PASS**
- Activity/Archive/Compact: **97 / 97 PASS**
- 기존 waiting, position animation, practice/qualifying best lap, participant별 race terminal timing 테스트 PASS 유지
- 공통 카드 6종 × 배율 (1.5, 0.8), (0.8, 1.6), (1, 1): 실제 Visual bounds가 지정 영역을 채움
- 합성 PNG: `work/ui-responsive-check/` (Git 제외). 타워 편집/적용 20행, 전후방, 세션, 대기, Race Control 축약/확장 이미지 시각 확인. 실제 AMS2 스크린샷은 아님.

실제 WPF OverlayWindow들과 20Hz 합성 타이밍 갱신을 함께 실행한 모션 프로브, Render Tier 2:

| 실행 | 타임라인 설정 | 애니메이션 값 변경/초 | Rendering 간격 p95 |
|---|---|---:|---:|
| 1 | system(null) | 143.8 | 7.33 ms |
| 1 | 144 요청 | 143.7 | 7.48 ms |
| 2, 최종 빌드 | system(null) | 144.0 | 7.28 ms |
| 2, 최종 빌드 | 144 요청 | 142.8 | 7.59 ms |

각 단계 7초 중 첫 1초 제외. 같은 RenderingTime의 중복 콜백을 제외하고, 실제 Transform 값 변경도 별도로 센다. 이 프로브는 타워 전체의 반복 이동이며 실제 추월/황색기 애니메이션의 GPU present 측정은 아니다. 게임 부하에서의 프레임 드롭도 측정하지 않는다. system과 144 요청 차이가 유의미한 개선을 보여주지 않았으므로 **144 설정 자체가 끊김을 해결했다는 주장은 하지 않는다.**

## 재현 명령

저장소 루트 PowerShell:

```powershell
.\work\dotnet8\dotnet.exe build .\AMS2KRLeague.sln -c Release --no-restore
.\work\dotnet8\dotnet.exe run --project .\tests\AMS2LeagueClient.Tests\AMS2LeagueClient.Tests.csproj -c Release --no-build
.\work\dotnet8\dotnet.exe run --project .\tests\AMS2LeagueActivity.Tests\AMS2LeagueActivity.Tests.csproj -c Release --no-build
# 선택: PNG를 생성하는 합성 레이아웃 테스트
.\work\dotnet8\dotnet.exe run --project .\tests\AMS2LeagueClient.Tests\AMS2LeagueClient.Tests.csproj -c Release --no-build -- --capture-layout work/ui-responsive-check
# 선택: 합성 창을 잠시 표시하며 약 14초간 측정. 게임 조작/서버 전송 없음.
.\work\dotnet8\dotnet.exe run --project .\tests\AMS2LeagueClient.Tests\AMS2LeagueClient.Tests.csproj -c Release --no-build -- --motion-probe
```

## 사용자 실게임 확인

기존 오버레이를 정상 종료한 뒤 `src/AMS2LeagueClient/bin/Release/net8.0-windows/AMS2LeagueClient.exe`를 실행한다. 설치된 v0.3.0은 이번 수정본으로 자동 교체되지 않았다.

1. 레이아웃 편집에서 타워를 위아래로 늘리고 줄인다. 마지막 플레이어 행이 영역 밖에서 날아오지 않고, 추가 행이 즉시 보이는지 확인한다.
2. 다른 카드를 가로만/세로만 조절하고 저장한다. 편집 중 내용과 적용 후 내용이 일치하는지 확인한다.
3. 동일 황색기가 유지되는 동안 등장 효과가 반복되지 않고, 실제 새 메시지 또는 깃발 변경 때만 반응하는지 확인한다.
4. 게임 부하에서 여전히 끊기면 해당 구간의 PresentMon/ETW 실제 present/frame-time 측정이 필요하다. **인게임 120 FPS: NOT VERIFIED.**

AMS2 조작, Cafe24 배포, 서버/API/DB/Compact 프로토콜 변경, 운영 데이터 삭제, 버전 변경, 커밋/릴리스는 하지 않았다.

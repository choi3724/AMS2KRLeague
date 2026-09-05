# Changelog

## 0.3.0 — 2026-09-05

- Timing Tower에 F1 중계 그래픽 스타일 전환 추가: 순위 획득/상실 시 행 플래시(민트/레드)와 순위 숫자 롤, 세션 최속 랩 `BEST` 시 보라색 스윕, 상태 배지 팝, 타워 최초 표시·참가자 진입 시 좌측에서 순차 슬라이드 인
- 전후방 패널은 앞차/뒷차 교체 시 각 방향에서 슬라이드 인, 거리 유불리 색상 반전 시 팝
- 세션 카드의 랩 카운터와 순위 값 롤, 현재·섹터 타임 패널의 완주 랩·개인 최고·섹터 완료 팝
- 이벤트 카드와 Race Control 배너를 슬라이드 인/아웃과 액센트 바·플래그 스윕으로 교체하고, 종료 애니메이션이 끝날 때까지 창을 유지
- 행 opacity는 계속 애니메이션하지 않으며 주행 중 참가자는 절대 dim 처리하지 않음
- 오버레이별 켜기/끄기 체크박스를 레이아웃 편집 모드와 무관하게 항상 사용 가능하게 하고, 변경 즉시 `overlay-layout.json`에 저장. 시작 시 저장된 상태를 체크박스에 반영하고 `모두 켜기`/`모두 끄기` 추가
- 서버 저장 원본 기준 리플레이 전송 밀도 실측 도구(`work/replay-cadence-audit`)와 보고서 추가
- Compact 리플레이 downsampling 상수 4종(progress/world/extension/battle)을 `TelemetryArchiveOptions`로 옵션화하고 `CompactTelemetryChunkStore`·runtime archive factory에 전달. 범위는 archive 5 Hz gate 이상. 단위 테스트 추가
- cadence별 전송량 실측 도구(`work/replay-cadence-cost`) 추가: 60분/32대 fixture에서 world 5,000→500 ms 시 리플레이 wire 302,317→860,539 B(+184.6 %), 실제 리그 세션 약 +53 %
- Compact 리플레이 world cadence 기본값을 5,000 ms에서 500 ms로 변경해 2D 트랙 리플레이용 위치 밀도를 확보. 리플레이 업로드는 fixture 기준 +184.6 %, 실제 리그 세션 약 +53 % 증가하며 P024 512 KiB fixture 목표는 넘고 1 MiB 한계 안에 있음
- 서버로 전송하는 프로토콜/스키마와 Cafe24 API/DB 계약은 변경 없음(irregular-time block으로 기존 decoder 호환)

## 0.2.3-beta.3 — 2026-09-03

- GT3 등 주요 차량 클래스에 AMS2 HUD 계열의 고정 배지 색상과 명시적 fallback 색상 적용
- Timing Tower의 클래스·타임 글꼴, 행 높이와 열 폭을 함께 조정해 가독성과 잘림 방지 개선
- 상태 변경 애니메이션이 행 전체를 35% opacity로 낮추던 원인을 제거해 주행 중 참가자의 잘못된 회색 표시 수정
- `RET`/`DNF`/`DSQ`/연결 끊김만 비활성 스타일로 표시하고 Pit·일시적 timing 지연·완주는 밝기 유지
- 앞차·뒷차 거리 변화 색상을 RED=불리, BLUE=유리 규칙으로 통일하고 2 m hysteresis 적용
- 단순 `lapsCompleted` 차이로 즉시 `LAP 1`을 표시하던 오류를 누적 Race Progress 및 연속 2회 확인 방식으로 수정
- Track Length 또는 유효 진행 거리가 없으면 Lap Gap을 추정하지 않고 meter/gap 표시로 fallback

## 0.2.3-beta.2 — 2026-09-02

- 멀티플레이 대기 화면 설계 높이·여백을 보정하고 실제 WPF 배치 경계 검증을 추가
- Timing Tower가 20 Hz 값 갱신마다 행 컨테이너를 교체해 순위 이동 애니메이션이 즉시 끊기던 회귀 수정
- Practice/Qualifying 참가자 완료 시 AMS2 participant Best Lap을 표시하고 유효 기록이 없으면 `--` 표시
- Race 참가자별 `FIN` 전환을 적용해 선두 완주 뒤 주행 중인 후속 차량은 계속 갱신하고, 개별 완주 시 즉시 고정
- `DNF`/`RET`/`DSQ` 이후 남은 sector 합계를 임의 시간처럼 표시하지 않도록 수정
- 실제 멀티플레이 2회, Compact public chunk 72개의 Client→Cafe24→GET 원본 해시·바이트·decode를 대조
- 서버 저장 원본만으로 14명 참가자, Lap, Position History, Race Story와 2D movement 재처리 검증
- 실제 수집의 CadenceMissed 손실은 숨기지 않고 `PARTIAL`로 유지하며 beta 안정화 판단 자료로 기록

## 0.2.3-beta.1 — 2026-09-02

- 실제 멀티플레이어·장시간 Race·Incident 검증을 위한 P024 Compact Telemetry Closed Beta
- 고주기 JSON 대신 immutable fixed-schema `A2CT V1` binary와 gzip 전송/저장을 적용
- Replay adaptive cadence, Driver Fast/Motion/Slow/Change, Race Story, Track Geometry, Incident stream 추가
- `LOSS_LEDGER_V1` 뒤에 `ATTEMPT_FINALIZE_V1`을 마지막으로 기록하는 acknowledged close 경로 추가
- outer queue, worker, serialization, disk, cadence, finalize 손실을 attempt ledger에 보존하고 손실 시 `PARTIAL` 처리
- Shared Memory useful field `161/161` lineage와 binary ordinal/scale/privacy 계약 문서화
- private Driver analytics는 local durable archive에만 남기고 owner authority 확보 전 서버 업로드 차단
- 60분/32대 fixture에서 `465,279 B` wire, 11/11 offline reprocessing과 coaching fidelity 통과
- Cafe24 Application `1.6.0` / schema `15`와 public Compact 원본 `.a2ct.gz` 저장 계약 대응

## 0.2.2 — 2026-09-02

- 순위 타워, 전후방 거리, 현재/섹터 타임을 독립 overlay window로 분리
- 세션 정보, 이벤트, Race Control, 멀티 대기 화면을 포함한 각 UI의 개별 이동·크기 조절과 해상도 비율 기반 영구 저장 추가
- 레이아웃 편집 화면에서 순위 타워, 전후방 거리, 현재/섹터 타임 등 UI를 구성 요소별로 끄거나 다시 켜는 기능 추가
- 상태창에 레이아웃 편집/저장·잠금 및 기본 위치 복원 기능 추가
- Timing Tower의 `AMS2 LEAGUE · TIMING`, `리그 순위` 헤더를 제거하고 15개 드라이버 행에 공간 재배분
- 드라이버 이름을 24 px로 확대하고 차량 클래스 및 현재 랩 진행 시간을 행에 추가
- 전후방 차량 글꼴을 확대하고 거리가 증가하면 파란색 `▲`, 감소하면 빨간색 `▼`와 같은 색의 거리 값을 표시
- 일반 황색·이중 황색은 `mHighestFlagColour`로, 전 코스 황색기는 별도 `mYellowFlagState`로 판정해 세 상태를 독립 표시
- 클라이언트 전송 payload의 의미, 서버 정규화/원문 보존, 개인정보 범위와 멀티플레이어 판정 한계를 기록한 데이터 전송 보고서 추가
- 독립 배치의 해상도 변환·경계 제한, 타워 필드, 편집 중 click-through 해제와 종료 후 복원을 자동 검증

## 0.2.1 — 2026-09-01

- 주요 오버레이 패널·글꼴·간격을 약 80% 크기로 축소하고 1920×1080, 2560×1440, 3440×1440 앵커를 고정
- 일반 황색기와 이중 황색기 의미를 분리하고 검증되지 않은 전 코스 황색기 추정을 제거
- 첫 실행 시 로그인 없이 설치별 익명 자격증명을 자동 발급받아 Windows DPAPI로 보호
- 모든 멀티 참가 세션에서 Player/Host 관찰값을 동일한 Session Witness 계약으로 로컬 불변 저장·오프라인 큐 전송
- 동일 세션 다중 증인 그룹화, ±30초 시계 허용, 재시작 attempt 분리, 충돌 보존을 위한 서버 계약 추가
- 개인 활동/Time Attack과 공식 리그 결과 권한을 분리하고 모든 Witness 결과를 관리자 승인 전 `UNCLASSIFIED`로 유지
- Cafe24 FastCGI에서 표준 Authorization이 제거되는 경우를 위한 HTTPS 전용 호환 인증 헤더 추가
- 패널 팔레트를 WPF Brush 리소스로 중앙화하고 실제 OverlayWindow 생성 회귀 테스트 추가

## 0.2 — 2026-09-01

- 일반 실행 시 한국어 첫 실행 상태창과 제품 버전, AMS2/Shared Memory/서버/계정 상태 표시
- 사용자 로그와 활동 데이터를 `%LOCALAPPDATA%\AMS2KRLeague` 아래로 통일
- Player pairing credential을 평문 JSON 대신 Windows DPAPI CurrentUser로 보호
- 기존 평문 bearer 설정이 있으면 보존 가능한 공개 설정은 유지하면서 보호 저장소로 제한적 이전
- 0.1.x의 로컬 경기 이벤트는 더 이상 신뢰하지 않고, Canary 또는 명시적 사용자 식별 필드가 남은 설정은 자격만 무효화해 재연결 요구
- 실제 테스트 닉네임과 개발 PC 절대 경로가 제품 DLL/공개 패키지에 남지 않도록 정리
- Public Player에는 Host Recorder 경로·공식 결과 endpoint·Host credential을 포함하지 않는 경계 테스트 추가
- 서버가 오프라인이거나 계정이 연결되지 않아도 Shared Memory와 오버레이를 독립 실행
- .NET 8 `win-x64` self-contained Portable ZIP 및 일반 사용자용 per-user Installer 패키징 추가
- 기존 Timing Tower, Session Card, Race Control, Waiting Overlay 레이아웃 기준 유지

## 0.1.1 — 2026-09-01

- General Race 개인 결과와 Time Attack 랩의 Player 활동 캡처 추가
- Shared Memory v14의 root 차량, 개인/월드 최고 기록, 날씨, mandatory pit, private session 메타데이터 파싱 추가
- 완료 활동의 로컬 불변 저장, 충돌 격리, 재시작 가능한 업로드 대기열 추가
- 페어링된 Player bearer 전용 HTTPS 업로드와 scheduled event bootstrap 추가
- 30 Hz telemetry callback single-flight, detach 직렬화, 종료 시 callback drain 및 비동기 로그 처리 추가
- 멀티플레이어 메뉴·세션 종료 전환에서 주행 타워 대신 compact Waiting Overlay 표시
- 남은 시간 `-1` 순간의 동일 세션 3초 제한 유지와 비추정 종료 상태 표시
- Race Control을 Session 카드 아래로 이동하고 history-only 빈 카드와 상태 라벨 잘림 제거
- 멀티플레이어 grid/menu/result 전환을 하나의 Player race attempt로 유지하고 terminal 결과를 보존
- Player 전용 telemetry/UI 15개와 활동 캡처·큐·payload 33개 테스트를 공개 빌드에 포함
- Release build/test/self-contained publish/ZIP 검증을 `build-release.ps1`와 CI에 통합
- 웹서비스/server, Host Recorder, Host 인증 형식과 내부 운영 산출물은 공개 범위에서 제외

## 0.1.0 — 2026-09-01

- 한국어 Player Overlay 첫 공개 버전
- AMS2 Shared Memory v14 읽기 전용 연동
- League Classification Timing Tower와 Safety Car 제외
- 트랙 진행거리 기준 앞차·뒤차 정보
- 세션 정보, 랩/섹터 타이밍, Race Control, 동적 이벤트 UI
- 3440×1440 실화면 검증
- click-through, no-activate, bounded multi-window overlay

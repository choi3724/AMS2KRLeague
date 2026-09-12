# 2026-09-11 수신 누락 및 계기판 부하 진단

## 사용자 증상과 현재 증거

Test-4의 싱글 레이스에서 표시값이 누락되고 심하게 끊어지며, 연습에서도 오버레이 애니메이션이 느리다는 보고다. 실행 중인 프로세스의 경로를 확인했으며 Test-4 한 개가 실행 중이었다. 읽기 전용 조사 시 AMS2 게임 프로세스는 이미 종료됐다. 프로세스 및 게임 설정을 변경하지 않았다.

현재 HEAD는 85442a7c7b901907623aaecb55f4285d802e7692, v0.7.1이다. 이번 계기판 변경의 수신기 SharedMemoryReader 및 PlayerOverlayCoordinator diff는 없었다. 기존 규칙 문서와 다른 미커밋 변경은 보존했다.

원래 추가한 코드의 핵심은 AvanteClusterView.cs의 WPF 렌더러와 OverlayWindow.xaml.cs의 기존 DrivingTelemetrySample 전달 연결이다. 수집/서버 전송 주기를 변경한 것은 아니지만, 수신값 처리와 실게임 부하를 충분히 검증하지 않은 채 반복 실행본을 전달했다.

실제 로그 client-20260911-112354-232-bd4f14.log:
- 11:25:37 참가자 수 49.
- 11:25:59 shmHz=29.7, uiHz=19.8, drivingHz=40.6.
- 11:27 이후 shmHz가 대부분 0~1 수준으로 저하.
- 11:29:44 SEQUENCE_CONSISTENCY retryDelta=2697, dropDelta=899 (30초).
- 같은 구간 InconsistentSnapshot 메시지가 반복됐다.

shmHz는 전체 읽기 성공률, uiHz는 표현 갱신 횟수, drivingHz는 빠른 표시값 갱신 횟수다. 어느 것도 게임의 실제 렌더 FPS나 모니터 주사율을 뜻하지 않는다.

Honeycam GIF는 369프레임/9.83초, 프레임 지연 20~60ms다. 123번째 프레임에서 계기판 기어/속도 및 페달 값이 대시로 누락된 것을 확인했다. GIF 파일 자체의 프레임 수를 게임 FPS로 해석하지 않았다.

## 수신 경로에서 확인한 차단

SharedMemoryReader.TryRead는 홀수 시퀀스를 거부하며 복사 전/복사본/복사 후 시퀀스 일치를 검사한다. 실패하면 SpinWait(32) 후 최대 3회 시도한다. TryReadDriving도 같은 홀수/변경 검사로 값을 거부한다. DrivingFrame은 전체 snapshot이 500ms보다 오래되면 빠른 읽기를 시작하지 않으며, 새 표시 데이터가 150ms 동안 없으면 null을 전달한다. 이번 GIF의 누락과 로그의 드롭 급증은 이 경로와 일치한다.

단, 시퀀스가 홀수로 오래 유지됐는지, 복사 중 변경됐는지는 기존 로그가 구분하지 않는다. 정확한 원인은 아직 미확정이다. 검사 완화나 임의 정상값 대입은 하지 않았다. 게임 재실행 후 사용할 읽기 전용 work/read-shm-sequence.ps1을 준비했다. 사용자에게 동일 싱글 레이스 재실행을 요청했다.

## 계기판 렌더링에서 확인하고 수정한 부하

기존 코드가 매 샘플/보간 갱신 때 고정 눈금 Pen과 명령을 다시 만들고, 매 샘플에 같은 바늘 Geometry를 파싱하며, 고정 RPM 숫자와 단위를 다시 그렸다.

AvanteClusterView.cs에서 고정 눈금/숫자/단위를 별도 DrawingVisual로 유지하고, 바늘 형상은 데이터 유무가 바뀔 때만 생성하며 회전 Transform을 재사용하도록 수정했다. 기능/폰트/좌표/색상/전송 경계는 유지했다.

기존 HudResourceProbe에 avante-panels 모드를 추가했다. 고정 RPM 대신 변화하는 RPM을 공급해 렌더링 부하를 측정했다. 합성 데이터/로컬 데스크톱 측정으로 실제 게임·VR 결과가 아니다.

| 지표 (avante-panels) | 변경 전 | 변경 후 |
|---|---:|---:|
| 표시 업데이트 Hz | 59.9 | 59.9 |
| 할당 MB/s | 66.785 | 38.535 |
| update p95 ms | 1.29 | 0.84 |
| CPU 단일 코어 환산 % | 74.34 | 63.84 |
| 렌더 콜백 Hz | 88.4 | 88.8 |

두 실행에서 다른 모드의 렌더 콜백 빈도는 144와 60 수준으로 달라졌다. 따라서 이 결과를 프레임률 개선이나 완전히 동일한 표시 환경의 증거로 사용하지 않는다. 고정 요소 반복 생성 제거와 할당량 감소만 확인했으며, 심한 싱글 레이스 누락 해결은 별도로 남아 있다.

## 상태

수신 장애: 미해결, 실게임 읽기 측정 대기. 실게임 60fps/실제 VR: 미검증. 릴리스/설치/실행 중인 Test-4 교체는 하지 않았다. 소스의 국소 렌더링 개선과 재현용 측정만 수행했다.

근거: harness/reports/avante-performance-before.log, avante-performance-after.log, avante-performance-gate.log; 작업 폴더 work/telemetry-drop-evidence.log 및 work/gif-diagnostic/123.png.

자동 회귀: verify.sh 종료 코드 0, Release 빌드 경고 0/오류 0, Client 147 passed/0 failed/147 total, Activity 111 passed/0 failed/111 total (3264ms). 자동 회귀 통과는 위 수신 장애 해결을 의미하지 않는다.

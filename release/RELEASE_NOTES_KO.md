# AMS2 리그 오버레이 0.8.0 업데이트

### 표시 경로 개선

- 모니터 HUD의 기본 투명 표시를 DWM glass 합성 경로로 변경했습니다. 기존 layered WPF 창에서 확인된 동기 GPU→CPU readback 경로를 줄이기 위한 변경입니다.
- DWM glass를 사용할 수 없을 때는 기존 layered 경로로 돌아갑니다. 문제가 생기면 `--monitor-layered`를 실행 인자에 넣어 즉시 기존 경로를 선택할 수 있습니다.
- 0.7.7의 페널티 표시, N 눈금·문자·상태 재사용, 표시용 그래프 이력 개선을 유지합니다. 기록 수집·전송은 변경하지 않았습니다.

### 시험 기능 (기본 꺼짐)

- 실행 인자 `--monitor-retained-n`: N 계기판을 DirectComposition으로 그리는 시험 경로
- 기존 `--monitor-glass` 인자는 호환을 위해 허용하며, 이제 인자 없이 실행해도 같은 glass 경로를 사용합니다.
- `--monitor-layered`는 N 시험 인자보다 우선합니다.

### 검증 범위와 알려진 제한

- 기존 동일 입력 고부하 비교에서 연속 HUD의 DWM 전달 p95는 layered 약97ms, glass 약55ms, layered 복원 약97ms였습니다. 이 지표는 실제 화면 FPS가 아닙니다.
- 자동 회귀 테스트와 Release 빌드 경고·오류 0 확인
- 실게임·VR 실기기·물리 트리플·혼합 DPI·장시간 동작은 NOT TESTED
- 고부하 glass에도 60Hz 표시 목표를 넘는 지연이 남아 Monitor 성능 RED를 유지합니다.
- N 계기판 디자인·RPM 정책과 수집·기록·전송 동작 유지

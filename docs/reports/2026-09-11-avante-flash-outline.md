# 아반떼 N 계기판 점멸·색상·투명 외곽 수정 — 2026-09-11

## 기준선과 원인
- 앞서 추가한 두 계기판 및 미커밋 업데이트 안내 수정 위에서 작업. 버전은 0.7.1로 유지.
- 레드존 색상 전환만 있었고 점멸 타이머가 없어 RPM이 고정되면 계속 같은 밝기로 표시됐다.
- 경고등에 165/255 알파 색을 덧씌워 회색 원본과 섞였다.
- 일반형은 사각형 영역으로만 잘라 원형 바깥의 배경이 남았다.
- 변경 전 화면: harness/reports/avante-delivery-captures/avante-normal-6500.png.

## 변경
- AvanteClusterView.cs: 레드존에서 125ms마다 밝음/어두움 전환. 원본 HTML과 같은 250ms 전체 주기(초당 4회 점멸).
- 새 sample이 계속 들어와도 점멸을 매번 다시 시작하지 않음. 레드존 이탈, null sample, 숨김/언로드 시 중지.
- 경고등·따라오는 RPM 빛·안쪽 링만 점멸. 숫자·바늘·원본 배경은 유지.
- 회색과 섞이는 반투명 경고색 대신 선명한 색상 그라데이션 사용. 원본 PNG 파일 자체 변경 없음.
- 일반형: 원형 외곽과 하단 상태 영역을 합친 윤곽으로 모든 그리기 레이어를 자름. 원 밖은 알파 0. 확장형은 좌우 정보를 유지.
- AvanteClusterTests.cs: 양쪽 점멸 상태를 실제 Dispatcher 시간 진행으로 확인. 숨김/레드존 이탈 중지 및 실제 렌더링 모서리 알파 검사.
- 데이터 수집·기록·서버 전송 주기 변경 없음.

## 증거 및 배포 범위
- 중간 WPF 캡처: harness/reports/avante-flash-captures/avante-flash-True.png, avante-flash-False.png.
- 최종 게이트: harness/reports/avante-flash-final.log. scripts/verify.sh 종료 코드 0, 빌드 경고 0/오류 0.
- RESULT: 146 passed, 0 failed, 146 total
- RESULT: 111 passed, 0 failed, 111 total (3128 ms)
- 최종 선명한 경고색과 점멸 캡처: harness/reports/avante-flash-delivery/avante-flash-True.png 및 avante-flash-False.png.
- 새 self-contained 테스트 패키지 publish 종료 코드 0, --capture-all 실행 확인 종료 코드 0.
- 기존 실행 중인 프로그램과 설치 파일을 교체하지 않고 별도 Avante-Cluster-Test-2 테스트 폴더로 제공.
- 실제 게임 부하 60fps 및 VR 실기기 검증 NOT RUN. 커밋·태그·GitHub 릴리스 없음.

# 아반떼 N 계기판 일반형·확장형 — 2026-09-11

## 기준선
- E:\AMS2 KRLEAGUE\AMS2KRLeague / v0.7.1 HEAD 85442a7.
- 기존 미커밋 규칙 문서 4개 및 자동 업데이트 안내 수정은 보존.
- 작업 전 scripts/verify.sh는 Race batch uploads after whole-field finish and preserves late joins에서 실패했다. 후속 전체 검사에서 해당 테스트 PASS를 확인했으며 전송 코드는 수정하지 않았다.
- 보호: 기존 계기판·속도계·텔레메트리·타워, 저장된 레이아웃, 수집/전송 주기, 업데이트 안내.
- 현재 요청으로 일반형·확장형 추가와 해당 미리보기·글자·색상 변경만 승인됨. 커밋·릴리스 요청 없음.

## 요구사항
- REQ-AVANTE-01: 일반형 908×750 원본 좌표 영역, 확장형 2048×750 전체 영역. 가로세로 비율 유지. 각각 독립 표시·위치·크기 저장, 기본 OFF.
- REQ-AVANTE-02: 첫 선택 화면에 두 형태를 같은 줄의 미리보기 카드로 추가. 기존 항목 유지.
- REQ-AVANTE-03: 첨부 PNG를 그대로 사용. WOFF에서 sfnt 테이블을 복원한 원본 글꼴 사용. 숫자 기울기 -10도 shear, 좁은 자간, 밝은 윗면/푸른 아랫면, 1.1 원본 좌표 단위의 얇은 기어 그라데이션 테두리와 2단위 그림자.
- REQ-AVANTE-04: 5,000 미만 흰색, 5,000 이상~6,000 미만 노란색, 6,000 이상 붉은색. 바늘 뒤 그라데이션과 외곽 경고등, 안쪽 링 동기화. 바늘 자체는 첨부 예시의 주황/붉은 윤곽 유지.
- REQ-AVANTE-05: 기존 display-only DrivingTelemetrySample과 전체 스냅샷으로만 표시. 수집 Hz/기록 cadence/서버 payload/HTTP 요청 빈도 변경 없음.
- REQ-AVANTE-06: WPF retained drawing + 65ms 보간. 숨겨진 새 패널에는 sample 갱신을 보내지 않음. 새 브라우저·WebView·캡처 루프 없음.

## 원본 및 리소스
- 원본 파일: F:\Users\choi3\Downloads\AvanteN_Cluster_Preview (2).html
- 원본 SHA256: 2442ddc672499cecd35a84176d6dd711d01c3b7ecd9df0349af30bbf08b1c4ab
- 배경은 원본 base64 PNG 그대로. 글꼴은 WOFF 압축 해제만 수행하고 glyph 윤곽/이름 변경 없음.
- Assets/Fonts/AvanteN-LICENSE.txt에 첨부된 저작권·OFL 문구 보존.
- 원본 HTML에 삽입된 AdGuard 외부 스크립트는 제품에 포함하지 않음.
- 새 이미지 생성 없이 원본 배경 위에 기존 HTML 좌표 기반 WPF 색상·바늘·숫자 레이어를 사용.

## 데이터 표시
- 속도/기어/RPM/최대 RPM: 기존 빠른 화면용 sample.
- 기어 0=N, -1=R. 미수신은 —.
- 오일/냉각수 온도, 외기온, 토크, 연료량, 누적 거리: 기존 viewed vehicle/session snapshot.
- 참가자와 시각이 일치하는 보조 수치만 표시. generation/참가자 변경 및 null sample에서 보조 상태를 비움.
- ABS: 기존 AntiLockActive 표시. TCS와 피트 리미터: mCarFlags 비트 6/3. [공개 SHM 헤더](https://github.com/viper4gh/CREST2-AMS2/blob/master/SharedMemory.h) 참조.
- 터보: DATA_DICTIONARY.md의 단위 미확정에 따라 실제 raw 수치를 bar로 단정하지 않고 — bar. 미리보기 예시만 1.1 bar.
- 원본 PNG의 고정 연료/수온 게이지 채움은 가리고, 연료는 관측 비율로 채움. C/H 눈금의 온도 기준이 없어 수온 막대는 미확정 상태로 두고 숫자 온도를 표시.
- 원본의 437km 주행 가능 거리, CUSTOM1 등의 예시를 실데이터로 전송/표시하지 않음.

## 검증
- 경계 4999/5000/5999/6000, N 기어, 결측 표시, 독립 레이아웃, 양쪽 형태 캡처, 바늘이 중간각에서 목표각으로 이동하는 WPF animation 검사.
- 첫 화면 860×660 / 1120×900: 14개 항목, 16개 비어 있지 않은 미리보기. 기존 선택/색상/스크롤 회귀 검사 유지.
- 캡처: harness/reports/avante-captures-final/avante-normal-4500.png, avante-normal-5500.png, avante-normal-6500.png, avante-expanded-6500.png 등.
- 합성 표시 갱신 600회 평균 0.616~1.786ms 관측. 데이터 처리 CPU 시간으로, 모니터 주사율이나 실게임 FPS가 아니다.
- 실제 AMS2 부하 60fps, Quest 3 / Virtual Desktop 실기기 검증 NOT RUN.
- 원본 HTML 브라우저 열기는 브라우저 URL 정책에 의해 차단되어 우회하지 않음. 원본 코드·추출 PNG와 새 WPF 렌더링으로 확인.

## 최종 게이트
- scripts/verify.sh 종료 코드 0. Release build 경고 0 / 오류 0.
- RESULT: 146 passed, 0 failed, 146 total
- RESULT: 111 passed, 0 failed, 111 total (3260 ms)
- 최종 결과는 harness/reports/avante-final.log 참조. 소스·테스트 diff --check 통과.
- 후속 실제 WPF 렌더링 캡처: harness/reports/avante-delivery-captures. 일반형·확장형 카드가 같은 줄에 표시됨.
- 별도 self-contained 테스트 폴더: C:\\Users\\User\\Documents\\Codex\\2026-09-08\\plugin-computer-use-openai-bundled-play-2\\work\\Avante-Cluster-Test. 테스트 실행.cmd로 실행하며 이 실행에서만 자동 업데이트 비활성.
- publish 최초 시도는 win-x64 assets target 누락으로 실패. 해당 런타임 대상 restore 후 publish 종료 코드 0.
- 패키지 --capture-all smoke 종료 코드 0, 19개 파일 생성. 게임·서버 실행 없이 런타임 로딩 확인.
- 실제 경기·VR 및 터보 단위/C/H 온도 스케일은 미확인 상태.
- 커밋·태그·푸시·GitHub 릴리스 없음.

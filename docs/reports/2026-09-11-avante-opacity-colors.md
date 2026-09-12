# Avante 계기판 색상·배율·투명도 수정

기준 HEAD: 85442a7c7b901907623aaecb55f4285d802e7692 (v0.7.1). 기존 미커밋 변경을 보존했다. 커밋·태그·배포하지 않았다.

## 적용

- 원본 PNG의 회색 분할 테두리를 기본 상태로 사용한다. 기본 상태에 덧그리던 흰색 경고 테두리를 제거했다. 노랑/빨강 경고도 원본 이미지의 회색 분할 부분에서 추출한 마스크에 색상을 입힌다.
- 원본 청색 이미지 위에 노랑·빨강 RPM 배경 구간을 복원했다. 최대 RPM의 62.5%부터 노랑, 75%부터 빨강: 8,000 기준 5,000/6,000, 18,000 기준 11,250/13,500. 눈금은 최대 RPM을 포함하는 1,000 단위 상한을 사용한다.
- 빨강 구간 점멸과 일반형 외곽 투명 클리핑을 유지했다.
- ABS/TCS/PIT LIMITER를 모두 같은 33 크기로 맞추고, 세 줄 아이콘도 동일 크기로 그린다. km/h는 원본 HTML 좌표 (1157,607), 크기 22로 복원했다.
- 14개 오버레이 선택 카드에 각각 투명도 0~100% 슬라이더를 추가했다. 즉시 적용 및 저장하며 위치 초기화 후에도 유지한다. 편집 테두리는 투명도 영향을 받지 않는다.
- 데이터 수집, 서버 전송 빈도와 형식을 변경하지 않았다. 화면 애니메이션과 모니터 주사율은 별개다.

## 검증

전용 렌더링 검증 종료 코드 0. 일반형/확장형, 회색·노랑·빨강 상태와 하단 글자/단위 위치를 캡처 확인했다. 860×660 선택 화면에서도 투명도 조절기를 확인했다. 최대 RPM 6,000/7,500/8,000/18,000의 경고 경계 테스트를 통과했다.

```text
PROOF opacity=1 VR compositor alphaMax=255
PROOF opacity=0.5 VR compositor alphaMax=128
PROOF opacity=0 VR compositor alphaMax=0
PROOF gallery construction ms=272
RESULT: 147 passed, 0 failed, 147 total
RESULT: 111 passed, 0 failed, 111 total (2995 ms)
VERIFY_EXIT=0
```

Release 빌드: 경고 0, 오류 0. 최초 전체 게이트는 앞서 실행 중이던 캡처 테스트의 DLL 잠금으로 실패했다. 캡처 프로세스 종료 후 재실행에서는 기존 Race batch uploads after whole-field finish and preserves late joins 테스트가 실패했다. 해당 테스트 코드는 변경하지 않았으며 다음 전체 재실행에서 통과했다. 간헐 실패가 해결됐다는 의미는 아니다.

근거: harness/reports/opacity-preview.log 및 opacity-preview/*.png, avante-opacity-gate.log, avante-opacity-gate-final.log, avante-opacity-gate-retry.log.

별도 self-contained 테스트 패키지: C:/Users/User/Documents/Codex/2026-09-08/plugin-computer-use-openai-bundled-play-2/work/Avante-Cluster-Test-3. 테스트 실행.cmd는 자동 업데이트를 비활성화한다. 패키지 캡처 실행 종료 코드 0. 기존 Test-2는 보존했다.

실제 게임 부하에서 60fps 이상 유지 여부와 실제 VR 기기는 미검증이다. VR 합성 픽셀의 알파 검사는 실제 기기 검증을 대체하지 않는다.

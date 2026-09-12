# 2026-09-11 테두리 점등 수정 범위

기준 HEAD: 85442a7c7b901907623aaecb55f4285d802e7692. 기존 미커밋 변경 보존.
변경 파일: AvanteClusterView.cs, AvanteClusterTests.cs, 이번 보고서.
보호 대상: 기존 일반형/확장형 외곽, 폰트/단위, 투명도, 데이터 수집/서버 전송, 다른 오버레이.

영상은 브라우저에서 직접 재생 및 프레임 이동으로 확인했다.
- https://www.youtube.com/watch?v=kC_xkpUOjW0 : 0초/4초/5초 전체 회색, 6초 부근부터 양쪽 아래 흰색, 뒤이어 주황/빨강. 촬영 흔들림이 있어 정밀 RPM 경계 측정은 불가.
- https://www.youtube.com/shorts/3gVvkvw-VfE : 21~23% 탐색 위치(약5~6초)에 회색→아래 흰색→주황, 고회전에서 빨강 확인. 모든 칸의 정확한 RPM 경계를 동영상만으로 확정하지 않았다.

사용자의 기존 명시 경계 8,000 기준 5,000 주황/6,000 빨강과 이번 3칸→4칸 사이 1/3 주황 규칙을 우선 적용한다. 이 조건으로 3칸은 4,500, 4칸은 6,000으로 정했다. 나머지 단계는 4,000/4,250/6,500으로 잠정 조정했다. 영상에서 추출한 정확한 공장 설정값이라는 의미는 아니다. 사용자에게 기존 경계 유지/영상 기준 재조정을 질문했다.

## 검증 결과

수정 전후 scripts/verify.sh 종료 코드 0. Release 빌드 경고 0 / 오류 0.
Client: 147 passed, 0 failed, 147 total.
Activity 수정 전: 111 passed, 0 failed, 111 total (2936 ms).
Activity 수정 후: 111 passed, 0 failed, 111 total (2922 ms).

실제 WPF 렌더링 픽셀에서 좌우 동일 점등 수와 미점등 칸의 기존 픽셀 보존을 검사했다. 최대 RPM 6000/8000/18000 비례 검사 통과. 기존 애니메이션 테스트로 65ms 바늘 보간, 500ms 관측 중 빨강 점멸 두 상태 및 숨김/하강 종료를 확인했다. 게임 부하에서의 FPS 및 실제 VR 기기는 NOT RUN.

PROOF paired segment pixels rpm=3999 left=0 right=0 band=0
PROOF paired segment pixels rpm=4000 left=1 right=1 band=0
PROOF paired segment pixels rpm=4250 left=2 right=2 band=0
PROOF paired segment pixels rpm=4500 left=3 right=3 band=0
PROOF paired segment pixels rpm=4999 left=3 right=3 band=0
PROOF paired segment pixels rpm=5000 left=3 right=3 band=1
PROOF paired segment pixels rpm=5999 left=3 right=3 band=1
PROOF paired segment pixels rpm=6000 left=4 right=4 band=2
PROOF paired segment pixels rpm=6500 left=5 right=5 band=2
PROOF red flash both phases; hidden/below-red stop; normal outline corners transparent and footer retained

근거: harness/reports/avante-segments-before.log, avante-segments-after.log, avante-pairs.log 및 avante-pairs/avante-pairs-*.png.

패키지: C:/Users/User/Documents/Codex/2026-09-08/plugin-computer-use-openai-bundled-play-2/work/Avante-Cluster-Test-4. 자동 업데이트 비활성화 실행기를 포함. 패키지 캡처 종료 코드 0, 캡처 19개. 이전 Test-3 보존.

요청 외 제품 변경 0. commit/tag/push/release 수행하지 않음. 단계별 RPM 경계는 잠정 조정값이므로 영상의 공장 설정과 완전히 동일하다고 판정하지 않는다. 최종 PARTIAL: 점등 방식 구현 및 로컬 검증 완료, 실제 게임 확인 및 세부 경계 확정 남음.

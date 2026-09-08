# 배치 편집의 빈 패널 미리보기와 마우스 선택

- REQ-EDIT-01: 편집 중 비어 있는 이벤트·레이스 컨트롤에 가상 미리보기 표시. 실제 이벤트가 있으면 실제 내용을 유지하고, 편집 종료 시 즉시 실제 상태로 복원. 가상 데이터는 원본/기록/업로드에 포함하지 않음.
- REQ-EDIT-02: 편집 중 패널 내부 전체에서 마우스 선택·이동 커서가 유지되도록 투명 픽셀의 입력 통과 문제 수정. 크기 조절 손잡이와 편집 종료 후 클릭 통과 유지. 일반 플레이 UI 배경·저장 배치 유지.
- REQ-EDIT-03: 이벤트 종료 애니메이션 뒤 편집 진입, 편집 중 갱신·실제 이벤트, 저장/취소 복원, 전체 면적 hit test·렌더 alpha·크기 변화를 WPF로 검증. 빌드·두 테스트와 보고서 작성. 2026-09-09 사용자가 0.4.4 릴리스를 요청했으므로 패키지·설치·CI 검증 후 commit/tag/push/정식 Latest 게시.

기준선: HEAD 53b23a5, 공개 v0.4.3, clean. scripts/verify.sh 경고/오류 0, Client 112/112, Activity 102/102. work/validation-edit-preview/baseline/.
범위: OverlayWindow.xaml(.cs), 관련 WPF 회귀 테스트, README 작업 트리 안내와 완료 보고서. 원본 이벤트 모델·캡처·업로드·서버/DB·업데이트 코드 변경 없음.
원인: Windows layered window는 alpha=0 픽셀에서 마우스가 밑 창으로 통과한다. 기존 편집 drag surface의 Background=Transparent와 이벤트 퇴장 후 투명 패널이 조합되어 내부 선택이 어려움.
근거: https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features (Layered Windows).

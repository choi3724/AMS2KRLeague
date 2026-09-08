# 배치 편집 미리보기·입력 영역 수정 — 2026-09-08

## 기준선
- E:\AMS2 KRLEAGUE\AMS2KRLeague, HEAD 53b23a5, 공개 Latest v0.4.3. 변경 전 clean.
- 변경 전 scripts/verify.sh: Release 경고 0/오류 0, Client 112/112, Activity 102/102.
- 보호 대상: 실제 이벤트·레이스 컨트롤 모델, 원본 기록/업로드, 일반 플레이 클릭 통과, 타워 자동 높이와 기존 저장 배치.

## 원인
- 편집 테두리는 보이지만 drag surface의 Background가 완전 투명이었다. 이벤트 종료 애니메이션으로 내용도 투명해지면 내부 픽셀의 입력이 뒤의 게임으로 통과할 수 있었다.
- Windows layered window는 alpha=0 픽셀에서 마우스 메시지를 통과시킨다. 근거: https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features (Layered Windows).

## 요구사항
REQ-EDIT-01 [DONE]
- 배치 편집 중 빈 이벤트 카드에는 ‘이벤트 미리보기 / 개인 최고 기록 / 1:42.350 / 데모’를 표시한다.
- 빈 레이스 컨트롤에는 ‘미리보기 / 황색기’를 표시한다. 좁은 기본 폭에서도 잘리지 않도록 캡처를 확인했다.
- 실제 이벤트가 존재하면 실제 내용을 표시한다. 편집 중 들어오는 갱신에도 빈 패널의 미리보기를 유지한다.
- 편집 진입/종료 시 이벤트 표시 캐시를 새로 적용하여, 종료 애니메이션으로 남은 투명 상태를 제거하고 편집 종료 때 실제 최신 모델로 복원한다.
- 가상 모델은 뷰에만 전달한다. 실제 Shell의 이벤트·레이스 컨트롤을 변경하지 않으며 기록·업로드·레이아웃 파일에 가상 내용을 넣지 않는다.

REQ-EDIT-02 [DONE: WPF 입력 면과 창 스타일 검증]
- 타워와 보조 패널의 편집용 drag surface에 alpha=24의 배경을 적용한다. 편집 chrome이 숨겨지는 일반 플레이에서는 기존 투명 UI가 유지된다.
- 타워·이벤트·레이스 컨트롤 각각의 좌측 내부/중앙/우측 하단 여백 3지점, 합계 9지점에서 렌더 alpha=24, InputHitTest 이동 영역, SizeAll 커서, Win32 clickThrough=false를 확인했다.
- 오른쪽 아래 크기 조절 손잡이는 템플릿 자식을 포함해 hit test로 확인했다. 이벤트 창을 가로 1.25배·세로 1.5배로 변경하고 미리보기 유지 및 렌더를 확인했다.
- 편집 종료 후 clickThrough=true 복원 및 빈 실제 패널 숨김 확인. 저장된 배치에 ‘미리보기’가 없는 것도 확인했다.

REQ-EDIT-03 [DONE]
- 추가 WPF 회귀 테스트 1개. 기존 테스트 삭제/skip 없음.
- 첫 테스트에서는 ResizeGrip 자체만 비교해 템플릿 내부 자식 hit를 실패로 처리했다. 시각 트리의 부모까지 검사하도록 테스트를 수정했다. 그 뒤 Client 113/113 통과.
- 최종 전체 verify.sh의 결과는 아래 최종 게이트에 기록한다.

## 증거
- work/baseline-edit-preview.log 및 work/validation-edit-preview/baseline/.
- work/validation-edit-preview/final-verify.log 및 final/.
- work/validation-edit-preview/final/after/event-edit-preview.png.
- work/validation-edit-preview/final/after/event-edit-resized.png.
- work/validation-edit-preview/final/after/race-control-edit-preview.png.

## 범위와 제한
- 변경 코드: OverlayWindow.xaml 및 OverlayWindow.xaml.cs. 테스트와 작업·사용 안내 문서 외 파일 변경 없음.
- 게임 조작이나 마우스 입력 주입 없이 WPF 실제 창·픽셀·입력 대상·네이티브 스타일을 검사했다. 사용자의 실제 게임 화면 위에서 물리 마우스로 드래그한 실증은 NOT RUN이다.
- 새 릴리스 요청이 없으므로 버전 변경, commit/tag/push/게시, 실제 사용자 설치본 교체는 하지 않았다. 공개 v0.4.3에는 아직 포함되지 않았다.

## 요청하지 않은 변경
- 0건. 일반 플레이의 디자인·기록·서버·DB·업데이트 정책 변경 없음.

## 최종 게이트
- scripts/verify.sh PASS. Release 경고 0/오류 0, Client 113/113, Activity 102/102, 합계 215/215.
- WPF 편집 미리보기, 9지점 alpha/hit test/커서, 크기 조절 및 저장·취소 복원 PASS.
- 작업 트리 상태로 인계. 공개 버전은 v0.4.3 유지.

## 후속 릴리스 요청 (2026-09-09)
사용자가 0.4.4 릴리스를 승인했다. 위 미배포 기록은 구현 완료 당시의 상태이며, 후속 결과는 docs/RELEASE_0.4.4_2026-09-09_KO.md를 참조한다.

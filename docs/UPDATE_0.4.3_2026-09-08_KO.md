# AMS2KRLeague 0.4.3 작업·검증 보고서

## 기준선
- 작업 폴더: E:\AMS2 KRLEAGUE\AMS2KRLeague. 시작 HEAD 5e6aeae, 공개 Latest v0.4.2, clean. git fetch로 최신 원격 확인.
- 변경 전 scripts/verify.sh: Release 경고 0/오류 0, Client 111/111, Activity 102/102.
- 보호 대상: 자동 싱글·멀티 판정과 업로드 필터, 원본 기록/Compact, 페널티, 순위 선택·전이, 사용자 저장 배치, 서버/DB.

## 요구사항
REQ-043-01 [DONE]
- StateText의 BEST 한국어 상태만 최고속 랩으로 변경했다. 다른 랩 패널의 최고 문구와 내부 BEST 값은 유지한다.
- 실제 WPF 타워 캡처에서 한 줄로 표시되고 페널티 열과 겹치지 않는다.

REQ-043-02 [DONE]
- OverlayHudView 높이를 실제 표시 행 수로 계산하고 OverlayWindow의 표시 창 높이도 같은 비율로 축소한다. 최소 2행 공간을 유지한다.
- 표시 높이와 저장된 최대 행 수를 분리했다. 자동 축소로 최대 행 수가 줄어드는 피드백을 막고 편집 때는 저장된 전체 크기를 사용한다.
- WPF 실제 창 측정: 15명 648×608 → 2명 648×114 → 1명 648×114 → 5명 648×228 → 15명 648×608. 각 단계 capacity=15, 표시 행=참가자 수, 마지막 행 경계 안쪽 확인.
- 자동 축소 시 설정 파일 미작성 확인. 2명 상태에서 편집 저장 후에도 최대 15행 보존. 기존 10/20/8/20행 편집·재실행 테스트 통과.

REQ-043-03 [DONE: 실제 게임 실행 중 격리 설치 검증 통과]
- GitHubAutoUpdater의 게임 종료 루프, App의 업데이트 종료 거부, ApplyUpdate의 게임 프로세스 차단을 제거했다.
- 크기·SHA-256 확인과 준비된 설치 도구의 잠금 후 정상 종료/영구 저장 순서를 유지한다. 재시작 인자 마지막에 기존 --background 옵션을 적용하여 상태창이 게임의 포커스를 빼앗지 않도록 한다.
- 같은 설치 경로의 다른 오버레이는 설치를 차단한다. 다른 폴더의 오버레이는 유지한다. 설치 인자의 NOCLOSEAPPLICATIONS/NOFORCECLOSEAPPLICATIONS/NORESTART를 유지한다.
- 게임 파일·프로세스·설정·레지스트리 및 운영 서버/DB를 변경하지 않는다.

REQ-043-04 [게시 준비]
- 사용자가 0.4.3 릴리스를 명시적으로 요청했다. 버전과 설치·릴리스 문서를 갱신했다.
- scripts/verify.sh 변경 후: Release 경고 0/오류 0, Client 112/112, Activity 102/102. 기존 테스트 삭제·skip 없음. 추가 타워 전환 테스트 1개 및 기존 한국어 상태 검증 확장.
- 패키지 생성과 격리 설치, GitHub CI, Latest 게시, 공개 다운로드·해시 결과는 게시 보고서에 확정한다.

## 증거
- work/baseline-0.4.3.log, work/validation-0.4.3/baseline/.
- work/validation-0.4.3/verify-2.log, after/: 전체 게이트 및 창 높이 전환 계측.
- work/validation-0.4.3/after/after/tower-auto-2.png, tower-auto-15.png: 축소/확대 WPF 화면.
- work/validation-0.4.3/build-package.log: 버전 갱신 후 Release 및 공개 패키지 검사.
- 첫 검증에서 테스트 코드의 internal 저장소 접근으로 컴파일 실패했고 JSON 공개 모델 읽기로 수정했다. 최종 게이트와 구분해 실패 로그도 보존했다.

## 호환과 남은 제한
- 이미 실행 중인 0.4.1~0.4.2는 자신의 게임 종료 대기를 따르므로 최초 0.4.3 자동 설치에는 기존 대기가 적용된다. 0.4.3부터 이후 업데이트는 게임 종료 없이 적용한다.
- 설치·재시작 중 오버레이 표시와 수집에 공백이 있다. 원본을 추정하여 공백을 채우거나 무중단 수집이라고 주장하지 않는다.
- 자동 모드 로그 부족/세션 경계 UNKNOWN 보류, 운영 서버의 raceMode 저장·표시 미검증 및 기존 cadence/FPS 제한은 이전 보고서와 같다.

## 요청하지 않은 변경
- 0건. 외부 의존성 추가, 게임 조작, 서버·DB 배포 없음.

## 게임 실행 중 설치 실증
- work/update-install-proof-719dc29e94274674bbb4c79c836c59f8/summary.json 및 work/validation-0.4.3/update-install.log: PASS.
- 공개 0.4.2 ZIP의 해시를 검사하고 새 임시 폴더에 풀어 0.4.3 설치 도구로 교체했다. 0.4.2 자체의 옛 자동 업데이트 대기 루프를 통과했다고 주장하지 않는다.
- gameRunningDuringInstall=true, gameProcessesPreserved=true. 게임 프로세스 PID와 시작 시각이 전후 일치한다. 사용자 게임과 실제 설치본은 종료하지 않았다.
- from=0.4.2, to=0.4.3, helperExit=0, portablePreserved=true, userFilePreserved=true. 재실행 캡처 18개, 예외 로그 없음.
- 이전·이후 오버레이는 기존 --capture-all을 사용해 화면 표시·게임 연결 없이 격리 렌더와 정상 종료를 실행했다.
- Setup 51,270,171 bytes / SHA-256 03a0783af7a65eb1b174f06018e0ad29af36afe8d9e105db2158e5ef9bdf207d.
- ZIP 72,362,913 bytes / SHA-256 fa0f9d9a4059c4ba8385a666ad2058ec9da5e4e4fa414ab0c854d19348241462.
- 공개 폴더/ZIP 검사: 각 466개 파일, 금지 파일 0. 설치 파일 검사 PASS.

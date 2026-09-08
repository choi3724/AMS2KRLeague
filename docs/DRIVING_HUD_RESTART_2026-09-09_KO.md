# 주행 계기판·자동 재실행 구현 결과

후속 그래프·전송 수정과 0.4.5 게시 검증은 [0.4.5 보고서](RELEASE_0.4.5_2026-09-09_KO.md)를 참고한다. 아래는 앞선 테스트 후보의 기록이다.
2026-09-09 · 로컬 구현/검증 완료, 공개 릴리스 미게시

## 기준선
- 작업 폴더: `E:\AMS2 KRLEAGUE\AMS2KRLeague`
- 기준 HEAD: `4185705`, 공개 Latest: `0.4.4`, 시작 시 clean.
- 시작 시 검증: Client 113/113, Activity 102/102.
- 보호 대상: 기존 타워·전후방·랩타임·이벤트·대기 화면, 저장 배치, 읽기 전용 SHM, 전송 원본과 업데이트 해시 검증.

## 요구사항
| 요구 | 상태 | 구현 및 수용 결과 |
|---|---|---|
| REQ-DRIVE-01 자동 재실행 | DONE | 업데이트 후 상태창을 표시하되 게임 포커스를 가져오지 않는 시작 정책. 설치 성공과 실행 실패를 구분하며, 재실행 프로세스가 2초 이내 종료되면 실패로 기록. 정상/조기 종료 경로 검증. |
| REQ-DRIVE-02 독립 계기판 | DONE | 페달 그래프, 속도계, 기어를 별도 창으로 연결. 각자 표시·이동·크기 조절·저장. 속도는 xxx km/h, 기어는 상단 '기어'와 테두리 안 숫자. |
| REQ-DRIVE-03 외형 설정 | DONE | 네 입력 색상과 속도/기어 글꼴을 메뉴에서 각각 변경·저장. 재시작 유지, 기본 위치 복원 시 외형 보존. |
| REQ-DRIVE-04 실제 입력 | DONE | 기존 로컬 차량 SHM 값 사용. 다른 관전 차량/무효 값은 미확인 표시. 세션·참가자 변경/시점 불연속 시 그래프 초기화. |
| REQ-DRIVE-05 검증 | DONE | Release 빌드, 두 테스트, 실제 WPF 창의 크기·클릭 통과·설정 복원·프레임 이동 검사. 아래 제한 별도 표기. |
| REQ-DRIVE-06 아이콘 | DONE | 첨부 원본을 앱/상태창/설치 파일에 적용. 16~256px 7개 ICO 프레임 모두 실행 파일과 설치 파일의 내장 자원 해시 일치. |
| REQ-DRIVE-07 그래프 이동 | DONE | 새 입력은 오른쪽, 기존 점은 왼쪽으로 연속 이동. 세 페달 곡선과 핸드브레이크를 같은 그래프에 표시. 배치 편집 시 움직이는 가상 입력 제공. |
| REQ-DRIVE-08 표기·폰트 | DONE | 하단 한글 범례 제거. 우측 막대 아래 A·B·C·H 순서로 악셀·브레이크·클러치·핸드브레이크 표시. 한글패치와 동일한 Pretendard Medium을 공통 HUD/상태창과 새 패널 기본 글꼴로 포함. |
| REQ-WEB-01 경기 결과 경고 | DEFERRED | 사용자가 웹페이지 분석 후 별도 수정하도록 요청. 이번 구현의 종료·업로드 프로토콜 변경 없음. |

## 화면과 사용법
- [움직이는 편집 미리보기](C:/Users/User/Documents/Codex/2026-09-08/plugin-computer-use-openai-bundled-play-2/outputs/driving-hud/preview.html)
- [텔레메트리](C:/Users/User/Documents/Codex/2026-09-08/plugin-computer-use-openai-bundled-play-2/outputs/driving-hud/telemetry.png), [속도계](C:/Users/User/Documents/Codex/2026-09-08/plugin-computer-use-openai-bundled-play-2/outputs/driving-hud/speed.png), [기어](C:/Users/User/Documents/Codex/2026-09-08/plugin-computer-use-openai-bundled-play-2/outputs/driving-hud/gear.png)
- [색상·글꼴 설정](C:/Users/User/Documents/Codex/2026-09-08/plugin-computer-use-openai-bundled-play-2/outputs/driving-hud/settings.png), [상태창](C:/Users/User/Documents/Codex/2026-09-08/plugin-computer-use-openai-bundled-play-2/outputs/driving-hud/status.png)

상태창에서 각 패널 표시를 켜고 **레이아웃 편집**으로 배치를 조절한다. **텔레메트리·글꼴 설정**에서 네 색상과 숫자 글꼴을 고른다. 스크린샷과 동영상형 미리보기는 합성 입력을 사용한 실제 WPF 렌더링이다. 실게임 주행 기록으로 제시하지 않는다.

## 검증 근거
- [Release 빌드](../work/validation-driving-hud/final/build.log): 경고 0, 오류 0.
- [Client 테스트](../work/validation-driving-hud/final/client.log): **117/117**, 실패 0.
- [Activity 테스트](../work/validation-driving-hud/final/activity.log): **104/104**, 실패 0.
- `scripts/verify.sh`가 호출하는 동일한 Windows 게이트 `scripts/verify.ps1 -CaptureLayouts` 실행.
- 그래프의 기존 빨간 곡선 꼭짓점이 입력을 바꾸지 않은 350ms 동안 **x=322.2 → 308.2**로 이동. 실제 WPF 편집 프레임 30개 저장.
- 20Hz 최근 10초 기록 201개, 최대 256개 제한. 10,000개 입력 검사.
- 독립 창 크기 변경: 그래프 672×200, 속도계 264×100, 기어 120×125. 편집 후 클릭 통과 복원과 배치/색상/글꼴 저장 확인.
- 업데이트 helper는 격리된 설치/실행 시험 프로그램으로 재실행 성공과 실행 직후 종료 실패를 각각 확인.
- self-contained win-x64 publish 및 Inno Setup 빌드 성공. 이번 검증 파일은 `work/driving-hud-package/`의 비공개 후보이며 기존 0.4.4 릴리스를 덮어쓰지 않았다.
- 설치·실행 아이콘 각각 7개 내장 프레임 SHA-256 일치.
- 한글패치 0.7 원본 `Pretendard-Medium.otf`와 앱 내장 폰트 SHA-256 일치:
  `D39E50E4BB52B4993B6A4EEB821A171254745BD824446AF01E1F616B89FFACE0`
- 한글/숫자/A·B·C·H 글리프의 내장 폰트 해석 확인. [공식 폰트 라이선스](https://raw.githubusercontent.com/orioncactus/pretendard/main/LICENSE)를 배포 폴더에 함께 포함.

## 요청하지 않은 변경
- 이번 구현 범위 밖 변경 0건. 신규 패널/설정은 기존 독립 창·배치 저장 경로를 사용했다.
- 같은 작업 트리에 별도 작업으로 들어온 다음 변경은 보존했다:
  `HostRecorderEngine.cs`, `SessionWitnessTests.cs`,
  `FutureTelemetryCaptureRuntime.cs`, `FutureTelemetryRuntimeAdapterTests.cs`.
  Activity 테스트의 102→104 증가는 이 별도 변경에 해당한다.

## 보류와 다음 단계
- 웹사이트의 종료 무결성/사고 상대 정보 경고: 웹페이지 분석 후 별도 수정.
- 실게임 멀티플레이와 실제 GitHub 업데이트 전체 경로는 이번 작업에서 실행하지 않았다. 게임 조작·재시작 없음.
- 업데이트 설치 동안 오버레이 표시/기록 수집에는 기존과 같이 짧은 공백이 생긴다.
- 이번 새 기능의 버전 지정·공개 게시 요청은 아직 없으므로 커밋·태그·푸시·릴리스 없이 작업 트리에 남겼다.
- 최종 판정: **로컬 빌드·검증 PASS / 실게임 NOT RUN / 웹 경고 수정 DEFERRED / 공개 배포 미실행**.

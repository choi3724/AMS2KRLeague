# 0.4.0 릴리스 검증 및 인수인계

작성: 2026-09-06 KST. 사용자 요청 `0.4`를 저장소의 세 자리 버전 형식 `0.4.0`으로 정규화했다.

## 릴리스 대상

- 기준: `v0.3.1` / `427459312aa2d79d9518a72b1e2b234c9e7feff8` 이후 누적된 사용자 요청 수정.
- 대상 태그: `v0.4.0`, GitHub Latest, prerelease=false / draft=false.
- 릴리스 경로: https://github.com/choi3724/AMS2KRLeague/releases/tag/v0.4.0
- 최종 소스 SHA는 이 문서를 포함한 `v0.4.0` 태그의 commit을 기준으로 확인한다. 이 문서는 게시 전 검증 기록이며 원격 게시 성공 여부는 GitHub의 태그/릴리스/자산으로 확인한다.
- 기존 태그와 릴리스는 이동·수정하지 않는다. 사용자용 Overlay Client만 게시한다.

## 포함 내용

1. 참가자별 최고 랩 중심 타워, 아웃랩 → 랩 타임 주행 중 → 인정 기록 전환. 이미 유효 최고 기록이 있으면 유지한다.
2. 첫 계측에 랩 번호가 그대로인 경우의 native S1 전환, NotStarted 아웃랩, 일시정지/메뉴 전환의 출차 이력 보존.
3. PIT 배지의 모든 피트 모드 처리, 자기 영역에 맞춘 피트/트랙 전후방 차량 분리, 출발 전 근거리 보조 계산.
4. 연습/예선 LAP 차이 숨김과 기존 상대 시간차 칸 유지. 게임이 제공하지 않는 시간차는 추정하지 않는다.
5. 보조 패널의 독립 크기와 균등 글꼴 비율, Race Control 현재 상태 우선 표시, 신규 이벤트 플래시, 실제 무효 랩의 개인 타임 동결/알림.
6. 정확한 대기 세션명과 UI 전용 싱글/멀티 선택(자동 감지 아님).
7. Story 음수 거리 변환 방어, 실패 청크 격리 및 원본 로컬 보존, 부분 저장 후 동일 바이트 재시도, restart witness/archive 식별 연결, 정제된 저장 실패/403 진단과 영구 오류 격리.

이전 미커밋 Client 수정과 관련 보고서를 보존하여 함께 릴리스한다. 버전 props, Installer, 빌드 스크립트 기본값, 상태창, README, VERSIONING, CHANGELOG, 패키지 빠른 시작/릴리스 노트를 정렬했다. Ponytail 원칙에 따라 기존 빌드·테스트·감사·게시 스크립트를 재사용했으며 배포용 새 의존성/자동화는 추가하지 않았다.

## 출고 검증 결과

| 항목 | 결과 |
|---|---|
| Release solution build | PASS, 경고 0 / 오류 0 |
| Client/UI/SHM/Transport 전체 테스트 | PASS, 102/102 |
| Activity/Archive/Compact 전체 테스트 | PASS, 102/102 |
| self-contained win-x64 publish | PASS |
| publish 폴더 공개 패키지 감사 | PASS, 466 files / forbidden 0 |
| Portable ZIP 공개 패키지 감사 | PASS, 466 files / forbidden 0 |
| Installer 빌드 및 감사 | PASS, Inno Setup 6.7.3 / forbidden 0 |
| manifest ↔ 배포 파일 size/SHA256 | PASS |
| EXE/Client DLL/Core DLL 버전 | 0.4.0 / FileVersion 0.4.0.0 |
| git diff --check | PASS |
| 이번 릴리스의 신규 게임 주행/서버 업로드 | NOT RUN |

패키지 Core DLL SHA256: `FFE47BE12D5B66D3515F71553AC847038788A7343FE64914EABB5057208CC905`.

| 배포 파일 | bytes | SHA256 |
|---|---:|---|
| AMS2-League-Overlay-0.4.0-win-x64.zip | 70,148,373 | `393f1b7f8af66040a6f5a20a4df75c474a2b9d86b62a86b9693b8034a588ac3b` |
| AMS2-League-Overlay-0.4.0-Setup.exe | 51,256,722 | `0cbd42db469e97e09188e30885e6bcfc64d5b90c910078dd837a7fbd76ec7900` |

추가 자산: `SHA256SUMS-0.4.0.txt`, `release-manifest-0.4.0.json`. 실제 사용자 설정/자격증명, raw evidence, 개발용 work/artifacts는 소스 커밋이나 공개 실행 패키지에 넣지 않는다. 자동 테스트 소스와 분석 보고서는 저장소에 포함하지만 실행 패키지에는 넣지 않는다.

실행한 기존 패키징 명령(전체 빌드/두 테스트 스위트를 내부에서 수행):

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/build-release.ps1 -DotnetExecutable .\work\dotnet8\dotnet.exe -Version 0.4.0 -DisplayVersion 0.4.0
```

커밋/태그/원격 push 후 게시 명령:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/publish-github-release.ps1 -Version 0.4.0
```

## 남은 제한 / 하지 않은 것

- Cafe24 운영 파일·DB/API 배포, 기존 큐 초기화/재전송, 과거 경기 데이터 수정, 게임 조작, 설치본 자동 교체를 하지 않았다.
- Compact Protocol/schema/cadence/권한 정책은 그대로다. 이번 패키지 출고와 과거 멀티플레이 E2E 증거를 혼동하지 않는다.
- 과거 멀티 테스트는 공개 Compact 36/36 GET/decode/내용 해시 일치지만 cadence 손실 때문에 PARTIAL이다. 31ms 종료 경계 capture와 전환 분류 문제는 별도 후속 과제다. `ARCHIVE403_DAYTONA_LIVE_E2E_2026-09-06_KO.md` 참고.
- 아웃랩 수정의 실제 표시 projection 재생은 946 snapshots / 27,309행에서 불일치 0건이다. 최종 패키지의 실제 인게임 시각 검수 완료 주장은 하지 않는다. `LAP_LIFECYCLE_NATIVE_TIMING_2026-09-06_KO.md` 참고.
- 실게임 부하의 120 FPS 출력 보장, 전체 피트 곡선의 정확한 경로 복원, 권위 있는 싱글/멀티 자동 감지는 제공하지 않는다.

다음 확인: 기존 오버레이를 정상 종료한 뒤 설치판 또는 새 폴더의 Portable로 실행하고 상태창 버전 0.4.0을 확인한다. 실제 피트 출차/첫 계측/유효 기록 인정 전환과 사용자 저장 크기의 화면을 확인한다.

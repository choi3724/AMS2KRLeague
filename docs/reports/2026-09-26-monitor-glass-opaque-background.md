# v0.8.0 HUD 불투명 배경 회귀와 로컬 수정 후보

## 기준과 증상

- 공개 v0.8.0 기준 HEAD: `e388489e9fb7e67ded908a63bd3672e7cd476b3b`. 별도 빌드 트리에서 수정했으며 원본 분석 트리, 설치본, 게임, 운영 서버는 변경하지 않았다.
- 사용자 제공 화면에서 순위 타워, 세션 정보, N 계기판 등 여러 HUD의 투명해야 할 부분이 회색 또는 흰색 직사각형으로 표시된다. 공개 v0.8.0은 DWM glass를 기본 경로로 선택했다.

## 원인과 수정

- `ClientStartupPolicy`의 기본 `UseGlass=true`가 `OverlayWindow`와 `AuxiliaryOverlayWindow`의 `AllowsTransparency=false`를 선택한다. `OverlayWindowInterop.Configure`는 layered 스타일을 제거하고 `DwmExtendFrameIntoClientArea`를 음수 margin으로 호출한다.
- Microsoft 문서에 따르면 WPF `Background=Transparent`만으로 창은 투명해지지 않으며 `AllowsTransparency=true`가 필요하다. DWM 음수 margin은 창 전체의 단일 glass surface를 생성한다. 기존 테스트는 HWND 스타일과 실행 수명만 확인했고 화면의 픽셀 투명도를 검증하지 않았다.
- 기본 실행을 기존 WPF layered 경로로 복구했다. `--monitor-glass`와 `--monitor-retained-n`은 명시적 실험 경로로만 유지한다. `--monitor-layered`는 두 옵션보다 우선한다. 이미 설치된 v0.8.0은 `--monitor-layered`로 즉시 우회할 수 있다.
- 기본 실행에서 실제 생성한 HUD 창의 `AllowsTransparency`, layered, click-through, no-activate, tool-window 스타일을 검증하는 회귀를 추가했다. 데이터 수집·기록·전송·RPM·배치·HUD 디자인은 변경하지 않았다.

변경 파일: `src/AMS2LeagueClient.Core/Presentation/ClientStartupPolicy.cs`, `tests/AMS2LeagueClient.Tests/Program.cs`, `tests/AMS2LeagueClient.Tests/GlassHudTests.cs`. 비공개 0.8.1 후보를 위해 `Directory.Build.props`, `src/AMS2LeagueClient/Presentation/ClientStatusViewModel.cs`, `scripts/build-release.ps1`, `installer/AMS2LeagueOverlay.iss`, `release/README_KO.txt`, `release/RELEASE_NOTES_KO.md`의 버전·설명도 갱신했다.

## 검증과 한계

- 0.8.1 비공개 Release 패키지 빌드: warning 0, error 0. Client 178/178, Activity 111/111. `git diff --check` 통과. 폴더·ZIP 각 480개 파일, 설치 프로그램 1개 파일 감사에서 금지 항목 0건. NuGet 취약점 감사 엔드포인트에 접근할 수 없어 `NuGetAudit=false`로 복원했으며, 취약점 감사 결과는 **NOT TESTED**다.
- 후보 실제 Client 데모는 업데이트를 끄고 30초 실행 후 정상 종료했고, 패키지의 0.8.1 실행 파일도 10초 데모 실행 후 정상 종료했다. 패키지 시작 로그는 `CLIENT_VERSION ... version=0.8.1`, `MONITOR_PRESENTATION_PATH wpf-layered`, `OVERLAY_STYLES ... layered=True`를 기록했다. 기본 HUD 창 통합 회귀는 10개 이상의 표시 창에서 투명 스타일을 확인했다.
- 최초 비공개 후보(투명도 수정만 포함)는 새 게이지 후보와 구분해 `work/candidates/0.8.1-background-only/`에 보존했다. ZIP SHA256 `54f4271718a98c925ded594e0ea3ed86e01634f6b16405f4357d4e65e63c9752`, Setup SHA256 `f114a1255dab0fd920d94a047147dd9a416d078155081e08e00672cce8293790`. 두 파일 모두 해당 디렉터리의 manifest와 실제 파일 해시가 일치한다. 증거: `work/hotfix-0.8.1-build.log`, `work/hotfix-0.8.1-smoke/`. 후속 게이지 수정은 별도 최종 후보로 패키징한다.
- 이 자동화 세션의 `CopyFromScreen`은 `The handle is invalid`로 실패했다. 후보의 실제 데스크톱 픽셀 캡처, 실게임/VR/트리플 모니터 시각 결과와 성능은 **NOT TESTED**다. 사용자 스크린샷은 공개 v0.8.0의 결함 증거이며 수정본의 수용 증거는 아니다.
- layered 경로는 기존 투명 표시를 복구하지만 기존 WPF readback 성능 한계가 있다. Monitor 성능은 **RED**이며 이번 수정으로 성능 개선을 주장하지 않는다.
- 후보는 로컬 미커밋·비공개 상태다. 설치 교체, commit/tag/push/release는 수행하지 않았다.

문서: https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.allowstransparency · https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmextendframeintoclientarea

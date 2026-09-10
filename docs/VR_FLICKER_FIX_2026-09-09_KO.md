# Quest 3 / Virtual Desktop VR 깜빡임 수정 확인

## 관찰과 판단

- 기준 소스: v0.6.1, `ccb491b9d0390c22952e3047f7dda346160f17d3`. 작업 시작 시 변경 파일 없음.
- 사용자 환경: Quest 3, Virtual Desktop. 헤드셋 안에서도 동일한지에 대한 답변은 아직 없음.
- 첨부 `Honeycam 2026-09-09 22-45-38.gif`는 40프레임, 재생 시간 1,300ms다. 게임 장면은 계속 움직이는데 순위 타워와 텔레메트리 등 오버레이 전체가 함께 사라졌다가 나타난다.
- 기존 코드는 최대 15fps로 `SetOverlayRaw`에 매번 새 이미지를 보낸다. Valve 저장소의 [유사 증상 보고 #772](https://github.com/ValveSoftware/openvr/issues/772)는 이 방식에서 이전 이미지가 새 이미지 준비 전에 사라지는 현상을 설명한다. 오래된 다른 환경의 보고이므로 Quest 3의 원인을 확정하는 증거로 취급하지 않는다.
- [Valve API 문서](https://github.com/ValveSoftware/openvr/wiki/IVROverlay::SetOverlayRaw)는 가능한 경우 `SetOverlayTexture` 사용을 권장한다. 이번 수정은 전체 이미지 재로딩 경로를 제거하는 데 집중했다.
- 기존 `ImageLoaded` 대기 자체가 잘못된 계약은 아니다. 현재 사용 중인 [OpenVR 헤더](https://github.com/ValveSoftware/openvr/blob/0924064316de3effbcd1acf1e309182a2deb1c05/headers/openvr.h)는 Raw와 File 두 방식 모두에서 해당 이벤트를 명시한다. 타임아웃이 이번 1.3초 GIF의 원인이라고 단정하지 않았다.

## 변경 사항

- `SteamVrOverlayRuntime`: `SetOverlayRaw` 대신 `SetOverlayTexture`로 지속적인 Direct3D 11 텍스처를 제출한다. Raw 이미지 완료 이벤트와 3초 대기는 필요하지 않아 제거했다.
- `D3D11OverlayTexture`: SteamVR가 지정한 그래픽 어댑터에 별도의 D3D11 장치를 만든다. 게임의 그래픽 장치를 읽거나 가로채지 않는다.
- 화면 크기가 같으면 같은 텍스처를 재사용한다. 해상도가 바뀔 때만 새 텍스처를 만들고, 제출이 성공할 때까지 기존 텍스처를 유지한다.
- RGBA, 미리 곱한 알파, 감마 색 공간을 유지한다. `UpdateSubresource`로 갱신하고 제출 전후 GPU 명령을 Flush한다.
- [Microsoft 문서](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11devicecontext-updatesubresource)에 따라 CPU 버퍼는 UpdateSubresource 반환 후 재사용한다. Flush를 GPU 완료 대기로 취급하지 않는다.
- 장치 생성 실패, 텍스처 생성/제출 실패, 장치 제거 오류는 기존 VR 연결 재시도 처리로 전달된다. 종료 시 OpenVR를 정리한 후 텍스처와 D3D11 장치를 해제한다.
- Windows 기본 dxgi.dll / d3d11.dll API만 사용하며, 새 NuGet 종속성을 추가하지 않았다. 기존 OpenVR SDK 파일도 변경하지 않았다.
- 게임 표시 조건, 최대 15fps, 모니터 모드, 수집 및 업로드 정책은 변경하지 않았다. 설치된 프로그램과 게임·VR 설정도 변경하지 않았다.

## 검증

| 항목 | 결과 |
|---|---|
| Release 빌드 | PASS, 경고 0 / 오류 0 |
| Client 테스트 | PASS, 128/128 |
| Activity 테스트 | PASS, 107/107 |
| 실제 D3D11 반복 갱신 | PASS, 1280×720 이미지 30회, 동일 텍스처 핸들 유지 |
| GPU 데이터 읽기 | PASS, 매회 원본 RGBA/알파 전 바이트 일치. 제출 직후 CPU 배열을 지워도 GPU 데이터 유지 |
| 해상도 변경·제출 실패·종료 | PASS, 실패 시 기존 텍스처 보존, 정상 변경 후 픽셀 일치, 중복 Dispose 안전 |
| 기존 VR WPF 합성 | PASS, VR 전용·동시 출력의 알파 픽셀 수 동일 |
| Quest 3 / Virtual Desktop / SteamVR 실장비 | NOT RUN |

로그: `work/validation-vr-flicker/build.log`, `client.log`, `activity.log`.
테스트는 이 PC의 실제 D3D11 장치에서 실행했지만 OpenVR 초기화·SteamVR 합성·헤드셋 출력은 실행하지 않았다. 따라서 깜빡임 해결 여부는 실장비 비교가 필요하다.

## 전달 및 다음 확인

- 별도 테스트 ZIP: `artifacts/AMS2-League-Overlay-0.6.1-vr-fix-test-win-x64.zip`.
- 제품 버전은 0.6.1을 유지하고 테스트 빌드 정보는 `0.6.1+vr-fix.20260909`로 구분한다.
- 기존 오버레이를 종료한 뒤 ZIP을 새 폴더에 풀어 `AMS2LeagueClient.exe`를 실행한다. 기존 프로그램과 동시에 실행하지 않는다.
- 같은 Virtual Desktop / SteamVR / AMS2 환경에서 헤드셋과 PC 녹화 화면의 깜빡임을 각각 확인한다.
- 증상이 남으면 해당 PC의 `VR_STATUS`, `OVERLAY_SHOW`, `OVERLAY_HIDE` 로그를 같은 시간대에 비교한다. 재연결·게임 표시 조건과 텍스처 표시를 분리해서 판단한다.
- 이번 요청으로 commit/tag/push/정식 릴리스는 하지 않았다.

### 테스트 패키지 확인

- 공개 패키지 검사: PASS, 468파일, 금지 항목 0.
- 자체 포함 실행파일 데모 스모크: PASS, 2초 실행 후 종료 코드 0, ERROR/EXCEPTION 없음. VR 미초기화.
- ZIP 크기: 72,155,946바이트.
- ZIP SHA256: `AFBB8CEF743066F5ED0602ACAEA82BB7AC959E5D25C9E7AD3A3D3ECE44CD99E2`.
- 패키지 로그: `work/validation-vr-flicker/package.log`, `package-smoke/client-20260909-225946.log`.
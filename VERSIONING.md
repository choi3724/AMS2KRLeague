# Versioning

이 저장소는 `MAJOR.MINOR.PATCH` 형식을 사용합니다.

현재 Closed Beta 후보 표시 버전과 SemVer 기준은 `0.2.3-beta.3`, Assembly 기준은 `0.2.3.0`입니다. `v0.2.2`는 변경하지 않는 최신 안정 기준선입니다.

- 초기 버전: `0.1.0`
- 0.1 개발선의 버그 수정과 소규모 개선: `0.1.1`, `0.1.2`, `0.1.3` …
- 첫 Public Release: `0.2.0`
- Compact UI와 분산 세션 증인 수집: `0.2.1`
- 독립 UI 배치 편집과 대형 Timing Tower: `0.2.2`
- P024 Compact Telemetry 실제 검증용 Closed Beta: `0.2.3-beta.1`
- Overlay waiting/position/timing hotfix 후보: `0.2.3-beta.2`
- Overlay style/participant state/relative gap hotfix 후보: `0.2.3-beta.3`
- 안정판 호환성을 보장하기 시작하는 시점: `1.0.0`

Git tag와 GitHub Release에는 `v` 접두사를 붙입니다. 버전 문자열에 beta/rc 접미사가 있더라도 GitHub에서는 별도 Pre-release로 분리하지 않고, 새로 게시한 모든 릴리스를 항상 `Latest`로 지정합니다. 안정 기준선 태그 `v0.2.2` 자체는 수정하거나 이동하지 않습니다.

버전을 올릴 때 다음 항목을 함께 변경합니다.

1. `Directory.Build.props`의 `Version`, `VersionPrefix`, `AssemblyVersion`, `FileVersion`, `InformationalVersion`
2. `README.md`의 현재 버전
3. `CHANGELOG.md`
4. `scripts/build-release.ps1`의 기본 버전과 CI artifact 이름
5. Git tag와 GitHub Release

게시할 때는 `scripts/publish-github-release.ps1`을 사용합니다. 이 스크립트는 beta/rc 접미사와 관계없이 `--latest`를 적용하고 GitHub Pre-release 플래그를 사용하지 않습니다.

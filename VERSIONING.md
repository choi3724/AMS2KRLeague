# Versioning

이 저장소는 `MAJOR.MINOR.PATCH` 형식을 사용합니다.

현재 제품 표시 버전은 `0.2`, SemVer/Assembly 기준은 `0.2.0`입니다.

- 초기 버전: `0.1.0`
- 0.1 개발선의 버그 수정과 소규모 개선: `0.1.1`, `0.1.2`, `0.1.3` …
- 사용자 동작과 보안 경계가 달라진 현재 공개 개발선: `0.2.0` (표시 `0.2`)
- 안정판 호환성을 보장하기 시작하는 시점: `1.0.0`

Git tag와 GitHub Release에는 `v` 접두사를 붙입니다. Public Release 0.2의 태그는 `v0.2`입니다.

버전을 올릴 때 다음 항목을 함께 변경합니다.

1. `Directory.Build.props`의 `Version`, `VersionPrefix`, `AssemblyVersion`, `FileVersion`, `InformationalVersion`
2. `README.md`의 현재 버전
3. `CHANGELOG.md`
4. `scripts/build-release.ps1`의 기본 버전과 CI artifact 이름
5. Git tag와 GitHub Release

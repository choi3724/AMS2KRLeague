# OpenVR SDK

공식 Valve OpenVR SDK 2.15.6의 파일을 수정하지 않고 포함한다.

- 저장소: https://github.com/ValveSoftware/openvr
- 고정 커밋: `0924064316de3effbcd1acf1e309182a2deb1c05`
- `OpenVR.g.cs`: upstream `headers/openvr_api.cs` (이름만 .g.cs로 지정)
- `openvr_api.dll`: upstream `bin/win64/openvr_api.dll`
- `LICENSE.txt`: upstream `LICENSE` (배포 시 포함)

SHA-256:
- OpenVR.g.cs: C17E878B7B3B925D1F22EF5382561389C47DB8B92019DE840705FF5FF28C317A
- openvr_api.dll: BAB8AC6EF64E68A9CA53315B0014D131088584B2EFDFA6DB511D67EC03CFCB4A
- LICENSE.txt: F56FF606104D4EF18E617921A75C73AD73B5A1A1D70C69590C29DE16919E04AD

앱 폴더에 배포되는 DLL은 공식 SDK의 클라이언트 라이브러리다. 게임 프로세스에 주입하지 않는다.
SteamVR 설치와 실제 헤드셋이 별도로 필요하다.

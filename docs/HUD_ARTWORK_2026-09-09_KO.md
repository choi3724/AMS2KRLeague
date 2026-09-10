# HUD 이미지 · 2026-09-10

내장 image_gen 도구로 최종 PNG 3개를 생성해 프로그램 리소스에 포함했습니다. 숫자·회전·RPM 표시등·선수명·기록은 프로그램이 실시간으로 그립니다.

| 파일 | 사용 위치 |
|---|---|
| `src/AMS2LeagueClient/Assets/Hud/steering-wheel.png` | 단색 핸들 아이콘. 투명 배경, 회전 시 고품질 축소 |
| `src/AMS2LeagueClient/Assets/Hud/dashboard-housing.png` | 장식과 광택을 줄인 통합 계기판. 투명 배경 |
| `src/AMS2LeagueClient/Assets/Hud/tower-material.png` | 타워 기록 영역의 미세한 배경 질감 |

각도는 Bahnschrift SemiBold 14pt로 표시합니다. 별도 계산값 표시 없이 숫자와 °만 표시하며, 회전 범위 설정은 유지합니다.
계기판의 기어 숫자는 원 중심에 가로·세로 정렬하며, 사용자가 크기를 바꿔도 그림과 글자는 같은 비율로 확대됩니다.
타워는 이름 영역을 어둡게, 기록 영역을 반투명으로 구분합니다. 플레이어 행 강조와 클래스 색상 띠, 최고 랩 색상, 페널티 표시는 실시간 데이터에 따라 바뀝니다.

## 최종 생성 프롬프트

### 핸들
Use case: logo-brand. Asset type: crisp monochrome steering wheel HUD icon, transparent PNG, intended to be shown at 64 px. Create a completely flat vector-style symbol of a steering wheel viewed exactly from front. Neutral zero-angle orientation: two broad sculpted spokes go from solid oval central hub toward nine and three o'clock, one broad tapered spoke goes to six o'clock. All three spokes merge into one smooth filled organic Y-shaped center and a thick perfectly circular outer rim, leaving three large smooth rounded transparent holes. Rim thickness is about 9 percent of overall diameter. Spokes are wide, smoothly curved tapered solid shapes, NOT thin straight lines. Single uniform light gray #D6DADC fill with one tiny flat red center marker at twelve o'clock on the rim. Symmetric geometry, circle centered exactly at canvas center, 10 percent transparent margin on each edge to allow clean rotation. Crisp mathematically smooth antialiased edges, polished simple racing UI icon. Transparent background, including cutouts. NO texture, NO grain, NO leather, NO shading, NO gradients, NO lighting, NO bevel, NO chrome, NO bolts, NO buttons, NO text, NO perspective, NO background. Square high-resolution PNG.

### 계기판
Create a standalone transparent PNG game UI sprite: a clean dark motorsport dashboard background housing only. Wide landscape 3:1 canvas. One circular recessed gear display on left seamlessly attached to a horizontal pill shaped speed/RPM display body on right. Matte charcoal #10161B, perfectly smooth surfaces, only a very restrained subtle tonal gradient for depth, narrow muted gray rim around circular left display. Right body has a slim muted gray outline and blank dark area; no carbon fiber, no metallic shine, no chrome, no scratches, no grain, no gold accent, no texture. Flat straight-on front view with mathematically clean silhouette, solid opaque interior, crisp antialiasing. Everything outside this SINGLE housing is genuinely TRANSPARENT alpha zero, never checkerboard and never a backdrop. Entire shape visible with small transparent margin. Absolutely NO text, no symbols, no numbers, no LED dots, no pictures, no setting or surroundings. This is a production UI background asset, not a screenshot of an editor.

### 타워 질감
Use case: product-mockup. Asset type: seamless subtle material texture PNG for a professional motorsport timing tower HUD background, no interface itself. Create a straight-on orthographic closeup of dark blue-gray anodized metal with extremely fine evenly spaced round micro-perforation dots, like the elegant cool gray-blue patterned right column of broadcast racing timing graphics. Restrained soft studio sheen, top slightly lighter slate blue and bottom dark graphite, low contrast enough for crisp white timing numbers laid over it. Premium broadcast graphics material with real surface depth, quiet and clean, NOT shiny chrome, NOT carbon diagonal stripes. Texture fills the entire square canvas edge to edge; very fine detail that still looks subtle when scaled down to 256 px. No text, no logos, no numbers, no lines or grid layout, no bevel frame, no controls, no objects. Opaque square PNG background texture.

# How closed-source sim-racing overlays render HUDs on Windows, and how they handle GPU-bound games

Date of research: 2026-10-01. Proprietary binaries were not decompiled.

## Evidence levels used here

- **[V-direct]**: I read the primary source myself. That means GitHub wiki raw markdown, or open-source code I cloned.
- **[V-snippet]**: The primary vendor page was **blocked by this session's egress proxy**. These blocked domains were: simhubdash.com, racelab.app, garage.racelab.app, kapps.kutu.ru, ioverlay.app, learn.microsoft.com, dwayneneed.github.io, overtake.gg, forums.ea.com, iracerhub.com, chen.do, reizastudios forum, pcgamingwiki and gitlab.io. For these I used the search engine's extract of that page. The wording is close to the source but may be paraphrased. Re-check it before quoting it publicly.
- **[Inferred]**: My own reasoning or background knowledge, with no source behind it.

---

## 1. SimHub (SHWotever), WPF-based

### 1.1 Architecture: no injection; it uses ordinary borderless windows [V-direct]

The SimHub wiki page "DashStudio-performance" (https://github.com/SHWotever/SimHub/wiki/DashStudio-performance) says:

> "SimHub overlays and dashboards are not using d3d graphic hack, it's a series of borderless windows shown over or aside the game. In these conditions the result can vary a lot depending of your hardware/game settings and the GPU/CPU load."

The wiki page "Dash-Studio" says:

> "Dash Studio is powered by WPF, a fully graphics accelerated technology."

**Takeaway:** SimHub is in exactly our situation, with WPF transparent topmost windows over a borderless game. It does **not** have a secret low-level path.

### 1.2 SimHub's official stutter and low-fps guidance [V-direct]

These are verbatim bullets from the same wiki page:

- **"Overall make your game configuration leave GPU power to SimHub"**
  - "Use FPS limiter, enable vsync, appropriate game quality settings... On a safe side make sure that your GPU load is not above 90-95% load when running the game but not SimHub"
  - **This is SimHub's main answer to the GPU-bound case: leave GPU headroom.**
- **"GSync or Freesync can block WPF rendering"**
  - "...when configured to affect both desktop and fullscreen applications (configure it back to default or for fullscreen apps only), you can also configure exceptions for simhub in the driver control panel."
- **"When using overlays"**
  - "Reduce the overlay surfaces as much as possible (space taken on the screen, even if totally transparent have an impact)"
- **"Monitor refresh rate mismatch"**
  - "...the GPU might prioritize the primary monitor (game) and refresh slowly other screens no matter the actual GPU load."
- **"Disable any 'game focus' utility"**
  - "Native windows game mode, Norton game optimizer, process lasso, MSI's X3D Game Mode"
  - "Most of these optimizers 'strangle' any non-game process..."
- **"Some Dashboards/overlays are more heavy than others"**
  - "...try first the stock SimHub Dashboards/Overlays, they are made to be lightweight"
- **"Try to enable the HTML rendering mode of simhub"**
  - "(accessible by clicking on the gear on each overlay/dash), this can be configured globally or per dashboard/overlay"
- **"Power plan"**: assign SimHub to the dedicated GPU on laptops, and use the High performance power plan.

### 1.3 Overlay size limit [V-direct]

The wiki page "Dash-Studio-Overlays" says:

> "To avoid oversized overlays causing performances issues, a size (surface) limit exists. Width * Height must be below or equals to 800 * 600 (480 000 pixels)."

On the same page:

> "Make sure to set your game in windowed or borderless windowed mode, vsync is strongly suggested for optimal performances."

For AMS2 specifically, the page says:

> "Project cars 2 / AMS 2 does not offer borderless option in the menu, you can enable it using startup options inside steam: `-windowed -borderless`"

**[Inferred]** A hard surface cap per overlay strongly suggests SimHub pays a per-pixel cost on each frame. That fits WPF `AllowsTransparency` and `UpdateLayeredWindow`, where the readback and copy grow with window area. This is the same cost we see in our trace (PresentWithGDI → GetRenderTargetData).

### 1.4 Rendering-engine choice: WPF or HTML (Chromium/WebView2)

- **Setting name** [V-snippet, simhubdash forum "Custom overlay is too slow to be usable" plus overlay README text]:
  - "Use HTML engine" (overlay list → "more" button → "Use HTML engine").
  - Overlay authors' READMEs describe it as "Use HTML Engine (can be more smooth)".
  - A forum reply suggests "switching from the default WPF rendering engine to the lighter HTML engine in the 'Dashboard properties' settings" for an overlay that updated "seconds after the iRacing native overlays".
- **SimHub v9 advice** [V-snippet]: run all overlays in HTML for lower CPU cost, **except** overlays with moving parts (opponent trackers and maps). For those, "motion accuracy/smoothness is better in WPF mode".
  - Sources: overtake.gg "Aces AMS2 Simhub Overlay Suite" (thread page 6) and the reizastudios "Aces AMS2 Multiclass Simhub Overlay" thread.
- **WebView2** [V-snippet]: other users report FPS drops in WebView2 mode and none with WPF ("WebView engine consumes more CPU"). So the result depends on the system.
- **[Inferred]** SimHub's "HTML" engine is a Chromium-based (WebView2) transparent window. Chromium composites through DirectComposition, not GDI and `UpdateLayeredWindow`. That would explain why it is "smoother": it avoids the WPF layered-window GPU→system-memory readback.

### 1.5 Hardware acceleration cannot be turned off in SimHub [V-snippet]

In the simhubdash thread "When using SimHub with latest Nvidia drivers my mouse disappears and SimHub icons get huge", the developer reply (as extracted) says:

> "It's not possible to disable acceleration in SimHub as it would affect too much overall performance, since SimHub also shows dashboards and without hardware acceleration it would be awful."

The cause in that thread was NahimicOSD.dll. The wiki page "SimHub-Troubleshootings" also links dotnet/wpf#707 [V-direct].

- **I found no evidence** of any of these settings in SimHub: "software rendering", "Overlay compatibility mode", "Use DirectX", or `RenderOptions.ProcessRenderMode`.
- The only user-facing render switch I could document is **WPF vs HTML engine** (per overlay or global).
- I could not get exact changelog versions for when "Use HTML engine" was added. The simhubdash changelog is blocked, the GitHub releases API is blocked for this session, and the releases page only shows 9.12.x.
  - **The only related line I found** [V-direct, github.com/SHWotever/SimHub/releases]: 9.12.8 (17 Sep [2026]) "Fixed radial gauges rendering incorrectly in web/HTML dashboards when an offset was configured."
  - [V-snippet] 9.10.11 (13 Nov 2025): "HTML rendering would not show notifications" fixed.

### 1.6 Same symptom in SimHub's issue tracker, never solved in code [V-direct, issue pages]

- **#1119 "Low Refresh rate while Game is focused"** (2022-07-11, AC at 2K):
  - "If the game loses the main focus, I get normal/playable refresh rate on the overlay".
  - Closed. No maintainer fix is visible.
- **#1330** (F1 23, RTX 4090): the dash drops to 4–7.5 FPS in game and returns to 60 FPS when the game loses focus.
  - Labelled "Waiting for infos" and closed.
- **#1851** (2025-03, SimHub 9.7.5, iRacing): "refresh rate drops from 60fps to below 16fps". Closed, with no comments.
- **[Inferred]** This is the same pattern we measured. Windows GPU scheduling gives priority to the foreground fullscreen or borderless game process. Background WPF render threads then wait in the GPU queue, and readback (GetRenderTargetData) also waits for the GPU to finish the game's queued work. SimHub's answer is environmental (cap the game, leave 5–10 % GPU headroom), not an architectural workaround.

### 1.7 SimHub VR [V-snippet]

- SimHub overlays in VR work on SteamVR/OpenVR.
- OpenXR users usually use **OpenKneeboard** to show a SimHub window in VR.
- Sources: YouTube "SimHub Overlays in VR for iRacing using OpenXR"; overtake.gg "Best Apps/HUD VR".

---

## 2. RaceLab

- **Stack:**
  - [V-snippet] Unofficial support docs (Scottozy / rlasupport) tell users to "Monitor Chrome/Edge GPU process memory: if it climbs above 1 GB, restart overlays". They also refer to "hardware acceleration ... in the RaceLab browser".
  - [V-snippet] A release-notes or docs extract says "RaceLab renders its overlays in a separate transparent window on top of the sim".
  - **[Inferred]** This points to a Chromium-based (Electron or WebView2) desktop app, with one transparent Chromium window per overlay. **I found no official statement of "Electron".**
- **User-facing performance settings** [V-snippet, garage.racelab.app/docs/faq/performance_guide and Scottozy pages]:
  - **Hardware acceleration:** an on/off toggle.
    - "Hardware acceleration should be enabled ... for optimal performance".
    - "Some users have fixed FPS issues by disabling hardware acceleration in RaceLab".
  - **Overlay refresh rate:** "Faster refresh rates mean more CPU/GPU load; balance smoothness with efficiency". This implies a per-overlay or global update-rate setting.
  - **Monitoring:** RaceLab shows "overlay FPS and browser GPU load".
  - **Overlay count:** "Keep total overlay count reasonable"; "avoid stacking multiple large list overlays".
- **Recommended game and OS settings** [V-snippet, official performance guide]:
  - "Run the sim in Borderless (or Windowed) so RaceLab overlays can render".
  - **Disable VSync and VRR (G-SYNC/FreeSync) for the sim.** The rationale given is that sync features "add input latency, hide CPU bottlenecks, and create stutter when physics load changes".
  - Disable driver-side FPS caps. "If you need an FPS cap, use only the sim's in-game limiter."
  - "Windows Variable refresh rate setting should be kept Off".
  - Disable other overlays (Xbox Game Bar, Discord, GeForce/AMD, RTSS) while testing.
- **VR** [V-snippet, garage.racelab.app news "RacelabVR 3.0 is Here!" (2025-06-03) and docs/vr/open_vr]:
  - "native overlay rendering directly into your VR headset", supporting both OpenXR and OpenVR. The legacy Oculus API is not supported.
  - "OpenVR's limitations make OpenXR the preferred API for world-locked overlays".
  - **[Inferred]** For OpenXR this is most likely an OpenXR API layer that adds composition layers (quad layers) into the game's own OpenXR session. For OpenVR it is a separate `VRApplication_Overlay`.

## 3. Kapps (kutu)

- **Stack:** [V-snippet] Not officially stated. **[Inferred]** Probably Chromium/Electron, because the author is in the Node/irsdk ecosystem and the app has a "Hardware Acceleration" toggle. Development is currently suspended (fusion-racing.org).
- **Settings and FAQ** [V-snippet, kapps.kutu.ru/faq]:
  - "Kapps → Settings → Hardware Acceleration" (enable or disable).
  - For overlay stutter: "disable Nvidia G-Sync, disable Nvidia Surround, disable Game Mode in Windows and GPU Scheduling".
  - If the stream stutters while iRacing is smooth, turn off Hardware Acceleration.
  - The overlay "is only intended to work in Windowed Mode" (iRacing: not Fullscreen, Border unchecked).

## 4. iOverlay

- **Stack:** unknown. No public statement was found.
- **FAQ** [V-snippet, ioverlay.app/help]:
  - **Settings to change:**
    - "Disable Hardware acceleration in the general settings".
    - "In Windows settings, search for Game Mode and Hardware-accelerated GPU scheduling and disable them".
    - "On some systems, G-Sync might detect iOverlay as the primary application instead of iRacing" → adjust the driver settings or disable G-Sync.
  - **Injection tools:** Afterburner/RivaTuner or AMD Adrenaline "use injection methods that iOverlay can't block or control" → add iOverlay as an exception in RTSS (On-Screen Display tab).
  - **Requirements:** iRacing must be borderless or windowed.

## 5. Garage 61, SRT, Racing Insights, Popometer

- **Garage 61:** [V-snippet] a telemetry platform with a desktop "agent" that syncs ghosts and setups.
  - Overlays are offered through third parties, e.g. the open-source "input-telemetry-overlay".
  - I found no Garage 61-native desktop HUD with documented render settings.
- **Sim Racing Telemetry (SRT):** [V-snippet] a telemetry analysis app (Steam / iOS / Android).
  - The searches surfaced no documented HUD render settings. The "Edge Overlays" product appears alongside it in results.
- **Racing Insights, Popometer:** analysis tools (web or desktop). I found no in-game HUD rendering documentation. **[Inferred]** They are not relevant to the HUD stutter problem.

## 6. Crew Chief (open source, a useful contrast)

[V-direct] I did a sparse clone of gitlab.com/mr_belowski/CrewChiefV4 (HEAD 85ec4378, 2026-09-29).

- **Desktop overlay** (`CrewChiefV4/Overlay/CrewChiefOverlay.cs`): uses the **GameOverlay.Net** library:
  - `new GraphicsWindow(graphics){ IsTopmost = true, FPS = settings.windowFPS, ... }`
  - `VSync = settings.vSync`
  - Defaults in `OverlaySettings.cs`: `windowFPS = 30; vSync = false`.
  - **Takeaway:** the overlay runs at a **fixed cap of 30 fps, with VSync off**.
- **What GameOverlay.Net does** [V-direct, cloned github.com/michel-pi/GameOverlay.Net]:
  - `CreateWindowEx(WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOPMOST, WS_POPUP)`
  - `SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA)`
  - `DwmExtendFrameIntoClientArea(hwnd, margins = -1)`
  - Direct2D `HwndRenderTarget` with `B8G8R8A8_UNorm` and `AlphaMode.Premultiplied`
  - `PresentOptions = VSync ? None : Immediately`
  - **Takeaway:** this is the "DWM glass" approach. There is **no `UpdateLayeredWindow` and no GPU→CPU readback**, and per-pixel alpha comes from DWM composing the D2D swap surface.
- **VR overlay** (`CrewChiefV4/VROverlayWindow/*`): captures windows with **DXGI Desktop Duplication** (`output1.DuplicateOutput`, in `CaptureScreen.cs`) or GDI BitBlt. It submits them as **OpenVR overlays** (`EVRApplicationType.VRApplication_Overlay`, in `SteamVR.cs`).

## 7. VR desktop mirroring tools

- **Desktop+** [V-direct, its user guide on GitHub]:
  - Two capture methods.
    - "Desktop Duplication ... Only updates on screen changes".
    - "Graphics Capture ... Always updates at monitor refresh rate ... More efficient than Desktop Duplication".
  - It has an "Update Limiter" (in ms or fps).
  - Multi-GPU texture copies "come with a significant performance penalty".
- **VRScreenCap** [V-snippet, GitHub]: Rust, wgpu/Vulkan, OpenXR. Shows Katanga streams or Desktop Duplication.
- **OVR Toolkit:** closed source. **[Inferred]** An OpenVR overlay app using WGC or Desktop Duplication capture, similar to Desktop+.
- **Relevance [Inferred]:** none of these help desktop HUD smoothness. They are consumers of desktop windows.

## 8. Injection-based overlays (Steam, Discord, RTSS, NVIDIA, AMD)

- **Technique** [V-snippet; RTSS description on guru3d / rivatuner pages; Discord hook repos]:
  - **RTSS:** injects into D3D9/10/11/12, Vulkan and OpenGL processes and hooks Present.
    - Because D3D is COM, hook addresses are found by reading vtables: "RTSS creates dummy D3D objects inside itself on injection cache initialization ... then simply reuse cached addresses".
    - It draws the OSD into the game's back buffer before Present, and its frame limiter delays the frame "right before the graphics API's Present() call".
  - **Discord:** `DiscordHook64.dll` is loaded into the game by its helper process with LoadLibrary.
  - **All of these** draw inside the game's own swap chain, so they are presented **in the game's frame**, with no separate window, no DWM composition and no cross-process GPU readback.
- **Why sim-racing overlay vendors avoid it** [Inferred, plus V-snippet points]:
  1. **Anti-cheat.**
     - iRacing integrated **Easy Anti-Cheat** (bsimracing "iRacing - New cheat prevention & detection system coming"). EAC sandboxes the process and only whitelisted DLLs (recording software, SweetFX, SoftTH) may load.
     - Anti-cheat looks for `IDXGISwapChain::Present` hooks and injected wrapper DLLs. Steam, Discord and NVIDIA are whitelisted by vendor arrangement.
  2. **Fragility:** each game, API and driver update can break the hooks, and hooks conflict with RTSS, ReShade, OpenXR toolkits and other hooks. iOverlay's FAQ explicitly blames injected RTSS/Adrenaline for stutter.
  3. **Cross-sim support:** one windowed overlay works for every sim.
  4. **Development cost:** game-thread rendering must be written in D3D, not in WPF or HTML.
- **AMS2 anti-cheat** [V-snippet]: the Reiza forum thread "Anti-Cheat Ams2 ?" (March 2025) says AMS2 has **no anti-cheat system**.
  - **[Inferred]** Technically, a Present hook into AMS2's D3D11 would therefore not be blocked by anti-cheat. It would still be a Client architecture change and carries a high compatibility risk.
  - Per AGENTS.md it should be proposed only with reproduction evidence and explicit approval.

## 9. What vendors tell users about stutter (summary matrix)

| Vendor | Cap game FPS / GPU headroom | HW-accel toggle | G-Sync/VRR | HAGS | Game Mode | Borderless | Other |
|---|---|---|---|---|---|---|---|
| SimHub [V-direct] | Yes (≤90–95 % GPU, FPS limiter, **vsync on**) | No (dev: not possible) | Limit to fullscreen-only, or add a SimHub exception | — | Disable | Yes (`-windowed -borderless` for AMS2) | Smaller overlay area; 480k px cap; HTML engine; matching monitor Hz |
| RaceLab [V-snippet] | Use **only** the in-game limiter | Yes (try on and off) | **Disable** VSync and VRR for the sim | — | — | Yes | Fewer overlays; lower refresh rate; watch GPU process memory |
| Kapps [V-snippet] | — | Yes | Disable G-Sync / Surround | **Disable** | Disable | Windowed, no border | — |
| iOverlay [V-snippet] | — | Yes (disable) | Disable (G-Sync may treat iOverlay as the primary app) | **Disable** | Disable | Yes | RTSS exception |
| iRacerHUB guide [V-snippet] | — | — | — | — | — | Yes | **Same GPU** for game and overlay (cross-GPU framebuffer copy) |

Note: SimHub says enable vsync, while RaceLab says disable VSync/VRR. Both aim to stop the game from saturating the GPU queue or the display pipeline. SimHub does it by capping, RaceLab by avoiding sync gates.

## 10. Implications for our WPF overlay [Inferred unless noted]

1. **No major closed-source tool has a magic architecture.**
   - SimHub (WPF windows) and the Chromium-based tools (RaceLab and probably Kapps) all accept that a GPU-saturated game starves them.
   - They mitigate with: (a) user guidance (GPU headroom, FPS cap, no G-Sync on desktop, no Game Mode or HAGS), (b) smaller overlay surfaces, (c) an alternative compositor path (SimHub's HTML engine), and (d) a hardware-acceleration toggle (RaceLab, Kapps, iOverlay).
   - **Our SoftwareOnly default is the same idea as their "disable hardware acceleration" toggle.**
2. **The 480k-pixel cap in SimHub fits our trace.**
   - WPF `AllowsTransparency` cost grows with window area, because of GetRenderTargetData readback plus the `UpdateLayeredWindow` copy.
   - [V-snippet, MS blog by Seema, "Layered windows – SW is sometimes faster than HW"] gives two hardware-vs-software comparisons on a 300×300 semi-transparent window:
     - Animating a 50×50 rectangle: ~50 fps on hardware vs ~60 fps on software.
     - Resizing the empty window: ~8 fps on hardware vs ~60 fps on software.
   - The cause given is the "chain of bitblits from video memory to system memory to video memory".
   - So keeping each HUD window tight to its content and using SoftwareOnly matches both the vendors' practice and Microsoft's own notes.
3. **Crew Chief / GameOverlay.Net show a non-WPF alternative.**
   - Win32 `WS_EX_LAYERED|TRANSPARENT` + `SetLayeredWindowAttributes(255)` + `DwmExtendFrameIntoClientArea(-1)` + a premultiplied-alpha Direct2D/DXGI swap surface. Crew Chief caps it at 30 fps with VSync off.
   - This avoids the GDI readback but still depends on DWM composition, which matches our "DWM glass p95 ~55 ms".
   - A DirectComposition, premultiplied-alpha flip-model swap chain (which is how Chromium/WebView2 renders) is the modern version of this. It is probably why SimHub's HTML engine is "smoother".
4. **Injection (a Present hook)** is the only way to be presented in the game's own frame. AMS2 has no anti-cheat (V-snippet), but this is a Client architecture change: it needs separate approval and reproduction evidence per AGENTS.md.

## Sources

- **SimHub (primary, read directly)**
  - SimHub wiki, DashStudio performance: https://github.com/SHWotever/SimHub/wiki/DashStudio-performance
  - SimHub wiki, Dash Studio Overlays: https://github.com/SHWotever/SimHub/wiki/Dash-Studio-Overlays
  - SimHub wiki, Troubleshootings: https://github.com/SHWotever/SimHub/wiki/SimHub-Troubleshootings
  - SimHub issues #1119, #1330, #1851: https://github.com/SHWotever/SimHub/issues/1119 · /1330 · /1851
  - SimHub releases: https://github.com/SHWotever/SimHub/releases
- **SimHub (snippets only)**
  - simhubdash forum threads:
    - https://www.simhubdash.com/community-2/simhub-support/dashboard-is-too-slow-to-be-usable/
    - https://www.simhubdash.com/community-2/simhub-support/when-using-simhub-with-latest-nvidia-drivers-my-mouse-disappears-and-simhub-icons-get-huge/
  - Aces AMS2 SimHub overlay threads:
    - https://www.overtake.gg/threads/aces-ams2-simhub-overlay-suite.282919/page-6
    - https://forum.reizastudios.com/threads/aces-ams2-multiclass-simhub-overlay.35011/
- **RaceLab (snippets only)**
  - Performance guide: https://garage.racelab.app/docs/faq/performance_guide/
  - RaceLab VR 3.0: https://garage.racelab.app/news/2025/06/03/2025/racelab-vr-3-stable/
  - OpenVR notes: https://garage.racelab.app/docs/vr/open_vr/
  - Scottozy support: https://rlasupport.jdwinelist.com/contents/en-uk/d1205_racelab-performance-tips.html
- **Kapps (snippets only)**
  - FAQ: https://kapps.kutu.ru/faq/
  - fusion-racing: https://www.fusion-racing.org/iracing/iracing-apps/iracing-apps-kapps.php
- **iOverlay (snippet only):** https://ioverlay.app/help/
- **iRacerHUB (snippet only):** https://iracerhub.com/iracing-overlay-stuttering/
- **Open-source code (read directly)**
  - Crew Chief: https://gitlab.com/mr_belowski/CrewChiefV4 (`CrewChiefV4/Overlay/CrewChiefOverlay.cs`, `Overlay/OverlaySettings.cs`, `VROverlayWindow/CaptureScreen.cs`, `VROverlayWindow/SteamVR.cs`)
  - GameOverlay.Net: https://github.com/michel-pi/GameOverlay.Net (`source/GameOverlay/Windows/OverlayWindow.cs`, `Windows/WindowHelper.cs`, `Drawing/Graphics.cs`)
  - Desktop+ user guide: https://github.com/elvissteinjr/DesktopPlus/blob/master/docs/user_guide.md
  - irdashies (open-source Electron iRacing overlays): https://github.com/tariknz/irdashies
- **VRScreenCap (snippet only):** https://github.com/artumino/VRScreenCap
- **Microsoft WPF layered windows (snippets only)**
  - https://learn.microsoft.com/en-us/archive/blogs/seema/layered-windows-sw-is-sometimes-faster-than-hw
  - https://dwayneneed.github.io/wpf/2008/09/08/transparent-windows-in-wpf.html
  - KB938660: https://support.microsoft.com/en-us/help/938660
- **Anti-cheat and injection (snippets only)**
  - iRacing Easy Anti-Cheat: https://www.bsimracing.com/iracing-new-cheat-prevention-detection-system-coming/
  - AMS2 anti-cheat thread: https://forum.reizastudios.com/threads/anti-cheat-ams2.34962/
  - RTSS: https://www.guru3d.com/download/rtss-rivatuner-statistics-server-download
  - Discord hook: https://github.com/PicoShot/DiscordOverlayHook

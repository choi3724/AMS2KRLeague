# WPF monitor placement validation

Reuses PlacementBaseline and PlacementSession from the existing direct-graph proof harness. It references the released WPF Client only; no GPU renderer or replay input is added to the product.

Build `dotnet build harness/monitor-placement/placement.csproj -c Release -o work/placement`.
Run `work/placement/placement.exe <new-output>` for negative coordinates and game-client origin changes.
Run `work/placement/placement.exe <new-session-output> --session` three times to perform actual drag, save, and restore in new processes. The apphost uses the Client DPI manifest. It uses a separate layout file and never starts runtime collection or upload.

The fixture suite covers logical single/dual/triple layouts, negative coordinates and repeated serialization. Physical triple/mixed-DPI/hotplug and game behavior are NOT TESTED. Screenshots are validation-only CPU captures.

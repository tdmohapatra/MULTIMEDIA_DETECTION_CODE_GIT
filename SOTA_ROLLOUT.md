# SOTA Rollout and Regression Gates

## Acceptance SLOs (Ryzen 5 5600H / 24GB RAM)
- Sustained `20-24 FPS` in balanced mode for `DetectionView` and `ModelCanvas`.
- Average server frame latency <= `50 ms` on scene mode with SSD backend.
- Gesture stability: no visible flicker for stable hand pose over 3 seconds.
- OCR parity: no regression in extracted text length/quality compared to baseline preset.

## Benchmark Harness
- Run: `pwsh ./benchmark-realtime.ps1 -ApiBase http://localhost:5078 -Frames 120`.
- Capture:
  - Avg client latency
  - Avg server latency (`stageTimings.totalMs`)
  - Estimated FPS
  - Pipeline backend

## Staged Rollout
1. **Stage A (Performance/Scheduler)**  
   Enable ROI scheduler + adaptive skip + max person cap. Validate no API contract change.
2. **Stage B (Model Backends)**  
   Switch `sceneModelBackend` to `yolo` on test env only, fallback to SSD if unavailable.
3. **Stage C (Intelligence Events)**  
   Verify event feed in diagnostics + UI (`hand_raise_trigger`, `eye_close_alert`, `attention_drop`).
4. **Stage D (Acceleration)**  
   Verify `inferenceProvider` telemetry (`directml` preferred, CPU fallback accepted).

## Regression Checks
- `dotnet build` for API and UI.
- Smoke test:
  - `/api/detection/health`
  - `/api/detection/detector-health`
  - `/api/detection/detector-diagnostics/{sessionId}`
- UI checks:
  - `DetectionView` runtime diagnostics line updates every 1-2 seconds.
  - `SceneAnalysis`/`ModelCanvas` can switch SSD <-> YOLO backend.

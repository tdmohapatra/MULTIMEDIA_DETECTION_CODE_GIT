# Multimedia Detection Demo Checklist

## 1) Pre-Demo Setup (5-10 minutes before)

- Open terminal in:
  - `TDM_MULTIMEDIA DOTNET CORE/ImgToText`
- Confirm .NET SDK:
  - `dotnet --version`
- Restore/build:
  - `dotnet restore`
  - `dotnet build`

## 2) Required Runtime Files

- OCR language file:
  - `TDM_MULTIMEDIA DOTNET CORE/ImgToText/Uploads/tessdata/eng.traineddata`
- Required cascade files:
  - `TDM_MULTIMEDIA DOTNET CORE/ImgToText/Uploads/cascades/haarcascade_frontalface_alt.xml`
  - `TDM_MULTIMEDIA DOTNET CORE/ImgToText/Uploads/cascades/haarcascade_eye.xml`
- YOLO model file (optional but recommended):
  - `TDM_MULTIMEDIA DOTNET CORE/ImgToText/Uploads/models/yolov8n.onnx`

## 3) Start Application

- Run:
  - `dotnet run --urls http://localhost:5078`
- Open in browser:
  - API Home: `http://localhost:5078/`
  - UI Home: `http://localhost:5080/`
  - Scene Analysis: `http://localhost:5080/DetectionClient/SceneAnalysis`
  - Model Canvas: `http://localhost:5080/DetectionClient/ModelCanvas`

## 4) Health and Readiness Checks

- Health endpoint:
  - `http://localhost:5078/api/detection/health`
  - Expected: `200` and status payload
- Detector health endpoint:
  - `http://localhost:5078/api/detection/detector-health?sessionId=<sessionId>`
  - Expected: `200` and detector readiness payload
- Detector diagnostics endpoint:
  - `http://localhost:5078/api/detection/detector-diagnostics/<sessionId>`
  - Expected: `200` with average/last stage timings and source mix
- Readiness endpoint:
  - `http://localhost:5078/ready`
  - Expected:
    - `200` with `status = Ready`, or
    - `503` with list of missing files (`missing`)

## 5) Demo Flow (Recommended Order)

1. Show Home page loads successfully.
2. Show OCR upload flow:
   - upload image,
   - extract text,
   - show diagnostics panel (quality, signature, potential tamper),
   - download/export result.
3. Show Real-time detection page:
   - allow camera permission,
   - start processing,
   - show counters/notifications updating.
4. Open Scene Analysis page:
   - switch **Accuracy preset** (`Balanced`, `Human Focus`, `Animal Strict`, `All Detail`),
   - show detector status dots and SLO badge.
5. Open Model Canvas page:
   - show stage split and source mix diagnostics.
6. Open readiness and diagnostics endpoints and explain operational checks.

## 6A) YOLO Verification (Quick 3-step)

1. Open `Scene Analysis` or `Model Canvas` and set:
   - **Scene model backend** = `YOLOv8 ONNX`
2. Start processing and confirm UI pipeline badge includes:
   - `yolo` (for example: `yolo+face+fullbody+cat_cascade`)
3. Check diagnostics endpoint for same session:
   - `GET /api/detection/detector-diagnostics/<sessionId>`
   - Expected keys:
     - `sceneModelBackend = "yolo"`
     - `inferenceProvider = "directml"` (or `"cpu"` fallback)
     - `modulePerformance.scene` populated with latency/FPS values

## 6) Quick Troubleshooting During Demo

- If app does not start:
  - stop old process using app port, then rerun `dotnet run`.
- If OCR gives weak/no output:
  - verify `eng.traineddata` exists in `Uploads/tessdata`.
- If detection UI opens but no detections:
  - verify cascade XML files in `Uploads/cascades`.
- If scene/model diagnostics show high latency:
  - lower FPS slider,
  - lower resolution,
  - use `Human Focus` preset.
- If YOLO is not active:
  - verify `yolov8n.onnx` is inside `Uploads/models`,
  - ensure backend selector is `YOLOv8 ONNX`,
  - check diagnostics `sceneModelBackend` value for current session.
- If browser camera fails:
  - allow camera permission, close other apps using camera, refresh page.

## 7) Stop Application After Demo

- In terminal running app: `Ctrl + C`

## 8) Optional One-Command Endpoint Check

- PowerShell:
  - `curl.exe -s -o NUL -w "HOME=%{http_code}\n" http://localhost:5078/; curl.exe -s -o NUL -w "HEALTH=%{http_code}\n" http://localhost:5078/api/detection/health; curl.exe -s -o NUL -w "READY=%{http_code}\n" http://localhost:5078/ready; curl.exe -s -o NUL -w "UIHOME=%{http_code}\n" http://localhost:5080/`


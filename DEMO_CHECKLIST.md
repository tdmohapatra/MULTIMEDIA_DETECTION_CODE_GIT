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

## 3) Start Application

- Run:
  - `dotnet run --urls http://localhost:5078`
- Open in browser:
  - Home: `http://localhost:5078/`
  - Real-time detection: `http://localhost:5078/DetectionView/Index`

## 4) Health and Readiness Checks

- Health endpoint:
  - `http://localhost:5078/api/detection/health`
  - Expected: `200` and status payload
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
   - download/export result.
3. Show Real-time detection page:
   - allow camera permission,
   - start processing,
   - show counters/notifications updating.
4. Open readiness endpoint and explain operational checks.

## 6) Quick Troubleshooting During Demo

- If app does not start:
  - stop old process using app port, then rerun `dotnet run`.
- If OCR gives weak/no output:
  - verify `eng.traineddata` exists in `Uploads/tessdata`.
- If detection UI opens but no detections:
  - verify cascade XML files in `Uploads/cascades`.
- If browser camera fails:
  - allow camera permission, close other apps using camera, refresh page.

## 7) Stop Application After Demo

- In terminal running app: `Ctrl + C`

## 8) Optional One-Command Endpoint Check

- PowerShell:
  - `curl.exe -s -o NUL -w "HOME=%{http_code}\n" http://localhost:5078/; curl.exe -s -o NUL -w "HEALTH=%{http_code}\n" http://localhost:5078/api/detection/health; curl.exe -s -o NUL -w "READY=%{http_code}\n" http://localhost:5078/ready`


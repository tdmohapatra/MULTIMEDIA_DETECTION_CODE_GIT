# 🌟 STAR MULTIMEDIA

> **STAR MULTIMEDIA** is a powerful .NET Core–based multimedia intelligence system designed to perform **advanced object detection**, **live text (OCR) recognition**, and **real-time human detection (face, eyes, hands, and gestures)** using AI and computer vision.  

Developed with **C# (.NET Core)** and integrated with **OpenCV**, **MediaPipe.NET**, and **Tesseract OCR**, this project demonstrates how multimedia inputs can be analyzed in real time through both local and live camera feeds.

---

## 🚀 Features

### 🧠 Advanced Object Detection
- Detect **faces**, **eyes**, **hands**, and **gestures** from live camera feed  
- Supports **Haar Cascade** and **MediaPipe.NET**-based detection  
- Displays **real-time detection statistics**

### 🧾 Text Detection (OCR)
- Upload an image and extract visible text  
- Perform **live text detection** from webcam video streams  
- Store detected text for further analysis  
- Modify and re-save recognized text dynamically

### 📊 Live Analytics
- Displays runtime stats (detections per frame, performance metrics)  
- Provides graphical visualization support (e.g., detection frequency, frame rate)  
- Supports database or file-based text/metric storage

---

## 🌐 UI Paths (Current)

Assuming local defaults:
- **UI Base URL:** `http://localhost:5080`
- **API Base URL:** `http://localhost:5078`

Open these pages in browser:
- `http://localhost:5080/` → **Main menu / Home dashboard**
- `http://localhost:5080/DetectionView` → **Live tracking & behavior workspace**
- `http://localhost:5080/DetectionView/Index` → **Live tracking & behavior workspace** (same view)
- `http://localhost:5080/DetectionClient/SceneAnalysis` → **Humans / animals / objects scene analysis**
- `http://localhost:5080/DetectionClient/ModelCanvas` → **Large model canvas with live diagnostics**
- `http://localhost:5080/Home/Privacy` → **Privacy page**

Useful API paths:
- `http://localhost:5078/api/detection/health` → **Detection service health**
- `http://localhost:5078/api/detection/detector-health?sessionId=<id>` → **Detector readiness snapshot**
- `http://localhost:5078/api/detection/detector-diagnostics/<sessionId>` → **Per-session stage timings + source mix**
- `http://localhost:5078/api/system/health` → **System health + memory summary**
- `http://localhost:5078/ready` → **Startup readiness checks**

---

## 🏗️ Production-Grade Project Split

- `ImgToText_UI` → **UI-only project** (all web pages, navigation, client scripts)
- `ImgToText` → **API-only project** (OCR, detection, SignalR hub, observability endpoints)

### New observability files (API)
- `Services/Observability/IApplicationLoggingService.cs`
- `Services/Observability/ApplicationLoggingService.cs`
- `Services/Observability/IMemoryMonitoringService.cs`
- `Services/Observability/MemoryMonitoringService.cs`
- `Services/Observability/MemoryMonitoringBackgroundService.cs`
- `Models/Observability/ApplicationLogEntry.cs`
- `Models/Observability/MemorySnapshot.cs`
- `Controllers/SystemMonitoringController.cs`

### New observability API routes
- `GET /api/system/health` → health + latest memory snapshot
- `GET /api/system/memory?count=120` → memory history
- `POST /api/system/memory/capture` → manual snapshot trigger
- `GET /api/system/logs?count=150` → recent structured app logs
- `GET /api/detection/detector-health?sessionId=<id>` → detector availability and model readiness
- `GET /api/detection/detector-diagnostics/<sessionId>` → avg/last stage timing split + source/category counts

Logs are persisted under: `ImgToText/logs/observability/`

### Detection telemetry highlights
- `DetectionResult.stageTimings` returns `decodeMs`, `detectionMs`, `renderMs`, `postAnalysisMs`, `totalMs`
- `DetectionResult.detectorHealth` returns per-detector readiness (face/eye/hand/full-body/cat + SSD)
- Scene UIs show:
  - model status dots,
  - SLO/latency status,
  - stage timing split,
  - source mix diagnostics

### Security hardening (API)
- Global API rate limiting configured via `Security:RateLimit`
- API payload cap via `Security:MaxApiRequestBytes`
- Multipart cap via `Security:MaxMultipartBytes`
- Configurable in:
  - `appsettings.json`
  - `appsettings.Development.json`
  - `appsettings.Production.json`

### CI quality gates
- GitHub Actions workflow: `.github/workflows/ci-quality-gates.yml`
- On push/PR, pipeline runs:
  - solution restore
  - API release build
  - UI release build
  - API smoke checks (`/api/detection/health`, `/ready`)

---

## 🧩 Technology Stack

| Category | Tools & Frameworks |
|-----------|--------------------|
| **Framework** | .NET Core 8.0 / ASP.NET Core |
| **Language** | C# |
| **Computer Vision** | OpenCvSharp4 |
| **AI / ML Integration** | MediaPipe.NET |
| **Text Recognition** | Tesseract OCR |
| **UI Framework** | WinForms / WPF / Blazor (depending on module) |
| **Database** | SQLite / SQL Server (optional for storing text logs) |
| **IDE** | Visual Studio 2022 |

---

## 🗂️ Project Structure

STAR_MULTIMEDIA/
│
├── STAR_MULTIMEDIA.sln
│
├── wwwroot/
│ ├── cascades/
│ │ ├── haarcascade_frontalface_default.xml
│ │ ├── haarcascade_eye.xml
│ │ ├── haarcascade_hand.xml
│ │
│ └── assets/
│ └── sample_images/
│
├── Controllers/
│ ├── HomeController.cs
│ └── DetectionController.cs
│
├── Models/
│ ├── TextDetectionResult.cs
│ ├── HumanDetectionStats.cs
│ └── FrameAnalytics.cs
│
├── Services/
│ ├── TextDetectionService.cs
│ ├── FaceHandDetectionService.cs
│ └── AnalyticsService.cs
│
├── Pages/ (if Blazor)
│ ├── Index.razor
│ └── Detection.razor
│
├── Views/ (if MVC)
│ ├── Home/
│ ├── Detection/
│ └── Shared/
│
├── appsettings.json
├── Program.cs
├── Startup.cs
└── README.md


---

## ⚙️ Installation

### 🧾 Step 1: Clone Repository
```bash
git clone https://github.com/<your-username>/STAR_MULTIMEDIA.git
cd STAR_MULTIMEDIA
🧩 Step 2: Open in Visual Studio 2022

Open STAR_MULTIMEDIA.sln

Ensure your environment uses:

.NET SDK 8.0 or higher

C# version 10 or higher

🧱 Step 3: Install Required NuGet Packages

From Tools → NuGet Package Manager → Manage NuGet Packages for Solution, install:

OpenCvSharp4
OpenCvSharp4.runtime.win
MediaPipe.NET
Tesseract
Microsoft.ML.OnnxRuntime

Or run in the Package Manager Console:

Install-Package OpenCvSharp4
Install-Package OpenCvSharp4.runtime.win
Install-Package MediaPipe.NET
Install-Package Tesseract
Install-Package Microsoft.ML.OnnxRuntime

▶️ Running the Application
🧾 For Web Application (ASP.NET Core / Blazor):
dotnet run


Then open in your browser:

http://localhost:5000

🖥️ For Windows Form / WPF Application:

Just click Start (▶) in Visual Studio 2022.

📂 Models and Detection Files

Make sure the following cascade and model files exist:

wwwroot/cascades/
 ├── haarcascade_frontalface_default.xml
 ├── haarcascade_eye.xml
 └── haarcascade_hand.xml


If missing, download them from:
🔗 OpenCV Haarcascades

📊 Output Examples
Module	Output
Text Detection	Extracted text displayed in UI and saved in file/database
Face Detection	Bounding boxes drawn on live camera feed
Gesture Detection	Tracked hand positions + status indicators
Analytics	Logs and statistics displayed in dashboard
🌐 Future Enhancements

Deep learning–based gesture detection (ONNX / TensorFlow models)

Integrated Blazor Dashboard for analytics visualization

Multi-threaded frame processing

API endpoints for external app integration

Speech-to-text and voice-based feedback

📘 Example NuGet References in .csproj
<ItemGroup>
  <PackageReference Include="OpenCvSharp4" Version="4.9.0.20240108" />
  <PackageReference Include="OpenCvSharp4.runtime.win" Version="4.9.0.20240108" />
  <PackageReference Include="MediaPipe.NET" Version="0.10.0" />
  <PackageReference Include="Tesseract" Version="5.0.0-beta" />
  <PackageReference Include="Microsoft.ML.OnnxRuntime" Version="1.17.0" />
</ItemGroup>

👨‍💻 Author

Tarakanta Dasmohapatra
📍 Bengaluru, India
🌐 GitHub Profile

📧 tarakanta@example.com

Building intelligent multimedia systems powered by AI & .NET Core.

⭐ Contribute

Contributions and pull requests are welcome!
If you’d like to enhance detection models or UI dashboards:

Fork the repository

Create a branch (feature-branch-name)

Commit your changes

Submit a Pull Request 🚀

🏁 License

Licensed under the MIT License — free to use and modify for personal or research projects.


---

## ✅ Current Status
- OCR normal + advanced flows are integrated with diagnostics and tamper signals.
- SceneAnalysis + ModelCanvas are unified around shared transport/frame processing utilities.
- Production observability and endpoint diagnostics are available for runtime monitoring.


// STAR_MUTIMEDIA/Models/RealTimeDetectionViewModel.cs
using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace STAR_MUTIMEDIA.Models
{
    public class RealTimeDetectionViewModel
    {
        public string SessionId { get; set; }
        public bool IsMobile { get; set; }
        public bool CameraAccess { get; set; }
        public List<string> AvailableCameras { get; set; }
        public DetectionSettings Settings { get; set; }
        public DateTime StartTime { get; set; }
        public string Version { get; set; }
        public string UserAgent { get; set; }
        public string CurrentTime { get; set; }
        public int ScreenWidth { get; set; }
        public int ScreenHeight { get; set; }
        public bool SupportsWebRTC { get; set; }
        public bool SupportsCanvas { get; set; }
        public string ConnectionType { get; set; }
        public bool IsSecureContext { get; set; }

        public RealTimeDetectionViewModel(HttpContext httpContext)
        {
            SessionId = Guid.NewGuid().ToString();
            AvailableCameras = new List<string>();
            Settings = new DetectionSettings();
            StartTime = DateTime.UtcNow;
            Version = "1.0.0";

            // Detect mobile device
            var userAgent = httpContext?.Request?.Headers["User-Agent"].ToString() ?? "";
            IsMobile = userAgent.Contains("Mobi", StringComparison.OrdinalIgnoreCase) ||
                      userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase) ||
                      userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase);

            UserAgent = userAgent;
            CurrentTime = DateTime.Now.ToString("HH:mm:ss");

            // Default screen dimensions
            ScreenWidth = IsMobile ? 375 : 1920;
            ScreenHeight = IsMobile ? 667 : 1080;

            // Feature detection (these would be set by JavaScript)
            SupportsWebRTC = true; // Assume modern browser
            SupportsCanvas = true;
            ConnectionType = "unknown";
            IsSecureContext = httpContext?.Request.IsHttps ?? false;
        }
    }

    // DTOs for API communication
    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public T Data { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime Timestamp { get; set; }

        public ApiResponse()
        {
            Timestamp = DateTime.UtcNow;
        }

        public static ApiResponse<T> SuccessResponse(T data)
        {
            return new ApiResponse<T>
            {
                Success = true,
                Data = data
            };
        }

        public static ApiResponse<T> ErrorResponse(string message)
        {
            return new ApiResponse<T>
            {
                Success = false,
                ErrorMessage = message
            };
        }
    }

    public class ProcessFrameRequest
    {
        public string ImageData { get; set; }
        public string SessionId { get; set; }
        public string ProcessingMode { get; set; } = "standard";
        public Dictionary<string, object> Options { get; set; } = new Dictionary<string, object>();
    }

    public class SessionInfo
    {
        public string SessionId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastActivity { get; set; }
        public bool IsActive { get; set; }
        public DetectionStats Stats { get; set; }
        public DetectionSettings Settings { get; set; }

        public SessionInfo()
        {
            Stats = new DetectionStats();
            Settings = new DetectionSettings();
        }
    }

    public class CameraInfo
    {
        public string DeviceId { get; set; }
        public string Label { get; set; }
        public string GroupId { get; set; }
        public List<string> Capabilities { get; set; } = new List<string>();
        public bool IsFrontFacing { get; set; }
        public bool IsBackFacing { get; set; }
        public int MaxWidth { get; set; }
        public int MaxHeight { get; set; }
        public double MaxFrameRate { get; set; }
    }

    public class SystemStatus
    {
        public string Status { get; set; } = "initializing";
        public DateTime LastUpdate { get; set; }
        public double CpuUsage { get; set; }
        public double MemoryUsage { get; set; }
        public double NetworkLatency { get; set; }
        public int ActiveConnections { get; set; }
        public List<ComponentStatus> Components { get; set; } = new List<ComponentStatus>();

        public SystemStatus()
        {
            LastUpdate = DateTime.UtcNow;
        }
    }

    public class ComponentStatus
    {
        public string Name { get; set; }
        public string Status { get; set; } // online, offline, degraded, error
        public DateTime LastCheck { get; set; }
        public double ResponseTime { get; set; }
        public string Message { get; set; }
    }

    public class NotificationSettings
    {
        public bool EnableDesktopNotifications { get; set; } = true;
        public bool EnableSoundNotifications { get; set; } = false;
        public bool EnableToastNotifications { get; set; } = true;
        public Dictionary<string, bool> NotificationTypes { get; set; } = new Dictionary<string, bool>
        {
            { "face_detected", true },
            { "movement_detected", true },
            { "text_detected", true },
            { "camera_unstable", true },
            { "system_alert", true }
        };
        public int NotificationTimeout { get; set; } = 5000; // ms
    }

    public class UserPreferences
    {
        public string Theme { get; set; } = "dark";
        public string Language { get; set; } = "en";
        public bool EnableAnimations { get; set; } = true;
        public bool EnableTooltips { get; set; } = true;
        public bool EnableShortcuts { get; set; } = true;
        public string DefaultMode { get; set; } = "dashboard";
        public Dictionary<string, object> Customizations { get; set; } = new Dictionary<string, object>();
    }

    public class ExportOptions
    {
        public string Format { get; set; } = "json"; // json, csv, xml
        public bool IncludeImages { get; set; } = false;
        public bool IncludeMetadata { get; set; } = true;
        public bool IncludeLogs { get; set; } = true;
        public bool IncludeAnalytics { get; set; } = true;
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public List<string> DataTypes { get; set; } = new List<string>
        {
            "detections", "statistics", "notifications", "system_logs"
        };
    }
}
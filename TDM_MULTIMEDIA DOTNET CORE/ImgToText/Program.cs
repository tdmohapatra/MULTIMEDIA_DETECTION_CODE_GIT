using ImgToText.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.CookiePolicy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using STAR_MUTIMEDIA.Hubs;
using STAR_MUTIMEDIA.Services;
using STAR_MUTIMEDIA.Services.Observability;
using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
var configuredTessDataPath = builder.Configuration["TessDataPath"];
var fallbackTessDataPath = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "tessdata");
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
var useRedisSessions = builder.Configuration.GetValue<bool>("Session:UseRedis");
var redisConnection = builder.Configuration.GetConnectionString("Redis");
var effectiveTessDataPath = string.IsNullOrWhiteSpace(configuredTessDataPath)
    ? fallbackTessDataPath
    : (Path.IsPathRooted(configuredTessDataPath)
        ? configuredTessDataPath
        : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), configuredTessDataPath)));

// ---------------------- SERVICE REGISTRATION ----------------------
// Add MVC controllers with views
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 5 * 1024 * 1024;
});

// Add Razor Pages support (required for MapRazorPages)
builder.Services.AddRazorPages();

// ---------------------- SESSION SETUP ----------------------
if (useRedisSessions && !string.IsNullOrWhiteSpace(redisConnection))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnection;
        options.InstanceName = builder.Configuration["Session:RedisInstanceName"] ?? "STAR_MUTIMEDIA";
    });
}
else
{
    builder.Services.AddDistributedMemoryCache();
}

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(builder.Configuration.GetValue<int?>("Session:IdleTimeoutMinutes") ?? 30);
    options.IOTimeout = TimeSpan.FromSeconds(builder.Configuration.GetValue<int?>("Session:IOTimeoutSeconds") ?? 10);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.Name = builder.Configuration["Session:CookieName"] ?? ".STAR_MUTIMEDIA.Session";
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AppCors", policy =>
    {
        if (corsOrigins.Length > 0)
        {
            policy.WithOrigins(corsOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        }
        else if (builder.Environment.IsDevelopment())
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
    });
});

// ---------------------- CUSTOM SERVICE REGISTRATION ----------------------
// Image text service
builder.Services.AddScoped<IImageTextService>(provider =>
{
    return new ImageTextService(effectiveTessDataPath);
});

// Test service is development-only to keep production container clean.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<IRealTimeDetectionService_test>(provider =>
    {
        return new RealTimeDetectionService_test(effectiveTessDataPath);
    });
}

// Real-time detection service
builder.Services.AddSingleton<IRealTimeDetectionService>(provider =>
{
    return new RealTimeDetectionService(effectiveTessDataPath);
});

// Observability (production-grade logging + memory monitoring)
builder.Services.AddSingleton<IApplicationLoggingService, ApplicationLoggingService>();
builder.Services.AddSingleton<IMemoryMonitoringService, MemoryMonitoringService>();
builder.Services.AddHostedService<MemoryMonitoringBackgroundService>();

// ----------------------------------------------------------------------

// Build the app
var app = builder.Build();

// ---------------------- MIDDLEWARE PIPELINE ----------------------
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession(); // Must be before Authorization
app.UseCookiePolicy(new CookiePolicyOptions
{
    MinimumSameSitePolicy = SameSiteMode.Lax,
    Secure = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always
});
app.UseCors("AppCors");

// Lightweight API request timing log
app.Use(async (context, next) =>
{
    var sw = Stopwatch.StartNew();
    await next();
    sw.Stop();

    if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
    {
        var logger = context.RequestServices.GetRequiredService<IApplicationLoggingService>();
        logger.LogEvent(
            category: "HttpRequest",
            message: $"{context.Request.Method} {context.Request.Path} => {context.Response.StatusCode}",
            metadata: new Dictionary<string, object>
            {
                ["elapsedMs"] = Math.Round(sw.Elapsed.TotalMilliseconds, 2),
                ["traceId"] = context.TraceIdentifier
            });
    }
});

app.UseAuthorization();

// Map routes
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorPages(); // Ensure Razor Pages work if used
app.MapHub<DetectionHub>("/detectionHub");

// ---------------------- DIRECTORY CHECKS ----------------------

// Ensure tessdata directory exists
if (!Directory.Exists(effectiveTessDataPath))
{
    Directory.CreateDirectory(effectiveTessDataPath);
    app.Logger.LogWarning("Created tessdata directory at {TessDataPath}. Place language files (e.g. eng.traineddata) in this folder.", effectiveTessDataPath);
}
else
{
    app.Logger.LogInformation("Using tessdata path: {TessDataPath}", effectiveTessDataPath);
}

if (useRedisSessions && !string.IsNullOrWhiteSpace(redisConnection))
{
    app.Logger.LogInformation("Session provider: Redis");
}
else
{
    app.Logger.LogWarning("Session provider: InMemory (single-instance mode).");
}

// Ensure cascades directory exists
var cascadesPath = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "cascades");
if (!Directory.Exists(cascadesPath))
{
    Directory.CreateDirectory(cascadesPath);
    app.Logger.LogWarning("Created cascades directory at {CascadesPath}. Download required Haar cascade XML files before detection demo.", cascadesPath);
}

var requiredCascades = new[]
{
    "haarcascade_frontalface_alt.xml",
    "haarcascade_eye.xml",
    "haarcascade_mcs_leftear.xml",
    "haarcascade_mcs_rightear.xml"
};

foreach (var cascadeFile in requiredCascades)
{
    var fullCascadePath = Path.Combine(cascadesPath, cascadeFile);
    if (!File.Exists(fullCascadePath))
    {
        app.Logger.LogWarning("Missing required cascade file: {CascadeFile}", fullCascadePath);
    }
}

app.MapGet("/ready", () =>
{
    var missingItems = new List<string>();

    var requiredLanguageFile = Path.Combine(effectiveTessDataPath, "eng.traineddata");
    if (!File.Exists(requiredLanguageFile))
    {
        missingItems.Add(requiredLanguageFile);
    }

    foreach (var cascadeFile in requiredCascades)
    {
        var fullCascadePath = Path.Combine(cascadesPath, cascadeFile);
        if (!File.Exists(fullCascadePath))
        {
            missingItems.Add(fullCascadePath);
        }
    }

    if (missingItems.Count > 0)
    {
        return Results.Json(new
        {
            status = "NotReady",
            missing = missingItems
        }, statusCode: 503);
    }

    return Results.Ok(new
    {
        status = "Ready",
        timestamp = DateTime.UtcNow
    });
});

// Run the app
app.Run();

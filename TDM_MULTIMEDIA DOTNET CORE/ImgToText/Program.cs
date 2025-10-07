using ImgToText.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using STAR_MUTIMEDIA.Services;
using System;
using System.IO;

var builder = WebApplication.CreateBuilder(args);

// ---------------------- SERVICE REGISTRATION ----------------------
// Add MVC controllers with views
builder.Services.AddControllersWithViews();

// Add Razor Pages support (required for MapRazorPages)
builder.Services.AddRazorPages();

// ---------------------- SESSION SETUP ----------------------
builder.Services.AddDistributedMemoryCache(); // Required for session
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30); // Session timeout
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// ---------------------- CUSTOM SERVICE REGISTRATION ----------------------
// Image text service
builder.Services.AddScoped<IImageTextService>(provider =>
{
    var path = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "tessdata");
    return new ImageTextService(path);
});

// Real-time detection service (test)
builder.Services.AddSingleton<IRealTimeDetectionService_test>(provider =>
{
    var tessDataPath = builder.Configuration["TessDataPath"]
                       ?? Path.Combine(Directory.GetCurrentDirectory(), "tessdata");
    return new RealTimeDetectionService_test(tessDataPath);
});

// Real-time detection service
builder.Services.AddSingleton<IRealTimeDetectionService>(provider =>
{
    var tessDataPath = builder.Configuration["TessDataPath"]
                       ?? Path.Combine(Directory.GetCurrentDirectory(), "tessdata");
    return new RealTimeDetectionService(tessDataPath);
});

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
app.UseCors("AllowAll");
app.UseAuthorization();

// Map routes
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorPages(); // Ensure Razor Pages work if used

// ---------------------- DIRECTORY CHECKS ----------------------

// Ensure tessdata directory exists
var tessDataPath = Path.Combine(Directory.GetCurrentDirectory(), "tessdata");
if (!Directory.Exists(tessDataPath))
{
    Directory.CreateDirectory(tessDataPath);
    Console.WriteLine($"Created tessdata directory: {tessDataPath}");
}

// Ensure cascades directory exists
var cascadesPath = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "cascades");
if (!Directory.Exists(cascadesPath))
{
    Directory.CreateDirectory(cascadesPath);
    Console.WriteLine($"Created cascades directory: {cascadesPath}");

    Console.WriteLine("Please download the following Haar cascade files and place them in the cascades directory:");
    Console.WriteLine("- haarcascade_frontalface_alt.xml");
    Console.WriteLine("- haarcascade_eye.xml");
    Console.WriteLine("- haarcascade_hand.xml (optional)");
    Console.WriteLine("Download from: https://github.com/opencv/opencv/tree/master/data/haarcascades");
}

// Run the app
app.Run();

using Microsoft.AspNetCore.Mvc;

namespace ImgToText_UI.Controllers;

public class DetectionClientController : Controller
{
    private readonly IConfiguration _configuration;

    public DetectionClientController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public IActionResult Index()
    {
        ViewData["ApiBaseUrl"] = _configuration["DetectionApi:BaseUrl"] ?? "http://localhost:5078";
        return View();
    }

    [HttpGet("/DetectionView/Index")]
    [HttpGet("/DetectionView")]
    public IActionResult DetectionView()
    {
        ViewData["ApiBaseUrl"] = _configuration["DetectionApi:BaseUrl"] ?? "http://localhost:5078";
        return View("Index");
    }
}

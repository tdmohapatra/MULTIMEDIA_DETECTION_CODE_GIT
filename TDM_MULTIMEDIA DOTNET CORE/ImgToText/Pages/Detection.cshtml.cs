// Pages/Detection.cshtml.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace STAR_MUTIMEDIA.Pages
{
    public class DetectionModel : PageModel
    {
        private readonly IConfiguration _configuration;

        public DetectionModel(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string TessDataPath { get; private set; }

        public void OnGet()
        {
            TessDataPath = _configuration["TessDataPath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "tessdata");
        }
    }
}
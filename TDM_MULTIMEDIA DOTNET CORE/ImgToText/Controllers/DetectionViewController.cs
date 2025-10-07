using Microsoft.AspNetCore.Mvc;
using STAR_MUTIMEDIA.Services;

namespace STAR_MUTIMEDIA.Controllers
{
    public class DetectionViewController : Controller
    {
        private readonly IRealTimeDetectionService _detectionService;

        public DetectionViewController(IRealTimeDetectionService detectionService)
        {
            _detectionService = detectionService;
        }
        public IActionResult Index()
        {
            return View();
        }
    }
}

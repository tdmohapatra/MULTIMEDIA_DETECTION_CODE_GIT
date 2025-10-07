using Microsoft.AspNetCore.Mvc;
using STAR_MUTIMEDIA.Services;
namespace STAR_MUTIMEDIA.Controllers
{
    public class DetectionView_testController : Controller
    {
        private readonly IRealTimeDetectionService_test _detectionService;

        public DetectionView_testController(IRealTimeDetectionService_test detectionService)
        {
            _detectionService = detectionService;
        }
        public IActionResult Index()
        {
            return View();
        }
    }
}

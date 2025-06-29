using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SantaFeWaterSystem.Data;
using System.IO;
using System.Linq;

namespace SantaFeWaterSystem.Controllers
{
    [Authorize(Roles = "Admin,Staff")]
    public class SettingsController : BaseController
    {
        private readonly IWebHostEnvironment _env;

        public SettingsController(
            IWebHostEnvironment env,
            PermissionService permissionService,
            ApplicationDbContext context) // ✅ Added for BaseController
            : base(permissionService, context)
        {
            _env = env;
        }

        [HttpGet]
        public IActionResult QrCodes()
        {
            string gcashPath = "/images/gcash-qr.png";
            string mayaPath = "/images/maya-qr.png";

            string gcashPhysical = Path.Combine(_env.WebRootPath, "images", "gcash-qr.png");
            string mayaPhysical = Path.Combine(_env.WebRootPath, "images", "maya-qr.png");

            ViewBag.GcashImagePath = System.IO.File.Exists(gcashPhysical) ? gcashPath : null;
            ViewBag.MayaImagePath = System.IO.File.Exists(mayaPhysical) ? mayaPath : null;

            return View();
        }

        [HttpPost]
        public IActionResult UploadGcashQr(IFormFile qrImage)
        {
            return SaveQrImage(qrImage, "gcash-qr.png", "GCash");
        }

        [HttpPost]
        public IActionResult UploadMayaQr(IFormFile qrImage)
        {
            return SaveQrImage(qrImage, "maya-qr.png", "Maya");
        }

        [HttpPost]
        public IActionResult DeleteGcashQr()
        {
            return DeleteQrImage("gcash-qr.png", "GCash");
        }

        [HttpPost]
        public IActionResult DeleteMayaQr()
        {
            return DeleteQrImage("maya-qr.png", "Maya");
        }

        // Reusable method to save QR images
        private IActionResult SaveQrImage(IFormFile qrImage, string fileName, string label)
        {
            if (qrImage != null && qrImage.Length > 0)
            {
                var supportedTypes = new[] { "image/png", "image/jpeg" };
                if (!supportedTypes.Contains(qrImage.ContentType))
                {
                    TempData["Message"] = $"Invalid file type for {label}. Only PNG or JPEG allowed.";
                    return RedirectToAction("QrCodes");
                }

                string folder = Path.Combine(_env.WebRootPath, "images");
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                string filePath = Path.Combine(folder, fileName);
                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                }

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    qrImage.CopyTo(stream);
                }

                TempData["Message"] = $"{label} QR Code uploaded successfully.";
            }
            else
            {
                TempData["Message"] = $"Please select a file to upload for {label}.";
            }

            return RedirectToAction("QrCodes");
        }

        // Reusable method to delete QR images
        private IActionResult DeleteQrImage(string fileName, string label)
        {
            string filePath = Path.Combine(_env.WebRootPath, "images", fileName);

            if (System.IO.File.Exists(filePath))
            {
                System.IO.File.Delete(filePath);
                TempData["Message"] = $"{label} QR Code deleted successfully.";
            }
            else
            {
                TempData["Message"] = $"{label} QR Code not found.";
            }

            return RedirectToAction("QrCodes");
        }
    }
}

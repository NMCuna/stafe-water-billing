﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SantaFeWaterSystem.Data;
using SantaFeWaterSystem.Models;
using SantaFeWaterSystem.Models.ViewModels;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using SantaFeWaterSystem.ViewModels;
using SantaFeWaterSystem.Services;
using System.Security.Claims;
using System.Globalization;


namespace SantaFeWaterSystem.Controllers
{
    [Authorize(Roles = "User")]
    public class UserController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;

        public UserController(ApplicationDbContext context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        public async Task<IActionResult> Dashboard()
        {
            var userIdClaim = User.FindFirst("UserId");
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
                return Unauthorized("User ID not found or invalid.");

            var consumer = await _context.Consumers
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.UserId == userId);

            if (consumer == null)
                return Content("Consumer information not found.");

            // ✅ Set 2FA status for header display
            ViewBag.IsMfaEnabled = consumer.User?.IsMfaEnabled ?? false;

            // Get recent 5 bills
            var recentBills = await _context.Billings
                .Where(b => b.ConsumerId == consumer.Id)
                .OrderByDescending(b => b.BillingDate)
                .Take(5)
                .ToListAsync();

            var billIds = recentBills.Select(b => b.Id).ToList();
            var relatedPayments = await _context.Payments
                .Where(p => billIds.Contains(p.BillingId))
                .ToListAsync();

            foreach (var bill in recentBills)
            {
                var paymentsForBill = relatedPayments.Where(p => p.BillingId == bill.Id);
                bool hasVerified = paymentsForBill.Any(p => p.IsVerified);
                bool hasAny = paymentsForBill.Any();

                bill.Status = hasVerified ? "Paid" : hasAny ? "Pending" : "Unpaid";
            }

            var recentPayments = await _context.Payments
                .Where(p => p.ConsumerId == consumer.Id)
                .Include(p => p.Billing)
                .OrderByDescending(p => p.PaymentDate)
                .Take(5)
                .ToListAsync();

            var recentSupportTickets = await _context.Supports
                .Where(s => s.ConsumerId == consumer.Id)
                .OrderByDescending(s => s.CreatedAt)
                .Take(5)
                .ToListAsync();

            var notifications = await _context.Notifications
                .Where(n => n.ConsumerId == consumer.Id)
                .OrderByDescending(n => n.CreatedAt)
                .ToListAsync();

            var vm = new UserDashboardViewModel
            {
                Consumer = consumer,
                RecentBills = recentBills,
                RecentPayments = recentPayments,
                RecentSupportTickets = recentSupportTickets,
                Notifications = notifications
            };

            return View(vm);
        }




        ///////////////////////////
        /////BillingHistory////
        ///////////////////////////

        [Authorize(Roles = "User")]
        public async Task<IActionResult> BillingHistory(string? searchTerm, string? statusFilter)
        {
            int userId = int.Parse(User.FindFirst("UserId")!.Value);

            var consumer = await _context.Consumers
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.UserId == userId);

            if (consumer == null) return NotFound();

            // Build billing query
            var query = _context.Billings
                .Where(b => b.ConsumerId == consumer.Id)
                .AsQueryable();

            if (!string.IsNullOrEmpty(searchTerm))
            {
                query = query.Where(b =>
                    b.Status.Contains(searchTerm) ||
                    b.BillingDate.ToString().Contains(searchTerm)
                );
            }

            if (!string.IsNullOrEmpty(statusFilter))
            {
                query = query.Where(b => b.Status == statusFilter);
            }

            var billings = await query.OrderByDescending(b => b.BillingDate).ToListAsync();

            var billingIds = billings.Select(b => b.Id).ToList();

            // ✅ One query to get all payment statuses
            var paymentStatuses = await _context.Payments
                .Where(p => billingIds.Contains(p.BillingId))
                .GroupBy(p => p.BillingId)
                .Select(g => new
                {
                    BillingId = g.Key,
                    HasVerified = g.Any(p => p.IsVerified),
                    HasAny = g.Any()
                })
                .ToListAsync();

            // ✅ Build view models
            var billingViewModels = billings.Select(b =>
            {
                var statusInfo = paymentStatuses.FirstOrDefault(p => p.BillingId == b.Id);

                string status = statusInfo?.HasVerified == true
                    ? "Paid"
                    : statusInfo?.HasAny == true
                        ? "Pending"
                        : "Unpaid";

                return new BillingViewModel
                {
                    Id = b.Id,
                    BillNo = b.BillNo ?? "",
                    BillingDate = b.BillingDate,
                    DueDate = b.DueDate,
                    PreviousReading = b.PreviousReading,
                    PresentReading = b.PresentReading,
                    CubicMeterUsed = b.CubicMeterUsed,
                    AmountDue = b.AmountDue,
                    Penalty = b.Penalty,
                    AdditionalFees = b.AdditionalFees,
                    TotalAmount = b.TotalAmount,
                    Status = status,
                    FullName = $"{consumer.FirstName} {(string.IsNullOrWhiteSpace(consumer.MiddleName) ? "" : consumer.MiddleName + " ")}{consumer.LastName}".Trim(),
                    AccountNumber = consumer.User?.AccountNumber ?? ""
                };
            }).ToList();

            var viewModel = new BillingHistoryViewModel
            {
                ConsumerName = consumer.FirstName,
                ConsumerAddress = consumer.HomeAddress,
                Billings = billingViewModels
            };

            return View(viewModel);
        }




        [Authorize(Roles = "User")]
        public async Task<IActionResult> BillingDetails(int id)
        {
            var userId = int.Parse(User.FindFirst("UserId")!.Value);
            var consumer = await _context.Consumers
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.UserId == userId);

            if (consumer == null) return NotFound();

            var billing = await _context.Billings.FirstOrDefaultAsync(b => b.Id == id && b.ConsumerId == consumer.Id);
            if (billing == null) return NotFound();

            var viewModel = new BillingViewModel
            {
                Id = billing.Id,
                BillNo = billing.BillNo ?? "",
                BillingDate = billing.BillingDate,
                DueDate = billing.DueDate,
                AmountDue = billing.AmountDue,
                PreviousReading = billing.PreviousReading,
                PresentReading = billing.PresentReading,
                CubicMeterUsed = billing.CubicMeterUsed,
                Penalty = billing.Penalty,
                AdditionalFees = billing.AdditionalFees ?? 0,
                TotalAmount = billing.TotalAmount,
                Status = billing.Status,
                FullName = $"{consumer.FirstName} {(string.IsNullOrWhiteSpace(consumer.MiddleName) ? "" : consumer.MiddleName + " ")}{consumer.LastName}".Trim(),
                AccountNumber = consumer.User?.AccountNumber ?? ""
            };

            return View(viewModel);
        }



        [Authorize(Roles = "User")]
        [HttpGet]
        public async Task<IActionResult> DownloadBillingHistoryPdf(string? searchTerm, string? statusFilter)
        {
            int userId = int.Parse(User.FindFirst("UserId")!.Value);

            var consumer = await _context.Consumers
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.UserId == userId);

            if (consumer == null)
                return NotFound();

            var billingsQuery = _context.Billings
                .Where(b => b.ConsumerId == consumer.Id);

            if (!string.IsNullOrEmpty(searchTerm))
            {
                billingsQuery = billingsQuery.Where(b =>
                    b.BillNo.Contains(searchTerm) ||
                    b.Status.Contains(searchTerm));
            }

            if (!string.IsNullOrEmpty(statusFilter))
            {
                billingsQuery = billingsQuery.Where(b => b.Status == statusFilter);
            }

            var billings = await billingsQuery
                .OrderByDescending(b => b.BillingDate)
                .ToListAsync();

            // 🔄 Map Billing model to BillingViewModel
            var billingViewModels = billings.Select(b => new BillingViewModel
            {
                AccountNumber = consumer.User?.AccountNumber ?? "-",
                FullName = consumer.FullName,
                BillNo = b.BillNo,
                BillingDate = b.BillingDate,
                DueDate = b.DueDate,
                PreviousReading = b.PreviousReading,
                PresentReading = b.PresentReading,
                CubicMeterUsed = b.CubicMeterUsed,
                AmountDue = b.AmountDue,
                Penalty = b.Penalty,
                AdditionalFees = b.AdditionalFees,
                TotalAmount = b.TotalAmount,
                Status = b.Status
            }).ToList();

            var model = new BillingHistoryViewModel
            {
                Billings = billingViewModels
            };

            var pdfBytes = BillingHistoryPdfService.Generate(model, searchTerm, statusFilter);

            return File(pdfBytes, "application/pdf", "BillingHistory.pdf");
        }




        [Authorize(Roles = "User")]
        public async Task<IActionResult> DownloadBillingReceiptPdf(int billingId)
        {
            int userId = int.Parse(User.FindFirst("UserId")!.Value);

            var consumer = await _context.Consumers
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.UserId == userId);

            if (consumer == null || consumer.User == null)
                return NotFound();

            var billing = await _context.Billings
                .Where(b => b.Id == billingId && b.ConsumerId == consumer.Id)
                .Select(b => new BillingViewModel
                {
                    AccountNumber = consumer.User.AccountNumber ?? "-",
                    FullName = consumer.FullName,
                    BillNo = b.BillNo,
                    BillingDate = b.BillingDate,
                    DueDate = b.DueDate,
                    PreviousReading = b.PreviousReading,
                    PresentReading = b.PresentReading,
                    CubicMeterUsed = b.CubicMeterUsed,
                    AmountDue = b.AmountDue,
                    Penalty = b.Penalty,
                    AdditionalFees = b.AdditionalFees,
                    TotalAmount = b.TotalAmount,
                    Status = b.Status
                })
                .FirstOrDefaultAsync();

            if (billing == null)
                return NotFound();

            var pdfBytes = BillingPdfService.Generate(billing);

            return File(pdfBytes, "application/pdf", "BillingReceipt.pdf");
        }








        ///////////////////////////
        /////Payment user /////////
        ///////////////////////////


        [Authorize(Roles = "User")]
        public async Task<IActionResult> Payment()
        {
            var userIdClaim = User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(userIdClaim))
                return Unauthorized();

            int userId = int.Parse(userIdClaim);

            var consumer = await _context.Consumers
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.UserId == userId);

            if (consumer == null)
                return NotFound("Consumer not found");

            // Get all unpaid bills for the consumer
            var unpaidBills = await _context.Billings
                .Where(b => b.ConsumerId == consumer.Id && !b.IsPaid)
                .ToListAsync();

            // Get all related payments for these bills
            var billIds = unpaidBills.Select(b => b.Id).ToList();

            var relatedPayments = await _context.Payments
                .Where(p => billIds.Contains(p.BillingId))
                .ToListAsync();

            // Flag bills that have pending/unverified payment, and calculate TotalAmount
            foreach (var bill in unpaidBills)
            {
                bill.HasPendingPayment = relatedPayments.Any(p => p.BillingId == bill.Id && !p.IsVerified);
                bill.TotalAmount = bill.AmountDue + bill.Penalty + bill.AdditionalFees.GetValueOrDefault();
            }

            // 💡 Show all unpaid bills (no more filtering!)
            var pendingBills = unpaidBills;

            // Load previous payments for display
            var previousPayments = await _context.Payments
                .Where(p => p.ConsumerId == consumer.Id)
                .OrderByDescending(p => p.PaymentDate)
                .ToListAsync();

            var vm = new UserPaymentViewModel
            {
                Consumer = consumer,
                FirstName = consumer.FirstName,
                MiddleName = consumer.MiddleName,
                LastName = consumer.LastName,
                PendingBills = pendingBills,
                PreviousPayments = previousPayments
            };

            return View(vm);
        }




        [Authorize(Roles = "User")]
        [HttpGet]
        public async Task<IActionResult> Pay(int billId)
        {
            var userIdClaim = User.FindFirst("UserId");
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
                return Unauthorized();

            var bill = await _context.Billings
                .Include(b => b.Consumer)
                .ThenInclude(c => c.User)
                .FirstOrDefaultAsync(b => b.Id == billId && b.Consumer.UserId == userId);

            if (bill == null)
                return NotFound("Bill not found or unauthorized");

            var gcashSetting = await _context.Settings.FirstOrDefaultAsync(s => s.Key == "GcashQrImagePath");
            var mayaSetting = await _context.Settings.FirstOrDefaultAsync(s => s.Key == "MayaQrImagePath");

            var model = new PayViewModel
            {
                BillingId = bill.Id,
                FirstName = bill.Consumer.FirstName,
                MiddleName = bill.Consumer.MiddleName,
                LastName = bill.Consumer.LastName,
                ConsumerName = $"{bill.Consumer.FirstName} {bill.Consumer.MiddleName} {bill.Consumer.LastName}".Replace("  ", " ").Trim(),
                AccountNumber = bill.Consumer.User?.AccountNumber,
                AmountDue = bill.AmountDue,
                AdditionalFee = bill.AdditionalFees ?? 0m,
                Penalty = bill.Penalty,
                PreviousReading = bill.PreviousReading,
                PresentReading = bill.PresentReading,
                TransactionId = GenerateTransactionId(),
                GcashQrImageUrl = gcashSetting?.Value ?? "/images/gcash-qr.png",
                MayaQrImageUrl = mayaSetting?.Value ?? "/images/maya-qr.png"
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Pay(PayViewModel model)
        {
            var userIdClaim = User.FindFirst("UserId");
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
                return Unauthorized();

            var bill = await _context.Billings
                .Include(b => b.Consumer)
                .ThenInclude(c => c.User)
                .FirstOrDefaultAsync(b => b.Id == model.BillingId && b.Consumer.UserId == userId);

            if (bill == null)
                return NotFound("Bill not found or unauthorized");

            var gcashSetting = await _context.Settings.FirstOrDefaultAsync(s => s.Key == "GcashQrImagePath");
            var mayaSetting = await _context.Settings.FirstOrDefaultAsync(s => s.Key == "MayaQrImagePath");

            model.GcashQrImageUrl = gcashSetting?.Value ?? "/images/gcash-qr.png";
            model.MayaQrImageUrl = mayaSetting?.Value ?? "/images/maya-qr.png";
            model.AccountNumber = bill.Consumer.User?.AccountNumber;

            if (!ModelState.IsValid)
                return View(model);

            if (model.Receipt == null || model.Receipt.Length == 0)
            {
                ModelState.AddModelError("Receipt", "Please upload the receipt image.");
                return View(model);
            }

            if (model.Receipt.Length > 5 * 1024 * 1024)
            {
                ModelState.AddModelError("Receipt", "File size cannot exceed 5 MB.");
                return View(model);
            }

            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".pdf" };
            var extension = Path.GetExtension(model.Receipt.FileName).ToLowerInvariant();
            if (!allowedExtensions.Contains(extension))
            {
                ModelState.AddModelError("Receipt", "Invalid file type. Allowed: JPG, JPEG, PNG, PDF.");
                return View(model);
            }

            var supportedTypes = new[] { "image/png", "image/jpeg", "image/jpg", "application/pdf" };
            if (!supportedTypes.Contains(model.Receipt.ContentType.ToLower()))
            {
                ModelState.AddModelError("Receipt", "Invalid file type. Only PNG, JPEG, or PDF allowed.");
                return View(model);
            }

            // Save uploaded receipt file
            string receiptFileName = $"{Guid.NewGuid()}{extension}";
            var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "receipts");
            Directory.CreateDirectory(uploadsFolder);
            var filePath = Path.Combine(uploadsFolder, receiptFileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await model.Receipt.CopyToAsync(stream);
            }

            // Save payment record
            var payment = new Payment
            {
                ConsumerId = bill.ConsumerId,
                BillingId = bill.Id,
                AmountPaid = model.TotalAmount,
                Method = model.SelectedPaymentMethod,
                PaymentDate = DateTime.Now,
                TransactionId = model.TransactionId,
                ReceiptPath = receiptFileName,
                IsVerified = false
            };

            _context.Payments.Add(payment);

            // Update billing status
            bill.Status = "Pending";
            bill.IsPaid = false;

            await _context.SaveChangesAsync();

            // ✅ Store Transaction Info in TempData for Confirmation View
            TempData["TransactionId"] = model.TransactionId;
            TempData["AmountPaid"] = model.TotalAmount.ToString("F2"); // <- 🔧 Fixed here
            TempData["Method"] = model.SelectedPaymentMethod;
            TempData["PaymentDate"] = DateTime.Now.ToString("MMM dd, yyyy hh:mm tt");

            // Message for optional alert (you may keep this or remove)
            TempData["Message"] = "Payment submitted successfully. Waiting for admin verification.";

            // Redirect to confirmation view
            return RedirectToAction("PaymentConfirmation");
        }

        public IActionResult PaymentConfirmation()
        {
            // Redirect if accessed directly without payment submission
            if (TempData["TransactionId"] == null ||
                TempData["AmountPaid"] == null ||
                TempData["Method"] == null ||
                TempData["PaymentDate"] == null)
            {
                return RedirectToAction("Dashboard", "User");
            }

            // Keep TempData values alive for the view
            TempData.Keep();

            ViewBag.TransactionId = TempData["TransactionId"];
            ViewBag.AmountPaid = TempData["AmountPaid"];
            ViewBag.Method = TempData["Method"];
            ViewBag.Date = TempData["PaymentDate"];

            return View();
        }



        private string GenerateTransactionId()
        {
            return "TXN" + DateTime.Now.ToString("yyyyMMddHHmmss") + new Random().Next(1000, 9999);
        }






        ///////////////////////////
        /////  Profile CONTROLLER////
        ///////////////////////////



        [Authorize(Roles = "User")]
        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            var userIdClaim = User.FindFirst("UserId");
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
                return Unauthorized();

            var consumer = await _context.Consumers
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.UserId == userId);

            if (consumer == null) return NotFound();

            var vm = new UserProfileViewModel
            {
                Id = consumer.Id,
                FirstName = consumer.FirstName,
                MiddleName = consumer.MiddleName,
                LastName = consumer.LastName,
                Address = consumer.HomeAddress,
                ContactNumber = consumer.ContactNumber,
                Email = consumer.Email,
                AccountNumber = consumer.User?.AccountNumber,
                AccountType = consumer.AccountType,
                MeterNo = consumer.MeterNo
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Profile(UserProfileViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var consumer = await _context.Consumers.Include(c => c.User).FirstOrDefaultAsync(c => c.Id == model.Id);
            if (consumer == null)
                return NotFound();

            var userIdClaim = User.FindFirst("UserId");
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId) || consumer.UserId != userId)
                return Unauthorized();

            // ✅ Update only editable fields
            consumer.FirstName = model.FirstName;
            consumer.MiddleName = model.MiddleName;
            consumer.LastName = model.LastName;
            consumer.HomeAddress = model.Address;
            consumer.ContactNumber = model.ContactNumber;
            consumer.Email = model.Email;

            await _context.SaveChangesAsync();

            TempData["Message"] = "Profile updated successfully.";

            return RedirectToAction("Profile");
        }





        ///////////////////////////
        /////Support CONTROLLER////
        ///////////////////////////


        [Authorize(Roles = "User")]
        [HttpGet]
        public async Task<IActionResult> Support()
        {
            var userIdClaim = User.FindFirst("UserId");
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
                return Unauthorized();

            var consumer = await _context.Consumers.FirstOrDefaultAsync(c => c.UserId == userId);
            if (consumer == null)
            {
                TempData["ToastType"] = "danger";
                TempData["ToastMessage"] = "Consumer account not found.";
                return RedirectToAction("Index", "Home");
            }

            var tickets = await _context.Supports
                .Where(s => s.ConsumerId == consumer.Id)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();

            var vm = new UserSupportViewModel
            {
                PreviousTickets = tickets
            };

            return View(vm);
        }

        [Authorize(Roles = "User")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Support(UserSupportViewModel model)
        {
            var userIdClaim = User.FindFirst("UserId");
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
                return Unauthorized();

            var consumer = await _context.Consumers.FirstOrDefaultAsync(c => c.UserId == userId);
            if (consumer == null)
            {
                TempData["ToastType"] = "danger";
                TempData["ToastMessage"] = "Unable to find your account. Please contact support.";
                return RedirectToAction("Support");
            }

            if (ModelState.IsValid)
            {
                var support = new Support
                {
                    ConsumerId = consumer.Id,
                    Subject = model.Subject,
                    Message = model.Message,
                    CreatedAt = DateTime.UtcNow,
                    Status = "Open",              // ✅ Ensure it is marked Open
                    IsResolved = false,           // ✅ Not resolved by default
                    AdminReply = null,            // ✅ No reply yet
                    IsReplySeen = false,
                    IsArchived = false
                };

                _context.Supports.Add(support);
                await _context.SaveChangesAsync();

                TempData["ToastType"] = "success";
                TempData["ToastMessage"] = "Support request submitted successfully!";
                return RedirectToAction("Support");
            }

            TempData["ToastType"] = "danger";
            TempData["ToastMessage"] = "There were issues with your submission. Please correct the form.";

            // Repopulate previous tickets
            model.PreviousTickets = await _context.Supports
                .Where(s => s.ConsumerId == consumer.Id)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();

            return View(model);
        }










        // Show the empty feedback form
        [HttpGet]
        public IActionResult Feedback()
        {
            return View("~/Views/User/Feedback.cshtml", new Feedback());
        }

        // Process submitted feedback form
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Feedback(Feedback model)
        {
            if (ModelState.IsValid)
            {
                var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (int.TryParse(userIdString, out int userId))
                {
                    model.UserId = userId;
                    model.SubmittedAt = DateTime.UtcNow;

                    _context.Feedbacks.Add(model);
                    await _context.SaveChangesAsync();

                    TempData["Message"] = "Thank you for your feedback!";
                    return RedirectToAction("Feedback"); // reload form with success message
                }
                ModelState.AddModelError("", "Unable to determine your user ID.");
            }

            return View("~/Views/User/Feedback.cshtml", model);
        }
    }
}

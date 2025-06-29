﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SantaFeWaterSystem.Data;
using SantaFeWaterSystem.Models;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using SantaFeWaterSystem.ViewModels;
using Microsoft.AspNetCore.Mvc.Rendering;
using QuestPDF.Fluent;
using X.PagedList;
using SantaFeWaterSystem.Helpers;
using SantaFeWaterSystem.Services;
using Microsoft.AspNetCore.Antiforgery;
using SantaFeWaterSystem.Settings;
using Microsoft.Extensions.Options;
using System.Globalization;


namespace SantaFeWaterSystem.Controllers
{
    [Authorize(Roles = "Admin,Staff")]
    public class BillingController(
        ApplicationDbContext context,
        PermissionService permissionService,
        ISemaphoreSmsService smsService,     
        IOptions<SemaphoreSettings> semaphoreOptions,
         IWebHostEnvironment env,
          ISmsQueue smsQueue
    ) : BaseController(permissionService, context)
    {
        private readonly ISemaphoreSmsService _smsService = smsService;        
        private readonly SemaphoreSettings _semaphoreSettings = semaphoreOptions.Value;
        private readonly IWebHostEnvironment _env = env;
        private readonly ISmsQueue _smsQueue = smsQueue;




        public IActionResult TriggerDisconnectionCheck()
        {
            CheckAndScheduleDisconnections();
            return RedirectToAction("Index", "Disconnection");
        }

        public void CheckAndScheduleDisconnections()
        {
            var today = DateTime.Today;
            var overdueThreshold = today.AddMonths(-2).AddDays(-3); // 2 months + 3 days ago

            // Find all unpaid bills that are overdue past the threshold
            var overdueBills = _context.Billings
                .Include(b => b.Consumer)
                .Where(b => !b.IsPaid && b.BillingDate <= overdueThreshold)
                .ToList();

            foreach (var bill in overdueBills)
            {
                var existing = _context.Disconnections
                    .FirstOrDefault(d => d.ConsumerId == bill.ConsumerId && !d.IsReconnected);

                if (existing == null)
                {
                    var disconnection = new Disconnection
                    {
                        ConsumerId = bill.ConsumerId,
                        DisconnectionDate = today,
                        Reason = "Unpaid bill for over 2 months + 3 days"
                    };

                    _context.Disconnections.Add(disconnection);
                }
            }

            _context.SaveChanges();
        }



        // GET: Billing    
public async Task<IActionResult> Index(string searchTerm, string statusFilter, int page = 1)
    {
        int pageSize = 5;

        var query = _context.Billings
            .Include(b => b.Consumer)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            query = query.Where(b =>
                b.Consumer.FirstName.Contains(searchTerm) ||
                b.Consumer.LastName.Contains(searchTerm));
        }

        if (!string.IsNullOrWhiteSpace(statusFilter))
        {
            query = query.Where(b => b.Status == statusFilter);
        }

            var billingsPaged = await query
         .OrderByDescending(b => b.BillingDate)
         .ToPagedListAsync(page, pageSize);

            // Apply penalty and audit on just the current page
            foreach (var bill in billingsPaged)
            {
                var consumer = bill.Consumer;

                var rateRecord = await _context.Rates
                    .Where(r => r.AccountType == consumer.AccountType && r.EffectiveDate <= bill.BillingDate)
                    .OrderByDescending(r => r.EffectiveDate)
                    .FirstOrDefaultAsync();

                decimal penaltyFromRate = rateRecord?.PenaltyAmount ?? 10m;
                decimal originalPenalty = bill.Penalty;

                if (bill.DueDate < DateTime.Today && bill.Status != "Paid")
                {
                    bill.Penalty = penaltyFromRate;

                    if (originalPenalty != penaltyFromRate)
                    {
                        _context.AuditTrails.Add(new AuditTrail
                        {
                            Action = "PenaltyApplied",
                            PerformedBy = User.Identity?.Name ?? "System",
                            Timestamp = DateTime.Now,
                            Details = $"Applied ₱{penaltyFromRate} penalty to BillNo {bill.BillNo} (Consumer: {consumer.FirstName} {consumer.LastName})"
                        });
                    }
                }
                else
                {
                    bill.Penalty = 0m;
                }

                decimal additionalFees = bill.AdditionalFees ?? 0m;
                bill.TotalAmount = bill.AmountDue + bill.Penalty + additionalFees;
            }

            _context.Billings.UpdateRange(billingsPaged);
            await _context.SaveChangesAsync();

            return View(billingsPaged);

        }




        [Authorize(Roles = "Admin,Staff")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExportSelectedToPdf(string selectedBillingIds)
        {
            if (string.IsNullOrWhiteSpace(selectedBillingIds))
                return BadRequest("No billing IDs selected.");

            var ids = selectedBillingIds.Split(',')
                .Where(id => int.TryParse(id, out _))
                .Select(int.Parse)
                .ToList();

            if (!ids.Any())
                return BadRequest("Invalid billing ID list.");

            var billings = await _context.Billings
                .Include(b => b.Consumer)
                .Where(b => ids.Contains(b.Id))
                .ToListAsync();

            if (!billings.Any())
                return NotFound("No billing records found for the selected IDs.");

            var model = new MonthlyBillingSummaryViewModel
            {
                Month = DateTime.Now.Month, // Optional, since these are mixed
                Year = DateTime.Now.Year,
                Billings = billings
            };

            var document = new MonthlyBillingPdfDocument(model);
            var pdfStream = new MemoryStream();
            document.GeneratePdf(pdfStream);
            pdfStream.Position = 0;

            string filename = $"SelectedBillingExport_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            return File(pdfStream, "application/pdf", filename);
        }





        [Authorize(Roles = "Admin,Staff")]
        public async Task<IActionResult> ExportMonthlySummaryPdf(int month, int year)
        {
            if (month < 1 || month > 12 || year < 2000 || year > DateTime.Now.Year)
                return BadRequest("Invalid month or year.");

            var billings = await _context.Billings
                .Include(b => b.Consumer)
                .Where(b => b.BillingDate.Month == month && b.BillingDate.Year == year)
                .ToListAsync();

            if (!billings.Any())
                return NotFound("No billing records found for the selected month and year.");

            var model = new MonthlyBillingSummaryViewModel
            {
                Month = month,
                Year = year,
                Billings = billings
            };

            var document = new MonthlyBillingPdfDocument(model);
            var pdfStream = new MemoryStream();
            document.GeneratePdf(pdfStream);
            pdfStream.Position = 0;

            string filename = $"BillingSummary_{year}_{month}.pdf";
            return File(pdfStream, "application/pdf", filename);
        }









        // GET: Billing/Create
        // GET: AdminDashboard/Billing/Create
        public IActionResult Create()
        {
            DateTime now = DateTime.Now;
            int currentMonth = now.Month;
            int currentYear = now.Year;

            // Get consumers who already have an active bill this month
            var consumersWithActiveBills = _context.Billings
                .Where(b => b.BillingDate.Month == currentMonth &&
                            b.BillingDate.Year == currentYear &&
                            b.DueDate >= now)
                .Select(b => b.ConsumerId)
                .Distinct()
                .ToList();

            var eligibleConsumers = _context.Consumers
                .Where(c => !consumersWithActiveBills.Contains(c.Id))
                .ToList();

            var billingDate = now.Date;
            var dueDate = billingDate.AddDays(20);

            var viewModel = new BillingFormViewModel
            {
                Billing = new Billing
                {
                    BillingDate = billingDate,
                    DueDate = dueDate,
                    // BillNo will be set later during POST
                },
                Consumers = eligibleConsumers
            };

            return View(viewModel);
        }



        // POST: AdminDashboard/Billing/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(BillingFormViewModel viewModel)
        {
            if (ModelState.IsValid)
            {
                var billing = viewModel.Billing;

                // Get latest billing for PreviousReading
                var lastBilling = await _context.Billings
                    .Where(b => b.ConsumerId == billing.ConsumerId)
                    .OrderByDescending(b => b.BillingDate)
                    .FirstOrDefaultAsync();

                billing.PreviousReading = lastBilling?.PresentReading ?? 0m;
                billing.CubicMeterUsed = billing.PresentReading - billing.PreviousReading;

                if (billing.CubicMeterUsed < 0)
                {
                    ModelState.AddModelError("", "Present Reading must be greater than or equal to Previous Reading.");
                    await LoadEligibleConsumersAsync(viewModel);
                    return View(viewModel);
                }

                var consumer = await _context.Consumers.FindAsync(billing.ConsumerId);
                if (consumer == null)
                {
                    ModelState.AddModelError("", "Consumer not found.");
                    await LoadEligibleConsumersAsync(viewModel);
                    return View(viewModel);
                }

                // Get rate by account type and billing date
                var rateRecord = await _context.Rates
                    .Where(r => r.AccountType == consumer.AccountType && r.EffectiveDate <= billing.BillingDate)
                    .OrderByDescending(r => r.EffectiveDate)
                    .FirstOrDefaultAsync();

                if (rateRecord == null)
                {
                    ModelState.AddModelError("", "No rate defined for the consumer's account type at the billing date.");
                    await LoadEligibleConsumersAsync(viewModel);
                    return View(viewModel);
                }

                decimal rate = rateRecord.RatePerCubicMeter;

                // Apply minimum usage if below 10 cubic meters
                if (billing.CubicMeterUsed < 10)
                {
                    billing.CubicMeterUsed = 10;
                }

                billing.AmountDue = rate * billing.CubicMeterUsed;

                // Set default due date if not set
                if (billing.DueDate == default)
                {
                    billing.DueDate = billing.BillingDate.AddDays(20);
                }

                // Set default status if empty
                if (string.IsNullOrWhiteSpace(billing.Status))
                {
                    billing.Status = "Unpaid";
                }

                decimal additionalFees = billing.AdditionalFees ?? 0m;

                // Calculate penalty if overdue and unpaid
                billing.Penalty = 0m;
                if (DateTime.Now > billing.DueDate && billing.Status != "Paid")
                {
                    billing.Penalty = rateRecord.PenaltyAmount > 0 ? rateRecord.PenaltyAmount : 10m;
                }

                billing.TotalAmount = billing.AmountDue + billing.Penalty + additionalFees;

                // ✅ Generate and assign BillNo here
                billing.BillNo = await GenerateBillNoForConsumerAsync(billing.ConsumerId);

                _context.Billings.Add(billing);
                await _context.SaveChangesAsync();

                // ✅ Add AuditTrail after successful SaveChanges
                _context.AuditTrails.Add(new AuditTrail
                {
                    PerformedBy = User.Identity?.Name ?? "Unknown",
                    Action = "Created Billing",
                    Timestamp = DateTime.Now,
                    Details = $"Created billing for Consumer ID: {billing.ConsumerId}, " +
                              $"Bill No: {billing.BillNo}, " +
                              $"Billing Date: {billing.BillingDate:MM/dd/yyyy}, " +
                              $"Previous Reading: {billing.PreviousReading}, " +
                              $"Present Reading: {billing.PresentReading}, " +
                              $"Cubic Meter Used: {billing.CubicMeterUsed}, " +
                              $"Amount Due: ₱{billing.AmountDue:N2}, " +
                              $"Penalty: ₱{billing.Penalty:N2}, " +
                              $"Additional Fees: ₱{billing.AdditionalFees ?? 0:N2}, " +
                              $"Total: ₱{billing.TotalAmount:N2}, " +
                              $"Due Date: {billing.DueDate:MM/dd/yyyy}, " +
                              $"Status: {billing.Status}"
                });

                await _context.SaveChangesAsync();

                return RedirectToAction(nameof(Index));
            }

            // If we got here, ModelState is invalid — reload eligible consumers
            await LoadEligibleConsumersAsync(viewModel);
            return View(viewModel);
        }


        // Helper method to populate consumer dropdown
        private async Task LoadEligibleConsumersAsync(BillingFormViewModel viewModel)
        {
            DateTime now = DateTime.Now;
            int currentMonth = now.Month;
            int currentYear = now.Year;

            var consumersWithActiveBills = await _context.Billings
                .Where(b => b.BillingDate.Month == currentMonth &&
                            b.BillingDate.Year == currentYear &&
                            b.DueDate >= now)
                .Select(b => b.ConsumerId)
                .Distinct()
                .ToListAsync();

            viewModel.Consumers = await _context.Consumers
                .Where(c => !consumersWithActiveBills.Contains(c.Id))
                .ToListAsync();
        }

        // Helper to generate unique BillNo per consumer
        private async Task<string> GenerateBillNoForConsumerAsync(int consumerId)
        {
            var lastBill = await _context.Billings
                .Where(b => b.ConsumerId == consumerId)
                .OrderByDescending(b => b.Id)
                .FirstOrDefaultAsync();

            int newBillNumber = 1;

            if (lastBill != null && int.TryParse(lastBill.BillNo, out int lastBillNo))
            {
                newBillNumber = lastBillNo + 1;
            }

            return newBillNumber.ToString("D4"); // Example: 0001, 0002, etc.
        }




        // GET: Billing/Details/5
        [Authorize(Roles = "Admin,Staff")]
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
                return NotFound();

            // Retrieve the billing record including related Consumer
            var billing = await _context.Billings
                .Include(b => b.Consumer)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (billing == null)
                return NotFound();

            // ✅ Log the audit trail
            _context.AuditTrails.Add(new AuditTrail
            {
                PerformedBy = User.Identity?.Name ?? "Unknown",
                Action = "Viewed Billing Details",
                Timestamp = DateTime.Now,
                Details = $"Viewed Billing ID: {billing.Id}, Bill No: {billing.BillNo}, Consumer: {billing.Consumer?.FirstName} {billing.Consumer?.LastName}"
            });

            await _context.SaveChangesAsync();

            // Pass the billing model to the Details view
            return View(billing);
        }


        // GET: Billing/Edit/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var billing = await _context.Billings.FindAsync(id);
            if (billing == null) return NotFound();

            ViewBag.Consumers = _context.Consumers.ToList();
            return View(billing);
        }


        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Billing billing)
        {
            if (id != billing.Id) return NotFound();

            if (!ModelState.IsValid)
            {
                ViewBag.Consumers = _context.Consumers.ToList();
                return View(billing);
            }

            var existing = await _context.Billings
                .Include(b => b.Consumer)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (existing == null) return NotFound();

            try
            {
                // Store old values for audit
                var oldReading = existing.PresentReading;
                var oldAmountDue = existing.AmountDue;
                var oldPenalty = existing.Penalty;
                var oldAdditionalFees = existing.AdditionalFees;
                var oldTotalAmount = existing.TotalAmount;
                var oldStatus = existing.Status;
                var oldDueDate = existing.DueDate;
                var oldConsumerId = existing.ConsumerId;
                var oldBillNo = existing.BillNo;

                // Recalculate fields and update
                existing.BillNo = billing.BillNo;
                existing.BillingDate = billing.BillingDate;
                existing.PreviousReading = billing.PreviousReading;
                existing.PresentReading = billing.PresentReading;
                existing.CubicMeterUsed = billing.PresentReading - billing.PreviousReading;
                existing.AmountDue = billing.AmountDue;
                existing.AdditionalFees = billing.AdditionalFees ?? 0;
                existing.Status = billing.Status;
                existing.ConsumerId = billing.ConsumerId;

                if (billing.DueDate == default || billing.DueDate < billing.BillingDate)
                    existing.DueDate = billing.BillingDate.AddDays(20);
                else
                    existing.DueDate = billing.DueDate;

                if (existing.Status == "Unpaid" && DateTime.Now > existing.DueDate)
                    existing.Penalty = existing.AmountDue * 0.10m;
                else
                    existing.Penalty = 0;

                existing.TotalAmount = existing.AmountDue + existing.Penalty + existing.AdditionalFees.GetValueOrDefault();

                _context.Update(existing);

                // ✅ Audit trail with old vs. new values
                _context.AuditTrails.Add(new AuditTrail
                {
                    PerformedBy = User.Identity?.Name ?? "Unknown",
                    Action = "Edited Billing",
                    Timestamp = DateTime.Now,
                    Details = $"Billing ID: {existing.Id}, Consumer ID: {oldConsumerId} → {existing.ConsumerId}, " +
                              $"Bill No: {oldBillNo} → {existing.BillNo}, " +
                              $"Present Reading: {oldReading} → {existing.PresentReading}, " +
                              $"Amount Due: ₱{oldAmountDue:N2} → ₱{existing.AmountDue:N2}, " +
                              $"Penalty: ₱{oldPenalty:N2} → ₱{existing.Penalty:N2}, " +
                              $"Additional Fees: ₱{oldAdditionalFees:N2} → ₱{existing.AdditionalFees:N2}, " +
                              $"Total: ₱{oldTotalAmount:N2} → ₱{existing.TotalAmount:N2}, " +
                              $"Status: {oldStatus} → {existing.Status}, " +
                              $"Due Date: {oldDueDate:MM/dd/yyyy} → {existing.DueDate:MM/dd/yyyy}"
                });

                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!_context.Billings.Any(e => e.Id == id))
                    return NotFound();
                else
                    throw;
            }
        }



        // GET: Billing/Delete/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var billing = await _context.Billings
                .Include(b => b.Consumer)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (billing == null) return NotFound();

            return View(billing);
        }


        [Authorize(Roles = "Admin")]
        [HttpPost, ActionName("DeleteConfirmed")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var billing = await _context.Billings
                .Include(b => b.Consumer)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (billing == null) return NotFound();

            _context.Billings.Remove(billing);

            // ✅ Audit trail log for deletion
            _context.AuditTrails.Add(new AuditTrail
            {
                PerformedBy = User.Identity?.Name ?? "Unknown",
                Action = "Deleted Billing",
                Timestamp = DateTime.Now,
                Details = $"Deleted Billing ID: {billing.Id}, Bill No: {billing.BillNo}, " +
                          $"Consumer: {billing.Consumer?.FirstName} {billing.Consumer?.LastName}, " +
                          $"Amount Due: ₱{billing.AmountDue:N2}, Total: ₱{billing.TotalAmount:N2}, " +
                          $"Status: {billing.Status}, Billing Date: {billing.BillingDate:MM/dd/yyyy}"
            });

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

















        [HttpPost]
        public async Task<IActionResult> SendSms([FromBody] List<int> billingIds)
        {
            var antiForgery = HttpContext.RequestServices.GetRequiredService<IAntiforgery>();
            await antiForgery.ValidateRequestAsync(HttpContext);

            if (billingIds == null || billingIds.Count == 0)
                return BadRequest("No billing IDs provided.");

            var billings = await _context.Billings
                .Include(b => b.Consumer)
                .Where(b => billingIds.Contains(b.Id))
                .ToListAsync();

            int sentCount = 0;
            List<string> failed = new();

            foreach (var billing in billings)
            {
                var consumer = billing.Consumer;
                var number = consumer?.ContactNumber?.Trim();
                if (string.IsNullOrWhiteSpace(number)) continue;

                string amount = billing.TotalAmount.ToString("C", new CultureInfo("en-PH"));
                string message = $"Santa Fe Water Billing Reminder:\n" +
                                 $"Name: {consumer.FirstName} {consumer.LastName}\n" +
                                 $"Amount Due: {amount}\n" +
                                 $"Due Date: {billing.DueDate:MMMM dd, yyyy}";

                if (_env.IsDevelopment())
                {
                    var (success, response) = await _smsService.SendSmsAsync(number, message);
                    if (success)
                        sentCount++;
                    else
                        failed.Add($"{number}: {response}");
                }
                else
                {
                    await _smsQueue.QueueMessageAsync(number, message, consumer.Id);
                    sentCount++;
                }
            }

            return Ok(new
            {
                success = true,
                messagesSent = sentCount,
                failedRecipients = failed
            });
        }


    }
}

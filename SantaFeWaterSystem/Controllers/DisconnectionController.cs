using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc;
using SantaFeWaterSystem.Data;
using SantaFeWaterSystem.Models;
using Microsoft.EntityFrameworkCore;
using SantaFeWaterSystem.ViewModels;
using X.PagedList;
using X.PagedList.Mvc.Core;
using SantaFeWaterSystem.Helpers;
using SantaFeWaterSystem.Controllers;




[Authorize(Roles = "Admin,Staff")]
public class DisconnectionController : BaseController
{
    
    private const int PageSize = 5;

    public DisconnectionController(ApplicationDbContext context, PermissionService permissionService)
        : base(permissionService, context)
    {
       
    }

    public async Task<IActionResult> Index(string searchTerm, int page = 1, int pageSize = 5)
    {
        var cutoffDate = DateTime.Today.AddDays(-3).AddMonths(-2);

        var query = _context.Billings
            .Include(b => b.Consumer)
            .Where(b => !b.IsPaid && b.DueDate <= cutoffDate && b.Consumer != null);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            query = query.Where(b =>
                b.Consumer.FirstName.Contains(searchTerm) ||
                b.Consumer.LastName.Contains(searchTerm) ||
                b.Consumer.HomeAddress.Contains(searchTerm));
        }

        var disconnections = await query
            .OrderByDescending(b => b.DueDate)
            .Select(b => new DisconnectionViewModel
            {
                BillingId = b.Id,
                ConsumerName = b.Consumer.FirstName,
                Address = b.Consumer.HomeAddress,
                DueDate = b.DueDate,
                AmountDue = b.TotalAmount,
                Status = b.Status
            })
            .ToPagedListAsync(page, pageSize); // Requires using X.PagedList.EntityFrameworkCore

        ViewBag.SearchTerm = searchTerm;
        return View(disconnections);
    }

    public IActionResult Details(int id)
    {
        // You would typically load billing and consumer details here.
        // For now, just return a placeholder view.
        return View();
    }

    public IActionResult Notify(int id)
    {
        var billing = _context.Billings.Include(b => b.Consumer).FirstOrDefault(b => b.Id == id);
        if (billing != null)
        {
            var notif = new Notification
            {
                ConsumerId = billing.ConsumerId,
                Title = "Disconnection Warning",
                Message = $"Your water service is scheduled for disconnection due to unpaid bill dated {billing.DueDate:yyyy-MM-dd}. Please settle immediately."
            };

            _context.Notifications.Add(notif);
            _context.SaveChanges();
        }

        // You could send an email/SMS here or just mark them as notified.
        TempData["Message"] = $"Notification sent for Billing ID: {id}";
        return RedirectToAction("Index");
    }

    public IActionResult Reconnect(int id)
    {
        var billing = _context.Billings.Include(b => b.Consumer).FirstOrDefault(b => b.Id == id);
        if (billing != null)
        {
            billing.Status = "Reconnected";
            _context.SaveChanges();
            TempData["Message"] = $"Consumer {billing.Consumer.FirstName} marked as reconnected.";
        }

        return RedirectToAction("Index");
    }

}

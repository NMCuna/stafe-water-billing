// AuditTrailController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SantaFeWaterSystem.Data;
using SantaFeWaterSystem.Models;
using System.Linq;
using System.Threading.Tasks;
using X.PagedList;
using X.PagedList.Extensions;

namespace SantaFeWaterSystem.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AuditTrailController : BaseController
    {
       

        public AuditTrailController(ApplicationDbContext context, PermissionService permissionService)
            : base(permissionService, context)
        {
          
        }



        public IActionResult Index(string month, string actionType, int page = 1)
        {
            var query = _context.AuditTrails.AsQueryable();

            if (!string.IsNullOrEmpty(month) && DateTime.TryParse(month + "-01", out var selectedMonth))
            {
                var nextMonth = selectedMonth.AddMonths(1);
                query = query.Where(a => a.Timestamp >= selectedMonth && a.Timestamp < nextMonth);
            }

            if (!string.IsNullOrEmpty(actionType))
            {
                query = query.Where(a => a.Action.Contains(actionType));
            }

            var auditLogs = query
                .OrderByDescending(a => a.Timestamp)
                .ToPagedList(page, 9);

            return View(auditLogs);
        }



        public IActionResult Details(int id)
        {
            var log = _context.AuditTrails.FirstOrDefault(a => a.Id == id);
            if (log == null)
            {
                return NotFound();
            }
            return View(log);
        }


    }
}

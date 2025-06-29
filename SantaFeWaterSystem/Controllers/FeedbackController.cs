using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using SantaFeWaterSystem.Data;
using SantaFeWaterSystem.Models;
using X.PagedList;
using SantaFeWaterSystem.Helpers;


namespace SantaFeWaterSystem.Controllers
{
    public class FeedbackController : BaseController
    {
       

        public FeedbackController(ApplicationDbContext context, PermissionService permissionService)
            : base(permissionService, context)
        {
           
        }





        // ===================== USER =========================

        // GET: Feedback/Create
        [Authorize(Roles = "User")]
        public IActionResult Create()
        {
            return View();
        }

        // POST: Feedback/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "User")]
        public async Task<IActionResult> Create(Feedback feedback)
        {
            if (ModelState.IsValid)
            {
                var accountNumber = User.Identity?.Name;

                var user = await _context.Users.FirstOrDefaultAsync(u => u.AccountNumber == accountNumber);
                if (user == null)
                    return Unauthorized();

                feedback.UserId = user.Id;
                feedback.SubmittedAt = DateTime.UtcNow;
                feedback.Status = "Unread";
                feedback.IsArchived = false;

                _context.Feedbacks.Add(feedback);
                await _context.SaveChangesAsync();
                return RedirectToAction("ThankYou"); // optional
            }

            return View(feedback);
        }


        // Show logged-in user's feedbacks
        [Authorize(Roles = "User")]
        public async Task<IActionResult> UserFeedbacks()
        {
            var accountNumber = User.Identity?.Name;
            var user = await _context.Users.FirstOrDefaultAsync(u => u.AccountNumber == accountNumber);

            if (user == null)
                return Unauthorized();

            var feedbacks = await _context.Feedbacks
                .Where(f => f.UserId == user.Id)
                .OrderByDescending(f => f.SubmittedAt)
                .ToListAsync();

            return View(feedbacks); // looks for Views/Feedback/UserFeedbacks.cshtml
        }

        // Thank You page after feedback submission
        [Authorize(Roles = "User")]
        public IActionResult ThankYou()
        {
            return View(); // looks for Views/Feedback/ThankYou.cshtml
        }











        // =================== ADMIN / STAFF ==================

        // GET: Feedback/Index    
[Authorize(Roles = "Admin,Staff")]
    public async Task<IActionResult> Index(string sortOrder, int? ratingFilter, int page = 1, int pageSize = 8)
    {
        var feedbacks = _context.Feedbacks
            .Include(f => f.User)
            .Where(f => !f.IsArchived) // ✅ Exclude archived feedback
            .AsQueryable();

        if (ratingFilter.HasValue)
            feedbacks = feedbacks.Where(f => f.Rating == ratingFilter.Value);

        feedbacks = sortOrder switch
        {
            "newest" => feedbacks.OrderByDescending(f => f.SubmittedAt),
            "oldest" => feedbacks.OrderBy(f => f.SubmittedAt),
            _ => feedbacks.OrderByDescending(f => f.SubmittedAt)
        };

        // Set ViewBag values for paging/filter state (optional)
        ViewBag.SortOrder = sortOrder;
        ViewBag.RatingFilter = ratingFilter;

        // Return paginated list
        var pagedList = await feedbacks.ToPagedListAsync(page, pageSize);
        return View(pagedList);
    }



    // GET: Feedback/Details/5
    [Authorize(Roles = "Admin,Staff")]
        public async Task<IActionResult> Details(int id)
        {
            var feedback = await _context.Feedbacks
                .Include(f => f.User)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (feedback == null) return NotFound();

            return View(feedback);
        }

        // GET: Feedback/Edit/5
        [Authorize(Roles = "Admin,Staff")]
        public async Task<IActionResult> Edit(int id)
        {
            var feedback = await _context.Feedbacks.FindAsync(id);
            if (feedback == null) return NotFound();
            return View(feedback);
        }

        // POST: Feedback/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Staff")]
        public async Task<IActionResult> Edit(int id, Feedback updated)
        {
            if (id != updated.Id) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    var feedback = await _context.Feedbacks.FindAsync(id);
                    if (feedback == null) return NotFound();

                    feedback.Rating = updated.Rating;
                    feedback.Comment = updated.Comment;
                    await _context.SaveChangesAsync();
                    return RedirectToAction(nameof(Index));
                }
                catch
                {
                    return BadRequest();
                }
            }
            return View(updated);
        }

        // GET: Feedback/Reply/5
        [Authorize(Roles = "Admin,Staff")]
        public async Task<IActionResult> Reply(int id)
        {
            var feedback = await _context.Feedbacks.FindAsync(id);
            if (feedback == null) return NotFound();
            return View(feedback);
        }

        // POST: Feedback/Reply/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Staff")]
        public async Task<IActionResult> Reply(int id, string reply)
        {
            var feedback = await _context.Feedbacks.FindAsync(id);
            if (feedback == null) return NotFound();

            feedback.Reply = reply;
            feedback.RepliedAt = DateTime.UtcNow;
            feedback.Status = "Read";

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // POST: Feedback/Delete/5
        [HttpPost]
        [Authorize(Roles = "Admin,Staff")]
        public async Task<IActionResult> Delete(int id)
        {
            var feedback = await _context.Feedbacks.FindAsync(id);
            if (feedback == null) return NotFound();

            _context.Feedbacks.Remove(feedback);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // POST: Feedback/Archive/5
        [HttpPost]
        [Authorize(Roles = "Admin,Staff")]
        public async Task<IActionResult> Archive(int id)
        {
            var feedback = await _context.Feedbacks.FindAsync(id);
            if (feedback == null) return NotFound();

            feedback.IsArchived = true;
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // POST: Feedback/Unarchive/5
        [HttpPost]
        [Authorize(Roles = "Admin,Staff")]
        public async Task<IActionResult> Unarchive(int id)
        {
            var feedback = await _context.Feedbacks.FindAsync(id);
            if (feedback == null) return NotFound();

            feedback.IsArchived = false;
            await _context.SaveChangesAsync();

            // ✅ Redirect back to the Archive page
            return RedirectToAction("Archive");
        }
 

        // POST: Feedback/MarkRead/5
        [HttpPost]
        [Authorize(Roles = "Admin,Staff")]
        public async Task<IActionResult> MarkRead(int id)
        {
            var feedback = await _context.Feedbacks.FindAsync(id);
            if (feedback == null) return NotFound();

            feedback.Status = "Read";
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // POST: Feedback/MarkUnread/5
        [HttpPost]
        [Authorize(Roles = "Admin,Staff")]
        public async Task<IActionResult> MarkUnread(int id)
        {
            var feedback = await _context.Feedbacks.FindAsync(id);
            if (feedback == null) return NotFound();

            feedback.Status = "Unread";
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // GET: Feedback/Archive
        [Authorize(Roles = "Admin,Staff")]
        public async Task<IActionResult> Archive(int page = 1, int pageSize = 8)
        {
            var archived = _context.Feedbacks
                .Include(f => f.User)
                .Where(f => f.IsArchived)
                .OrderByDescending(f => f.SubmittedAt);

            var pagedList = await archived.ToPagedListAsync(page, pageSize);

            return View(pagedList);
        }



    }
}

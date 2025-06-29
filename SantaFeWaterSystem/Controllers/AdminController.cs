using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using SantaFeWaterSystem.Models;
using SantaFeWaterSystem.Data;
using SantaFeWaterSystem.ViewModels;
using SantaFeWaterSystem.Services;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace SantaFeWaterSystem.Controllers;

[Authorize(Roles = "Admin,Staff")]
public class AdminController(PermissionService permissionService, ApplicationDbContext context)
    : BaseController(permissionService, context)
{
  



        private async Task<List<string>> GetUserPermissionsAsync()
        {
            // Get current logged in user ID (assuming user ID is stored in claims)
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdStr, out int userId))
                return new List<string>();

            // Load user from DB including role and permissions
            var user = await _context.Users
                .Include(u => u.StaffPermissions)
                    .ThenInclude(sp => sp.Permission)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
                return new List<string>();

            // If user is Admin, get all permissions (full access)
            if (user.Role == "Admin")
            {
                return await _context.Permissions.Select(p => p.Name).ToListAsync();
            }

            // If user is Staff, get only assigned permissions
            if (user.Role == "Staff")
            {
                return user.StaffPermissions.Select(sp => sp.Permission.Name).ToList();
            }

            // For others, no permissions
            return new List<string>();


        }


        ///////////////////////////
        /////DASHBOARD CONTROLLER////
        ///////////////////////////


        public async Task<IActionResult> Dashboard()
        {
            ViewBag.TotalConsumers = await _context.Consumers.CountAsync();
            ViewBag.TotalBillings = await _context.Billings.CountAsync();
            ViewBag.TotalPayments = await _context.Payments.SumAsync(p => (decimal?)p.AmountPaid) ?? 0m;
            ViewBag.PendingDisconnections = await _context.Disconnections.CountAsync(d => !d.IsReconnected);
            ViewBag.UnverifiedPayments = await _context.Payments.CountAsync(p => !p.IsVerified);

            var currentYear = DateTime.UtcNow.Year;
            var monthlyPayments = await _context.Payments
                .Where(p => p.PaymentDate.Year == currentYear)
                .GroupBy(p => p.PaymentDate.Month)
                .Select(g => new
                {
                    Month = g.Key,
                    Total = g.Sum(p => p.AmountPaid)
                })
                .ToListAsync();

            var chartData = Enumerable.Range(1, 12).Select(m => new
            {
                Month = new DateTime(currentYear, m, 1).ToString("MMM"),
                Total = monthlyPayments.FirstOrDefault(mp => mp.Month == m)?.Total ?? 0m
            }).ToList();

            ViewBag.MonthlyPaymentChart = chartData;

            ViewBag.UserPermissions = await GetUserPermissionsAsync();

            // ✅ Fix NullReferenceException on user ID
            var userIdClaim = User.FindFirst("UserId");
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
            {
                return RedirectToAction("AdminLogin", "Account");
            }

            var user = await _context.Users.FindAsync(userId);
            ViewBag.IsMfaEnabled = user?.IsMfaEnabled ?? false;

            return View();
        }



    ///////////////////////////
    //     USER MANAGEMENT   //
    ///////////////////////////
    // GET: Admin/UserList
    [Authorize(Roles = "Admin,Staff")]
    public async Task<IActionResult> ManageUsers(string? searchTerm, string? roleFilter, int page = 1, int pageSize = 5)
    {
        var model = new ManageUsersViewModel
        {
            SearchTerm = searchTerm ?? "",
            SelectedRole = roleFilter ?? "",
            CurrentPage = page
        };

        // Queries before search (for full summary counts)
        var allAdmins = _context.Users.Where(u => u.Role == "Admin");
        var allStaffs = _context.Users.Where(u => u.Role == "Staff");
        var allUsers = _context.Users.Where(u => u.Role == "User");

        model.TotalAdmins = await allAdmins.CountAsync();
        model.TotalStaffs = await allStaffs.CountAsync();
        model.TotalUsers = await allUsers.CountAsync();

        // Clone base queries
        var adminQuery = allAdmins;
        var staffQuery = allStaffs;
        var userQuery = allUsers;

        // Filter only the query for the current tab
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            string lowerSearch = searchTerm.ToLower();

            switch (roleFilter)
            {
                case "Admin":
                    adminQuery = adminQuery.Where(u => u.Username.ToLower().Contains(lowerSearch));
                    break;
                case "Staff":
                    staffQuery = staffQuery.Where(u => u.Username.ToLower().Contains(lowerSearch));
                    break;
                case "User":
                    userQuery = userQuery.Where(u =>
                        (!string.IsNullOrEmpty(u.AccountNumber) && u.AccountNumber.ToLower().Contains(lowerSearch)) ||
                        u.Username.ToLower().Contains(lowerSearch));
                    break;
            }
        }

        // Assign filtered counts for table headers
        model.AdminCount = await adminQuery.CountAsync();
        model.StaffCount = await staffQuery.CountAsync();
        model.UserCount = await userQuery.CountAsync();

        // Load paginated data only for selected tab
        switch (roleFilter)
        {
            case "Admin":
                model.Admins = await adminQuery.OrderBy(u => u.Username)
                                               .Skip((page - 1) * pageSize)
                                               .Take(pageSize).ToListAsync();
                model.TotalPages = (int)Math.Ceiling(model.AdminCount / (double)pageSize);
                model.Staffs = new();
                model.Users = new();
                break;
            case "Staff":
                model.Staffs = await staffQuery.OrderBy(u => u.Username)
                                               .Skip((page - 1) * pageSize)
                                               .Take(pageSize).ToListAsync();
                model.TotalPages = (int)Math.Ceiling(model.StaffCount / (double)pageSize);
                model.Admins = new();
                model.Users = new();
                break;
            case "User":
            default:
                model.Users = await userQuery.OrderBy(u => u.Username)
                                             .Skip((page - 1) * pageSize)
                                             .Take(pageSize).ToListAsync();
                model.TotalPages = (int)Math.Ceiling(model.UserCount / (double)pageSize);
                model.Admins = new();
                model.Staffs = new();
                model.SelectedRole = "User"; // default to User tab
                break;
        }

        // Set search term only for the current tab
        ViewBag.AdminSearchTerm = roleFilter == "Admin" ? searchTerm : "";
        ViewBag.StaffSearchTerm = roleFilter == "Staff" ? searchTerm : "";
        ViewBag.UserSearchTerm = roleFilter == "User" ? searchTerm : "";

        return View(model);
    }





    // GET: Admin/EditConsumerUser/5
    [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> EditConsumerUser(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null || user.Role != "User")
                return NotFound();

            return View("EditConsumerUser", user);
        }

        // POST: Admin/EditConsumerUser/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> EditConsumerUser(int id, User updatedUser)
        {
            if (id != updatedUser.Id)
                return BadRequest();

            var user = await _context.Users.FindAsync(id);
            if (user == null || user.Role != "User")
                return NotFound();

            // Preserve the role and validate model
            updatedUser.Role = user.Role;

            if (!ModelState.IsValid)
                return View("EditConsumerUser", updatedUser);

            // Optional: Check for duplicate account number
            bool accountNumberChanged = user.AccountNumber?.Trim() != updatedUser.AccountNumber?.Trim();
            if (accountNumberChanged)
            {
                var duplicate = await _context.Users
                    .Where(u => u.Id != user.Id && u.AccountNumber == updatedUser.AccountNumber)
                    .FirstOrDefaultAsync();

                if (duplicate != null)
                {
                    ModelState.AddModelError("AccountNumber", "This account number is already in use.");
                    return View("EditConsumerUser", updatedUser);
                }
            }

            // Save old values for audit
            string oldAccountNumber = user.AccountNumber ?? "";
            string oldUsername = user.Username ?? "";
            bool oldMfa = user.IsMfaEnabled;

            // Update editable fields
            user.AccountNumber = updatedUser.AccountNumber?.Trim();
            user.Username = updatedUser.Username?.Trim();
            user.IsMfaEnabled = updatedUser.IsMfaEnabled;

            var auditDetails = new List<string> { $"User ID: {user.Id}" };
            if (oldAccountNumber != user.AccountNumber)
                auditDetails.Add($"Account Number: {oldAccountNumber} → {user.AccountNumber}");
            if (oldUsername != user.Username)
                auditDetails.Add($"Username: {oldUsername} → {user.Username}");
            if (oldMfa != user.IsMfaEnabled)
                auditDetails.Add($"MFA Enabled: {oldMfa} → {user.IsMfaEnabled}");

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                _context.Update(user);
                await _context.SaveChangesAsync();

                if (auditDetails.Count > 1)
                {
                    _context.AuditTrails.Add(new AuditTrail
                    {
                        PerformedBy = User?.Identity?.IsAuthenticated == true ? User.Identity.Name : "System",
                        Action = "Edited Consumer User",
                        Timestamp = DateTime.Now,
                        Details = string.Join(", ", auditDetails)
                    });

                    await _context.SaveChangesAsync();
                }

                await transaction.CommitAsync();
                TempData["SuccessMessage"] = "Consumer user updated successfully.";
                return RedirectToAction("ManageUsers");
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync();
                ModelState.AddModelError("", "Database error while updating. Please try again later.");
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                ModelState.AddModelError("", "An unexpected error occurred. Please contact support.");
            }

            return View("EditConsumerUser", updatedUser);
        }





        // GET: Admin/EditAdminUser/5
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> EditAdminUser(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null || (user.Role != "Admin" && user.Role != "Staff"))
                return NotFound();

            return View("EditAdminUser", user);
        }

        // POST: Admin/EditAdminUser/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> EditAdminUser(int id, User updatedUser)
        {
            if (id != updatedUser.Id)
                return BadRequest();

            var user = await _context.Users.FindAsync(id);
            if (user == null || (user.Role != "Admin" && user.Role != "Staff"))
                return NotFound();

            updatedUser.Role = user.Role; // Preserve original role

            if (!ModelState.IsValid)
                return View("EditAdminUser", updatedUser);

            string oldFullName = user.FullName ?? "";
            bool oldMfa = user.IsMfaEnabled;

            user.FullName = updatedUser.FullName?.Trim();
            user.IsMfaEnabled = updatedUser.IsMfaEnabled;

            var auditDetails = new List<string> { $"User ID: {user.Id}" };
            if (oldFullName != user.FullName)
                auditDetails.Add($"Full Name: {oldFullName} → {user.FullName}");
            if (oldMfa != user.IsMfaEnabled)
                auditDetails.Add($"MFA Enabled: {oldMfa} → {user.IsMfaEnabled}");

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                _context.Users.Update(user);
                await _context.SaveChangesAsync();

                if (auditDetails.Count > 1)
                {
                    _context.AuditTrails.Add(new AuditTrail
                    {
                        PerformedBy = User?.Identity?.Name ?? "Unknown",
                        Action = "Edited Admin/Staff User",
                        Timestamp = DateTime.Now,
                        Details = string.Join(", ", auditDetails)
                    });

                    await _context.SaveChangesAsync();
                }

                await transaction.CommitAsync();
                TempData["SuccessMessage"] = "Admin/Staff user updated successfully.";
                return RedirectToAction("ManageUsers");
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                ModelState.AddModelError("", "An error occurred while updating the user.");
                return View("EditAdminUser", updatedUser);
            }
        }

        // GET: Admin/DeleteUser/5
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
                return NotFound();

            return View(user); // Make sure you have a View: Views/Admin/DeleteUser.cshtml
        }

        // POST: Admin/DeleteUser/5
        [HttpPost, ActionName("DeleteUser")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteUserConfirmed(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
                return NotFound();

            // Optional: Add audit trail
            var audit = new AuditTrail
            {
                PerformedBy = User.Identity?.Name ?? "System",
                Action = "Deleted User",
                Timestamp = DateTime.Now,
                Details = $"Deleted user ID: {user.Id}, Username: {user.Username}"
            };

            _context.Users.Remove(user);
            _context.AuditTrails.Add(audit);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "User deleted successfully.";
            return RedirectToAction("ManageUsers");
        }




        ///////////////////////////
        /////CONSUMER CONTROLLER////
        ///////////////////////////


        public async Task<IActionResult> Consumers(string searchTerm, string addressFilter, int page = 1, int pageSize = 6)
        {
            searchTerm = searchTerm?.Trim();
            addressFilter = addressFilter?.Trim();

            var consumersQuery = _context.Consumers
                .Include(c => c.User)
                .AsQueryable();

            if (!string.IsNullOrEmpty(searchTerm))
            {
                var normalizedTerm = searchTerm.ToLower();
                consumersQuery = consumersQuery.Where(c =>
                    c.FirstName.ToLower().Contains(normalizedTerm) ||
                    c.LastName.ToLower().Contains(normalizedTerm) ||
                    c.Email.ToLower().Contains(normalizedTerm));
            }

            if (!string.IsNullOrEmpty(addressFilter))
            {
                consumersQuery = consumersQuery.Where(c => c.HomeAddress.Contains(addressFilter));
            }

            int totalConsumers = await consumersQuery.CountAsync();
            var consumers = await consumersQuery
                .OrderBy(c => c.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewData["searchTerm"] = searchTerm;
            ViewData["addressFilter"] = addressFilter;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = (int)Math.Ceiling((double)totalConsumers / pageSize);

            return View(consumers);
        }



        [HttpGet]
        public IActionResult CreateConsumer()
        {
            var linkedUserIds = _context.Consumers
                .Where(c => c.UserId != null)
                .Select(c => c.UserId)
                .ToList();

            var availableUsers = _context.Users
                .Where(u => u.Role == "User" && !linkedUserIds.Contains(u.Id))
                .ToList();

            ViewBag.AccountTypes = new SelectList(Enum.GetValues(typeof(ConsumerType)));
            ViewBag.Users = new SelectList(availableUsers, "Id", "Username"); // Show Username here
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult CreateConsumer(Consumer consumer)
        {
            if (ModelState.IsValid)
            {
                _context.Consumers.Add(consumer);
                _context.SaveChanges();

                // ✅ Add audit trail entry
                _context.AuditTrails.Add(new AuditTrail
                {
                    PerformedBy = User.Identity?.Name ?? "Unknown",
                    Action = "Created Consumer",
                    Timestamp = DateTime.Now,
                    Details = $"New Consumer Added: ID = {consumer.Id}, Name = {consumer.LastName}, {consumer.FirstName} {consumer.MiddleName}, " +
                              $"Meter No = {consumer.MeterNo}, Email = {consumer.Email}, Type = {consumer.AccountType}, " +
                              $"Contact = {consumer.ContactNumber}, Linked User ID = {consumer.UserId}"
                });

                _context.SaveChanges(); // Save the audit entry

                TempData["SuccessMessage"] = "Consumer added successfully!";
                return RedirectToAction("Consumers");
            }

            // Reload dropdowns if validation fails
            var linkedUserIds = _context.Consumers
                .Where(c => c.UserId != null && c.UserId != consumer.UserId)
                .Select(c => c.UserId)
                .ToList();

            var availableUsers = _context.Users
                .Where(u => u.Role == "User" && !linkedUserIds.Contains(u.Id))
                .ToList();

            ViewBag.AccountTypes = new SelectList(Enum.GetValues(typeof(ConsumerType)), consumer.AccountType);
            ViewBag.Users = new SelectList(availableUsers, "Id", "Username", consumer.UserId);
            return View(consumer);
        }



        public IActionResult ConsumerDetails(int id)
        {
            ViewBag.Users = new SelectList(_context.Users, "Id", "AccountNumber");

            var consumer = _context.Consumers
                .Include(c => c.User)
                .FirstOrDefault(c => c.Id == id);

            if (consumer == null)
                return NotFound();

            // ✅ Audit trail: log view of consumer details
            _context.AuditTrails.Add(new AuditTrail
            {
                PerformedBy = User.Identity?.Name ?? "Unknown",
                Action = "Viewed Consumer Details",
                Timestamp = DateTime.Now,
                Details = $"Viewed details of Consumer ID: {consumer.Id}, Name: {consumer.LastName}, {consumer.FirstName} {consumer.MiddleName}, Meter No: {consumer.MeterNo}"
            });

            _context.SaveChanges(); // Save audit log

            return View(consumer);
        }




        // GET: EditConsumer
        [HttpGet]
        public async Task<IActionResult> EditConsumer(int id)
        {
            var consumer = await _context.Consumers.FindAsync(id);
            if (consumer == null)
                return NotFound();

            ViewBag.Users = new SelectList(_context.Users, "Id", "AccountNumber", consumer.UserId);
            ViewBag.AccountTypes = new SelectList(new[] { "Residential", "Commercial", "Institutional" }, consumer.AccountType);

            return View(consumer);
        }

        // POST: EditConsumer
        [HttpPost]
        public async Task<IActionResult> EditConsumer(Consumer consumer)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Users = new SelectList(_context.Users, "Id", "AccountNumber", consumer.UserId);
                ViewBag.AccountTypes = new SelectList(new[] { "Residential", "Commercial", "Institutional" }, consumer.AccountType);
                return View(consumer);
            }

            var existing = await _context.Consumers
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.Id == consumer.Id);

            if (existing == null)
                return NotFound();

            // Store old values for audit trail
            var oldValues = $"Name: {existing.LastName}, {existing.FirstName} {existing.MiddleName}, " +
                            $"Email: {existing.Email}, Address: {existing.HomeAddress}, " +
                            $"MeterNo: {existing.MeterNo}, Type: {existing.AccountType}, UserId: {existing.UserId}";

            // Update properties
            existing.FirstName = consumer.FirstName;
            existing.LastName = consumer.LastName;
            existing.MiddleName = consumer.MiddleName;
            existing.AccountType = consumer.AccountType;
            existing.Email = consumer.Email;
            existing.HomeAddress = consumer.HomeAddress;
            existing.MeterAddress = consumer.MeterAddress;
            existing.MeterNo = consumer.MeterNo;
            existing.ContactNumber = consumer.ContactNumber;
            existing.UserId = consumer.UserId;

            _context.Consumers.Update(existing);

            // Add audit trail entry
            _context.AuditTrails.Add(new AuditTrail
            {
                PerformedBy = User.Identity?.Name ?? "Unknown",
                Action = "Edited Consumer",
                Timestamp = DateTime.Now,
                Details = $"Updated Consumer ID: {consumer.Id}\n" +
                          $"Old: {oldValues}\n" +
                          $"New: Name: {consumer.LastName}, {consumer.FirstName} {consumer.MiddleName}, " +
                          $"Email: {consumer.Email}, Address: {consumer.HomeAddress}, " +
                          $"MeterNo: {consumer.MeterNo}, Type: {consumer.AccountType}, UserId: {consumer.UserId}"
            });

            await _context.SaveChangesAsync();
            return RedirectToAction("Consumers");
        }



        // GET: Confirm Delete
        public IActionResult DeleteConsumer(int id)
        {
            var consumer = _context.Consumers
                .Include(c => c.User)
                .FirstOrDefault(c => c.Id == id);

            if (consumer == null)
                return NotFound();

            return View(consumer);
        }

        // POST: Delete
        [HttpPost, ActionName("DeleteConsumer")]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteConsumerConfirmed(int id)
        {
            var consumer = _context.Consumers
                .Include(c => c.User)
                .FirstOrDefault(c => c.Id == id);

            if (consumer == null)
                return NotFound();

            // Store deleted info for audit
            var deletedInfo = $"Deleted Consumer ID: {consumer.Id}, " +
                              $"Name: {consumer.LastName}, {consumer.FirstName} {consumer.MiddleName}, " +
                              $"Email: {consumer.Email}, Address: {consumer.HomeAddress}, " +
                              $"Meter No: {consumer.MeterNo}, Account Type: {consumer.AccountType}, " +
                              $"Linked User: {consumer.User?.Username}";

            _context.Consumers.Remove(consumer);

            _context.AuditTrails.Add(new AuditTrail
            {
                PerformedBy = User.Identity?.Name ?? "Unknown",
                Action = "Deleted Consumer",
                Timestamp = DateTime.Now,
                Details = deletedInfo
            });

            _context.SaveChanges();
            return RedirectToAction(nameof(Consumers));
        }


        [HttpGet]
        public async Task<IActionResult> GetConsumerInfo(int consumerId)
        {
            var consumer = await _context.Consumers.FindAsync(consumerId);
            if (consumer == null) return NotFound();

            var rate = await _context.Rates
                .Where(r => r.AccountType == consumer.AccountType && r.EffectiveDate <= DateTime.Today)
                .OrderByDescending(r => r.EffectiveDate)
                .FirstOrDefaultAsync();

            var previousReading = await _context.Billings
                .Where(b => b.ConsumerId == consumerId)
                .OrderByDescending(b => b.BillingDate)
                .Select(b => b.PresentReading)
                .FirstOrDefaultAsync();

        return Json(new
        {
            accountType = consumer.AccountType.ToString(),
            rate = rate?.RatePerCubicMeter.ToString("F2") ?? "0.00",
            penalty = rate?.PenaltyAmount.ToString("F2") ?? "0.00",
            previousReading
        });
    }





    public IActionResult EditPermissions(int staffId)
        {
            var allPermissions = _context.Permissions.ToList();

            var userPermissions = _context.StaffPermissions
                                   .Where(sp => sp.StaffId == staffId)
                                   .Select(sp => sp.PermissionId)
                                   .ToList();

            var model = new EditPermissionsViewModel
            {
                StaffId = staffId,
                Permissions = allPermissions.Select(p => new PermissionCheckbox
                {
                    PermissionId = p.Id,
                    Name = p.Name,
                    IsAssigned = userPermissions.Contains(p.Id)
                }).ToList()
            };

            return View(model);
        }

        [HttpPost]
        public IActionResult EditPermissions(EditPermissionsViewModel model)
        {
            // Remove existing permissions
            var existing = _context.StaffPermissions
                .Where(sp => sp.StaffId == model.StaffId);
            _context.StaffPermissions.RemoveRange(existing);

            // Get the list of permission names assigned for audit logging
            var assignedPermissions = model.Permissions
                .Where(p => p.IsAssigned)
                .Select(p => p.Name)
                .ToList();

            // Add newly assigned permissions
            foreach (var p in model.Permissions.Where(p => p.IsAssigned))
            {
                _context.StaffPermissions.Add(new StaffPermission
                {
                    StaffId = model.StaffId,
                    PermissionId = p.PermissionId
                });
            }

            // Save permission changes
            _context.SaveChanges();

            // Get staff details for audit trail
            var staff = _context.Users.FirstOrDefault(u => u.Id == model.StaffId);
            string staffInfo = staff != null
                ? $"{staff.FullName} ({staff.Username})"
                : $"Staff ID: {model.StaffId}";

            // Create audit log
            _context.AuditTrails.Add(new AuditTrail
            {
                PerformedBy = User.Identity?.Name ?? "Unknown",
                Action = "Edited Staff Permissions",
                Timestamp = DateTime.Now,
                Details = $"Permissions updated for {staffInfo}. Assigned: " +
                          (assignedPermissions.Any() ? string.Join(", ", assignedPermissions) : "None")
            });

            _context.SaveChanges();

            return RedirectToAction("ManageUsers");
        }






        public async Task<IActionResult> Profile()
        {
            if (!User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Login", "Account");
            }

            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
            {
                return RedirectToAction("Login", "Account");
            }

            var user = await _context.Users.FindAsync(userId);
            ViewBag.ProfileImage = user?.ProfileImageUrl ?? "/images/default-avatar.png";

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadProfileImage(IFormFile profileImage)
        {
            if (!User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Login", "Account");
            }

            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
            {
                return RedirectToAction("Login", "Account");
            }

            if (profileImage != null && profileImage.Length > 0)
            {
                // Validate file extension
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
                var extension = Path.GetExtension(profileImage.FileName).ToLowerInvariant();

                if (!allowedExtensions.Contains(extension))
                {
                    TempData["Message"] = "Invalid file type. Please upload JPG, PNG, or GIF.";
                    return RedirectToAction("Profile");
                }

                // Validate file size (2MB limit)
                if (profileImage.Length > 2 * 1024 * 1024)
                {
                    TempData["Message"] = "File too large. Maximum allowed size is 2MB.";
                    return RedirectToAction("Profile");
                }

                var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads");
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                var fileName = $"{Guid.NewGuid()}_{Path.GetFileName(profileImage.FileName)}";
                var filePath = Path.Combine(uploadsFolder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await profileImage.CopyToAsync(stream);
                }

                var user = await _context.Users.FindAsync(userId);
                if (user != null)
                {
                    // Optional: delete old image if not default
                    if (!string.IsNullOrEmpty(user.ProfileImageUrl) && user.ProfileImageUrl != "/images/default-avatar.png")
                    {
                        var oldPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", user.ProfileImageUrl.TrimStart('/'));
                        if (System.IO.File.Exists(oldPath))
                        {
                            System.IO.File.Delete(oldPath);
                        }
                    }

                    user.ProfileImageUrl = $"/uploads/{fileName}";
                    _context.Update(user);
                    await _context.SaveChangesAsync();

                    TempData["Message"] = "Profile image updated successfully!";
                }
            }
            else
            {
                TempData["Message"] = "No file selected.";
            }

            return RedirectToAction("Profile");
        }

    }









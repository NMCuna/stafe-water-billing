using System.ComponentModel.DataAnnotations;

namespace SantaFeWaterSystem.Models
{
   public class User
{
    public int Id { get; set; }

    // Common fields
    public string? PasswordHash { get; set; }
    public string? Role { get; set; } // "Admin", "Staff", "User"

    // For Admin/Staff
    public string? Username { get; set; }
    public string? FullName { get; set; }

    // For Users
    public string? AccountNumber { get; set; }

    // Lockout functionality
    public int AccessFailedCount { get; set; }
    public DateTime? LockoutEnd { get; set; }

    // Password Reset
    public string? PasswordResetToken { get; set; }
    public DateTime? PasswordResetExpiry { get; set; }

    // MFA
    public bool IsMfaEnabled { get; set; }
    public string? MfaSecret { get; set; }

        public string? ProfileImageUrl { get; set; }


        // FK to Consumer
        public int? ConsumerId { get; set; }
        public Consumer? Consumer { get; set; }

        // ✅ NEW: Permissions assigned to staff
        public ICollection<StaffPermission> StaffPermissions { get; set; } = new List<StaffPermission>();
        public ICollection<Feedback> Feedbacks { get; set; }



    }
}

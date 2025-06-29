using System.ComponentModel.DataAnnotations;
using SantaFeWaterSystem.Models;

namespace SantaFeWaterSystem.ViewModels
{
    public class UserProfileViewModel
    {
        public int Id { get; set; }

        [Required]
        public string FirstName { get; set; } = string.Empty;

        public string? MiddleName { get; set; }

        [Required]
        public string LastName { get; set; } = string.Empty;

        // ✅ Computed, not for input binding
        public string? FullName => $"{FirstName} {MiddleName} {LastName}".Replace("  ", " ").Trim();

        [Required]
        public string? Address { get; set; } = string.Empty;

        [Required]
        [Phone]
        public string? ContactNumber { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        public string? AccountNumber { get; set; } = string.Empty;

        public ConsumerType? AccountType { get; set; }

        public string? MeterNo { get; set; }
    }
}

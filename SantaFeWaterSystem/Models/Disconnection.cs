using System.ComponentModel.DataAnnotations;

namespace SantaFeWaterSystem.Models
{
    public class Disconnection
    {
        public int Id { get; set; }

        [Required]
        public int ConsumerId { get; set; }
        public Consumer Consumer { get; set; }

        [Required]
        [Display(Name = "Disconnection Date")]
        public DateTime DisconnectionDate { get; set; }

        [Required]
        [MaxLength(100)]
        public string Reason { get; set; } = string.Empty;

         [Display(Name = "Reconnected?")]
        public bool IsReconnected { get; set; } = false;

        [Display(Name = "Reconnection Date")]
        public DateTime? ReconnectionDate { get; set; }
    }

}

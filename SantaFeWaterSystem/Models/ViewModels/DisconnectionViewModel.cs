using SantaFeWaterSystem.Models;
using System;

namespace SantaFeWaterSystem.ViewModels
{
    public class DisconnectionViewModel
    {
        public int BillingId { get; set; }
        public string ConsumerName { get; set; }
        public string Address { get; set; }
        public DateTime DueDate { get; set; }
        public decimal AmountDue { get; set; }
        public string Status { get; set; }
    }
}

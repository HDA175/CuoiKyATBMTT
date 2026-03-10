using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace CuoiKy.Models
{
    public class RevenueDailyViewModel
    {
        public DateTime Date { get; set; }
        public decimal TotalRevenue { get; set; }
        public int OrderCount { get; set; }
        public int CustomerCount { get; set; }
        public decimal AverageOrderValue { get; set; }

        // Format properties for display
        public string DateDisplay => Date.ToString("dd/MM/yyyy");
        public string TotalRevenueDisplay => TotalRevenue.ToString("N0") + " ₫";
        public string AverageOrderValueDisplay => AverageOrderValue.ToString("N0") + " ₫";
        public string DayOfWeekDisplay => Date.ToString("dddd", new System.Globalization.CultureInfo("vi-VN"));
    }
}
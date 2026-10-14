using Microsoft.AspNetCore.Mvc.Rendering;
using TruckMate.Domain;

namespace TruckMate.Models;

public class ReportFilter
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string? VehicleNo { get; set; }
    public string? Direction { get; set; }
    public string? Supplier { get; set; }
    public string? Material { get; set; }
    public string? Transporter { get; set; }
    public string? Status { get; set; }

    public List<SelectListItem> Suppliers { get; set; } = new();
    public List<SelectListItem> Materials { get; set; } = new();
    public List<SelectListItem> Transporters { get; set; } = new();
    public List<SelectListItem> Directions { get; set; } = new();
    public List<SelectListItem> Statuses { get; set; } = new();
}

public class ReportResultViewModel
{
    public ReportFilter Filter { get; set; } = new();
    public List<WeighingTransaction> Rows { get; set; } = new();
    public int TotalRecords { get; set; }
    public int CountPending { get; set; }
    public int CountCompleted { get; set; }
    public int CountNotCompleted { get; set; }
    public int CountCancelled { get; set; }
    public decimal TotalNetWeight { get; set; }
    public string DocumentNo { get; set; } = string.Empty;
    public DateTime GeneratedOn { get; set; } = DateTime.Now;
}

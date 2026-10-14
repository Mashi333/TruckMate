using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using TruckMate.Domain;

namespace TruckMate.Models;

public class FirstWeightViewModel
{
    [Required(ErrorMessage = "Enter the vehicle number.")]
    [Display(Name = "Vehicle Number")]
    public string VehicleNo { get; set; } = string.Empty;

    [Required(ErrorMessage = "Select a direction.")]
    public Direction Direction { get; set; } = Direction.IN;

    [Required(ErrorMessage = "Select a sub type.")]
    public string SubType { get; set; } = string.Empty;

    [Required(ErrorMessage = "Select a supplier.")]
    public string Supplier { get; set; } = string.Empty;

    [Required(ErrorMessage = "Select a material.")]
    public int? MaterialId { get; set; }

    public string? Transporter { get; set; }
    public string? Driver { get; set; }

    [Required(ErrorMessage = "Enter the job or order number.")]
    [Display(Name = "Job No / Order No")]
    public string OrderNo { get; set; } = string.Empty;

    [Display(Name = "1st Remark")]
    public string? Remark { get; set; }

    public decimal LiveWeight { get; set; }
    public string LiveState { get; set; } = "";

    public List<SelectListItem> Directions { get; set; } = new();
    public List<SelectListItem> SubTypes { get; set; } = new();
    public List<SelectListItem> Suppliers { get; set; } = new();
    public List<SelectListItem> Materials { get; set; } = new();
    public List<SelectListItem> Transporters { get; set; } = new();
    public List<SelectListItem> Drivers { get; set; } = new();
}

public class SecondWeightViewModel
{
    public long TransactionId { get; set; }
    public string TicketNo { get; set; } = string.Empty;
    public string VehicleNo { get; set; } = string.Empty;
    public string DirectionAndSubType { get; set; } = string.Empty;
    public string? Supplier { get; set; }
    public string? Material { get; set; }
    public string? TransporterDriver { get; set; }
    public decimal FirstWeight { get; set; }
    public DateTime FirstWeighedAt { get; set; }
    public string FirstOperator { get; set; } = string.Empty;

    [Display(Name = "2nd Remark")]
    public string? SecondRemark { get; set; }

    public decimal LiveWeight { get; set; }
    public string LiveState { get; set; } = "";
    public bool ManualSave { get; set; }

    [Display(Name = "Reason")]
    public string? ManualReason { get; set; }
}

public class ProceedViewModel
{
    [Required(ErrorMessage = "Enter the vehicle number.")]
    public string VehicleNo { get; set; } = string.Empty;
    public string? PrintMode { get; set; }
}

public class ReprintViewModel
{
    public string? Search { get; set; }
    public long? SelectedId { get; set; }
    public List<TruckMate.Domain.WeighingTransaction> Results { get; set; } = new();
    public string? Reason { get; set; }
}

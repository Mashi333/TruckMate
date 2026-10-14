namespace TruckMate.Services;

public class AppSettings
{
    public int PendingExpiryDays { get; set; } = 7;
    public bool RequireReasonForManualSave { get; set; } = true;
    public bool RequireReasonForReprint { get; set; } = true;
    public string TicketFooterText { get; set; } = "DESIGN BY: RESEARCH & DEVELOPMENT TEAM AT HAYLEYS AVENTURA (PVT) LTD · CONTACT : 011-799 9777";
    public string PdfSaveFolder { get; set; } = "App_Data/Tickets";
    public bool AutoSavePdfWhenNotPrinted { get; set; } = true;
    public string ScalePort { get; set; } = "COM1";
    public string IndicatorPort { get; set; } = "COM2";
    public string Camera { get; set; } = "";
    public string BarrierController { get; set; } = "";
    public string CompanyName { get; set; } = "COMPANY";
}

public static class SettingKeys
{
    public const string PendingExpiryDays = "PendingExpiryDays";
    public const string RequireReasonForManualSave = "RequireReasonForManualSave";
    public const string RequireReasonForReprint = "RequireReasonForReprint";
    public const string TicketFooterText = "TicketFooterText";
    public const string PdfSaveFolder = "PdfSaveFolder";
    public const string AutoSavePdfWhenNotPrinted = "AutoSavePdfWhenNotPrinted";
    public const string ScalePort = "ScalePort";
    public const string IndicatorPort = "IndicatorPort";
    public const string Camera = "Camera";
    public const string BarrierController = "BarrierController";
    public const string CompanyName = "CompanyName";
}

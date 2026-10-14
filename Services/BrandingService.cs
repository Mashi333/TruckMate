namespace TruckMate.Services;

public class BrandingService
{
    private const string BaseName = "company-logo";
    private const string FolderName = "uploads";
    private const long MaxBytes = 5 * 1024 * 1024;

    private static readonly string[] Allowed =
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp"
    };

    private readonly IWebHostEnvironment _env;

    public BrandingService(IWebHostEnvironment env)
    {
        _env = env;
    }

    private string Folder => Path.Combine(_env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot"), FolderName);

    public string? GetLogoPhysicalPath() => FindLogoFile();

    public string? GetLogoRelativeUrl()
    {
        var file = FindLogoFile();
        if (file is null) return null;
        var version = File.GetLastWriteTimeUtc(file).Ticks;
        return "/" + FolderName + "/" + Path.GetFileName(file) + "?v=" + version;
    }

    public byte[]? GetLogoBytes()
    {
        var file = FindLogoFile();
        return file is null ? null : File.ReadAllBytes(file);
    }

    public string Save(IFormFile file)
    {
        if (file is null || file.Length == 0)
            throw new InvalidOperationException("Please choose a logo file.");

        if (file.Length > MaxBytes)
            throw new InvalidOperationException("The logo must be 5 MB or smaller.");

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!Allowed.Contains(ext))
            throw new InvalidOperationException("Unsupported image type. Use PNG, JPG, GIF or WEBP.");

        Directory.CreateDirectory(Folder);
        RemoveExisting();

        var target = Path.Combine(Folder, BaseName + ext);
        using (var stream = System.IO.File.Create(target))
        {
            file.CopyTo(stream);
        }
        return "/" + FolderName + "/" + Path.GetFileName(target);
    }

    public void Remove() => RemoveExisting();

    private void RemoveExisting()
    {
        foreach (var ext in Allowed)
        {
            var path = Path.Combine(Folder, BaseName + ext);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private string? FindLogoFile()
    {
        if (!Directory.Exists(Folder)) return null;
        foreach (var ext in Allowed)
        {
            var path = Path.Combine(Folder, BaseName + ext);
            if (File.Exists(path)) return path;
        }
        return null;
    }
}

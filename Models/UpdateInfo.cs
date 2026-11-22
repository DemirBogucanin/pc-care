namespace PCCare.Models;

public sealed record UpdateInfo(
    Version Version,
    string Tag,
    string DownloadUrl,
    string? Sha256,
    string Notes);

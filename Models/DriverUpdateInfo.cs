namespace PCCare.Models;

public sealed class DriverUpdateInfo
{
    public required string Title { get; init; }
    public required string Provider { get; init; }
    public required string DriverClass { get; init; }
    public required string DriverModel { get; init; }
    public required string DateText { get; init; }
    public required string SizeText { get; init; }
    public long SizeBytes { get; init; }
    public required string DetailsText { get; init; }
    internal object? Payload { get; init; }
}

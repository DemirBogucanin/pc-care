namespace PCCare.Models;

public sealed class DriverDeviceInfo
{
    public required string Name { get; init; }
    public required string ClassName { get; init; }
    public required string Manufacturer { get; init; }
    public required string DeviceId { get; init; }
    public required bool HasProblem { get; init; }
    public required string ProblemText { get; init; }
    public required string DriverVersion { get; init; }
    public required string DriverDateText { get; init; }
    public required string Provider { get; init; }

    public string AccentHex => HasProblem ? "#F4B860" : "#3DDC97";
    public string StatusText => HasProblem ? "NEEDS ATTENTION" : "OK";
    public string Initial
    {
        get
        {
            var trimmed = Name.Trim();
            return trimmed.Length > 0 ? trimmed[..1].ToUpperInvariant() : "?";
        }
    }

    public string DetailsText =>
        $"{ClassName}  |  {Provider}  |  Driver {DriverVersion}  |  {DriverDateText}";
}

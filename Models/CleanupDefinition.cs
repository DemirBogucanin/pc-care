namespace PCCare.Models;

public sealed class CleanupDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Glyph { get; init; }
    public required string AccentHex { get; init; }
    public required IReadOnlyList<string> Paths { get; init; }
    public bool Recursive { get; init; } = true;
    public IReadOnlyList<string> Patterns { get; init; } = ["*"];
    public TimeSpan MinimumAge { get; init; } = TimeSpan.Zero;
    public bool SelectedByDefault { get; init; } = true;
    public bool RequiresAdministrator { get; init; }
}

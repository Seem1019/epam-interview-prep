namespace EpamPrep.Api.Domain;

/// <summary>EPAM competency scale. <see cref="None"/> = not evaluated / no evidence.</summary>
public enum Level
{
    None = 0,
    Novice = 1,
    Intermediate = 2,
    Advanced = 3,
    Expert = 4,
}

public static class LevelExtensions
{
    public static Level ParseLevel(string? value) =>
        Enum.TryParse<Level>(value?.Trim(), ignoreCase: true, out var level) ? level : Level.None;

    public static Level Max(Level a, Level b) => (Level)Math.Max((int)a, (int)b);
}

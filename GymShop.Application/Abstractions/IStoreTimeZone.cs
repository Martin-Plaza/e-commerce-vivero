namespace GymShop.Application.Abstractions;

public interface IStoreTimeZone
{
    TimeZoneInfo TimeZone { get; }
    string Id { get; }
    bool IsFallback { get; }
}

public sealed class StoreTimeZone : IStoreTimeZone
{
    // Missing configuration uses the store default. An unresolvable identifier falls
    // back to UTC so the application remains available and can report a safe zone.
    public const string DefaultId = "America/Argentina/Buenos_Aires";
    private const string WindowsArgentinaId = "Argentina Standard Time";

    public StoreTimeZone(string? configuredId)
    {
        var requested = string.IsNullOrWhiteSpace(configuredId) ? DefaultId : configuredId.Trim();
        var candidates = requested switch
        {
            DefaultId => new[] { DefaultId, WindowsArgentinaId },
            WindowsArgentinaId => new[] { WindowsArgentinaId, DefaultId },
            _ => new[] { requested }
        };

        foreach (var candidate in candidates)
        {
            try
            {
                TimeZone = TimeZoneInfo.FindSystemTimeZoneById(candidate);
                Id = requested;
                return;
            }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        TimeZone = TimeZoneInfo.Utc;
        Id = TimeZoneInfo.Utc.Id;
        IsFallback = true;
    }

    public TimeZoneInfo TimeZone { get; }
    public string Id { get; }
    public bool IsFallback { get; }
}

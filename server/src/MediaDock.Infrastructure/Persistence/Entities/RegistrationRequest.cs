namespace MediaDock.Infrastructure.Persistence.Entities;

public sealed class RegistrationRequest
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTimeOffset RequestedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecidedBy { get; set; }
}
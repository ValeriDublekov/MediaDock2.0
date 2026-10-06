namespace MediaDock.Infrastructure.Persistence.Entities;

public sealed class ExternalIdentity
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public User User { get; set; } = null!;
    public string Issuer { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
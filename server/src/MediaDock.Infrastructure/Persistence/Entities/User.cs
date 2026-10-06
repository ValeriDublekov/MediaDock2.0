namespace MediaDock.Infrastructure.Persistence.Entities;

public sealed class User
{
    public long Id { get; set; }
    public string NormalizedEmail { get; set; } = string.Empty;
    public string GivenName { get; set; } = string.Empty;
    public string FamilyName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Role { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ICollection<ExternalIdentity> ExternalIdentities { get; } = new List<ExternalIdentity>();
    public ICollection<RegistrationRequest> RegistrationRequests { get; } = new List<RegistrationRequest>();
}
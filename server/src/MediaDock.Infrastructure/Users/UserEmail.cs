namespace MediaDock.Infrastructure.Users;

public static class UserEmail
{
    public static string Normalize(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        var normalized = email.Trim().ToLowerInvariant();
        if (normalized.Length > 320)
        {
            throw new ArgumentOutOfRangeException(nameof(email), "Email exceeds the maximum supported length.");
        }

        return normalized;
    }
}
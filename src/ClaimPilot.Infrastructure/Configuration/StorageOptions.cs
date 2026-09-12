namespace ClaimPilot.Infrastructure.Configuration;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";
    public string UploadRoot { get; set; } = "uploads";
}

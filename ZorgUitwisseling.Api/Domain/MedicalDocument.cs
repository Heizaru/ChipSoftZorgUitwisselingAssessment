namespace ZorgUitwisseling.Api.Domain;

public enum DocumentType
{
    ReferralLetter = 1,
    AllergyInformation = 2,
    MedicationOverview = 3,
    Other = 4
}

public class MedicalDocument
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ReferralId { get; init; }
    public Referral Referral { get; init; } = null!;
    public Guid? InformationRequestId { get; init; }
    public InformationRequest? InformationRequest { get; init; }
    public DocumentType Type { get; init; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public string ContentType { get; set; } = "text/plain";
    public byte[]? FileBytes { get; set; }
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}

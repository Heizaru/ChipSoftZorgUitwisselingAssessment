namespace ZorgUitwisseling.Api.Domain;

public enum InformationRequestStatus
{
    Pending,
    Fulfilled
}

public class InformationRequest
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ReferralId { get; init; }
    public Referral Referral { get; init; } = null!;
    public DocumentType RequestedDocumentType { get; init; }
    public string? Notes { get; init; }
    public InformationRequestStatus Status { get; set; } = InformationRequestStatus.Pending;
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime? FulfilledAtUtc { get; set; }
    public MedicalDocument? ResponseDocument { get; set; }
}

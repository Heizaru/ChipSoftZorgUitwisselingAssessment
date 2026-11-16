namespace ZorgUitwisseling.Api.Domain;

public enum ReferralStatus
{
    Pending = 0,
    Accepted = 1,
    Rejected = 2
}

public class Referral
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string PatientBsn { get; init; }
    public required string PatientName { get; init; }
    public DateOnly PatientDateOfBirth { get; init; }
    public required string SenderOrganizationId { get; init; }
    public required  string ReceiverOrganizationId { get; init; }
    public string Reason { get; init; } = string.Empty;
    public ReferralStatus Status { get; set; } = ReferralStatus.Pending;
    public DateTime? DecidedAtUtc { get; set; }
    public string? DecisionNotes { get; set; }
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public List<MedicalDocument> Documents { get; init; } = [];
    public List<InformationRequest> InformationRequests { get; init; } = [];
}

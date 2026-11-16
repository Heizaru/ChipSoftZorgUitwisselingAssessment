using JetBrains.Annotations;

namespace ZorgUitwisseling.Api.Domain;

public enum ExchangeAction
{
    ReferralCreated,
    InformationRequested,
    InformationProvided,
    DocumentsRetrieved,
    DocumentAdded,
    ReferralAccepted,
    ReferralRejected
}

public class ExchangeLog
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public ExchangeAction Action { get; init; }
    public Guid ReferralId { get; init; }
    public Guid? InformationRequestId { get; init; }
    public Guid? DocumentId { get; init; }
    public DocumentType? DocumentType { get; init; }
    public string SenderOrganizationId { get; init; } = string.Empty;
    public string ReceiverOrganizationId { get; init; } = string.Empty;
}

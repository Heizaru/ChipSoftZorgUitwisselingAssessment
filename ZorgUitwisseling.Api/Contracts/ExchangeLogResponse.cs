using JetBrains.Annotations;
using ZorgUitwisseling.Api.Domain;

namespace ZorgUitwisseling.Api.Contracts;

[UsedImplicitly]
public record ExchangeLogResponse(
    Guid Id,
    DateTime TimestampUtc,
    ExchangeAction Action,
    Guid ReferralId,
    Guid? InformationRequestId,
    Guid? DocumentId,
    DocumentType? DocumentType,
    string SenderOrganizationId,
    string ReceiverOrganizationId);

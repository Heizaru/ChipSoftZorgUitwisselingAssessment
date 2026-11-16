using System.ComponentModel.DataAnnotations;
using ZorgUitwisseling.Api.Domain;

namespace ZorgUitwisseling.Api.Contracts;

public class CreateInformationRequest
{
    [EnumDataType(typeof(DocumentType))]
    public DocumentType RequestedDocumentType { get; init; }

    [MaxLength(2000)]
    public string? Notes { get; init; }
}

public class RespondToInformationRequest
{
    [Required, MaxLength(200)]
    public string Title { get; init; } = string.Empty;

    [Required, MaxLength(64000)]
    public string Content { get; init; } = string.Empty;
}

public record InformationRequestResponse(
    Guid Id,
    Guid ReferralId,
    string RequestingOrganizationId,
    string TargetOrganizationId,
    DocumentType RequestedDocumentType,
    string? Notes,
    InformationRequestStatus Status,
    DateTime CreatedAtUtc,
    DateTime? FulfilledAtUtc,
    Guid? FulfilledDocumentId);

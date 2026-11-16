using System.ComponentModel.DataAnnotations;
using ZorgUitwisseling.Api.Domain;

namespace ZorgUitwisseling.Api.Contracts;

public class AddReferralDocumentRequest
{
    [EnumDataType(typeof(DocumentType))]
    public DocumentType Type { get; init; } = DocumentType.Other;

    [Required, MaxLength(200)]
    public string Title { get; init; } = string.Empty;

    [Required, MaxLength(64000)]
    public string Content { get; init; } = string.Empty;
}

public class ReferralDecisionRequest
{
    [MaxLength(2000)]
    public string? Notes { get; init; }
}

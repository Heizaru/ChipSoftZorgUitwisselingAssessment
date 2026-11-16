using System.ComponentModel.DataAnnotations;
using JetBrains.Annotations;
using ZorgUitwisseling.Api.Common.Validation;
using ZorgUitwisseling.Api.Domain;

namespace ZorgUitwisseling.Api.Contracts;

public class CreateReferralRequest : IValidatableObject
{
    [Required, Bsn]
    public string PatientBsn { get; init; } = string.Empty;

    [Required, MaxLength(200)]
    public string PatientName { get; init; } = string.Empty;

    public DateOnly PatientDateOfBirth { get; init; }

    [Required, MaxLength(100)]
    public string SenderOrganizationId { get; init; } = string.Empty;

    [Required, MaxLength(100)]
    public string ReceiverOrganizationId { get; init; } = string.Empty;

    [Required, MaxLength(2000)]
    public string Reason { get; init; } = string.Empty;

    [Required]
    public ReferralLetterPdfRequest ReferralLetter { get; init; } = null!;

    [Required]
    public AllergyInformationRequest AllergyInformation { get; init; } = null!;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (PatientDateOfBirth == default)
        {
            yield return new ValidationResult(
                "Patient date of birth is required.",
                [nameof(PatientDateOfBirth)]);
        }
        else if (PatientDateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            yield return new ValidationResult(
                "Patient date of birth cannot be in the future.",
                [nameof(PatientDateOfBirth)]);
        }

        if (!string.IsNullOrWhiteSpace(SenderOrganizationId)
            && SenderOrganizationId == ReceiverOrganizationId)
        {
            yield return new ValidationResult(
                "Sender and receiver organizations must be different.",
                [nameof(SenderOrganizationId), nameof(ReceiverOrganizationId)]);
        }
    }
}

public class ReferralLetterPdfRequest
{
    [MaxLength(200)]
    public string? Title { get; init; }

    [Required, MaxLength(255)]
    public string FileName { get; init; } = string.Empty;

    [Required]
    public string FileBase64 { get; init; } = string.Empty;
}

public enum AllergySeverity
{
    Mild = 1,
    Moderate = 2,
    Severe = 3,
    Unknown = 4
}

public class AllergyEntryRequest
{
    [Required, MaxLength(200)]
    public string Substance { get; init; } = string.Empty;

    [Required, MaxLength(500)]
    public string Reaction { get; init; } = string.Empty;

    [EnumDataType(typeof(AllergySeverity))]
    public AllergySeverity Severity { get; init; } = AllergySeverity.Unknown;

    [MaxLength(1000)]
    public string? Notes { get; init; }
}

public class AllergyInformationRequest : IValidatableObject
{
    [MaxLength(200)]
    public string? Title { get; init; }

    public bool HasKnownAllergies { get; init; }

    public List<AllergyEntryRequest> Allergies { get; init; } = [];

    [MaxLength(2000)]
    public string? Summary { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (HasKnownAllergies && Allergies.Count == 0)
        {
            yield return new ValidationResult(
                "At least one allergy entry is required when HasKnownAllergies is true.",
                [nameof(Allergies)]);
        }

        if (!HasKnownAllergies && Allergies.Count > 0)
        {
            yield return new ValidationResult(
                "Allergies must be empty when HasKnownAllergies is false.",
                [nameof(Allergies)]);
        }
    }
}

public record ReferralResponse(
    Guid Id,
    string PatientBsn,
    string PatientName,
    DateOnly PatientDateOfBirth,
    string SenderOrganizationId,
    string ReceiverOrganizationId,
    string Reason,
    ReferralStatus Status,
    DateTime? DecidedAtUtc,
    string? DecisionNotes,
    DateTime CreatedAtUtc,
    IReadOnlyList<MedicalDocumentResponse> Documents);

[UsedImplicitly]
public record ReferralSummaryResponse(
    Guid Id,
    string PatientBsn,
    string PatientName,
    DateOnly PatientDateOfBirth,
    string SenderOrganizationId,
    string ReceiverOrganizationId,
    string Reason,
    ReferralStatus Status,
    DateTime CreatedAtUtc,
    int DocumentCount);

[UsedImplicitly]
public record MedicalDocumentResponse(
    Guid Id,
    Guid ReferralId,
    Guid? InformationRequestId,
    DocumentType Type,
    string Title,
    string Content,
    string? FileName,
    string ContentType,
    DateTime CreatedAtUtc);

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using ZorgUitwisseling.Api.Common.Errors;
using ZorgUitwisseling.Api.Contracts;
using ZorgUitwisseling.Api.Data;
using ZorgUitwisseling.Api.Domain;

namespace ZorgUitwisseling.Api.Services;

public class ExchangeService(AppDbContext db, ILogger<ExchangeService> logger)
{
    private const int MaxFileBytes = 5 * 1024 * 1024;
    private const int MaxBase64Length = (MaxFileBytes + 2) / 3 * 4 + 1024;

    private static readonly JsonSerializerOptions AllergyJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public async Task<IReadOnlyList<ReferralSummaryResponse>> GetReceivedReferralsAsync(
        string organizationId, CancellationToken ct)
    {
        await EnsureOrganizationExistsAsync(organizationId, ct);

        return await db.Referrals.AsNoTracking()
            .Where(r => r.ReceiverOrganizationId == organizationId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Select(r => new ReferralSummaryResponse(
                r.Id,
                r.PatientBsn,
                r.PatientName,
                r.PatientDateOfBirth,
                r.SenderOrganizationId,
                r.ReceiverOrganizationId,
                r.Reason,
                r.Status,
                r.CreatedAtUtc,
                r.Documents.Count))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<InformationRequestResponse>> GetIncomingInformationRequestsAsync(
        string organizationId, InformationRequestStatus? status, CancellationToken ct)
    {
        await EnsureOrganizationExistsAsync(organizationId, ct);

        IQueryable<InformationRequest> query = db.InformationRequests.AsNoTracking()
            .Include(r => r.Referral)
            .Include(r => r.ResponseDocument)
            .Where(r => r.Referral.SenderOrganizationId == organizationId);

        if (status.HasValue)
        {
            query = query.Where(r => r.Status == status.Value);
        }

        List<InformationRequest> requests = await query.OrderBy(r => r.CreatedAtUtc).ToListAsync(ct);
        return requests.Select(ToResponse).ToList();
    }

    public async Task<ReferralResponse> CreateReferralAsync(
        CreateReferralRequest input, CancellationToken ct)
    {
        int orgCount = await db.Organizations.CountAsync(
            o => o.Id == input.SenderOrganizationId || o.Id == input.ReceiverOrganizationId, ct);
        if (orgCount != 2)
        {
            throw new ResourceNotFoundException("Sender or receiver organization does not exist.");
        }

        byte[] referralPdfBytes = DecodeAndValidateBase64Pdf(input.ReferralLetter.FileBase64);
        MedicalDocument referralLetterDoc = BuildPdfDocument(
            referralId: null,
            informationRequestId: null,
            DocumentType.ReferralLetter,
            input.ReferralLetter.FileName,
            input.ReferralLetter.Title,
            referralPdfBytes);

        string allergyTitle = string.IsNullOrWhiteSpace(input.AllergyInformation.Title)
            ? "Allergie-informatie"
            : input.AllergyInformation.Title.Trim();
        string allergyJson = JsonSerializer.Serialize(new
        {
            input.AllergyInformation.HasKnownAllergies,
            input.AllergyInformation.Allergies,
            input.AllergyInformation.Summary
        }, AllergyJsonOptions);

        var referral = new Referral
        {
            PatientBsn = input.PatientBsn,
            PatientName = input.PatientName,
            PatientDateOfBirth = input.PatientDateOfBirth,
            SenderOrganizationId = input.SenderOrganizationId,
            ReceiverOrganizationId = input.ReceiverOrganizationId,
            Reason = input.Reason
        };

        referral.Documents.Add(new MedicalDocument
        {
            ReferralId = referral.Id,
            Type = DocumentType.ReferralLetter,
            Title = referralLetterDoc.Title,
            Content = referralLetterDoc.Content,
            FileName = referralLetterDoc.FileName,
            ContentType = referralLetterDoc.ContentType,
            FileBytes = referralLetterDoc.FileBytes
        });

        referral.Documents.Add(new MedicalDocument
        {
            ReferralId = referral.Id,
            Type = DocumentType.AllergyInformation,
            Title = allergyTitle,
            Content = allergyJson,
            FileName = "allergy-information.json",
            ContentType = "application/json"
        });

        db.Referrals.Add(referral);
        foreach (MedicalDocument doc in referral.Documents)
        {
            LogExchange(ExchangeAction.ReferralCreated, referral.Id, null, doc.Id, doc.Type,
                referral.SenderOrganizationId, referral.ReceiverOrganizationId);
        }

        await SaveChangesWithAuditLoggingAsync(ct);
        return ToResponse(referral);
    }

    public async Task<ReferralResponse> GetReferralAsync(Guid id, CancellationToken ct)
    {
        Referral referral = await db.Referrals
                                .AsNoTracking()
                                .Include(r => r.Documents)
                                .SingleOrDefaultAsync(r => r.Id == id, ct)
                            ?? throw new ResourceNotFoundException("Referral does not exist.");

        foreach (MedicalDocument doc in referral.Documents)
        {
            LogExchange(ExchangeAction.DocumentsRetrieved, referral.Id, doc.InformationRequestId, doc.Id, doc.Type,
                referral.SenderOrganizationId, referral.ReceiverOrganizationId);
        }

        await SaveChangesWithAuditLoggingAsync(ct);
        return ToResponse(referral);
    }

    public async Task<MedicalDocumentResponse> AddDocumentAsync(
        Guid referralId, AddReferralDocumentRequest input, CancellationToken ct)
    {
        Referral referral = await db.Referrals.SingleOrDefaultAsync(r => r.Id == referralId, ct)
                            ?? throw new ResourceNotFoundException("Referral does not exist.");

        EnsureReferralIsPending(referral);

        var document = new MedicalDocument
        {
            ReferralId = referral.Id,
            Type = input.Type,
            Title = input.Title.Trim(),
            Content = input.Content
        };

        db.Documents.Add(document);
        LogExchange(ExchangeAction.DocumentAdded, referral.Id, null, document.Id, document.Type,
            referral.SenderOrganizationId, referral.ReceiverOrganizationId);

        await SaveChangesWithAuditLoggingAsync(ct);
        return ToDocumentResponse(document);
    }

    public async Task<MedicalDocumentResponse> AddDocumentFileAsync(
        Guid referralId, string? documentType, string? title, IFormFile file, CancellationToken ct)
    {
        Referral referral = await db.Referrals.SingleOrDefaultAsync(r => r.Id == referralId, ct)
                            ?? throw new ResourceNotFoundException("Referral does not exist.");

        EnsureReferralIsPending(referral);

        DocumentType resolvedType = ParseDocumentType(documentType);
        byte[] bytes = await ReadPdfFileAsync(file, ct);
        MedicalDocument document = BuildPdfDocument(
            referral.Id, null, resolvedType, file.FileName, title, bytes);

        db.Documents.Add(document);
        LogExchange(ExchangeAction.DocumentAdded, referral.Id, null, document.Id, document.Type,
            referral.SenderOrganizationId, referral.ReceiverOrganizationId);

        await SaveChangesWithAuditLoggingAsync(ct);
        return ToDocumentResponse(document);
    }

    public async Task<ReferralResponse> DecideReferralAsync(
        Guid referralId, ReferralStatus decision, ReferralDecisionRequest? input, CancellationToken ct)
    {
        if (decision is not (ReferralStatus.Accepted or ReferralStatus.Rejected))
        {
            throw new ExchangeValidationException("Decision must be Accepted or Rejected.");
        }

        Referral referral = await db.Referrals
                                .Include(r => r.Documents)
                                .SingleOrDefaultAsync(r => r.Id == referralId, ct)
                            ?? throw new ResourceNotFoundException("Referral does not exist.");

        EnsureReferralIsPending(referral);

        referral.Status = decision;
        referral.DecidedAtUtc = DateTime.UtcNow;
        referral.DecisionNotes = string.IsNullOrWhiteSpace(input?.Notes) ? null : input.Notes.Trim();

        ExchangeAction action = decision == ReferralStatus.Accepted
            ? ExchangeAction.ReferralAccepted
            : ExchangeAction.ReferralRejected;

        LogExchange(action, referral.Id, null, null, null,
            referral.ReceiverOrganizationId, referral.SenderOrganizationId);

        await SaveChangesWithAuditLoggingAsync(ct);
        return ToResponse(referral);
    }

    public async Task<(byte[] Bytes, string ContentType, string FileName)> DownloadDocumentFileAsync(
        Guid documentId, CancellationToken ct)
    {
        MedicalDocument doc = await db.Documents.AsNoTracking()
                                  .Include(d => d.Referral)
                                  .SingleOrDefaultAsync(d => d.Id == documentId, ct)
                              ?? throw new ResourceNotFoundException("Document does not exist.");

        LogExchange(ExchangeAction.DocumentsRetrieved, doc.ReferralId, doc.InformationRequestId, doc.Id, doc.Type,
            doc.Referral.SenderOrganizationId, doc.Referral.ReceiverOrganizationId);
        await SaveChangesWithAuditLoggingAsync(ct);

        byte[] bytes = doc.FileBytes ?? Encoding.UTF8.GetBytes(doc.Content);
        string fileName = !string.IsNullOrWhiteSpace(doc.FileName)
            ? doc.FileName
            : $"{doc.Type}-{doc.Id:N}.txt";

        return (bytes, doc.ContentType, fileName);
    }

    public async Task<InformationRequestResponse> CreateInformationRequestAsync(
        Guid referralId, CreateInformationRequest input, CancellationToken ct)
    {
        Referral referral = await db.Referrals.SingleOrDefaultAsync(r => r.Id == referralId, ct)
                            ?? throw new ResourceNotFoundException("Referral does not exist.");

        EnsureReferralIsPending(referral);

        var request = new InformationRequest
        {
            ReferralId = referral.Id,
            Referral = referral,
            RequestedDocumentType = input.RequestedDocumentType,
            Notes = input.Notes
        };

        db.InformationRequests.Add(request);
        LogExchange(ExchangeAction.InformationRequested, referral.Id, request.Id, null,
            request.RequestedDocumentType, referral.ReceiverOrganizationId, referral.SenderOrganizationId);

        await SaveChangesWithAuditLoggingAsync(ct);
        return ToResponse(request);
    }

    public async Task<InformationRequestResponse> GetInformationRequestAsync(Guid id, CancellationToken ct)
    {
        InformationRequest request = await db.InformationRequests.AsNoTracking()
                                         .Include(r => r.Referral)
                                         .Include(r => r.ResponseDocument)
                                         .SingleOrDefaultAsync(r => r.Id == id, ct)
                                     ?? throw new ResourceNotFoundException("Information request does not exist.");

        return ToResponse(request);
    }

    public async Task<InformationRequestResponse> RespondAsync(
        Guid id, RespondToInformationRequest input, CancellationToken ct)
    {
        InformationRequest request = await LoadPendingRequestAsync(id, ct);

        var document = new MedicalDocument
        {
            ReferralId = request.ReferralId,
            InformationRequestId = request.Id,
            Type = request.RequestedDocumentType,
            Title = input.Title,
            Content = input.Content
        };

        return await FulfillRequestAsync(request, document, ct);
    }

    public async Task<InformationRequestResponse> RespondWithFileAsync(
        Guid id, string? title, IFormFile file, CancellationToken ct)
    {
        InformationRequest request = await LoadPendingRequestAsync(id, ct);
        byte[] bytes = await ReadPdfFileAsync(file, ct);

        MedicalDocument document = BuildPdfDocument(
            request.ReferralId, request.Id, request.RequestedDocumentType, file.FileName, title, bytes);

        return await FulfillRequestAsync(request, document, ct);
    }

    public async Task<IReadOnlyList<ExchangeLogResponse>> GetExchangeLogsAsync(
        Guid referralId, CancellationToken ct)
    {
        if (!await db.Referrals.AnyAsync(r => r.Id == referralId, ct))
        {
            throw new ResourceNotFoundException("Referral does not exist.");
        }

        return await db.ExchangeLogs.AsNoTracking()
            .Where(l => l.ReferralId == referralId)
            .OrderBy(l => l.TimestampUtc)
            .Select(l => new ExchangeLogResponse(
                l.Id, l.TimestampUtc, l.Action, l.ReferralId,
                l.InformationRequestId, l.DocumentId, l.DocumentType,
                l.SenderOrganizationId, l.ReceiverOrganizationId))
            .ToListAsync(ct);
    }

    private static DocumentType ParseDocumentType(string? documentType)
    {
        if (string.IsNullOrWhiteSpace(documentType))
        {
            return DocumentType.Other;
        }

        string trimmed = documentType.Trim();
        if (int.TryParse(trimmed, out _)
            || !Enum.TryParse(trimmed, ignoreCase: true, out DocumentType parsed)
            || !Enum.IsDefined(parsed))
        {
            throw new ExchangeValidationException("Invalid document type.");
        }

        return parsed;
    }

    private static byte[] DecodeAndValidateBase64Pdf(string fileBase64)
    {
        if (string.IsNullOrWhiteSpace(fileBase64) || fileBase64.Length > MaxBase64Length)
        {
            throw new ExchangeValidationException("PDF file must be between 1 byte and 5 MB.");
        }

        try
        {
            return Convert.FromBase64String(fileBase64.Trim());
        }
        catch (FormatException)
        {
            throw new ExchangeValidationException("Referral letter must contain valid Base64-encoded PDF data.");
        }
    }

    private async static Task<byte[]> ReadPdfFileAsync(IFormFile file, CancellationToken ct)
    {
        if (file.Length is 0 or > MaxFileBytes)
        {
            throw new ExchangeValidationException("PDF file must be between 1 byte and 5 MB.");
        }

        using var stream = new MemoryStream((int)file.Length);
        await file.CopyToAsync(stream, ct);
        return stream.ToArray();
    }

    private static MedicalDocument BuildPdfDocument(
        Guid? referralId,
        Guid? informationRequestId,
        DocumentType type,
        string rawFileName,
        string? title,
        byte[] bytes)
    {
        if (bytes.Length is 0 or > MaxFileBytes)
        {
            throw new ExchangeValidationException("PDF file must be between 1 byte and 5 MB.");
        }

        string fileName = Path.GetFileName(rawFileName);
        if (string.IsNullOrWhiteSpace(fileName)
            || !fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw new ExchangeValidationException("Only .pdf files are supported.");
        }

        if (bytes.Length < 5 || !bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
        {
            throw new ExchangeValidationException("Uploaded file is not a valid PDF document.");
        }

        string resolvedTitle = string.IsNullOrWhiteSpace(title)
            ? Path.GetFileNameWithoutExtension(fileName)
            : title.Trim();
        if (resolvedTitle.Length is 0 or > 200)
        {
            throw new ExchangeValidationException("Document title must be between 1 and 200 characters.");
        }

        return new MedicalDocument
        {
            ReferralId = referralId ?? Guid.Empty,
            InformationRequestId = informationRequestId,
            Type = type,
            Title = resolvedTitle,
            Content = $"[PDF attachment: {fileName}]",
            FileName = fileName,
            ContentType = "application/pdf",
            FileBytes = bytes
        };
    }

    private async Task EnsureOrganizationExistsAsync(string organizationId, CancellationToken ct)
    {
        if (!await db.Organizations.AnyAsync(o => o.Id == organizationId, ct))
        {
            throw new ResourceNotFoundException("Organization does not exist.");
        }
    }

    private static void EnsureReferralIsPending(Referral referral)
    {
        if (referral.Status != ReferralStatus.Pending)
        {
            throw new ExchangeConflictException(
                $"Referral has already been {referral.Status.ToString().ToLowerInvariant()} and can no longer be modified.");
        }
    }

    private async Task<InformationRequest> LoadPendingRequestAsync(Guid id, CancellationToken ct)
    {
        InformationRequest request = await db.InformationRequests
                                         .Include(r => r.Referral)
                                         .Include(r => r.ResponseDocument)
                                         .SingleOrDefaultAsync(r => r.Id == id, ct)
                                     ?? throw new ResourceNotFoundException("Information request does not exist.");

        EnsureReferralIsPending(request.Referral);

        if (request.Status != InformationRequestStatus.Pending)
        {
            throw new ExchangeConflictException("Information request has already been fulfilled.");
        }

        return request;
    }

    private async Task<InformationRequestResponse> FulfillRequestAsync(
        InformationRequest request, MedicalDocument document, CancellationToken ct)
    {
        db.Documents.Add(document);
        request.Status = InformationRequestStatus.Fulfilled;
        request.FulfilledAtUtc = DateTime.UtcNow;
        request.ResponseDocument = document;

        LogExchange(ExchangeAction.InformationProvided, request.ReferralId, request.Id, document.Id,
            document.Type, request.Referral.SenderOrganizationId, request.Referral.ReceiverOrganizationId);

        await SaveChangesWithAuditLoggingAsync(ct);
        return ToResponse(request);
    }

    private void LogExchange(
        ExchangeAction action, Guid referralId, Guid? requestId, Guid? documentId,
        DocumentType? documentType, string senderId, string receiverId)
    {
        db.ExchangeLogs.Add(new ExchangeLog
        {
            Action = action,
            ReferralId = referralId,
            InformationRequestId = requestId,
            DocumentId = documentId,
            DocumentType = documentType,
            SenderOrganizationId = senderId,
            ReceiverOrganizationId = receiverId
        });
    }

    private async Task SaveChangesWithAuditLoggingAsync(CancellationToken ct)
    {
        List<ExchangeLog> pendingLogs =
        [
            .. db.ChangeTracker.Entries<ExchangeLog>()
                .Where(e => e.State == EntityState.Added)
                .Select(e => e.Entity)
        ];

        await db.SaveChangesAsync(ct);

        foreach (ExchangeLog log in pendingLogs)
        {
            logger.LogInformation(
                "Exchange {Action}: {Sender} -> {Receiver} (Referral: {ReferralId}, DocumentType: {DocumentType})",
                log.Action, log.SenderOrganizationId, log.ReceiverOrganizationId, log.ReferralId, log.DocumentType);
        }
    }

    private static ReferralResponse ToResponse(Referral r) => new(
        r.Id, r.PatientBsn, r.PatientName, r.PatientDateOfBirth,
        r.SenderOrganizationId, r.ReceiverOrganizationId, r.Reason,
        r.Status, r.DecidedAtUtc, r.DecisionNotes, r.CreatedAtUtc,
        r.Documents
            .OrderBy(d => d.CreatedAtUtc)
            .Select(ToDocumentResponse)
            .ToList());

    private static MedicalDocumentResponse ToDocumentResponse(MedicalDocument d) => new(
        d.Id, d.ReferralId, d.InformationRequestId, d.Type, d.Title,
        d.Content, d.FileName, d.ContentType, d.CreatedAtUtc);

    private static InformationRequestResponse ToResponse(InformationRequest r) => new(
        r.Id, r.ReferralId, r.Referral.ReceiverOrganizationId, r.Referral.SenderOrganizationId,
        r.RequestedDocumentType, r.Notes, r.Status, r.CreatedAtUtc, r.FulfilledAtUtc, r.ResponseDocument?.Id);
}

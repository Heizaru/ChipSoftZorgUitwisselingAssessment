using Microsoft.AspNetCore.Mvc;
using ZorgUitwisseling.Api.Contracts;
using ZorgUitwisseling.Api.Domain;
using ZorgUitwisseling.Api.Services;

namespace ZorgUitwisseling.Api.Endpoints;

public static class ExchangeEndpoints
{
    public static void MapExchangeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder api = endpoints.MapGroup("/api");

        api.MapGet("/organizations/{organizationId}/referrals", async (
            string organizationId, ExchangeService service, CancellationToken ct) =>
                Results.Ok(await service.GetReceivedReferralsAsync(organizationId, ct)))
            .WithTags("Organizations")
            .WithSummary("List referrals received by an organization");

        api.MapGet("/organizations/{organizationId}/information-requests", async (
            string organizationId, InformationRequestStatus? status, ExchangeService service, CancellationToken ct) =>
                Results.Ok(await service.GetIncomingInformationRequestsAsync(organizationId, status, ct)))
            .WithTags("Organizations")
            .WithSummary("List information requests directed at an organization");

        api.MapPost("/referrals", async (
            CreateReferralRequest input, ExchangeService service, CancellationToken ct) =>
        {
            ReferralResponse referral = await service.CreateReferralAsync(input, ct);
            return Results.Created($"/api/referrals/{referral.Id}", referral);
        })
        .WithTags("Referrals")
        .WithSummary("Create a referral with a PDF referral letter and allergy information");

        api.MapGet("/referrals/{id:guid}", async (
            Guid id, ExchangeService service, CancellationToken ct) =>
                Results.Ok(await service.GetReferralAsync(id, ct)))
            .WithTags("Referrals")
            .WithSummary("Get a referral and all its documents");

        api.MapPost("/referrals/{id:guid}/documents", async (
            Guid id, AddReferralDocumentRequest input, ExchangeService service, CancellationToken ct) =>
        {
            MedicalDocumentResponse doc = await service.AddDocumentAsync(id, input, ct);
            return Results.Created($"/api/documents/{doc.Id}/file", doc);
        })
        .WithTags("Documents")
        .WithSummary("Add an additional text document to a pending referral");

        api.MapPost("/referrals/{id:guid}/documents/file", async (
            Guid id, [FromForm] string? type, [FromForm] string? title, IFormFile file, ExchangeService service, CancellationToken ct) =>
        {
            MedicalDocumentResponse doc = await service.AddDocumentFileAsync(id, type, title, file, ct);
            return Results.Created($"/api/documents/{doc.Id}/file", doc);
        })
        .DisableAntiforgery()
        .WithTags("Documents")
        .WithSummary("Upload an additional PDF document to a pending referral");

        api.MapPost("/referrals/{id:guid}/accept", async (
            Guid id, ReferralDecisionRequest? input, ExchangeService service, CancellationToken ct) =>
                Results.Ok(await service.DecideReferralAsync(id, ReferralStatus.Accepted, input, ct)))
            .WithTags("Referrals")
            .WithSummary("Accept a pending referral (locks the referral against further uploads)");

        api.MapPost("/referrals/{id:guid}/reject", async (
            Guid id, ReferralDecisionRequest? input, ExchangeService service, CancellationToken ct) =>
                Results.Ok(await service.DecideReferralAsync(id, ReferralStatus.Rejected, input, ct)))
            .WithTags("Referrals")
            .WithSummary("Reject a pending referral (locks the referral against further uploads)");

        api.MapPost("/referrals/{id:guid}/information-requests", async (
            Guid id, CreateInformationRequest input, ExchangeService service, CancellationToken ct) =>
        {
            InformationRequestResponse request = await service.CreateInformationRequestAsync(id, input, ct);
            return Results.Created($"/api/information-requests/{request.Id}", request);
        })
        .WithTags("Information Requests")
        .WithSummary("Request additional patient information from the referring organization");

        api.MapGet("/information-requests/{id:guid}", async (
            Guid id, ExchangeService service, CancellationToken ct) =>
                Results.Ok(await service.GetInformationRequestAsync(id, ct)))
            .WithTags("Information Requests")
            .WithSummary("Get the status of an information request");

        api.MapPost("/information-requests/{id:guid}/response", async (
            Guid id, RespondToInformationRequest input, ExchangeService service, CancellationToken ct) =>
                Results.Ok(await service.RespondAsync(id, input, ct)))
            .WithTags("Information Requests")
            .WithSummary("Fulfill a pending information request with text");

        api.MapPost("/information-requests/{id:guid}/response/file", async (
            Guid id, [FromForm] string? title, IFormFile file, ExchangeService service, CancellationToken ct) =>
                Results.Ok(await service.RespondWithFileAsync(id, title, file, ct)))
            .DisableAntiforgery()
            .WithTags("Information Requests")
            .WithSummary("Fulfill a pending information request by uploading a PDF file");

        api.MapGet("/documents/{id:guid}/file", async (
            Guid id, ExchangeService service, CancellationToken ct) =>
        {
            (byte[] bytes, string contentType, string fileName) = await service.DownloadDocumentFileAsync(id, ct);
            return Results.File(bytes, contentType, fileName);
        })
        .WithTags("Documents")
        .WithSummary("Download a document as a PDF, JSON, or text file");

        api.MapGet("/referrals/{id:guid}/exchange-logs", async (
            Guid id, ExchangeService service, CancellationToken ct) =>
                Results.Ok(await service.GetExchangeLogsAsync(id, ct)))
            .WithTags("Exchange History")
            .WithSummary("Get the exchange audit log for a referral");
    }
}

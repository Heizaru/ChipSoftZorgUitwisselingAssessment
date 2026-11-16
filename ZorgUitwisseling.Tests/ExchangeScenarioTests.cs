using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ZorgUitwisseling.Api.Contracts;
using ZorgUitwisseling.Api.Domain;
using ZorgUitwisseling.Tests.Infrastructure;
using static ZorgUitwisseling.Tests.Infrastructure.ApiTestData;

namespace ZorgUitwisseling.Tests;

public class ExchangeScenarioTests
{
    [Fact]
    public async Task Complete_referral_and_medication_request_flow_works_end_to_end()
    {
        await using var factory = new TestApiFactory();
        using HttpClient client = factory.CreateClient();

        // 1. Ziekenhuis A maakt een verwijzing aan met PDF-verwijsbrief en allergie-informatie
        ReferralResponse created = await CreateReferralAsync(client);
        Assert.Equal(ReferralStatus.Pending, created.Status);
        Assert.Equal(2, created.Documents.Count);
        MedicalDocumentResponse letterDoc = Assert.Single(created.Documents, d => d.Type == DocumentType.ReferralLetter);
        Assert.Equal("application/pdf", letterDoc.ContentType);
        Assert.Equal("verwijsbrief.pdf", letterDoc.FileName);
        MedicalDocumentResponse allergyDoc = Assert.Single(created.Documents, d => d.Type == DocumentType.AllergyInformation);
        Assert.Equal("application/json", allergyDoc.ContentType);
        Assert.Contains("Penicilline", allergyDoc.Content);

        // 2. Verpleeghuis B bekijkt verwijzingen
        ReferralSummaryResponse[] inbox = (await client.GetFromJsonAsync<ReferralSummaryResponse[]>(
            $"/api/organizations/{Receiver}/referrals", Json))!;
        ReferralSummaryResponse summary = Assert.Single(inbox);
        Assert.Equal(created.Id, summary.Id);
        Assert.Equal(ReferralStatus.Pending, summary.Status);
        Assert.Equal(2, summary.DocumentCount);

        // 3. Verpleeghuis B haalt de verwijzing inclusief documenten op en downloadt de PDF-verwijsbrief
        var initialRead = (await client.GetFromJsonAsync<ReferralResponse>(
            $"/api/referrals/{summary.Id}", Json))!;
        Assert.Equal(2, initialRead.Documents.Count);

        using HttpResponseMessage letterDownload = await client.GetAsync($"/api/documents/{letterDoc.Id}/file");
        Assert.Equal(HttpStatusCode.OK, letterDownload.StatusCode);
        Assert.Equal("application/pdf", letterDownload.Content.Headers.ContentType?.MediaType);
        Assert.Equal(SamplePdfBytes, await letterDownload.Content.ReadAsByteArrayAsync());

        // 4. Ziekenhuis A voegt nog een extra document toe aan de openstaande verwijzing
        using HttpResponseMessage extraDocResponse = await client.PostAsJsonAsync(
            $"/api/referrals/{summary.Id}/documents",
            new AddReferralDocumentRequest
            {
                Type = DocumentType.Other,
                Title = "Verpleegkundige overdracht",
                Content = "Patiënt mobiliseert zelfstandig met rollator."
            },
            Json);
        Assert.Equal(HttpStatusCode.Created, extraDocResponse.StatusCode);

        // 5. Verpleeghuis B vraagt aanvullend medicatiegebruik op bij Ziekenhuis A
        using HttpResponseMessage requestResponse = await client.PostAsJsonAsync(
            $"/api/referrals/{summary.Id}/information-requests",
            new CreateInformationRequest
            {
                RequestedDocumentType = DocumentType.MedicationOverview,
                Notes = "Graag het actuele medicatieoverzicht meesturen."
            },
            Json);
        Assert.Equal(HttpStatusCode.Created, requestResponse.StatusCode);
        var infoRequest = (await requestResponse.Content
            .ReadFromJsonAsync<InformationRequestResponse>(Json))!;
        Assert.Equal(InformationRequestStatus.Pending, infoRequest.Status);

        // 6. Ziekenhuis A haalt informatieverzoeken op en beantwoordt het verzoek
        InformationRequestResponse[] openRequests = (await client.GetFromJsonAsync<InformationRequestResponse[]>(
            $"/api/organizations/{Sender}/information-requests?status=Pending", Json))!;
        Assert.Single(openRequests, r => r.Id == infoRequest.Id);

        using HttpResponseMessage fulfillResponse = await client.PostAsJsonAsync(
            $"/api/information-requests/{infoRequest.Id}/response",
            new RespondToInformationRequest
            {
                Title = "Actueel medicatieoverzicht",
                Content = "Metformine 500mg 2dd1; Paracetamol 1000mg zo nodig."
            },
            Json);
        Assert.Equal(HttpStatusCode.OK, fulfillResponse.StatusCode);
        var fulfilled = (await fulfillResponse.Content
            .ReadFromJsonAsync<InformationRequestResponse>(Json))!;
        Assert.Equal(InformationRequestStatus.Fulfilled, fulfilled.Status);
        Assert.NotNull(fulfilled.FulfilledDocumentId);

        // 7. Verpleeghuis B haalt de verwijzing opnieuw op en accepteert de verwijzing
        var finalRead = (await client.GetFromJsonAsync<ReferralResponse>(
            $"/api/referrals/{created.Id}", Json))!;
        Assert.Equal(4, finalRead.Documents.Count);
        Assert.Contains(finalRead.Documents, d => d.Type == DocumentType.MedicationOverview);

        using HttpResponseMessage acceptResponse = await client.PostAsJsonAsync(
            $"/api/referrals/{created.Id}/accept",
            new ReferralDecisionRequest { Notes = "Patiënt geaccepteerd voor opname op afdeling Revalidatie." },
            Json);
        Assert.Equal(HttpStatusCode.OK, acceptResponse.StatusCode);
        var accepted = (await acceptResponse.Content.ReadFromJsonAsync<ReferralResponse>(Json))!;
        Assert.Equal(ReferralStatus.Accepted, accepted.Status);
        Assert.NotNull(accepted.DecidedAtUtc);
        Assert.Equal("Patiënt geaccepteerd voor opname op afdeling Revalidatie.", accepted.DecisionNotes);

        // 8. Na acceptatie kunnen geen nieuwe documenten of informatieverzoeken meer worden toegevoegd
        using HttpResponseMessage blockedDoc = await client.PostAsJsonAsync(
            $"/api/referrals/{created.Id}/documents",
            new AddReferralDocumentRequest
            {
                Type = DocumentType.Other,
                Title = "Nagekomen notitie",
                Content = "Mag niet meer worden toegevoegd."
            },
            Json);
        Assert.Equal(HttpStatusCode.Conflict, blockedDoc.StatusCode);

        // 9. Controleer dat alle uitwisselingen zijn vastgelegd zonder patiëntdata
        using HttpResponseMessage logsResponse = await client.GetAsync($"/api/referrals/{created.Id}/exchange-logs");
        string logsBody = await logsResponse.Content.ReadAsStringAsync();
        ExchangeLogResponse[] logs = (await logsResponse.Content.ReadFromJsonAsync<ExchangeLogResponse[]>(Json))!;

        Assert.Contains(logs, l => l.Action == ExchangeAction.ReferralCreated);
        Assert.Contains(logs, l => l.Action == ExchangeAction.DocumentAdded);
        Assert.Contains(logs, l => l.Action == ExchangeAction.InformationRequested);
        Assert.Contains(logs, l => l.Action == ExchangeAction.InformationProvided);
        Assert.Contains(logs, l => l.Action == ExchangeAction.DocumentsRetrieved);
        Assert.Contains(logs, l => l.Action == ExchangeAction.ReferralAccepted);
        Assert.DoesNotContain(ValidBsn, logsBody);
        Assert.DoesNotContain("J. de Vries", logsBody);
    }

    [Fact]
    public async Task Can_upload_and_download_pdf_documents_and_record_audit_log()
    {
        await using var factory = new TestApiFactory();
        using HttpClient client = factory.CreateClient();

        ReferralResponse referral = await CreateReferralAsync(client);

        // Extra PDF-document rechtstreeks toevoegen aan een openstaande verwijzing
        byte[] extraPdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4\n% Labuitslag PDF\n%%EOF");
        using var extraForm = new MultipartFormDataContent();
        extraForm.Add(new StringContent("Other"), "type");
        extraForm.Add(new StringContent("Labuitslagen (PDF)"), "title");
        var extraFileContent = new ByteArrayContent(extraPdfBytes);
        extraFileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        extraForm.Add(extraFileContent, "file", "labuitslagen.pdf");

        using HttpResponseMessage extraUploadResponse = await client.PostAsync(
            $"/api/referrals/{referral.Id}/documents/file", extraForm);
        Assert.Equal(HttpStatusCode.Created, extraUploadResponse.StatusCode);
        var extraDoc = (await extraUploadResponse.Content.ReadFromJsonAsync<MedicalDocumentResponse>(Json))!;
        Assert.Equal(DocumentType.Other, extraDoc.Type);
        Assert.Equal("application/pdf", extraDoc.ContentType);

        using HttpResponseMessage reqMsg = await client.PostAsJsonAsync(
            $"/api/referrals/{referral.Id}/information-requests",
            new CreateInformationRequest { RequestedDocumentType = DocumentType.MedicationOverview },
            Json);
        var infoRequest = (await reqMsg.Content
            .ReadFromJsonAsync<InformationRequestResponse>(Json))!;

        // Ziekenhuis A beantwoordt het verzoek met een PDF-bestand
        byte[] fakePdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4\n% Fictief medicatieoverzicht PDF\n%%EOF");
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("Medicatieoverzicht (PDF)"), "title");
        var fileContent = new ByteArrayContent(fakePdfBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "file", "medicatieoverzicht.pdf");

        using HttpResponseMessage uploadResponse = await client.PostAsync(
            $"/api/information-requests/{infoRequest.Id}/response/file", form);
        Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);

        var fulfilled = (await uploadResponse.Content
            .ReadFromJsonAsync<InformationRequestResponse>(Json))!;
        Assert.NotNull(fulfilled.FulfilledDocumentId);

        // Verpleeghuis B downloadt het PDF-bestand
        using HttpResponseMessage downloadResponse = await client.GetAsync(
            $"/api/documents/{fulfilled.FulfilledDocumentId}/file");
        Assert.Equal(HttpStatusCode.OK, downloadResponse.StatusCode);
        Assert.Equal("application/pdf", downloadResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal("medicatieoverzicht.pdf", downloadResponse.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal(fakePdfBytes, await downloadResponse.Content.ReadAsByteArrayAsync());

        // Controleer dat ook de download in de logs staat
        ExchangeLogResponse[] logs = (await client.GetFromJsonAsync<ExchangeLogResponse[]>(
            $"/api/referrals/{referral.Id}/exchange-logs", Json))!;
        Assert.Contains(logs, l => l.Action == ExchangeAction.DocumentsRetrieved
            && l.DocumentId == fulfilled.FulfilledDocumentId);
        Assert.Contains(logs, l => l.Action == ExchangeAction.DocumentAdded
            && l.DocumentId == extraDoc.Id);
    }

    [Fact]
    public async Task Rejected_referral_blocks_all_document_uploads_requests_and_decisions()
    {
        await using var factory = new TestApiFactory();
        using HttpClient client = factory.CreateClient();

        ReferralResponse referral = await CreateReferralAsync(client);

        // Maak een informatieverzoek aan 
        using HttpResponseMessage reqMsg = await client.PostAsJsonAsync(
            $"/api/referrals/{referral.Id}/information-requests",
            new CreateInformationRequest { RequestedDocumentType = DocumentType.MedicationOverview },
            Json);
        Assert.Equal(HttpStatusCode.Created, reqMsg.StatusCode);
        var pendingInfoRequest = (await reqMsg.Content.ReadFromJsonAsync<InformationRequestResponse>(Json))!;

        // Verpleeghuis B wijst de verwijzing af
        using HttpResponseMessage rejectResponse = await client.PostAsync(
            $"/api/referrals/{referral.Id}/reject", content: null);
        Assert.Equal(HttpStatusCode.OK, rejectResponse.StatusCode);
        var rejected = (await rejectResponse.Content.ReadFromJsonAsync<ReferralResponse>(Json))!;
        Assert.Equal(ReferralStatus.Rejected, rejected.Status);
        Assert.NotNull(rejected.DecidedAtUtc);

        // 1. tekstdocument
        using HttpResponseMessage addTextResp = await client.PostAsJsonAsync(
            $"/api/referrals/{referral.Id}/documents",
            new AddReferralDocumentRequest
            {
                Type = DocumentType.Other,
                Title = "Extra document",
                Content = "Niet toegestaan na afwijzing."
            },
            Json);
        Assert.Equal(HttpStatusCode.Conflict, addTextResp.StatusCode);

        // 2. PDF-bestand uploaden 
        using var pdfForm = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(SamplePdfBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        pdfForm.Add(fileContent, "file", "extra.pdf");
        using HttpResponseMessage addFileResp = await client.PostAsync(
            $"/api/referrals/{referral.Id}/documents/file", pdfForm);
        Assert.Equal(HttpStatusCode.Conflict, addFileResp.StatusCode);

        // 3. Nieuw informatieverzoek aanmaken kan niet
        using HttpResponseMessage newReqResp = await client.PostAsJsonAsync(
            $"/api/referrals/{referral.Id}/information-requests",
            new CreateInformationRequest { RequestedDocumentType = DocumentType.MedicationOverview },
            Json);
        Assert.Equal(HttpStatusCode.Conflict, newReqResp.StatusCode);

        // 4. Openstaand informatieverzoek beantwoorden kan niet
        using HttpResponseMessage fulfillJsonResp = await client.PostAsJsonAsync(
            $"/api/information-requests/{pendingInfoRequest.Id}/response",
            new RespondToInformationRequest { Title = "Medicatie", Content = "Niet meer toegestaan." },
            Json);
        Assert.Equal(HttpStatusCode.Conflict, fulfillJsonResp.StatusCode);

        using var fulfillPdfForm = new MultipartFormDataContent();
        var fulfillFileContent = new ByteArrayContent(SamplePdfBytes);
        fulfillFileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        fulfillPdfForm.Add(fulfillFileContent, "file", "medicatie.pdf");
        using HttpResponseMessage fulfillFileResp = await client.PostAsync(
            $"/api/information-requests/{pendingInfoRequest.Id}/response/file", fulfillPdfForm);
        Assert.Equal(HttpStatusCode.Conflict, fulfillFileResp.StatusCode);

        // 5. Opnieuw accepteren of afwijzen kan niet
        using HttpResponseMessage reAcceptResp = await client.PostAsync(
            $"/api/referrals/{referral.Id}/accept", content: null);
        Assert.Equal(HttpStatusCode.Conflict, reAcceptResp.StatusCode);

        // 6. Inzien en logboek blijven wel beschikbaar
        using HttpResponseMessage readResp = await client.GetAsync($"/api/referrals/{referral.Id}");
        Assert.Equal(HttpStatusCode.OK, readResp.StatusCode);

        ExchangeLogResponse[] logs = (await client.GetFromJsonAsync<ExchangeLogResponse[]>(
            $"/api/referrals/{referral.Id}/exchange-logs", Json))!;
        Assert.Contains(logs, l => l.Action == ExchangeAction.ReferralRejected);
    }

    [Fact]
    public async Task Fulfilling_an_already_fulfilled_request_returns_409_conflict()
    {
        await using var factory = new TestApiFactory();
        using HttpClient client = factory.CreateClient();

        ReferralResponse referral = await CreateReferralAsync(client);
        using HttpResponseMessage reqMsg = await client.PostAsJsonAsync(
            $"/api/referrals/{referral.Id}/information-requests",
            new CreateInformationRequest { RequestedDocumentType = DocumentType.MedicationOverview },
            Json);
        var infoRequest = (await reqMsg.Content
            .ReadFromJsonAsync<InformationRequestResponse>(Json))!;

        var payload = new RespondToInformationRequest
        {
            Title = "Medicatieoverzicht",
            Content = "Geen actieve medicatie."
        };

        using HttpResponseMessage first = await client.PostAsJsonAsync(
            $"/api/information-requests/{infoRequest.Id}/response", payload, Json);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using HttpResponseMessage duplicate = await client.PostAsJsonAsync(
            $"/api/information-requests/{infoRequest.Id}/response", payload, Json);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text;
using ZorgUitwisseling.Api.Contracts;
using ZorgUitwisseling.Api.Domain;
using ZorgUitwisseling.Tests.Infrastructure;
using static ZorgUitwisseling.Tests.Infrastructure.ApiTestData;

namespace ZorgUitwisseling.Tests;

public class ValidationTests
{
    [Fact]
    public async Task CreateReferral_without_required_letter_or_invalid_allergies_returns_400()
    {
        await using var factory = new TestApiFactory();
        using HttpClient client = factory.CreateClient();

        var missingAllergyRequest = new CreateReferralRequest
        {
            PatientBsn = ValidBsn,
            PatientName = "J. de Vries",
            PatientDateOfBirth = new DateOnly(1945, 5, 12),
            SenderOrganizationId = Sender,
            ReceiverOrganizationId = Receiver,
            Reason = "Overdracht",
            ReferralLetter = ValidReferralRequest().ReferralLetter,
            AllergyInformation = null!
        };

        using HttpResponseMessage missingResponse = await client.PostAsJsonAsync("/api/referrals", missingAllergyRequest, Json);
        Assert.Equal(HttpStatusCode.BadRequest, missingResponse.StatusCode);
        Assert.Equal("application/problem+json", missingResponse.Content.Headers.ContentType?.MediaType);

        var inconsistentAllergyRequest = new CreateReferralRequest
        {
            PatientBsn = ValidBsn,
            PatientName = "J. de Vries",
            PatientDateOfBirth = new DateOnly(1945, 5, 12),
            SenderOrganizationId = Sender,
            ReceiverOrganizationId = Receiver,
            Reason = "Overdracht",
            ReferralLetter = ValidReferralRequest().ReferralLetter,
            AllergyInformation = new AllergyInformationRequest
            {
                HasKnownAllergies = true,
                Allergies = []
            }
        };

        using HttpResponseMessage inconsistentResponse = await client.PostAsJsonAsync(
            "/api/referrals", inconsistentAllergyRequest, Json);
        Assert.Equal(HttpStatusCode.BadRequest, inconsistentResponse.StatusCode);
    }

    [Fact]
    public async Task CreateReferral_with_invalid_pdf_base64_or_non_pdf_content_returns_400()
    {
        await using var factory = new TestApiFactory();
        using HttpClient client = factory.CreateClient();

        var invalidBase64Request = new CreateReferralRequest
        {
            PatientBsn = ValidBsn,
            PatientName = "J. de Vries",
            PatientDateOfBirth = new DateOnly(1945, 5, 12),
            SenderOrganizationId = Sender,
            ReceiverOrganizationId = Receiver,
            Reason = "Overdracht",
            ReferralLetter = new ReferralLetterPdfRequest
            {
                FileName = "verwijsbrief.pdf",
                FileBase64 = "dit-is-geen-base64!!!"
            },
            AllergyInformation = new AllergyInformationRequest { HasKnownAllergies = false }
        };

        using HttpResponseMessage badBase64Resp = await client.PostAsJsonAsync("/api/referrals", invalidBase64Request, Json);
        Assert.Equal(HttpStatusCode.BadRequest, badBase64Resp.StatusCode);

        var nonPdfBytesRequest = new CreateReferralRequest
        {
            PatientBsn = ValidBsn,
            PatientName = "J. de Vries",
            PatientDateOfBirth = new DateOnly(1945, 5, 12),
            SenderOrganizationId = Sender,
            ReceiverOrganizationId = Receiver,
            Reason = "Overdracht",
            ReferralLetter = new ReferralLetterPdfRequest
            {
                FileName = "verwijsbrief.pdf",
                FileBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("Geen echte PDF"))
            },
            AllergyInformation = new AllergyInformationRequest { HasKnownAllergies = false }
        };

        using HttpResponseMessage nonPdfResp = await client.PostAsJsonAsync("/api/referrals", nonPdfBytesRequest, Json);
        Assert.Equal(HttpStatusCode.BadRequest, nonPdfResp.StatusCode);

        var wrongExtensionRequest = new CreateReferralRequest
        {
            PatientBsn = ValidBsn,
            PatientName = "J. de Vries",
            PatientDateOfBirth = new DateOnly(1945, 5, 12),
            SenderOrganizationId = Sender,
            ReceiverOrganizationId = Receiver,
            Reason = "Overdracht",
            ReferralLetter = new ReferralLetterPdfRequest
            {
                FileName = "verwijsbrief.txt",
                FileBase64 = SamplePdfBase64
            },
            AllergyInformation = new AllergyInformationRequest { HasKnownAllergies = false }
        };

        using HttpResponseMessage wrongExtResp = await client.PostAsJsonAsync("/api/referrals", wrongExtensionRequest, Json);
        Assert.Equal(HttpStatusCode.BadRequest, wrongExtResp.StatusCode);
    }

    [Fact]
    public async Task CreateReferral_with_invalid_bsn_or_same_organization_returns_400()
    {
        await using var factory = new TestApiFactory();
        using HttpClient client = factory.CreateClient();

        CreateReferralRequest valid = ValidReferralRequest();

        var invalidBsnRequest = new CreateReferralRequest
        {
            PatientBsn = "123456789", // Ongeldige 11-proef
            PatientName = "J. de Vries",
            PatientDateOfBirth = new DateOnly(1945, 5, 12),
            SenderOrganizationId = Sender,
            ReceiverOrganizationId = Receiver,
            Reason = "Overdracht",
            ReferralLetter = valid.ReferralLetter,
            AllergyInformation = valid.AllergyInformation
        };

        using HttpResponseMessage bsnResponse = await client.PostAsJsonAsync("/api/referrals", invalidBsnRequest, Json);
        Assert.Equal(HttpStatusCode.BadRequest, bsnResponse.StatusCode);

        var sameOrgRequest = new CreateReferralRequest
        {
            PatientBsn = ValidBsn,
            PatientName = "J. de Vries",
            PatientDateOfBirth = new DateOnly(1945, 5, 12),
            SenderOrganizationId = Sender,
            ReceiverOrganizationId = Sender,
            Reason = "Overdracht naar zichzelf",
            ReferralLetter = valid.ReferralLetter,
            AllergyInformation = valid.AllergyInformation
        };

        using HttpResponseMessage sameOrgResponse = await client.PostAsJsonAsync("/api/referrals", sameOrgRequest, Json);
        Assert.Equal(HttpStatusCode.BadRequest, sameOrgResponse.StatusCode);
    }

    [Fact]
    public async Task RespondWithFile_with_invalid_pdf_header_returns_400()
    {
        await using var factory = new TestApiFactory();
        using HttpClient client = factory.CreateClient();

        ReferralResponse referral = await CreateReferralAsync(client);
        using HttpResponseMessage reqMsg = await client.PostAsJsonAsync(
            $"/api/referrals/{referral.Id}/information-requests",
            new CreateInformationRequest { RequestedDocumentType = DocumentType.MedicationOverview },
            Json);
        var infoRequest = (await reqMsg.Content.ReadFromJsonAsync<InformationRequestResponse>(Json))!;

        using var invalidPdfForm = new MultipartFormDataContent();
        invalidPdfForm.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("Geen echte PDF")), "file", "nep.pdf");

        using HttpResponseMessage response = await client.PostAsync(
            $"/api/information-requests/{infoRequest.Id}/response/file", invalidPdfForm);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddDocumentFile_with_invalid_document_type_returns_400()
    {
        await using var factory = new TestApiFactory();
        using HttpClient client = factory.CreateClient();

        ReferralResponse referral = await CreateReferralAsync(client);

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("OnbekendType"), "type");
        form.Add(new ByteArrayContent(SamplePdfBytes), "file", "extra.pdf");

        using HttpResponseMessage response = await client.PostAsync(
            $"/api/referrals/{referral.Id}/documents/file", form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_organization_or_referral_returns_404_problem_details()
    {
        await using var factory = new TestApiFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage unknownOrg = await client.GetAsync("/api/organizations/onbekend-ziekenhuis/referrals");
        Assert.Equal(HttpStatusCode.NotFound, unknownOrg.StatusCode);
        Assert.Equal("application/problem+json", unknownOrg.Content.Headers.ContentType?.MediaType);

        using HttpResponseMessage unknownReferral = await client.GetAsync($"/api/referrals/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, unknownReferral.StatusCode);
    }
}

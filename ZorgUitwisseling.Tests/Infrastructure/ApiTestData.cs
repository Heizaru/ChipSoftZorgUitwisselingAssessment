using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZorgUitwisseling.Api.Contracts;

namespace ZorgUitwisseling.Tests.Infrastructure;

internal static class ApiTestData
{
    public const string ValidBsn = "999999990";
    public const string Sender = "ziekenhuis-a";
    public const string Receiver = "verpleeghuis-b";

    public static readonly byte[] SamplePdfBytes =
        Encoding.ASCII.GetBytes("%PDF-1.4\n% Fictieve verwijsbrief PDF\n%%EOF");

    public static readonly string SamplePdfBase64 = Convert.ToBase64String(SamplePdfBytes);

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static CreateReferralRequest ValidReferralRequest() => new()
    {
        PatientBsn = ValidBsn,
        PatientName = "J. de Vries",
        PatientDateOfBirth = new DateOnly(1945, 5, 12),
        SenderOrganizationId = Sender,
        ReceiverOrganizationId = Receiver,
        Reason = "Revalidatie na ziekenhuisopname.",
        ReferralLetter = new ReferralLetterPdfRequest
        {
            Title = "Verwijsbrief",
            FileName = "verwijsbrief.pdf",
            FileBase64 = SamplePdfBase64
        },
        AllergyInformation = new AllergyInformationRequest
        {
            Title = "Allergie-overzicht",
            HasKnownAllergies = true,
            Summary = "Penicilline-allergie bekend.",
            Allergies =
            [
                new AllergyEntryRequest
                {
                    Substance = "Penicilline",
                    Reaction = "Huiduitslag (urticaria)",
                    Severity = AllergySeverity.Moderate,
                    Notes = "Vastgesteld in 2021."
                }
            ]
        }
    };

    public async static Task<ReferralResponse> CreateReferralAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/referrals", ValidReferralRequest(), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ReferralResponse>(Json))!;
    }
}

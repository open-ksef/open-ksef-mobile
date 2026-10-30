using System.Text.Json;
using System.Text.Json.Nodes;
using OpenKSeF.Mobile.Models;
using Xunit;

namespace OpenKSeF.Mobile.Tests.Services;

public class IssuedInvoiceContractTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public const string SellerProfileRequestJson = """
        { "legalName": "OpenKSeF Test A sp. z o.o.", "addressLine1": "ul. Prosta 1", "addressLine2": "00-001 Warszawa",
          "bankAccount": "61109010140000071219812874", "cashAccounting": false,
          "numberingPattern": "FV/{nr}/{MM}/{RRRR}", "nextNumber": 57, "nextNumberPeriod": "2026-11" }
        """;

    public const string SellerProfileResponseJson = """
        { "legalName": "OpenKSeF Test A sp. z o.o.", "addressLine1": "ul. Prosta 1", "addressLine2": "00-001 Warszawa",
          "bankAccount": "61109010140000071219812874", "cashAccounting": false,
          "numberingPattern": "FV/{nr}/{MM}/{RRRR}", "nextNumber": 57, "nextNumberPeriod": "2026-11",
          "nip": "1234567890", "isComplete": true, "nextNumberPreview": "FV/57/11/2026", "ksefEnvironment": "test" }
        """;

    public const string NipLookupJson = """
        { "nip": "1234567890", "name": "Firma Testowa", "addressLine1": "ul. Prosta 1", "addressLine2": "00-001 Warszawa",
          "vatStatus": "Czynny", "bankAccounts": ["61109010140000071219812874"] }
        """;

    public const string DraftRequestJson = """
        { "buyer": { "kind": "Company", "nip": "1234567890", "name": "Nabywca sp. z o.o.", "addressLine1": "ul. Krzywa 2", "addressLine2": "00-002 Warszawa" },
          "issueDate": "2026-11-03", "saleDate": "2026-11-03", "dueDate": "2026-11-17",
          "paymentMethod": "Transfer", "splitPayment": false,
          "exemptionBasis": { "kind": "Act", "text": "art. 113 ust. 1 ustawy o VAT" },
          "lines": [ { "name": "Usługa", "unit": "szt.", "quantity": 1, "unitNetPrice": 100.00, "vatRate": "23" } ] }
        """;

    public const string PersonDraftRequestJson = """
        { "buyer": { "kind": "Person", "nip": null, "name": "Jan Kowalski", "addressLine1": null, "addressLine2": null },
          "issueDate": null, "saleDate": null, "dueDate": null,
          "paymentMethod": "Cash", "splitPayment": false, "exemptionBasis": null,
          "lines": [ { "name": "Usługa", "unit": null, "quantity": 2.5, "unitNetPrice": 0.33, "vatRate": "zw" } ] }
        """;

    public const string InvoiceResponseJson = """
        { "id": "0b7c9a62-3f6e-4d1a-9c55-1f2e3d4c5b6a", "status": "Draft", "number": null,
          "seller": { "nip": "1234567890", "name": "OpenKSeF Test A sp. z o.o.", "addressLine1": "ul. Prosta 1", "addressLine2": "00-001 Warszawa", "bankAccount": "61109010140000071219812874", "cashAccounting": false },
          "buyer": { "kind": "Company", "nip": "1234567890", "name": "Nabywca sp. z o.o.", "addressLine1": "ul. Krzywa 2", "addressLine2": "00-002 Warszawa" },
          "issueDate": "2026-11-03", "saleDate": "2026-11-03", "dueDate": "2026-11-17",
          "paymentMethod": "Transfer", "splitPayment": false, "exemptionBasis": null,
          "lines": [ { "lineNo": 1, "name": "Usługa", "unit": "szt.", "quantity": 1, "unitNetPrice": 100.00, "vatRate": "23", "netAmount": 100.00, "vatAmount": 23.00 } ],
          "vatSummary": [ { "vatRate": "23", "net": 100.00, "vat": 23.00 } ],
          "totalNet": 100.00, "totalVat": 23.00, "totalGross": 123.00, "currency": "PLN",
          "ksef": { "environment": "test", "ksefNumber": null, "acceptedAt": null, "statusCode": null, "statusMessage": null,
                    "lastError": null, "verificationUrl": null, "hasUpo": false },
          "warnings": [ { "code": "INV-W-MPP", "field": "splitPayment", "message": "Sprawdź, czy faktura wymaga mechanizmu podzielonej płatności (załącznik nr 15)" } ],
          "createdAt": "2026-11-03T10:00:00Z", "updatedAt": "2026-11-03T10:00:00Z", "issuedAt": null }
        """;

    public const string AcceptedKsefJson = """
        { "environment": "test", "ksefNumber": "1234567890-20261103-0100A0B0C0D0-E1", "acceptedAt": "2026-11-03T10:05:00Z", "statusCode": 200,
          "statusMessage": "Sukces", "lastError": null, "verificationUrl": "https://qr-test.ksef.mf.gov.pl/invoice/1234567890/03-11-2026/abc", "hasUpo": true }
        """;

    public const string ListItemJson = """
        { "id": "0b7c9a62-3f6e-4d1a-9c55-1f2e3d4c5b6a", "status": "Accepted", "number": "FV/1/11/2026", "buyerName": "Nabywca sp. z o.o.", "buyerNip": "1234567890",
          "issueDate": "2026-11-03", "dueDate": "2026-11-17", "totalNet": 100.00, "totalGross": 123.00, "currency": "PLN", "ksefNumber": "1234567890-20261103-0100A0B0C0D0-E1" }
        """;

    public const string ValidationEnvelopeJson = """
        { "errors": [ { "code": "INV-VAL-020", "field": "buyer.nip", "message": "Dla nabywcy firmy wymagany jest poprawny NIP" } ], "warnings": [] }
        """;

    public static TheoryData<string, Type> Fixtures => new()
    {
        { SellerProfileRequestJson, typeof(SellerProfileRequest) },
        { SellerProfileResponseJson, typeof(SellerProfileDto) },
        { NipLookupJson, typeof(NipLookupDto) },
        { DraftRequestJson, typeof(IssuedInvoiceDraftRequest) },
        { PersonDraftRequestJson, typeof(IssuedInvoiceDraftRequest) },
        { InvoiceResponseJson, typeof(IssuedInvoiceDto) },
        { AcceptedKsefJson, typeof(KsefSubmissionDto) },
        { ListItemJson, typeof(IssuedInvoiceListItemDto) },
        { ValidationEnvelopeJson, typeof(ValidationEnvelope) },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Fixture_RoundTripsWithoutLoss(string json, Type type)
    {
        var model = JsonSerializer.Deserialize(json, type, Web);
        var reserialized = JsonSerializer.Serialize(model, type, Web);

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(reserialized)), reserialized);
    }

    [Fact]
    public void InvoiceResponse_MapsNestedValues()
    {
        var invoice = JsonSerializer.Deserialize<IssuedInvoiceDto>(InvoiceResponseJson, Web)!;

        Assert.Equal(new DateOnly(2026, 11, 17), invoice.DueDate);
        Assert.Equal(23.00m, Assert.Single(invoice.Lines).VatAmount);
        Assert.Equal("INV-W-MPP", Assert.Single(invoice.Warnings).Code);
        Assert.Equal(new DateTime(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc), invoice.CreatedAt);
    }
}

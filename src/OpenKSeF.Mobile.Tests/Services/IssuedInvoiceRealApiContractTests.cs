using System.Text.Json;
using System.Text.Json.Nodes;
using OpenKSeF.Mobile.Models;
using Xunit;

namespace OpenKSeF.Mobile.Tests.Services;

/// <summary>Consumer contract: bodies captured from the running API at open-ksef 7263db3 (T-222), party data scrubbed.</summary>
public class IssuedInvoiceRealApiContractTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private const string ListJson = """
        {"items":[{"id":"30f51d69-1183-4e95-8c94-2f07cf3d06fc","status":"Accepted","number":"QA/2/12/2026","buyerName":"T222 Rejected","buyerNip":"9876543210","issueDate":"2026-11-02","dueDate":"2026-11-16","totalNet":10.00,"totalGross":12.30,"currency":"PLN","ksefNumber":"1234567890-20261102-44868D000000-7D"},{"id":"5affa8df-0a4c-40fc-b743-7189582effa8","status":"Accepted","number":"QA/5/11/2026","buyerName":"Jan Testowy T222","buyerNip":null,"issueDate":"2026-11-02","dueDate":"2026-11-16","totalNet":251.25,"totalGross":309.04,"currency":"PLN","ksefNumber":"1234567890-20261102-43D40D000000-53"}],"page":1,"pageSize":2,"totalCount":7,"totalPages":4}
        """;

    private const string AcceptedCompanyJson = """
        {"id":"e11c388d-b22f-44a5-85c6-49b0d5a8dc5d","status":"Accepted","number":"QA/4/11/2026","seller":{"nip":"1234567890","name":"Sprzedawca Testowy","addressLine1":"ul. Prosta 1","addressLine2":"00-001 Warszawa","bankAccount":"61109010140000071219812874","cashAccounting":false},"buyer":{"kind":"Company","nip":"9876543210","name":"OpenKSeF Test B T222","addressLine1":"ul. Testowa 2","addressLine2":"00-002 Warszawa"},"issueDate":"2026-11-02","saleDate":"2026-11-02","dueDate":"2026-11-16","paymentMethod":"Transfer","splitPayment":false,"exemptionBasis":null,"lines":[{"lineNo":1,"name":"Usluga T222 B2B","unit":"szt.","quantity":2.500000,"unitNetPrice":100.50,"vatRate":"23","netAmount":251.25,"vatAmount":57.79}],"vatSummary":[{"vatRate":"23","net":251.25,"vat":57.79}],"totalNet":251.25,"totalVat":57.79,"totalGross":309.04,"currency":"PLN","ksef":{"environment":"test","ksefNumber":"1234567890-20261102-4350A1C00000-8C","acceptedAt":"2026-11-02T08:34:26.663047Z","statusCode":200,"statusMessage":"Sukces","statusDetails":null,"lastError":null,"verificationUrl":"https://qr-test.ksef.mf.gov.pl/invoice/1234567890/02-11-2026/GwSekLYu4m5xpIBelVvthw6z2cA2gMalzs773dsrE6U","hasUpo":true},"warnings":[],"createdAt":"2026-11-02T08:29:30.429446Z","updatedAt":"2026-11-02T08:34:32.371009Z","issuedAt":"2026-11-02T08:34:19.771759Z"}
        """;

    private const string AcceptedPersonJson = """
        {"id":"5affa8df-0a4c-40fc-b743-7189582effa8","status":"Accepted","number":"QA/5/11/2026","seller":{"nip":"1234567890","name":"Sprzedawca Testowy","addressLine1":"ul. Prosta 1","addressLine2":"00-001 Warszawa","bankAccount":"61109010140000071219812874","cashAccounting":false},"buyer":{"kind":"Person","nip":null,"name":"Jan Testowy T222","addressLine1":"ul. Prywatna 5","addressLine2":"00-002 Warszawa"},"issueDate":"2026-11-02","saleDate":"2026-11-02","dueDate":"2026-11-16","paymentMethod":"Transfer","splitPayment":false,"exemptionBasis":null,"lines":[{"lineNo":1,"name":"Usluga T222 B2B","unit":"szt.","quantity":2.500000,"unitNetPrice":100.50,"vatRate":"23","netAmount":251.25,"vatAmount":57.79}],"vatSummary":[{"vatRate":"23","net":251.25,"vat":57.79}],"totalNet":251.25,"totalVat":57.79,"totalGross":309.04,"currency":"PLN","ksef":{"environment":"test","ksefNumber":"1234567890-20261102-43D40D000000-53","acceptedAt":"2026-11-02T08:38:48.546522Z","statusCode":200,"statusMessage":"Sukces","statusDetails":null,"lastError":null,"verificationUrl":"https://qr-test.ksef.mf.gov.pl/invoice/1234567890/02-11-2026/0GFpsMcREdXYSWEodpHM1q8dCgxqJtDebcwvq_hvAJY","hasUpo":true},"warnings":[],"createdAt":"2026-11-02T08:37:39.76263Z","updatedAt":"2026-11-02T08:38:55.944484Z","issuedAt":"2026-11-02T08:38:43.570356Z"}
        """;

    private const string DraftJson = """
        {"id":"02155123-dae3-4a69-bb7e-88b4b8436830","status":"Draft","number":null,"seller":{"nip":"1234567890","name":"Sprzedawca Testowy","addressLine1":"ul. Prosta 1","addressLine2":"00-001 Warszawa","bankAccount":"61109010140000071219812874","cashAccounting":false},"buyer":{"kind":"Person","nip":null,"name":"Jan Kowalski","addressLine1":"ul. Prosta 1","addressLine2":null},"issueDate":"2026-11-02","saleDate":"2026-11-02","dueDate":"2026-11-16","paymentMethod":"Cash","splitPayment":false,"exemptionBasis":{"kind":"Act","text":"art. 113 ust. 1"},"lines":[{"lineNo":1,"name":"Usluga","unit":"szt.","quantity":2.5,"unitNetPrice":0.33,"vatRate":"zw","netAmount":0.83,"vatAmount":0.00}],"vatSummary":[{"vatRate":"zw","net":0.83,"vat":0.00}],"totalNet":0.83,"totalVat":0.00,"totalGross":0.83,"currency":"PLN","ksef":{"environment":"test","ksefNumber":null,"acceptedAt":null,"statusCode":null,"statusMessage":null,"statusDetails":null,"lastError":null,"verificationUrl":null,"hasUpo":false},"warnings":[],"createdAt":"2026-11-02T08:57:17.1641001Z","updatedAt":"2026-11-02T08:57:17.1641001Z","issuedAt":null}
        """;

    private const string SellerProfileJson = """
        {"legalName":"Sprzedawca Testowy","addressLine1":"ul. Prosta 1","addressLine2":"00-001 Warszawa","bankAccount":"61109010140000071219812874","cashAccounting":false,"numberingPattern":"QA/{nr}/{MM}/{RRRR}","nextNumber":6,"nextNumberPeriod":"2026-11","nip":"1234567890","isComplete":true,"nextNumberPreview":"QA/6/11/2026","ksefEnvironment":"test"}
        """;

    private const string NipLookupJson = """
        {"nip":"1234567890","name":"Sprzedawca Testowy","addressLine1":"ul. Prosta 1","addressLine2":"00-001 Warszawa","vatStatus":"Czynny","bankAccounts":["61109010140000071219812874"]}
        """;

    private const string ValidationJson = """
        {"errors":[{"code":"INV-VAL-070","field":"buyer.kind","message":"Rodzaj nabywcy: Company lub Person"}],"warnings":[]}
        """;

    public static TheoryData<string, Type> Captured => new()
    {
        { ListJson, typeof(PagedResult<IssuedInvoiceListItemDto>) },
        { AcceptedCompanyJson, typeof(IssuedInvoiceDto) },
        { AcceptedPersonJson, typeof(IssuedInvoiceDto) },
        { DraftJson, typeof(IssuedInvoiceDto) },
        { SellerProfileJson, typeof(SellerProfileDto) },
        { NipLookupJson, typeof(NipLookupDto) },
        { ValidationJson, typeof(ValidationEnvelope) },
    };

    [Theory]
    [MemberData(nameof(Captured))]
    public void CapturedBody_RoundTripsWithoutLoss(string json, Type type)
    {
        var model = JsonSerializer.Deserialize(json, type, Web);
        var reserialized = JsonSerializer.Serialize(model, type, Web);

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(reserialized)), reserialized);
    }

    [Fact]
    public void AcceptedInvoice_MapsKsefBlock()
    {
        var invoice = JsonSerializer.Deserialize<IssuedInvoiceDto>(AcceptedCompanyJson, Web)!;

        Assert.Equal("Accepted", invoice.Status);
        Assert.Equal(200, invoice.Ksef.StatusCode);
        Assert.True(invoice.Ksef.HasUpo);
        Assert.StartsWith("https://qr-test.ksef.mf.gov.pl/invoice/", invoice.Ksef.VerificationUrl);
        Assert.Equal(309.04m, invoice.TotalGross);
        Assert.Equal(new DateOnly(2026, 11, 2), invoice.IssueDate);
    }

    [Fact]
    public void PersonBuyer_HasNullNip()
    {
        var invoice = JsonSerializer.Deserialize<IssuedInvoiceDto>(AcceptedPersonJson, Web)!;

        Assert.Equal("Person", invoice.Buyer.Kind);
        Assert.Null(invoice.Buyer.Nip);
    }

    [Fact]
    public void UnknownNewField_IsIgnored_ForInstalledClients()
    {
        var node = JsonNode.Parse(AcceptedCompanyJson)!;
        node["ksef"]!["duplicateKsefNumber"] = "1234567890-20261102-AAAAAAAAAAAA-00";
        node["somethingNew"] = 1;

        var invoice = JsonSerializer.Deserialize<IssuedInvoiceDto>(node.ToJsonString(), Web)!;

        Assert.Equal("Accepted", invoice.Status);
    }

    [Fact]
    public void DraftRequest_SerializesBackendFieldNames()
    {
        var json = JsonSerializer.SerializeToNode(new IssuedInvoiceDraftRequest
        {
            Buyer = new PartyDto { Kind = "Company", Nip = "9876543210", Name = "N", AddressLine1 = "A", AddressLine2 = "B" },
            IssueDate = new DateOnly(2026, 11, 2),
            SaleDate = new DateOnly(2026, 11, 2),
            DueDate = new DateOnly(2026, 11, 16),
            Lines = [new IssuedInvoiceLineRequest { Name = "L", Unit = "szt.", Quantity = 2.5m, UnitNetPrice = 100.50m, VatRate = "23" }]
        }, Web)!.AsObject();

        Assert.Equal(
            ["buyer", "dueDate", "exemptionBasis", "issueDate", "lines", "paymentMethod", "saleDate", "splitPayment"],
            json.Select(p => p.Key).Order(StringComparer.Ordinal));
        Assert.Equal("2026-11-02", json["issueDate"]!.GetValue<string>());
        Assert.Equal(
            ["name", "quantity", "unit", "unitNetPrice", "vatRate"],
            json["lines"]![0]!.AsObject().Select(p => p.Key).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["addressLine1", "addressLine2", "kind", "name", "nip"],
            json["buyer"]!.AsObject().Select(p => p.Key).Order(StringComparer.Ordinal));
    }
}

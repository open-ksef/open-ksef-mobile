namespace OpenKSeF.Mobile.E2E.Android.Support;

/// <summary>
/// Random checksum-valid NIPs with prefix 000, which no tax office code uses, so E2E tenants never collide with leftovers or real companies.
/// </summary>
public static class FakeNip
{
    private static readonly int[] Weights = [6, 5, 7, 2, 3, 4, 5, 6, 7];

    public static string Next()
    {
        while (true)
        {
            var body = "000" + Random.Shared.Next(1_000_000).ToString("D6");
            var check = body.Select((c, i) => (c - '0') * Weights[i]).Sum() % 11;
            if (check < 10)
                return body + check;
        }
    }
}

using OpenKSeF.Mobile.Models;
using SQLite;

namespace OpenKSeF.Mobile.Services;

public class LocalDbService
{
    private SQLiteAsyncConnection? _db;
    private const int MaxCachedInvoicesPerTenant = 100;
    private const int CacheExpirationDays = 7;
    private const string LastUserKey = "last_user";
    private int _generation;

    /// <summary>Changes on every user-data clear; a writer passes the value read before its request so a late response of a previous session is dropped.</summary>
    public int Generation => Volatile.Read(ref _generation);

    public async Task InitAsync()
    {
        if (_db is not null)
            return;

        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "openksef.db");
        _db = new SQLiteAsyncConnection(dbPath);
        await _db.RunInTransactionAsync(CreateSchema);
        await ClearExpiredCacheAsync();
    }

    public static void CreateSchema(SQLiteConnection db)
    {
        db.CreateTable<CachedInvoice>();
        db.Execute("UPDATE CachedInvoice SET IsPaid = 0 WHERE IsPaid IS NULL");
    }

    public async Task<List<InvoiceDto>> GetInvoicesAsync(Guid tenantId, bool? isPaid = null)
    {
        await InitAsync();

        var tenantIdStr = tenantId.ToString();
        var query = _db!.Table<CachedInvoice>().Where(i => i.TenantId == tenantIdStr);
        if (isPaid is { } paid)
            query = query.Where(i => i.IsPaid == paid);
        var cached = await query
            .OrderByDescending(i => i.IssueDate)
            .Take(MaxCachedInvoicesPerTenant)
            .ToListAsync();

        var invoices = cached.Select(c => c.ToDto());
        return isPaid == false
            ? invoices.OrderBy(i => i.DueDate is null).ThenBy(i => i.DueDate).ThenByDescending(i => i.IssueDate).ThenBy(i => i.Id).ToList()
            : invoices.ToList();
    }

    public async Task SaveInvoicesAsync(Guid tenantId, List<InvoiceDto> invoices, int? generation = null)
    {
        await InitAsync();

        var cached = invoices.Select(i => CachedInvoice.FromDto(i, tenantId)).ToList();
        await _db!.RunInTransactionAsync(conn =>
        {
            if (generation is { } g && g != Generation)
                return;
            foreach (var item in cached)
                conn.InsertOrReplace(item);
        });
    }

    public async Task MarkPaidExceptAsync(Guid tenantId, IReadOnlyCollection<Guid> unpaidIds, int? generation = null)
    {
        await InitAsync();

        var tenantIdStr = tenantId.ToString();
        var unpaid = unpaidIds.Select(i => i.ToString()).ToHashSet();
        await _db!.RunInTransactionAsync(conn =>
        {
            if (generation is { } g && g != Generation)
                return;
            foreach (var row in conn.Table<CachedInvoice>().Where(i => i.TenantId == tenantIdStr && !i.IsPaid).ToList())
            {
                if (unpaid.Contains(row.Id))
                    continue;
                row.IsPaid = true;
                conn.Update(row);
            }
        });
    }

    private async Task ClearExpiredCacheAsync()
    {
        if (_db is null)
            return;

        var cutoff = DateTime.UtcNow.AddDays(-CacheExpirationDays);
        await _db.ExecuteAsync(
            "DELETE FROM CachedInvoice WHERE CachedAt < ?",
            cutoff);
    }

    public async Task ClearCacheForTenantAsync(Guid tenantId)
    {
        await InitAsync();

        var tenantIdStr = tenantId.ToString();
        await _db!.ExecuteAsync(
            "DELETE FROM CachedInvoice WHERE TenantId = ?",
            tenantIdStr);
    }

    private async Task ClearAllCacheAsync()
    {
        await InitAsync();
        await _db!.DeleteAllAsync<CachedInvoice>();
    }

    public async Task ClearUserDataAsync()
    {
        Interlocked.Increment(ref _generation);
        Preferences.Default.Remove("SelectedTenantId");
        Preferences.Default.Remove("SelectedTenantNip");
        Preferences.Default.Remove(LastUserKey);
        await ClearAllCacheAsync();
        await _db!.ExecuteAsync("VACUUM");
    }

    public async Task EnsureCurrentUserAsync(string userKey)
    {
        if (Preferences.Default.Get(LastUserKey, string.Empty) == userKey)
            return;

        await ClearUserDataAsync();
        Preferences.Default.Set(LastUserKey, userKey);
    }
}

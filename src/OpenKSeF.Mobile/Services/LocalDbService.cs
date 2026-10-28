using OpenKSeF.Mobile.Models;
using SQLite;

namespace OpenKSeF.Mobile.Services;

public class LocalDbService
{
    private SQLiteAsyncConnection? _db;
    private const int MaxCachedInvoicesPerTenant = 100;
    private const int CacheExpirationDays = 7;
    private const string LastUserKey = "last_user";

    public async Task InitAsync()
    {
        if (_db is not null)
            return;

        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "openksef.db");
        _db = new SQLiteAsyncConnection(dbPath);
        await _db.CreateTableAsync<CachedInvoice>();
        await ClearExpiredCacheAsync();
    }

    public async Task<List<InvoiceDto>> GetInvoicesAsync(Guid tenantId)
    {
        await InitAsync();

        var tenantIdStr = tenantId.ToString();
        var cached = await _db!.Table<CachedInvoice>()
            .Where(i => i.TenantId == tenantIdStr)
            .OrderByDescending(i => i.IssueDate)
            .Take(MaxCachedInvoicesPerTenant)
            .ToListAsync();

        return cached.Select(c => c.ToDto()).ToList();
    }

    public async Task SaveInvoicesAsync(Guid tenantId, List<InvoiceDto> invoices)
    {
        await InitAsync();

        var cached = invoices.Select(i => CachedInvoice.FromDto(i, tenantId)).ToList();
        await _db!.RunInTransactionAsync(conn =>
        {
            foreach (var item in cached)
                conn.InsertOrReplace(item);
        });
    }

    public async Task ClearExpiredCacheAsync()
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

    public async Task ClearAllCacheAsync()
    {
        await InitAsync();
        await _db!.DeleteAllAsync<CachedInvoice>();
    }

    public async Task ClearUserDataAsync()
    {
        Preferences.Default.Remove("SelectedTenantId");
        Preferences.Default.Remove("SelectedTenantNip");
        Preferences.Default.Remove(LastUserKey);
        await ClearAllCacheAsync();
    }

    public async Task EnsureCurrentUserAsync(string userKey)
    {
        if (Preferences.Default.Get(LastUserKey, string.Empty) == userKey)
            return;

        await ClearUserDataAsync();
        Preferences.Default.Set(LastUserKey, userKey);
    }
}

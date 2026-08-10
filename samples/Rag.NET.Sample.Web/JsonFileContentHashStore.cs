using System.Text.Json;
using Rag.NET.Abstractions;
using Rag.NET.Models;

namespace Rag.NET.Sample.Web;

public sealed class JsonFileContentHashStore : IContentHashStore
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private Dictionary<string, StoredRecord>? _records;

    public JsonFileContentHashStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
    }

    public async Task<string?> GetETagAsync(ProviderId providerId, EntryId entryId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            return _records!.TryGetValue(BuildKey(providerId, entryId), out var record)
                ? record.ETag
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> GetHashAsync(ProviderId providerId, EntryId entryId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            return _records!.TryGetValue(BuildKey(providerId, entryId), out var record)
                ? record.Hash
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetAsync(ProviderId providerId, EntryId entryId, string? etag, string hash, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            _records![BuildKey(providerId, entryId)] = new StoredRecord(etag, hash);
            await SaveAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlySet<EntryId>> GetAllIdsAsync(ProviderId providerId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

            var prefix = BuildProviderPrefix(providerId);
            var ids = new HashSet<EntryId>();

            foreach (var key in _records!.Keys)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                    ids.Add(new EntryId(key[prefix.Length..]));
            }

            return ids;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(ProviderId providerId, EntryId entryId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            if (_records!.Remove(BuildKey(providerId, entryId)))
                await SaveAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_records is not null)
            return;

        if (!File.Exists(_filePath))
        {
            _records = new Dictionary<string, StoredRecord>(StringComparer.Ordinal);
            return;
        }

        var json = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
        _records = JsonSerializer.Deserialize<Dictionary<string, StoredRecord>>(json)
            ?? new Dictionary<string, StoredRecord>(StringComparer.Ordinal);
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(_records, _jsonOptions);
        await File.WriteAllTextAsync(_filePath, json, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildProviderPrefix(ProviderId providerId) => providerId.Value + "\u001F";

    private static string BuildKey(ProviderId providerId, EntryId entryId) => BuildProviderPrefix(providerId) + entryId.Value;

    private sealed record StoredRecord(string? ETag, string Hash);
}

using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Rag.NET.Models;
using ZeroAlloc.Results;

namespace Rag.NET.DataProviders.Web;

public sealed class MySitemapDataProvider : IFileContentProvider
{
    private static readonly XNamespace s_ns = "http://www.sitemaps.org/schemas/sitemap/0.9";

    private readonly string _sitemapUrl;
    private readonly HttpClient _httpClient;
    private readonly string[] _excludedStartUrls;

    public MySitemapDataProvider(
        string sitemapUrl,
        HttpClient httpClient,
        IEnumerable<string>? excludedStartUrls = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sitemapUrl);

        _sitemapUrl = sitemapUrl;
        _httpClient = httpClient;
        _excludedStartUrls = (excludedStartUrls ?? [])
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .ToArray();
    }

    public async IAsyncEnumerable<Result<FileEntry, RagError>> GetFilesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var entry in LoadSitemapAsync(_sitemapUrl, cancellationToken).Take(99).ConfigureAwait(false))
        {
            yield return entry;
        }
    }

    private async IAsyncEnumerable<Result<FileEntry, RagError>> LoadSitemapAsync(
        string url,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var xml = await _httpClient.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        var root = XDocument.Parse(xml).Root!;

        if (string.Equals(root.Name.LocalName, "sitemapindex", StringComparison.Ordinal))
        {
            foreach (var sitemap in root.Elements(s_ns + "sitemap"))
            {
                var loc = sitemap.Element(s_ns + "loc")?.Value;
                if (loc is null || IsExcludedByStartUrl(loc))
                {
                    continue;
                }

                await foreach (var entry in LoadSitemapAsync(loc, cancellationToken).ConfigureAwait(false))
                {
                    yield return entry;
                }
            }
        }
        else
        {
            foreach (var urlEl in root.Elements(s_ns + "url"))
            {
                var loc = urlEl.Element(s_ns + "loc")?.Value;
                if (loc is null || IsExcludedByStartUrl(loc))
                {
                    continue;
                }

                var lastMod = urlEl.Element(s_ns + "lastmod")?.Value;
                var capturedLoc = loc;

                yield return Result<FileEntry, RagError>.Success(new FileEntry(
                    Id: new EntryId(loc),
                    FileName: InferFileName(loc),
                    OpenContentAsync: async ct =>
                    {
                        var response = await _httpClient.GetStreamAsync(capturedLoc, ct).ConfigureAwait(false);
                        var buffer = new MemoryStream();
                        await response.CopyToAsync(buffer, ct).ConfigureAwait(false);
                        await response.DisposeAsync().ConfigureAwait(false);
                        buffer.Position = 0;
                        return (Stream)buffer;
                    },
                    ETag: lastMod,
                    Metadata: BuildMetadata(loc, lastMod),
                    UpdatedAt: ConnectorTimestampParser.Parse(lastMod)));
            }
        }
    }

    /// <summary>
    /// Tags for a sitemap URL. <c>lastmod</c> is passed through verbatim — the sitemap protocol
    /// permits both a full W3C datetime and a bare date, and normalising would discard which
    /// precision the site actually published.
    /// </summary>
    private static Dictionary<string, MetadataValue>? BuildMetadata(string url, string? lastMod)
    {
        var metadata = new Dictionary<string, MetadataValue>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(url)) metadata["url"] = url;
        if (!string.IsNullOrEmpty(lastMod)) metadata["lastmod"] = lastMod;
        return metadata.Count == 0 ? null : metadata;
    }

    private bool IsExcludedByStartUrl(string url)
    {
        foreach (var excludedStartUrl in _excludedStartUrls)
        {
            if (url.StartsWith(excludedStartUrl, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string InferFileName(string url)
    {
        var path = new Uri(url).AbsolutePath;
        var segment = path.TrimEnd('/').Split('/').LastOrDefault() ?? "index";
        return string.IsNullOrEmpty(Path.GetExtension(segment)) ? segment + ".html" : segment;
    }
}

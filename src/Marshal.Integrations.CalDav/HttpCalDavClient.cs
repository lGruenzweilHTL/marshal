using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using Marshal.Abstractions;
using Marshal.Abstractions.Secrets;

namespace Marshal.Integrations.CalDav;

/// <summary>
/// <see cref="ICalDavClient"/> over HTTP with Basic auth. The password is read from the secret store per request, so a
/// rotated app password works without a restart. Hrefs are resolved against the URL that returned them (iCloud puts the
/// calendar home on another host).
/// </summary>
internal sealed class HttpCalDavClient(IHttpClientFactory clients, ISecretStore secrets) : ICalDavClient
{
    public const string HttpClientName = "caldav";

    private static readonly XNamespace Dav = "DAV:";
    private static readonly XNamespace CalDav = "urn:ietf:params:xml:ns:caldav";
    private static readonly HttpMethod Propfind = new("PROPFIND");
    private static readonly HttpMethod Report = new("REPORT");
    private static readonly string[] WritePrivileges = ["write", "write-content", "all"];

    public async Task<Result<CalDavDiscovery>> DiscoverAsync(CalDavAccount account, CancellationToken cancellationToken)
    {
        Result<XDocument> principalResponse = await SendXmlAsync(account, Propfind, account.Url, "0", """
            <?xml version="1.0" encoding="utf-8"?>
            <D:propfind xmlns:D="DAV:"><D:prop><D:current-user-principal/></D:prop></D:propfind>
            """, cancellationToken);
        if (principalResponse.IsFailure)
            return principalResponse.Error;
        Uri principal = Href(account.Url, principalResponse.Value.Descendants(Dav + "current-user-principal").Elements(Dav + "href").FirstOrDefault()) ?? account.Url;

        Result<XDocument> homeResponse = await SendXmlAsync(account, Propfind, principal, "0", """
            <?xml version="1.0" encoding="utf-8"?>
            <D:propfind xmlns:D="DAV:" xmlns:C="urn:ietf:params:xml:ns:caldav"><D:prop><C:calendar-home-set/><C:calendar-user-address-set/></D:prop></D:propfind>
            """, cancellationToken);
        if (homeResponse.IsFailure)
            return homeResponse.Error;
        Uri home = Href(principal, homeResponse.Value.Descendants(CalDav + "calendar-home-set").Elements(Dav + "href").FirstOrDefault()) ?? principal;
        List<string> addresses = [.. homeResponse.Value.Descendants(CalDav + "calendar-user-address-set").Elements(Dav + "href")
            .Select(href => href.Value.Trim())
            .Where(href => href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            .Select(href => href["mailto:".Length..])];

        Result<XDocument> collections = await SendXmlAsync(account, Propfind, home, "1", """
            <?xml version="1.0" encoding="utf-8"?>
            <D:propfind xmlns:D="DAV:" xmlns:C="urn:ietf:params:xml:ns:caldav"><D:prop><D:resourcetype/><D:displayname/><C:supported-calendar-component-set/><D:sync-token/><D:current-user-privilege-set/></D:prop></D:propfind>
            """, cancellationToken);
        if (collections.IsFailure)
            return collections.Error;

        List<CalDavCalendar> calendars = [.. collections.Value.Root!.Elements(Dav + "response").Select(response => Collection(home, response)).OfType<CalDavCalendar>()];
        return new CalDavDiscovery(principal, home, addresses, calendars);
    }

    public async Task<Result<IReadOnlyList<CalDavResource>>> QueryAsync(CalDavAccount account, Uri calendar, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken)
    {
        string body = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <C:calendar-query xmlns:D="DAV:" xmlns:C="urn:ietf:params:xml:ns:caldav"><D:prop><D:getetag/><C:calendar-data/></D:prop><C:filter><C:comp-filter name="VCALENDAR"><C:comp-filter name="VEVENT"><C:time-range start="{Utc(start)}" end="{Utc(end)}"/></C:comp-filter></C:comp-filter></C:filter></C:calendar-query>
            """;
        Result<XDocument> response = await SendXmlAsync(account, Report, calendar, "1", body, cancellationToken);
        if (response.IsFailure)
            return response.Error;

        return response.Value.Root!.Elements(Dav + "response")
            .Select(entry => (Href: Href(calendar, entry.Element(Dav + "href")), Prop: OkProp(entry)))
            .Where(entry => entry.Href is not null && entry.Prop?.Element(CalDav + "calendar-data") is not null)
            .Select(entry => new CalDavResource(entry.Href!, entry.Prop!.Element(Dav + "getetag")?.Value, entry.Prop.Element(CalDav + "calendar-data")!.Value))
            .ToList();
    }

    public async Task<Result<CalDavSyncResult>> SyncAsync(CalDavAccount account, Uri calendar, string? token, CancellationToken cancellationToken)
    {
        var request = new XDocument(new XElement(Dav + "sync-collection",
            new XAttribute(XNamespace.Xmlns + "D", Dav.NamespaceName),
            new XElement(Dav + "sync-token", token ?? ""),
            new XElement(Dav + "sync-level", "1"),
            new XElement(Dav + "prop", new XElement(Dav + "getetag"))));
        Result<XDocument> response = await SendXmlAsync(account, Report, calendar, "1", Declared(request), cancellationToken, syncReport: true);
        if (response.IsFailure)
            return response.Error;

        XElement root = response.Value.Root!;
        var changes = new List<CalDavChange>();
        foreach (XElement entry in root.Elements(Dav + "response"))
        {
            if (Href(calendar, entry.Element(Dav + "href")) is not { } href || href.AbsolutePath == calendar.AbsolutePath)
                continue;

            bool removed = entry.Element(Dav + "status")?.Value.Contains(" 404", StringComparison.Ordinal) == true;
            changes.Add(new CalDavChange(href, removed ? null : OkProp(entry)?.Element(Dav + "getetag")?.Value, removed));
        }

        return new CalDavSyncResult(root.Element(Dav + "sync-token")?.Value ?? token ?? "", changes);
    }

    public async Task<Result<IReadOnlyDictionary<Uri, string>>> ListETagsAsync(CalDavAccount account, Uri calendar, CancellationToken cancellationToken)
    {
        Result<XDocument> response = await SendXmlAsync(account, Propfind, calendar, "1", """
            <?xml version="1.0" encoding="utf-8"?>
            <D:propfind xmlns:D="DAV:"><D:prop><D:getetag/></D:prop></D:propfind>
            """, cancellationToken);
        if (response.IsFailure)
            return response.Error;

        var etags = new Dictionary<Uri, string>();
        foreach (XElement entry in response.Value.Root!.Elements(Dav + "response"))
        {
            if (Href(calendar, entry.Element(Dav + "href")) is { } href && href.AbsolutePath != calendar.AbsolutePath && OkProp(entry)?.Element(Dav + "getetag")?.Value is { Length: > 0 } etag)
                etags[href] = etag;
        }

        return etags;
    }

    public async Task<Result<CalDavResource?>> FindByUidAsync(CalDavAccount account, Uri calendar, string uid, CancellationToken cancellationToken)
    {
        var request = new XDocument(new XElement(CalDav + "calendar-query",
            new XAttribute(XNamespace.Xmlns + "D", Dav.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "C", CalDav.NamespaceName),
            new XElement(Dav + "prop", new XElement(Dav + "getetag"), new XElement(CalDav + "calendar-data")),
            new XElement(CalDav + "filter", new XElement(CalDav + "comp-filter", new XAttribute("name", "VCALENDAR"),
                new XElement(CalDav + "comp-filter", new XAttribute("name", "VEVENT"),
                    new XElement(CalDav + "prop-filter", new XAttribute("name", "UID"),
                        new XElement(CalDav + "text-match", new XAttribute("collation", "i;octet"), uid)))))));
        Result<XDocument> response = await SendXmlAsync(account, Report, calendar, "1", Declared(request), cancellationToken);
        if (response.IsFailure)
            return response.Error;

        return response.Value.Root!.Elements(Dav + "response")
            .Select(entry => (Href: Href(calendar, entry.Element(Dav + "href")), Prop: OkProp(entry)))
            .Where(entry => entry.Href is not null && entry.Prop?.Element(CalDav + "calendar-data") is not null)
            .Select(entry => new CalDavResource(entry.Href!, entry.Prop!.Element(Dav + "getetag")?.Value, entry.Prop.Element(CalDav + "calendar-data")!.Value))
            .FirstOrDefault();
    }

    public async Task<Result<CalDavResource>> GetAsync(CalDavAccount account, Uri resource, CancellationToken cancellationToken)
    {
        Result<HttpResponseMessage> sent = await SendAsync(account, HttpMethod.Get, resource, null, null, null, cancellationToken);
        if (sent.IsFailure)
            return sent.Error;

        using HttpResponseMessage response = sent.Value;
        if (response.StatusCode == HttpStatusCode.NotFound)
            return CalDavErrors.NotFound(resource);
        if (!response.IsSuccessStatusCode)
            return CalDavErrors.Failed($"GET {resource.AbsolutePath} returned {(int)response.StatusCode}.");

        return new CalDavResource(resource, response.Headers.ETag?.ToString(), await response.Content.ReadAsStringAsync(cancellationToken));
    }

    public async Task<Result<string?>> PutAsync(CalDavAccount account, Uri resource, string calendarData, string? ifMatch, CancellationToken cancellationToken)
    {
        var content = new StringContent(calendarData, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("text/calendar") { CharSet = "utf-8" };
        Result<HttpResponseMessage> sent = await SendAsync(account, HttpMethod.Put, resource, content, null, request =>
        {
            if (ifMatch is not null)
                request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
            else
                request.Headers.TryAddWithoutValidation("If-None-Match", "*");
        }, cancellationToken);
        if (sent.IsFailure)
            return sent.Error;

        using HttpResponseMessage response = sent.Value;
        if (response.StatusCode == HttpStatusCode.PreconditionFailed)
            return CalDavErrors.Conflict;
        if (!response.IsSuccessStatusCode)
            return CalDavErrors.Failed($"PUT {resource.AbsolutePath} returned {(int)response.StatusCode}.");

        return response.Headers.ETag?.ToString();
    }

    private async Task<Result<XDocument>> SendXmlAsync(CalDavAccount account, HttpMethod method, Uri url, string depth, string body, CancellationToken cancellationToken, bool syncReport = false)
    {
        var content = new StringContent(body, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/xml") { CharSet = "utf-8" };
        Result<HttpResponseMessage> sent = await SendAsync(account, method, url, content, depth, null, cancellationToken);
        if (sent.IsFailure)
            return sent.Error;

        using HttpResponseMessage response = sent.Value;
        string text = await response.Content.ReadAsStringAsync(cancellationToken);
        // RFC 6578 3.2: an unknown token is answered with the valid-sync-token precondition (403, some servers 409).
        if (syncReport && response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Conflict && text.Contains("valid-sync-token", StringComparison.Ordinal))
            return CalDavErrors.InvalidSyncToken;
        if ((int)response.StatusCode != 207 && !response.IsSuccessStatusCode)
            return CalDavErrors.Failed($"{method.Method} {url.AbsolutePath} returned {(int)response.StatusCode}.");

        try
        {
            return XDocument.Parse(text);
        }
        catch (System.Xml.XmlException)
        {
            return CalDavErrors.Failed($"{method.Method} {url.AbsolutePath} returned no valid XML.");
        }
    }

    private async Task<Result<HttpResponseMessage>> SendAsync(
        CalDavAccount account, HttpMethod method, Uri url, HttpContent? content, string? depth, Action<HttpRequestMessage>? configure, CancellationToken cancellationToken)
    {
        if (!SecretName.Create(account.PasswordSecret).IsSuccess || await secrets.GetAsync(SecretName.Create(account.PasswordSecret).Value, cancellationToken) is not { IsSuccess: true } password)
            return CalDavErrors.NoPassword(account.Id);

        using var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{account.Username}:{password.Value}")));
        if (depth is not null)
            request.Headers.TryAddWithoutValidation("Depth", depth);
        configure?.Invoke(request);

        try
        {
            HttpResponseMessage response = await clients.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized)
            {
                response.Dispose();
                return CalDavErrors.AuthFailed(account.Id);
            }

            return response;
        }
        catch (HttpRequestException exception)
        {
            return CalDavErrors.Failed($"{method.Method} {url.Host} failed: {exception.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return CalDavErrors.Failed($"{method.Method} {url.Host} timed out.");
        }
    }

    private static CalDavCalendar? Collection(Uri home, XElement response)
    {
        if (Href(home, response.Element(Dav + "href")) is not { } url || OkProp(response) is not { } prop)
            return null;
        if (prop.Element(Dav + "resourcetype")?.Element(CalDav + "calendar") is null)
            return null;

        // Without a component set the server allows every component (RFC 4791 5.2.3).
        XElement? components = prop.Element(CalDav + "supported-calendar-component-set");
        if (components is not null && components.HasElements && !components.Elements(CalDav + "comp").Any(comp => (string?)comp.Attribute("name") == "VEVENT"))
            return null;

        XElement? privileges = prop.Element(Dav + "current-user-privilege-set");
        bool canWrite = privileges is null || privileges.Elements(Dav + "privilege").SelectMany(privilege => privilege.Elements()).Any(privilege => privilege.Name.Namespace == Dav && WritePrivileges.Contains(privilege.Name.LocalName));
        string name = Uri.UnescapeDataString(url.AbsolutePath.TrimEnd('/').Split('/')[^1]);
        string display = prop.Element(Dav + "displayname")?.Value is { Length: > 0 } shown ? shown : name;
        return new CalDavCalendar(url, name, display, canWrite, prop.Element(Dav + "sync-token") is { Value.Length: > 0 });
    }

    /// <summary>The <c>prop</c> of the propstat with status 200, if any.</summary>
    private static XElement? OkProp(XElement response) =>
        response.Elements(Dav + "propstat").FirstOrDefault(propstat => propstat.Element(Dav + "status")?.Value.Contains(" 200", StringComparison.Ordinal) == true)?.Element(Dav + "prop");

    private static Uri? Href(Uri baseUrl, XElement? href) =>
        href is not null && Uri.TryCreate(baseUrl, href.Value.Trim(), out Uri? resolved) ? resolved : null;

    private static string Utc(DateTimeOffset time) => time.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    private static string Declared(XDocument document) => "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" + document.Root!.ToString(SaveOptions.DisableFormatting);
}

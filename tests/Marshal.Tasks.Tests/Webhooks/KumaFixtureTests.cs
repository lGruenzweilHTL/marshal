using System.Text.Json;

namespace Marshal.Tasks.Tests.Webhooks;

public sealed class KumaFixtureTests
{
    [Fact]
    public void Fixtures_LoadAndAllButInvalidAreJson()
    {
        foreach (string name in KumaFixtures.Names.Where(name => name != "invalid"))
            Assert.Equal(JsonValueKind.Object, JsonDocument.Parse(KumaFixtures.Text(name)).RootElement.ValueKind);
        Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse(KumaFixtures.Text("invalid")));

        (string contentType, string body) = KumaFixtures.Multipart();
        Assert.StartsWith("multipart/form-data; boundary=-", contentType, StringComparison.Ordinal);
        Assert.Contains("name=\"data\"", body, StringComparison.Ordinal);
    }
}

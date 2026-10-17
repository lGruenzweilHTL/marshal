using System.Text.Json;
using Marshal.Core.AiTasks;

namespace Marshal.Core.Tests.AiTasks;

public sealed class JsonSchemaSubsetValidatorTests
{
    private const string Doorbell = """
        {
          "type": "object",
          "properties": {
            "visitor": { "type": "string", "description": "Who is at the door" },
            "count": { "type": "integer" },
            "confidence": { "type": "number" },
            "parcel": { "type": "boolean" },
            "mood": { "type": "string", "enum": ["calm", "urgent"] },
            "tags": { "type": "array", "items": { "type": "string" } }
          },
          "required": ["visitor", "parcel"],
          "additionalProperties": false
        }
        """;

    [Fact]
    public void Valid_AllTypes_NoErrors()
    {
        Assert.Empty(Validate(Doorbell, """{"visitor":"courier","count":2,"confidence":0.8,"parcel":true,"mood":"calm","tags":["dhl"]}"""));
    }

    [Fact]
    public void Valid_OptionalPropertiesMissing_NoErrors()
    {
        Assert.Empty(Validate(Doorbell, """{"visitor":"courier","parcel":false}"""));
    }

    [Fact]
    public void Missing_RequiredProperty_Named()
    {
        Assert.Equal(["$.parcel: required property is missing"], Validate(Doorbell, """{"visitor":"courier"}"""));
    }

    [Theory]
    [InlineData("""{"visitor":3,"parcel":true}""", "$.visitor: expected string")]
    [InlineData("""{"visitor":"x","parcel":"yes"}""", "$.parcel: expected boolean")]
    [InlineData("""{"visitor":"x","parcel":true,"count":2.5}""", "$.count: expected integer")]
    [InlineData("""{"visitor":"x","parcel":true,"confidence":"high"}""", "$.confidence: expected number")]
    [InlineData("""{"visitor":"x","parcel":true,"mood":"angry"}""", "$.mood: must be one of calm, urgent")]
    [InlineData("""{"visitor":"x","parcel":true,"tags":"dhl"}""", "$.tags: expected array")]
    [InlineData("""{"visitor":"x","parcel":true,"tags":["dhl",4]}""", "$.tags[1]: expected string")]
    [InlineData("""["courier"]""", "$: expected object")]
    public void WrongType_ErrorWithPath(string instance, string expected)
    {
        Assert.Equal([expected], Validate(Doorbell, instance));
    }

    [Fact]
    public void Integer_WholeNumberWithFraction_Accepted()
    {
        Assert.Empty(Validate("""{"type":"integer"}""", "3.0"));
    }

    [Fact]
    public void Number_AcceptsIntegers()
    {
        Assert.Empty(Validate("""{"type":"number"}""", "3"));
    }

    [Fact]
    public void UnknownKeywords_Ignored()
    {
        Assert.Empty(Validate("""{"type":"string","format":"date","minLength":40,"x-ha":true}""", "\"2026-10-16\""));
    }

    [Fact]
    public void Enum_WithoutType_ComparesJsonValues()
    {
        Assert.Empty(Validate("""{"enum":[1,"two",true]}""", "1"));
        Assert.Equal(["$: must be one of 1, two, true"], Validate("""{"enum":[1,"two",true]}""", "2"));
    }

    [Fact]
    public void NestedObjectsAndArrays_PathsAccumulate()
    {
        const string schema = """
            {"type":"object","properties":{"people":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}}}}
            """;

        Assert.Equal(["$.people[1].name: required property is missing"], Validate(schema, """{"people":[{"name":"Ann"},{}]}"""));
    }

    [Fact]
    public void SeveralErrors_AllReported()
    {
        Assert.Equal(
            ["$.visitor: required property is missing", "$.parcel: required property is missing", "$.count: expected integer"],
            Validate(Doorbell, """{"count":"x"}"""));
    }

    [Fact]
    public void TypeList_AnyMatches()
    {
        Assert.Empty(Validate("""{"type":["string","null"]}""", "null"));
        Assert.Equal(["$: expected string or null"], Validate("""{"type":["string","null"]}""", "1"));
    }

    private static IReadOnlyList<string> Validate(string schema, string instance)
    {
        using var schemaDocument = JsonDocument.Parse(schema);
        using var instanceDocument = JsonDocument.Parse(instance);
        return JsonSchemaSubsetValidator.Validate(schemaDocument.RootElement, instanceDocument.RootElement);
    }
}

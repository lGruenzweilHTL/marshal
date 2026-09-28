using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Memory;
using Marshal.Testing;

namespace Marshal.Memory.Tests;

public sealed class ChunkIdsTests
{
    private static readonly OwnerId Owner = TestExecutionContext.OwnerA;
    private static readonly ProjectId Voron = ProjectId.From(Guid.Parse("00000000-0000-7000-8000-0000000000f1"));

    [Theory]
    [InlineData("Voron nozzle size", "voron nozzle size")]
    [InlineData("  The   Voron's Nozzle-Size! ", "the vorons nozzlesize")]
    [InlineData("Ｖｏｒｏｎ ﬁlament", "voron filament")]
    [InlineData("Größe\tDüse", "größe düse")]
    public void Normalize_NfkcLowercaseNoPunctuationSingleSpaces(string title, string normalized) =>
        Assert.Equal(normalized, ChunkIds.Normalize(title));

    [Fact]
    public void Compute_SameIdentityDifferentSpelling_SameId()
    {
        ChunkId first = ChunkIds.Compute(Owner, ChunkScope.General, null, ChunkType.Device, "Voron nozzle size");
        ChunkId second = ChunkIds.Compute(Owner, ChunkScope.General, null, ChunkType.Device, "  voron NOZZLE size. ");

        Assert.Equal(first, second);
        Assert.Matches("^[a-z2-7]{26}$", first.Value);
    }

    [Fact]
    public void Compute_OwnerScopeProjectOrType_Separate()
    {
        ChunkId general = ChunkIds.Compute(Owner, ChunkScope.General, null, ChunkType.Device, "Nozzle size");

        ChunkId[] others =
        [
            ChunkIds.Compute(TestExecutionContext.OwnerB, ChunkScope.General, null, ChunkType.Device, "Nozzle size"),
            ChunkIds.Compute(Owner, ChunkScope.Project, Voron, ChunkType.Device, "Nozzle size"),
            ChunkIds.Compute(Owner, ChunkScope.Project, ProjectId.New(), ChunkType.Device, "Nozzle size"),
            ChunkIds.Compute(Owner, ChunkScope.General, null, ChunkType.Fact, "Nozzle size"),
        ];

        Assert.Equal(5, others.Append(general).Distinct().Count());
    }

    [Fact]
    public void Compute_ProjectIdentityOnly_NameNeverEnters()
    {
        // The project is identified by its stable id, so renaming it cannot change chunk ids.
        Assert.Equal(
            ChunkIds.Compute(Owner, ChunkScope.Project, Voron, ChunkType.Procedure, "Calibrate z offset"),
            ChunkIds.Compute(Owner, ChunkScope.Project, ProjectId.From(Voron.Value), ChunkType.Procedure, "Calibrate z offset"));
    }

    [Fact]
    public void Compute_ScopeAndProjectMismatch_Throws()
    {
        Assert.Throws<ArgumentException>(() => ChunkIds.Compute(Owner, ChunkScope.Project, null, ChunkType.Fact, "x"));
        Assert.Throws<ArgumentException>(() => ChunkIds.Compute(Owner, ChunkScope.General, Voron, ChunkType.Fact, "x"));
    }

    [Theory]
    [InlineData("abcdefghijklmnopqrstuvwxyz", true)]
    [InlineData("abcdefghijklmnopqrstuvwxy1", false)]
    [InlineData("short", false)]
    public void ChunkId_Parse_ValidatesFormat(string value, bool valid) =>
        Assert.Equal(valid, ChunkId.TryParse(value, null, out _));
}

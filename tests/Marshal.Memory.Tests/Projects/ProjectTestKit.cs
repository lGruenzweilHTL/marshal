using Marshal.Memory.Projects;
using Marshal.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Marshal.Memory.Tests.Projects;

/// <summary>
/// Builds project activation services over a <see cref="TestDatabase"/>.
/// </summary>
internal static class ProjectTestKit
{
    public static ProjectLearning Learning(TestDatabase database, MemoryOptions options, FakeTimeProvider time) =>
        new(database.Factory, database.Accessor, Microsoft.Extensions.Options.Options.Create(options), time);

    public static ProjectActivator Activator(TestDatabase database, FakeEmbeddingGenerator embeddings, ProjectSessions sessions, MemoryOptions options, FakeTimeProvider time) =>
        new(database.Factory, embeddings, sessions, Learning(database, options, time), Microsoft.Extensions.Options.Options.Create(options), time);
}

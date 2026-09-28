using Marshal.Abstractions.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Marshal.Data.Persistence;

/// <summary>
/// Used by <c>dotnet ef</c> to create migrations. Never used at runtime.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<MarshalDbContext>
{
    public MarshalDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<MarshalDbContext>().UseMarshalSqlite("Data Source=marshal.db").Options, new NoExecutionContext());

    private sealed class NoExecutionContext : IExecutionContextAccessor
    {
        public MarshalExecutionContext? Current => null;

        public IDisposable Use(MarshalExecutionContext context) => throw new NotSupportedException();
    }
}

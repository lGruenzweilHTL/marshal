using Marshal.Abstractions;
using Marshal.Abstractions.Identity;
using Marshal.Demo.Seeding;

namespace Marshal.Api.Cli;

/// <summary>
/// <c>marshal demo reset | seed</c>: removes the seeded demo data of the instance owner, or seeds it again. Seeding
/// needs <c>demo.enabled</c>; reset also works after demo mode was turned off. Both are audited with actor kind
/// <c>system</c> by the seeder and the reset.
/// </summary>
internal sealed class DemoCommand(IDemoSeeder seeder, IDemoReset reset, IInstanceOwner instanceOwner, bool demoEnabled, TextWriter output, TextWriter error)
{
    private const string Usage = """
        Usage:
          marshal demo reset    removes the seeded demo data (rows created during the demo stay)
          marshal demo seed     seeds the demo data again (needs demo.enabled and no seeded data)
        """;

    public async Task<int> RunAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        switch (args)
        {
            case ["reset"]:
            {
                int deleted = await reset.ResetAsync(await instanceOwner.GetAsync(ct), ct);
                await output.WriteLineAsync($"Removed {deleted} seeded demo rows.");
                return 0;
            }

            case ["seed"] when !demoEnabled:
                await error.WriteLineAsync("Demo mode is off; set demo.enabled: true in marshal.yaml first.");
                return 1;

            case ["seed"]:
            {
                Result<int> seeded = await seeder.SeedAsync(await instanceOwner.GetAsync(ct), ct);
                if (seeded.IsFailure)
                {
                    await error.WriteLineAsync(seeded.Error.Message);
                    return 1;
                }

                await output.WriteLineAsync($"Seeded {seeded.Value} demo rows.");
                return 0;
            }

            default:
                await error.WriteLineAsync(Usage);
                return 2;
        }
    }
}

using System.Collections;

namespace Marshal.Api.Hosting;

/// <summary>
/// Runs the web host until shutdown. With a report writer, a failed start is reported there (see
/// <see cref="StartupReport"/>) and returns exit code 1; without one, the failure propagates to the caller.
/// </summary>
internal static class MarshalHost
{
    public static async Task<int> RunAsync(string[] args, IDictionary environment, TextWriter? report)
    {
        WebApplication? app = null;
        try
        {
            app = MarshalApp.Create(args, environment);
            ConfigurationStage.Ensure(app, environment);
            await app.StartAsync();
        }
        catch (Exception error) when (report is not null)
        {
            StartupReport.Write(report, error);
            if (app is not null)
                await app.DisposeAsync();

            return 1;
        }

        await using (app)
        {
            await app.WaitForShutdownAsync();
        }

        return 0;
    }
}

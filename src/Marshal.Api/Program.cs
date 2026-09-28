using Marshal.Api.Cli;
using Marshal.Api.Hosting;

System.Collections.IDictionary environment = Environment.GetEnvironmentVariables();
if (MarshalCli.IsCommand(args))
    return await MarshalCli.RunAsync(args, environment);

WebApplication app = MarshalApp.Create(args, environment);
await app.RunAsync();
return 0;

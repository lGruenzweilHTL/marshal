using System.Reflection;
using Marshal.Api.Cli;
using Marshal.Api.Hosting;

System.Collections.IDictionary environment = Environment.GetEnvironmentVariables();
if (MarshalCli.IsCommand(args))
    return await MarshalCli.RunAsync(args, environment);
if (OpenApiExport.IsRequested(args))
    return await OpenApiExport.RunAsync(args, environment);

// WebApplicationFactory runs Main in-process and observes a failed start through the exception, not the report.
TextWriter? report = Assembly.GetEntryAssembly() == typeof(MarshalApp).Assembly ? Console.Error : null;
return await MarshalHost.RunAsync(args, environment, report);

using System.Collections;

namespace Marshal.Api.Hosting;

/// <summary>The environment variables the host started with, so startup errors can name the variable that set a key.</summary>
internal sealed record ProcessEnvironment(IDictionary Variables);

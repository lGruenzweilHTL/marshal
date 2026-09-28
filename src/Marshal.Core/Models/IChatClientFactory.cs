using Microsoft.Extensions.AI;

namespace Marshal.Core.Models;

/// <summary>
/// Creates provider clients. Implemented by the host, the only project that references provider SDKs.
/// </summary>
public interface IChatClientFactory
{
    IChatClient Create(string key, ModelOptions options);
}

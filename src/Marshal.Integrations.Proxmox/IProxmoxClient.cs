using Marshal.Abstractions;

namespace Marshal.Integrations.Proxmox;

/// <summary>Read-only Proxmox VE API: only <c>GET</c> requests exist. Failures use <c>infra.unavailable</c>.</summary>
public interface IProxmoxClient
{
    /// <summary>Nodes, guests and storages of the cluster (<c>/cluster/resources</c>).</summary>
    Task<Result<ProxmoxResources>> GetResourcesAsync(CancellationToken cancellationToken);

    /// <summary>Recent cluster tasks (<c>/cluster/tasks</c>), as PVE returns them.</summary>
    Task<Result<IReadOnlyList<ProxmoxTask>>> GetTasksAsync(CancellationToken cancellationToken);
}

/// <summary>Everything <c>/cluster/resources</c> lists.</summary>
public sealed record ProxmoxResources(IReadOnlyList<ProxmoxNode> Nodes, IReadOnlyList<ProxmoxGuest> Guests, IReadOnlyList<ProxmoxStorage> Storages);

/// <summary>A cluster node; CPU as a fraction of all cores, memory in bytes, uptime in seconds.</summary>
public sealed record ProxmoxNode(string Name, bool Online, double Cpu, long Memory, long MaxMemory, long Uptime);

/// <summary>A VM (<c>qemu</c>) or container (<c>lxc</c>); CPU as a fraction of its cores, memory in bytes.</summary>
public sealed record ProxmoxGuest(int VmId, string Name, string Type, string Node, string Status, double Cpu, long Memory, long MaxMemory, long Uptime, bool Template);

/// <summary>A storage on a node; sizes in bytes.</summary>
public sealed record ProxmoxStorage(string Name, string Node, string Type, long Used, long Total, bool Shared);

/// <summary>A cluster task; <paramref name="End"/> and <paramref name="Status"/> are null while it runs.</summary>
public sealed record ProxmoxTask(string Upid, string Type, string? GuestId, string Node, string User, DateTimeOffset Start, DateTimeOffset? End, string? Status);

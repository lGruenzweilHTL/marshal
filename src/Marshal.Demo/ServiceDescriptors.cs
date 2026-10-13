using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Marshal.Demo;

/// <summary>Finds and removes the registrations an integration's own extension method added.</summary>
internal static class ServiceDescriptors
{
    /// <summary>Removes the non-keyed registrations of <paramref name="serviceType"/> that come from <paramref name="assembly"/>.</summary>
    public static IServiceCollection RemoveFrom(this IServiceCollection services, Type serviceType, Assembly assembly)
    {
        foreach (ServiceDescriptor descriptor in services.Where(descriptor => descriptor.ServiceType == serviceType && !descriptor.IsKeyedService && Origin(descriptor) == assembly).ToList())
            services.Remove(descriptor);
        return services;
    }

    /// <summary>Removes the non-keyed registrations of <paramref name="serviceType"/> implemented by <paramref name="implementation"/>.</summary>
    public static IServiceCollection RemoveImplementation(this IServiceCollection services, Type serviceType, Type implementation)
    {
        foreach (ServiceDescriptor descriptor in services.Where(descriptor => descriptor.ServiceType == serviceType && !descriptor.IsKeyedService && descriptor.ImplementationType == implementation).ToList())
            services.Remove(descriptor);
        return services;
    }

    /// <summary>
    /// Wraps the last non-keyed registration of <typeparamref name="TService"/>: the original moves to a keyed
    /// registration, whose key is returned, and <paramref name="decorate"/> builds the service from it.
    /// </summary>
    public static object Decorate<TService>(this IServiceCollection services, Func<IServiceProvider, TService, TService> decorate)
        where TService : class
    {
        ServiceDescriptor original = services.Last(descriptor => descriptor.ServiceType == typeof(TService) && !descriptor.IsKeyedService);
        object key = new();
        services.Remove(original);
        services.Add(original switch
        {
            { ImplementationInstance: { } instance } => new ServiceDescriptor(typeof(TService), key, instance),
            { ImplementationType: { } type } => new ServiceDescriptor(typeof(TService), key, type, original.Lifetime),
            _ => new ServiceDescriptor(typeof(TService), key, (provider, _) => original.ImplementationFactory!(provider), original.Lifetime),
        });
        services.Add(new ServiceDescriptor(typeof(TService), provider => decorate(provider, provider.GetRequiredKeyedService<TService>(key)), original.Lifetime));
        return key;
    }

    private static Assembly? Origin(ServiceDescriptor descriptor) =>
        descriptor.ImplementationType?.Assembly
        ?? descriptor.ImplementationInstance?.GetType().Assembly
        ?? descriptor.ImplementationFactory?.Method.DeclaringType?.Assembly;
}

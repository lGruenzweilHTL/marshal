using Marshal.Abstractions.Extraction;
using Marshal.Abstractions.Notes;
using Marshal.Abstractions.Tools;
using Marshal.Integrations.Notes.Index;
using Marshal.Integrations.Notes.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Marshal.Integrations.Notes;

/// <summary>
/// Registers the notes integration (<c>notes</c> in <c>marshal.yaml</c>): the git-backed source in
/// the notes directory (<c>/data/notes</c>), its own index, the background pull and the notes tools.
/// Deliberately no context contributor: notes enter a run only through the tools.
/// </summary>
public static class NotesServiceCollectionExtensions
{
    public static IServiceCollection AddMarshalNotes(this IServiceCollection services, IConfiguration configuration, Func<IServiceProvider, string> notesDirectory)
    {
        services.AddOptions<NotesOptions>().Bind(configuration.GetSection("notes"))
            .Validate(options => options.PullInterval >= TimeSpan.FromMinutes(1), "notes.pullInterval must be at least one minute.")
            .Validate(options => options.MaxReadCharacters >= 1000, "notes.maxReadCharacters must be at least 1000.")
            .ValidateOnStart();

        services.AddSingleton(provider => new NotesDirectory(notesDirectory(provider)));
        services.AddSingleton<INotesSource, GitNotesSource>();
        services.AddSingleton<INotesIndex, SqliteNotesIndex>();
        services.AddSingleton<NotesSyncService>();
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<NotesSyncService>());
        services.AddSingleton<IToolSource, NotesToolSource>();
        services.AddSingleton<IUntrustedContentResolver, NoteContentResolver>();
        return services;
    }
}

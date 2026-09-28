namespace Marshal.Abstractions.Configuration;

/// <summary>A <c>/config</c> file (or an entry in it) that was rejected on the last load.</summary>
/// <param name="File">File name relative to its config directory.</param>
/// <param name="Message">What is wrong; never contains secret values.</param>
public sealed record ConfigFileError(string File, string Message);

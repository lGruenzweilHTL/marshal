using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace Marshal.Integrations.Mail.Imap;

/// <summary>
/// Opaque message ids for IMAP: folder (base64url), UIDVALIDITY and UID. A changed UIDVALIDITY makes old ids stale.
/// </summary>
internal static class ImapMessageIds
{
    public static string Format(string folder, uint uidValidity, uint uid) =>
        string.Create(CultureInfo.InvariantCulture, $"{Base64Url.EncodeToString(Encoding.UTF8.GetBytes(folder))}.{uidValidity}.{uid}");

    public static bool TryParse(string id, out string folder, out uint uidValidity, out uint uid)
    {
        folder = "";
        uidValidity = 0;
        uid = 0;
        string[] parts = id.Split('.');
        if (parts.Length != 3
            || !uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out uidValidity)
            || !uint.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out uid)
            || uid == 0)
        {
            return false;
        }

        try
        {
            folder = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(parts[0]));
            return folder.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

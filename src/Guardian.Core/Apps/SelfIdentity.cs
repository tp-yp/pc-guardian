namespace Guardian.Core.Apps;

/// <summary>
/// Recognizes Geder's own program files (also a new version that is not installed yet, e.g. an update
/// downloaded into a standard account's Downloads folder), so they are never blocked. Matched by the
/// product and original file names written inside the file, which renaming the file does not change.
/// </summary>
public static class SelfIdentity
{
    private static readonly string[] Products = ["geder", "guardian"];
    private static readonly string[] Originals = ["geder.dll", "guardian.dll", "geder.exe", "guardian.exe"];

    public static bool IsGeder(string? originalName, string? productName) =>
        originalName is not null && productName is not null &&
        Originals.Contains(originalName.Trim(), StringComparer.OrdinalIgnoreCase) &&
        Products.Contains(productName.Trim(), StringComparer.OrdinalIgnoreCase);
}

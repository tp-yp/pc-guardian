using System.Security.Cryptography;

namespace Guardian.Core.Naming;

/// <summary>Generates a neutral-looking service name, e.g. "SysHostSync4821".</summary>
public static class ServiceNameGenerator
{
    private static readonly string[] A = ["Sys", "Host", "Core", "Net", "Data", "Task", "Svc", "Win"];
    private static readonly string[] B = ["Sync", "Broker", "Agent", "Monitor", "Helper", "Update", "Cache", "Policy"];

    public static string Generate() =>
        A[RandomNumberGenerator.GetInt32(A.Length)] + B[RandomNumberGenerator.GetInt32(B.Length)] +
        A[RandomNumberGenerator.GetInt32(A.Length)] + RandomNumberGenerator.GetInt32(1000, 10000);

    /// <summary>Windows service names: 1-256 chars, no '/' or '\'. We restrict to letters/digits.</summary>
    public static bool IsValid(string name) =>
        name.Length is > 0 and <= 80 && name.All(char.IsAsciiLetterOrDigit);
}

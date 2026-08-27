using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

public static class FileHashUtility
{
    public static string ComputeSha256(string filePath)
    {
        using FileStream stream = File.OpenRead(filePath);
        using SHA256 sha256 = SHA256.Create();
        return ToHex(sha256.ComputeHash(stream));
    }

    public static string ComputeSha256(byte[] bytes)
    {
        using SHA256 sha256 = SHA256.Create();
        return ToHex(sha256.ComputeHash(bytes));
    }

    public static bool Matches(string filePath, long expectedSize, string expectedSha256)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        FileInfo fileInfo = new FileInfo(filePath);

        if (fileInfo.Length != expectedSize)
        {
            return false;
        }

        return string.Equals(ComputeSha256(filePath), expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    private static string ToHex(byte[] bytes)
    {
        StringBuilder builder = new StringBuilder(bytes.Length * 2);

        foreach (byte value in bytes)
        {
            builder.Append(value.ToString("x2"));
        }

        return builder.ToString();
    }
}

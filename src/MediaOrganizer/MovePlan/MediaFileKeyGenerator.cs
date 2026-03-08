using System.Security.Cryptography;
using System.Text;

namespace MediaOrganizer.MovePlan;

/// <summary>
/// Generates unique keys for media files based on their file properties.
/// The key is a hash of the original file path and file size to ensure uniqueness.
/// </summary>
public class MediaFileKeyGenerator
{
    /// <summary>
    /// Generates a unique key for a media file based on its original path and size.
    /// </summary>
    /// <param name="filePath">The full file path</param>
    /// <returns>A unique key as a hex string</returns>
    public string GenerateKey(string filePath)
    {
        // Combine file path and size for uniqueness
        var keyData = $"{filePath}";

        using (var sha256 = SHA256.Create())
        {
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(keyData));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}

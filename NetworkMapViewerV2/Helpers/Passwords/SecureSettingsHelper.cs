using System.Security.Cryptography;
using System.Text;

namespace NetworkMapViewerV2.Helpers.Passwords
{
    public static class SecureSettingsHelper
    {
        /// <summary>
        /// Encrypts a plain text password and returns a safe Base64 string to store in settings.json.
        /// </summary>
        public static string? ProtectPassword(string? plainTextPassword)
        {
            if (string.IsNullOrEmpty(plainTextPassword))
                return null;

            // Convert the string to a byte array
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainTextPassword);

            // Encrypt the data. DataProtectionScope.CurrentUser ensures only the 
            // logged-in Windows user can decrypt it.
            byte[] encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);

            // Return as a clean Base64 string for your JSON file
            return Convert.ToBase64String(encryptedBytes);
        }

        /// <summary>
        /// Reads the Base64 string from settings.json and decrypts it back to plain text.
        /// </summary>
        public static string? UnprotectPassword(string? encryptedBase64)
        {
            if (string.IsNullOrEmpty(encryptedBase64))
                return null;

            try
            {
                // Convert the Base64 string back to encrypted bytes
                byte[] encryptedBytes = Convert.FromBase64String(encryptedBase64);

                // Decrypt the bytes
                byte[] plainBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);

                // Convert back to a readable string
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (CryptographicException)
            {
                // This triggers if someone copies settings.json to another computer
                // or tries to open it under a different Windows user account.
                Console.WriteLine("Decryption failed. The file was moved or user context changed.");
                return null;
            }
        }
    }
}
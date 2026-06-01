using System.IO;
using System.Security.Cryptography;

namespace NetworkMapViewerV2.Helpers.Passwords
{
    public static class SecureSettingsHelper
    {
        // A 32-byte key and 16-byte IV hidden in the compiled code.
        // (You can change these random bytes to anything you want, just keep the lengths 32 and 16!)
        private static readonly byte[] Key = [12, 45, 88, 221, 5, 99, 14, 76, 201, 10, 44, 7, 88, 120, 50, 4, 18, 9, 33, 44, 55, 66, 77, 88, 99, 11, 22, 33, 44, 55, 66, 77];
        private static readonly byte[] IV = [88, 11, 22, 33, 44, 55, 66, 77, 88, 99, 11, 22, 33, 44, 55, 66];

        /// <summary>
        /// Encrypts a plain text password using AES-256 for safe network-share storage.
        /// </summary>
        public static string? ProtectPassword(string? plainTextPassword)
        {
            if (string.IsNullOrEmpty(plainTextPassword)) return null;

            using Aes aes = Aes.Create();
            aes.Key = Key;
            aes.IV = IV;

            ICryptoTransform encryptor = aes.CreateEncryptor(aes.Key, aes.IV);

            using MemoryStream ms = new();
            using CryptoStream cs = new(ms, encryptor, CryptoStreamMode.Write);
            using (StreamWriter sw = new(cs))
            {
                sw.Write(plainTextPassword);
            }

            return Convert.ToBase64String(ms.ToArray());
        }

        /// <summary>
        /// Decrypts the Base64 string back to plain text. Works across all computers.
        /// </summary>
        public static string? UnprotectPassword(string? encryptedBase64)
        {
            if (string.IsNullOrEmpty(encryptedBase64)) return null;

            try
            {
                byte[] cipherBytes = Convert.FromBase64String(encryptedBase64);

                using Aes aes = Aes.Create();
                aes.Key = Key;
                aes.IV = IV;

                ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, aes.IV);

                using MemoryStream ms = new(cipherBytes);
                using CryptoStream cs = new(ms, decryptor, CryptoStreamMode.Read);
                using StreamReader sr = new(cs);

                return sr.ReadToEnd();
            }
            catch (Exception)
            {
                // Triggers if the string is completely malformed or manually edited
                return null;
            }
        }
    }
}
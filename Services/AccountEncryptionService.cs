using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Lvchaxs_ZH.Services
{
    public static class AccountEncryptionService
    {
        private static readonly byte[] FixedKey = Encoding.UTF8.GetBytes("GenshinAccountManager2024SecureKey1234567890");
        private static readonly byte[] Salt = new byte[] {
            0x47, 0x65, 0x6e, 0x73, 0x68, 0x69, 0x6e, 0x49,
            0x6d, 0x70, 0x61, 0x63, 0x74, 0x33, 0x2e, 0x30
        };

        private static byte[] _cachedKey = null;

        private static byte[] GetEncryptionKey()
        {
            return _cachedKey ??= GenerateEncryptionKey();
        }

        private static byte[] GenerateEncryptionKey()
        {
            using var deriveBytes = new Rfc2898DeriveBytes(FixedKey, Salt, 100000, HashAlgorithmName.SHA256);
            return deriveBytes.GetBytes(32);
        }

        public static string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return plainText;

            try
            {
                using var aes = Aes.Create();
                aes.Key = GetEncryptionKey();
                aes.GenerateIV();
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                byte[] encryptedBytes;
                using (var ms = new MemoryStream())
                {
                    ms.Write(aes.IV, 0, aes.IV.Length);
                    using (var encryptor = aes.CreateEncryptor())
                    using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                    using (var sw = new StreamWriter(cs, Encoding.UTF8))
                    {
                        sw.Write(plainText);
                    }
                    encryptedBytes = ms.ToArray();
                }

                return Convert.ToBase64String(encryptedBytes);
            }
            catch (Exception ex)
            {
                LogError($"加密失败: {ex.Message}");
                return CreateEncryptionFailedMarker(plainText);
            }
        }

        public static string Decrypt(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText))
                return cipherText;

            if (IsEncryptionFailedMarker(cipherText, out string base64Data))
            {
                return DecodeBase64(base64Data);
            }

            try
            {
                byte[] fullCipher = Convert.FromBase64String(cipherText);
                byte[] iv = new byte[16];
                byte[] cipher = new byte[fullCipher.Length - 16];

                Buffer.BlockCopy(fullCipher, 0, iv, 0, iv.Length);
                Buffer.BlockCopy(fullCipher, iv.Length, cipher, 0, cipher.Length);

                using var aes = Aes.Create();
                aes.Key = GetEncryptionKey();
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using var decryptor = aes.CreateDecryptor();
                using var ms = new MemoryStream(cipher);
                using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
                using var sr = new StreamReader(cs, Encoding.UTF8);

                return sr.ReadToEnd();
            }
            catch
            {
                return cipherText;
            }
        }

        public static bool IsEncrypted(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            if (IsInvalidBase64(text))
                return false;

            try
            {
                byte[] data = Convert.FromBase64String(text);
                return data.Length >= 32 && data.Length % 16 == 0;
            }
            catch
            {
                return false;
            }
        }

        public static string Obfuscate(string text, bool isPassword = false)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            if (isPassword)
                return new string('●', Math.Min(text.Length, 12));

            if (text.Length <= 4)
                return "****";

            if (text.Contains("@"))
            {
                var parts = text.Split('@');
                if (parts.Length == 2 && parts[0].Length > 3)
                    return $"{parts[0].Substring(0, 3)}●●●●@{parts[1]}";
            }

            if (text.Length > 8)
                return $"{text.Substring(0, 4)}●●●●{text.Substring(text.Length - 4)}";

            return new string('●', Math.Min(text.Length, 8));
        }

        // 辅助方法
        private static string CreateEncryptionFailedMarker(string plainText)
        {
            string base64Data = Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText));
            return $"[ENCRYPTION_FAILED:{base64Data}]";
        }

        private static bool IsEncryptionFailedMarker(string text, out string base64Data)
        {
            base64Data = string.Empty;

            if (text.StartsWith("[ENCRYPTION_FAILED:") && text.EndsWith("]"))
            {
                base64Data = text.Substring(19, text.Length - 20);
                return true;
            }

            return false;
        }

        private static string DecodeBase64(string base64Data)
        {
            try
            {
                byte[] bytes = Convert.FromBase64String(base64Data);
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return base64Data;
            }
        }

        private static bool IsInvalidBase64(string text)
        {
            return text.Length % 4 != 0 ||
                   text.Contains(" ") ||
                   text.Contains("\t") ||
                   text.Contains("\r") ||
                   text.Contains("\n");
        }

        private static void LogError(string message)
        {
            System.Diagnostics.Debug.WriteLine(message);
        }
    }
}
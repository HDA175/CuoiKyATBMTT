using System;
using System.Configuration;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace CuoiKy.Utils
{
    /// <summary>
    /// SecurityHelper - Các tiện ích bảo mật cho môn An toàn và Bảo mật thông tin
    /// - Password hashing (PBKDF2 - tương đương BCrypt về độ an toàn)
    /// - SQL Injection input validation
    /// </summary>
    public static class SecurityHelper
    {
        #region Password Hashing - Bảo mật mật khẩu (PBKDF2)
        private const int SaltSize = 16;
        private const int HashSize = 32;
        private const int Iterations = 10000;
        private const string Prefix = "$pbkdf2$";

        /// <summary>
        /// Hash mật khẩu bằng PBKDF2 - Không lưu password dạng text trong database
        /// </summary>
        public static string HashPassword(string plainPassword)
        {
            if (string.IsNullOrEmpty(plainPassword))
                throw new ArgumentNullException(nameof(plainPassword));
            var salt = new byte[SaltSize];
            using (var rng = new RNGCryptoServiceProvider())
                rng.GetBytes(salt);
            var hash = PBKDF2(plainPassword, salt, Iterations);
            return Prefix + Convert.ToBase64String(salt) + "$" + Convert.ToBase64String(hash);
        }

        /// <summary>
        /// Xác minh mật khẩu với hash đã lưu
        /// </summary>
        public static bool VerifyPassword(string plainPassword, string hashedPassword)
        {
            if (string.IsNullOrEmpty(plainPassword) || string.IsNullOrEmpty(hashedPassword))
                return false;
            try
            {
                if (hashedPassword.StartsWith(Prefix))
                {
                    var parts = hashedPassword.Substring(Prefix.Length).Split('$');
                    if (parts.Length != 2) return false;
                    var salt = Convert.FromBase64String(parts[0]);
                    var hash = Convert.FromBase64String(parts[1]);
                    var testHash = PBKDF2(plainPassword, salt, Iterations);
                    return ConstantTimeEquals(hash, testHash);
                }
                if (hashedPassword.StartsWith("$2"))
                    return false;
                return plainPassword == hashedPassword;
            }
            catch { return false; }
        }

        private static byte[] PBKDF2(string password, byte[] salt, int iterations)
        {
            using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations))
                return pbkdf2.GetBytes(HashSize);
        }

        private static bool ConstantTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        /// <summary>
        /// Kiểm tra xem chuỗi có phải hash mật khẩu không (BCrypt $2 hoặc PBKDF2 $pbkdf2)
        /// </summary>
        public static bool IsBcryptHash(string value)
        {
            return !string.IsNullOrEmpty(value) && (value.StartsWith("$2") || value.StartsWith(Prefix));
        }
        #endregion

        #region SQL Injection - Chống tấn công SQL Injection
        /// <summary>
        /// Các pattern nguy hiểm SQL Injection phổ biến
        /// </summary>
        private static readonly string[] SqlInjectionPatterns = new[]
        {
            @"('\s*OR\s*1\s*=\s*1|'\s*OR\s*'1'\s*=\s*'1)",  // ' OR 1=1 --
            @"('\s*;\s*--|\;\s*--|--\s*$)",                 // ; -- hoặc --
            @"(\bUNION\s+ALL\s+SELECT|\bUNION\s+SELECT)",   // UNION SELECT
            @"(\bDROP\s+TABLE|\bDELETE\s+FROM)",            // DROP/DELETE
            @"(\bEXEC\s*\(|\bEXECUTE\s*\()",               // EXEC
            @"(0x[0-9a-fA-F]+)",                            // Hex injection
            @"(\bINSERT\s+INTO|\bUPDATE\s+SET)",            // INSERT/UPDATE
            @"('\s*OR\s*'\w+'\s*=\s*')",                    // ' OR 'x'='x
            @"(\bSELECT\s+\*\s+FROM)",                      // SELECT * FROM
            @"(\bOR\s+1\s*=\s*1\s*--)",
            @"(\/\*|\*\/)",                                  // SQL comments
            @"(\bCHAR\s*\(|\bCONCAT\s*\()",                 // CHAR/CONCAT
            @"(\bBENCHMARK\s*\(|\bSLEEP\s*\()",             // Time-based injection
        };

        /// <summary>
        /// Kiểm tra input có chứa dấu hiệu SQL Injection hay không
        /// </summary>
        public static bool ContainsSqlInjection(string input)
        {
            if (string.IsNullOrEmpty(input)) return false;
            var normalized = input.Trim();
            foreach (var pattern in SqlInjectionPatterns)
            {
                if (Regex.IsMatch(normalized, pattern, RegexOptions.IgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Sanitize và validate input - Loại bỏ ký tự nguy hiểm, giới hạn độ dài
        /// Trả về null nếu input không an toàn
        /// </summary>
        public static string SanitizeForSearch(string input, int maxLength = 100)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            var trimmed = input.Trim();
            if (trimmed.Length > maxLength) trimmed = trimmed.Substring(0, maxLength);
            if (ContainsSqlInjection(trimmed))
            {
                SecurityAuditLogger.LogSqlInjectionAttempt("Search", trimmed);
                return null;
            }
            return trimmed;
        }

        /// <summary>
        /// Sanitize nội dung comment/đánh giá
        /// </summary>
        public static string SanitizeForComment(string input, int maxLength = 500)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            var trimmed = input.Trim();
            if (trimmed.Length > maxLength) trimmed = trimmed.Substring(0, maxLength);
            if (ContainsSqlInjection(trimmed))
            {
                SecurityAuditLogger.LogSqlInjectionAttempt("Comment", trimmed);
                return null;
            }
            // Loại bỏ thẻ HTML cơ bản
            trimmed = Regex.Replace(trimmed, @"<[^>]+>", "");
            return trimmed;
        }
        #endregion

        #region reCAPTCHA - Xác minh người dùng thật
        /// <summary>
        /// Xác minh token reCAPTCHA với API của Google.
        /// </summary>
        public static bool VerifyReCaptcha(string responseToken, string userIp = null)
        {
            if (string.IsNullOrWhiteSpace(responseToken))
                return false;

            var secret = ConfigurationManager.AppSettings["ReCaptchaSecretKey"];
            if (string.IsNullOrWhiteSpace(secret))
                return false;

            try
            {
                using (var client = new WebClient())
                {
                    var uri = $"https://www.google.com/recaptcha/api/siteverify?secret={secret}&response={responseToken}";
                    if (!string.IsNullOrEmpty(userIp))
                    {
                        uri += "&remoteip=" + Uri.EscapeDataString(userIp);
                    }

                    var googleReply = client.DownloadString(uri);
                    dynamic json = JsonConvert.DeserializeObject(googleReply);
                    return json != null && json.success == true;
                }
            }
            catch
            {
                return false;
            }
        }
        #endregion
    }
}

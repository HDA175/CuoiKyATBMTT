using System;
using System.IO;
using System.Web;

namespace CuoiKy.Utils
{
    /// <summary>
    /// Ghi nhật ký bảo mật - Tự động lưu lịch sử khi nghi ngờ bị tấn công
    /// </summary>
    public static class SecurityAuditLogger
    {
        private static readonly object _lock = new object();
        private const string LogFileName = "security_audit.txt";

        /// <summary>
        /// Ghi log khi phát hiện hoạt động nghi ngờ
        /// </summary>
        /// <param name="eventType">Loại sự kiện: Attack, Suspicious, FailedLogin, v.v.</param>
        /// <param name="details">Chi tiết</param>
        /// <param name="additionalInfo">Thông tin bổ sung (IP, URL, payload...)</param>
        public static void LogSuspiciousActivity(string eventType, string details, string additionalInfo = null)
        {
            try
            {
                var ip = HttpContext.Current?.Request?.UserHostAddress ?? "unknown";
                var url = HttpContext.Current?.Request?.RawUrl ?? "";
                var userAgent = HttpContext.Current?.Request?.UserAgent ?? "";
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                var log = $@"
================================================================================
[{timestamp}] CẢNH BÁO BẢO MẬT - NGHI NGỜ BỊ TẤN CÔNG
--------------------------------------------------------------------------------
Loại: {eventType}
Chi tiết: {details}
IP: {ip}
URL: {url}
User-Agent: {userAgent}
{(string.IsNullOrEmpty(additionalInfo) ? "" : "Thông tin thêm: " + additionalInfo)}
================================================================================
";

                WriteToFile(log);
            }
            catch { }
        }

        /// <summary>
        /// Ghi log khi WAF phát hiện pattern tấn công
        /// </summary>
        public static void LogAttackDetected(string pattern, string path = null)
        {
            var details = $"Pattern tấn công: {pattern}";
            var pathInfo = string.IsNullOrEmpty(path) ? "" : $"Path: {path}";
            LogSuspiciousActivity("WAF_BLOCKED", details, pathInfo);
        }

        /// <summary>
        /// Ghi log khi phát hiện SQL Injection
        /// </summary>
        public static void LogSqlInjectionAttempt(string source, string inputSnippet)
        {
            var details = $"SQL Injection - Nguồn: {source}";
            var info = $"Input (trích): {(inputSnippet?.Length > 200 ? inputSnippet.Substring(0, 200) + "..." : inputSnippet)}";
            LogSuspiciousActivity("SQL_INJECTION", details, info);
        }

        /// <summary>
        /// Ghi log đăng nhập thất bại nhiều lần
        /// </summary>
        public static void LogFailedLogin(string username)
        {
            LogSuspiciousActivity("FAILED_LOGIN", $"Đăng nhập thất bại - Username: {username}", null);
        }

        /// <summary>
        /// Ghi log upload file không hợp lệ
        /// </summary>
        public static void LogInvalidFileUpload(string reason, string fileName = null)
        {
            var details = $"Upload file không hợp lệ: {reason}";
            LogSuspiciousActivity("INVALID_UPLOAD", details, fileName);
        }

        /// <summary>
        /// Ghi log XSS attempt
        /// </summary>
        public static void LogXssAttempt(string source)
        {
            LogSuspiciousActivity("XSS_ATTEMPT", $"Phát hiện XSS - Nguồn: {source}", null);
        }

        private static void WriteToFile(string content)
        {
            lock (_lock)
            {
                var basePath = HttpContext.Current?.Server?.MapPath("~/") ?? AppDomain.CurrentDomain.BaseDirectory;
                var logDir = Path.Combine(basePath, "App_Data", "SecurityLogs");
                if (!Directory.Exists(logDir))
                    Directory.CreateDirectory(logDir);

                var logPath = Path.Combine(logDir, LogFileName);
                File.AppendAllText(logPath, content);
            }
        }
    }
}

using CuoiKy.Utils;
using Microsoft.Owin;
using Owin;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Web;

namespace CuoiKy.Middleware
{
    /// <summary>
    /// Mini WAF - Web Application Firewall
    /// Chặn các request có dấu hiệu tấn công XSS, SQL Injection
    /// </summary>
    public class MiniWafMiddleware : OwinMiddleware
    {
        public MiniWafMiddleware(OwinMiddleware next) : base(next)
        {
        }

        public override async Task Invoke(IOwinContext context)
        {
            string requestData = "";

            // Lấy dữ liệu từ query string (QueryString là struct, không dùng ?.)
            var queryValue = context.Request.QueryString.Value;
            if (!string.IsNullOrEmpty(queryValue))
                requestData += queryValue.ToLower();

            // Lấy dữ liệu từ form (POST) - dùng HttpContext khi chạy trên System.Web
            var httpContext = HttpContext.Current;
            if (httpContext?.Request?.Form != null && httpContext.Request.Form.Count > 0)
            {
                foreach (string key in httpContext.Request.Form.AllKeys)
                {
                    var val = httpContext.Request.Form[key];
                    if (!string.IsNullOrEmpty(val))
                        requestData += val.ToLower();
                }
            }

            // Các pattern tấn công cần chặn
            string[] attackPatterns =
            {
                "<script",
                "javascript:",
                "onerror=",
                "onload=",
                "alert(",
                "' or 1=1",
                "\" or 1=1",
                "union select",
                "drop table",
                "xp_cmdshell",
                "<img ",
                "document.cookie",
                "eval(",
                "insert into",
                "delete from"
            };

            foreach (var pattern in attackPatterns)
            {
                if (!string.IsNullOrEmpty(requestData) && requestData.Contains(pattern))
                {
                    LogAttack(context, pattern);
                    SecurityAuditLogger.LogAttackDetected(pattern, context.Request.Path.Value);

                    context.Response.StatusCode = 403;
                    context.Response.ContentType = "text/html";

                    await context.Response.WriteAsync(@"
                        <!DOCTYPE html>
                        <html>
                        <head>
                            <meta charset=""utf-8""/>
                            <title>Request Blocked</title>
                            <style>
                                body { font-family: Arial, sans-serif; text-align: center; margin-top: 100px; }
                                h1 { color: #c0392b; }
                                p { color: #555; }
                            </style>
                        </head>
                        <body>
                            <h1>Request Blocked</h1>
                            <p>Your request looks like a security attack.</p>
                        </body>
                        </html>
                    ");

                    return;
                }
            }

            await Next.Invoke(context);
        }

        private void LogAttack(IOwinContext context, string pattern)
        {
            try
            {
                var ip = HttpContext.Current?.Request?.UserHostAddress ?? "unknown";
                var path = context.Request.Path.Value ?? "";
                var log = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Attack: {pattern} | IP: {ip} | Path: {path}\n";

                var logPath = Path.Combine(HttpContext.Current?.Server?.MapPath("~/") ?? AppDomain.CurrentDomain.BaseDirectory, "App_Data", "attack_log.txt");
                var dir = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.AppendAllText(logPath, log);
            }
            catch { }
        }
    }
}

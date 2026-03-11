using CuoiKy.Middleware;
using Microsoft.Owin;
using Microsoft.Owin.Security;
using Microsoft.Owin.Security.Cookies;
using Microsoft.Owin.Security.Google;
using Owin;
using System;
using System.Configuration;
using System.Security.Claims;
using Microsoft.AspNet.SignalR;

[assembly: OwinStartup(typeof(CuoiKy.Startup))]
namespace CuoiKy
{
    public class Startup
    {

        public void Configuration(IAppBuilder app)
        {
            // Mini WAF - chặn tấn công XSS, SQL Injection (đặt trước routing)
            app.Use(typeof(MiniWafMiddleware));

            app.MapSignalR();
            ConfigureAuth(app);
        }

        private void ConfigureAuth(IAppBuilder app)
        {
            // Dùng cookie làm cơ chế đăng nhập chính
            app.SetDefaultSignInAsAuthenticationType(CookieAuthenticationDefaults.AuthenticationType);

            // Cấu hình cookie login
            app.UseCookieAuthentication(new CookieAuthenticationOptions
            {
                AuthenticationType = CookieAuthenticationDefaults.AuthenticationType,
                LoginPath = new PathString("/User/Login"),
                LogoutPath = new PathString("/User/Logout"),
                ExpireTimeSpan = TimeSpan.FromDays(30),
                SlidingExpiration = true
            });

            // Lấy ClientId / Secret từ Web.config
            var clientId = ConfigurationManager.AppSettings["GoogleClientId"];
            var clientSecret = ConfigurationManager.AppSettings["GoogleClientSecret"];

            if (!string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret))
            {
                var googleOptions = new GoogleOAuth2AuthenticationOptions
                {
                    ClientId = clientId,
                    ClientSecret = clientSecret,
                    // Thường KHÔNG cần set CallbackPath, mặc định là /signin-google
                    // CallbackPath = new PathString("/signin-google")
                };

                // Xin thêm scope email + profile cho chắc
                googleOptions.Scope.Add("email");
                googleOptions.Scope.Add("profile");

                app.UseGoogleAuthentication(googleOptions);
            }
            else
            {
                // Nếu thiếu config thì nên log cho dễ debug
                System.Diagnostics.Debug.WriteLine("⚠️ GoogleClientId hoặc GoogleClientSecret chưa được cấu hình trong Web.config");
            }
        }
    }
}
using System;
using System.Globalization;
using System.Threading;
using System.Web;
using System.Web.Mvc;

namespace CuoiKy.Controllers
{
    public class BaseController : Controller
    {
        protected override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            // ?lang=vi hoặc ?lang=en
            string lang = filterContext.HttpContext.Request["lang"];

            if (string.IsNullOrEmpty(lang))
            {
                var cookie = filterContext.HttpContext.Request.Cookies["lang"];
                lang = cookie?.Value ?? "vi";
            }

            try
            {
                var culture = new CultureInfo(lang);
                Thread.CurrentThread.CurrentCulture = culture;
                Thread.CurrentThread.CurrentUICulture = culture;
            }
            catch
            {
                var culture = new CultureInfo("vi");
                Thread.CurrentThread.CurrentCulture = culture;
                Thread.CurrentThread.CurrentUICulture = culture;
            }

            var newCookie = new HttpCookie("lang", lang)
            {
                Expires = DateTime.Now.AddYears(1)
            };
            filterContext.HttpContext.Response.Cookies.Add(newCookie);

            base.OnActionExecuting(filterContext);
        }
    }
}

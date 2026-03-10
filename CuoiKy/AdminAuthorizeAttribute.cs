using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;



namespace CuoiKy.Filters
{
    // Kế thừa từ AuthorizeAttribute cơ bản của ASP.NET MVC
    public class AdminAuthorizeAttribute : AuthorizeAttribute
    {
        private readonly string[] allowedRoles;

        // Constructor nhận vào danh sách các vai trò được phép
        public AdminAuthorizeAttribute(params string[] roles)
        {
            this.allowedRoles = roles ?? new string[] { };
        }

        // Override phương thức kiểm tra quyền
        protected override bool AuthorizeCore(HttpContextBase httpContext)
        {
            // 1. Kiểm tra đã đăng nhập chưa
            if (httpContext.Session["AdminLogged"] == null || !(bool)httpContext.Session["AdminLogged"])
            {
                return false; // Chưa đăng nhập
            }

            // 2. Lấy Vai trò từ Session
            string userRole = httpContext.Session["AdminRole"] as string;

            // Nếu không có vai trò, từ chối
            if (string.IsNullOrEmpty(userRole))
            {
                return false;
            }

            // 3. Kiểm tra Vai trò có trong danh sách cho phép không
            if (allowedRoles.Length == 0)
            {
                // Nếu không truyền vai trò nào, mặc định là đã đăng nhập là OK (nên tránh)
                return true;
            }

            // Vai trò "Admin" luôn được truy cập vào mọi nơi
            if (userRole == "Admin")
            {
                return true;
            }

            // Kiểm tra Vai trò hiện tại có nằm trong danh sách cho phép không
            return allowedRoles.Contains(userRole);
        }

        // Override khi quyền bị từ chối
        protected override void HandleUnauthorizedRequest(AuthorizationContext filterContext)
        {
            // Chuyển hướng về trang đăng nhập nếu chưa đăng nhập
            if (filterContext.HttpContext.Session["AdminLogged"] == null || !(bool)filterContext.HttpContext.Session["AdminLogged"])
            {
                filterContext.Result = new RedirectResult("~/Admin/Login");
            }
            else
            {
                // Chuyển hướng về trang thông báo lỗi (Ví dụ: 403 - Forbidden)
                // Hoặc về trang Dashboard với thông báo lỗi
                filterContext.Result = new RedirectToRouteResult(
                    new System.Web.Routing.RouteValueDictionary(
                        new { controller = "Admin", action = "Dashboard", error = "Bạn không có quyền truy cập chức năng này!" }
                    )
                );
            }
        }
    }
}
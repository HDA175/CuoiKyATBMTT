using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace CuoiKy.Controllers
{
    //[Authorize] // Yêu cầu đăng nhập cho admin
    public class ChatController : Controller
    {
        // User chat (cho khách hàng)
        [AllowAnonymous]
        public ActionResult UserChat()
        {
            ViewBag.UserName = User.Identity.IsAuthenticated
                ? User.Identity.Name
                : "Khách_" + new Random().Next(1000, 9999);
            return View();
        }

        // Admin chat dashboard
        //[Authorize(Roles = "Admin,Support")] // Chỉ admin/support mới vào được
        public ActionResult AdminChat()
        {
            return View();
        }

        // Trang chat đơn giản (cho test)
        public ActionResult Index()
        {
            return RedirectToAction("UserChat");
        }
    }
}
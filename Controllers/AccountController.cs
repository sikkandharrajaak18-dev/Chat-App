using Chat_App.Models;
using Chat_App.Services.Dtos;
using Chat_App.Services.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
namespace Chat_App.Controllers
{
    public class AccountController : Controller
    {
        private readonly IAccountService _accountService;
        public AccountController(IAccountService accountService)
        {
            _accountService = accountService;
        }
        public IActionResult Login()
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToAction("Index", "Chat");
            ViewBag.SuccessMessage = TempData["Success"];
            return View();
        }
        public IActionResult Register(bool reset = false)
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToAction("Index", "Chat");
            ViewData["Title"] = "Register";
            if (reset)
            {
                HttpContext.Session.Remove("RegisterEmail");
                HttpContext.Session.Remove("RegisterUsername");
                HttpContext.Session.Remove("RegisterPassword");
            }
            var model = new UsersList
            {
                email = HttpContext.Session.GetString("RegisterEmail"),
                username = HttpContext.Session.GetString("RegisterUsername") ?? string.Empty,
                password = HttpContext.Session.GetString("RegisterPassword") ?? string.Empty
            };
            return View(model);
        }
        [HttpPost]
        public async Task<IActionResult> Register(UsersList user, IFormFile? ProfilePhoto)
        {
            ViewData["Title"] = "Register";
            if (!string.IsNullOrWhiteSpace(user.email))
                HttpContext.Session.SetString("RegisterEmail", user.email);
            if (!string.IsNullOrWhiteSpace(user.username))
                HttpContext.Session.SetString("RegisterUsername", user.username);
            if (!string.IsNullOrWhiteSpace(user.password))
                HttpContext.Session.SetString("RegisterPassword", user.password);
            if (await _accountService.EmailExists(user.email))
            {
                ViewBag.EmailError = "Email already exists.";
                return View(user);
            }
            if (await _accountService.UsernameExists(user.username))
            {
                ViewBag.ErrorMessage = "Username already exists.";
                return View(user);
            }
            if (ProfilePhoto != null && ProfilePhoto.Length > 0)
            {
                var (imageUrl, fileName, fileType) = await _accountService.UploadProfilePhoto(ProfilePhoto);
                if (imageUrl != null)
                {
                    user.ProfileImagePath = imageUrl;
                    user.FileName = fileName;
                    user.FileType = fileType;
                }
            }
            await _accountService.CreateUser(user);
            HttpContext.Session.Remove("RegisterEmail");
            HttpContext.Session.Remove("RegisterUsername");
            HttpContext.Session.Remove("RegisterPassword");
            TempData["Success"] = "Registered Successfully";
            return RedirectToAction("Login", "Account");
        }
        [HttpGet]
        public async Task<IActionResult> CheckMail(string email)
        {
            if (string.IsNullOrEmpty(email))
                return Json(new { exists = false });
            var result = await _accountService.CheckMail(email);
            return Json(new { exists = result.Exists, otpSent = result.OtpSent, error = result.Error });
        }
        [HttpGet]
        public async Task<IActionResult> VerifyOtp(string email, string otp)
        {
            var valid = await _accountService.VerifyOtp(email, otp);
            return Json(new { valid });
        }
        [HttpPost]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequestDto request)
        {
            var (success, error) = await _accountService.ResetPassword(request.Email, request.NewPassword);
            return Json(new { success, error });
        }
        [HttpGet]
        public async Task<IActionResult> CheckEmail(string email)
        {
            var result = await _accountService.ValidateEmail(email);
            return Json(new { exists = result.Exists, valid = result.Valid });
        }
        [HttpGet]
        public async Task<IActionResult> CheckUsername(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return Json(new { exists = false });
            var exists = await _accountService.UsernameExists(username);
            return Json(new { exists });
        }
        [HttpPost]
        public async Task<IActionResult> Login(UsersList users, bool rememberMe = false)
        {
            var existingUser = await _accountService.GetUserByUsernameOrEmail(users.username, users.password);
            if (existingUser != null)
            {
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, existingUser.Id.ToString()),
                    new Claim(ClaimTypes.Name, existingUser.username),
                    new Claim(ClaimTypes.Role,existingUser?.Role ?? "User")
                };
                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var principal = new ClaimsPrincipal(identity);
                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
                    new AuthenticationProperties
                    {
                        IsPersistent = rememberMe,
                        ExpiresUtc = rememberMe ? DateTimeOffset.UtcNow.AddDays(30) : null
                    });
                return RedirectToAction("Index", "Chat");
            }
            ViewBag.ErrorMessage = "Invalid username or password.";
            return View();
        }
        public async Task<IActionResult> Logout()
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(userIdClaim, out var userId))
                await _accountService.ClearPushSubscriptions(userId);
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login", "Account");
        }
        [HttpPost]
        public async Task<IActionResult> UploadProfile(IFormFile profileImage)
        {
            try
            {
                if (profileImage == null || profileImage.Length == 0)
                    return BadRequest(new { message = "No file uploaded" });
                var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userIdClaim))
                    return Unauthorized(new { message = "User is not authenticated" });
                int userId = int.Parse(userIdClaim);
                var imageUrl = await _accountService.UpdateProfileImage(userId, profileImage);
                if (imageUrl == null)
                    return NotFound(new { message = "User not found" });
                return Ok(new
                {
                    message = "Profile updated successfully",
                    imageUrl
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }
    }
}
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using VailITDashboard.Security;

namespace VailITDashboard.Pages;

/// <summary>Sign-in page for Auth:Mode = Password.</summary>
[AllowAnonymous]
public sealed class LoginModel(IOptions<DashboardOptions> options, ILogger<LoginModel> log) : PageModel
{
    public string? Error { get; private set; }
    public bool NoUsers => options.Value.Auth.Users.Count == 0;

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(string userName, string password, string? returnUrl)
    {
        var user = options.Value.Auth.Users.FirstOrDefault(u => string.Equals(u.UserName, userName?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (user is null || string.IsNullOrEmpty(password) || !PasswordHasher.Verify(password, user.PasswordHash))
        {
            log.LogWarning("Failed sign-in for {User} from {Ip}", userName, HttpContext.Connection.RemoteIpAddress);
            await Task.Delay(800);   // slow down guessing
            Error = "Wrong user name or password.";
            return Page();
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, user.UserName)], CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        return LocalRedirect(!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
    }

    public async Task<IActionResult> OnPostLogoutAsync()
    {
        if (options.Value.Auth.Mode.Equals("Password", StringComparison.OrdinalIgnoreCase))
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToPage("/Login");
    }
}

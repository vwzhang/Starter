using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Starter.Web.Data;
using Starter.Web.Services;

namespace Starter.Web.Security;

public static class AdminIdentityEndpoints
{
    public static IEndpointRouteBuilder MapAdminIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/admin/sign-in", SignInAsync)
            .AllowAnonymous()
            .DisableAntiforgery();

        endpoints.MapPost("/admin/sign-out", async (SignInManager<ApplicationUser> signInManager) =>
            {
                await signInManager.SignOutAsync();
                return Results.Redirect("/admin/login");
            })
            .RequireAuthorization()
            .DisableAntiforgery();

        return endpoints;
    }

    private static async Task<IResult> SignInAsync(
        [FromForm] LoginRequest request,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        SystemConfigurationService systemConfiguration)
    {
        var returnUrl = GetLocalReturnUrl(request.ReturnUrl);
        var failureUrl = $"/admin/login?failed=1&returnUrl={Uri.EscapeDataString(returnUrl)}";
        var user = await userManager.FindByEmailAsync(request.Email);

        if (user is null || !user.IsActive)
        {
            return Results.Redirect(failureUrl);
        }

        if (!user.EmailConfirmed && await systemConfiguration.IsEmailConfirmationRequiredAsync())
        {
            return Results.Redirect($"/admin/login?confirmRequired=1&returnUrl={Uri.EscapeDataString(returnUrl)}");
        }

        var result = await signInManager.PasswordSignInAsync(
            user.UserName!,
            request.Password,
            request.RememberMe,
            lockoutOnFailure: true);

        return result.Succeeded
            ? Results.Redirect(returnUrl)
            : Results.Redirect(failureUrl);
    }

    private static string GetLocalReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl)
            || returnUrl[0] != '/'
            || (returnUrl.Length > 1 && (returnUrl[1] == '/' || returnUrl[1] == '\\')))
        {
            return "/admin";
        }

        return returnUrl;
    }
}

public sealed class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}

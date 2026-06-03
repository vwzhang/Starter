using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Starter.Web.Data;
using Starter.Web.Services;

namespace Starter.Web.Security;

public static class AccountIdentityEndpoints
{
    public static IEndpointRouteBuilder MapAccountIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/account/register", RegisterAsync)
            .AllowAnonymous()
            .DisableAntiforgery();

        endpoints.MapGet("/account/confirm-email", ConfirmEmailAsync)
            .AllowAnonymous();

        endpoints.MapPost("/account/forgot-password", ForgotPasswordAsync)
            .AllowAnonymous()
            .DisableAntiforgery();

        endpoints.MapPost("/account/reset-password", ResetPasswordAsync)
            .AllowAnonymous()
            .DisableAntiforgery();

        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(
        [FromForm] RegisterRequest request,
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        SystemConfigurationService systemConfiguration,
        IAccountEmailSender emailSender,
        ILoggerFactory loggerFactory)
    {
        var returnUrl = GetLocalReturnUrl(request.ReturnUrl, "/");
        var failureUrl = $"/account/register?failed=1&returnUrl={Uri.EscapeDataString(returnUrl)}";

        if (!await systemConfiguration.IsSelfRegistrationEnabledAsync())
        {
            return Results.Redirect("/account/register?disabled=1");
        }

        if (!string.Equals(request.Password, request.ConfirmPassword, StringComparison.Ordinal))
        {
            return Results.Redirect(failureUrl);
        }

        var email = request.Email.Trim();

        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return Results.Redirect(failureUrl);
        }

        var requireConfirmedEmail = await systemConfiguration.IsEmailConfirmationRequiredAsync();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = !requireConfirmedEmail,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim(),
            IsActive = true,
        };

        var createResult = await userManager.CreateAsync(user, request.Password);

        if (!createResult.Succeeded)
        {
            return Results.Redirect(failureUrl);
        }

        var roleResult = await userManager.AddToRoleAsync(user, AdminPermissionCatalog.UserRoleName);

        if (!roleResult.Succeeded)
        {
            await userManager.DeleteAsync(user);
            return Results.Redirect(failureUrl);
        }

        if (requireConfirmedEmail)
        {
            var confirmationLink = await GenerateEmailConfirmationLinkAsync(
                httpContext,
                systemConfiguration,
                userManager,
                user,
                email);

            var logger = loggerFactory.CreateLogger("AccountIdentity");
            logger.LogInformation("Generated email confirmation link for {Email}: {ConfirmationLink}", email, confirmationLink);

            var emailResult = await emailSender.SendEmailConfirmationAsync(
                email,
                confirmationLink,
                httpContext.RequestAborted);

            if (!emailResult.Attempted)
            {
                logger.LogInformation("Email confirmation message was not sent for {Email}: {Reason}", email, emailResult.Message);
            }
            else if (!emailResult.Succeeded)
            {
                logger.LogWarning("Email confirmation message failed for {Email}: {Reason}", email, emailResult.Message);
            }

            var redirectUrl = $"/admin/login?registered=1&returnUrl={Uri.EscapeDataString(returnUrl)}";

            if (await systemConfiguration.ShouldDisplayEmailConfirmationLinkAsync())
            {
                redirectUrl += $"&confirmationLink={Uri.EscapeDataString(confirmationLink)}";
            }

            return Results.Redirect(redirectUrl);
        }

        await signInManager.SignInAsync(user, isPersistent: false);
        return Results.Redirect(returnUrl);
    }

    private static async Task<IResult> ConfirmEmailAsync(
        [FromQuery] string? email,
        [FromQuery] string? token,
        UserManager<ApplicationUser> userManager)
    {
        const string failureUrl = "/admin/login?confirmFailed=1";

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
        {
            return Results.Redirect(failureUrl);
        }

        var user = await userManager.FindByEmailAsync(email.Trim());

        if (user is null || !user.IsActive)
        {
            return Results.Redirect(failureUrl);
        }

        if (user.EmailConfirmed)
        {
            return Results.Redirect("/admin/login?confirmed=1");
        }

        var result = await userManager.ConfirmEmailAsync(user, token);

        return result.Succeeded
            ? Results.Redirect("/admin/login?confirmed=1")
            : Results.Redirect(failureUrl);
    }

    private static async Task<IResult> ForgotPasswordAsync(
        [FromForm] ForgotPasswordRequest request,
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager,
        SystemConfigurationService systemConfiguration,
        IAccountEmailSender emailSender,
        ILoggerFactory loggerFactory)
    {
        var redirectUrl = "/account/forgot-password?sent=1";
        var email = request.Email.Trim();
        var user = await userManager.FindByEmailAsync(email);

        if (user is null || !user.IsActive)
        {
            return Results.Redirect(redirectUrl);
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var resetLink = await BuildAbsoluteUrlAsync(
            httpContext,
            systemConfiguration,
            "/account/reset-password",
            [
                new("email", email),
                new("token", token),
            ]);

        loggerFactory
            .CreateLogger("AccountIdentity")
            .LogInformation("Generated password reset link for {Email}: {ResetLink}", email, resetLink);

        var emailResult = await emailSender.SendPasswordResetAsync(email, resetLink, httpContext.RequestAborted);
        var logger = loggerFactory.CreateLogger("AccountIdentity");

        if (!emailResult.Attempted)
        {
            logger.LogInformation("Password reset email was not sent for {Email}: {Reason}", email, emailResult.Message);
        }
        else if (!emailResult.Succeeded)
        {
            logger.LogWarning("Password reset email failed for {Email}: {Reason}", email, emailResult.Message);
        }

        if (await systemConfiguration.ShouldDisplayPasswordResetLinkAsync())
        {
            redirectUrl += $"&resetLink={Uri.EscapeDataString(resetLink)}";
        }

        return Results.Redirect(redirectUrl);
    }

    private static async Task<IResult> ResetPasswordAsync(
        [FromForm] ResetPasswordRequest request,
        UserManager<ApplicationUser> userManager)
    {
        var failureUrl = $"/account/reset-password?failed=1&email={Uri.EscapeDataString(request.Email)}&token={Uri.EscapeDataString(request.Token)}";

        if (!string.Equals(request.Password, request.ConfirmPassword, StringComparison.Ordinal))
        {
            return Results.Redirect(failureUrl);
        }

        var user = await userManager.FindByEmailAsync(request.Email.Trim());

        if (user is null || !user.IsActive)
        {
            return Results.Redirect(failureUrl);
        }

        var result = await userManager.ResetPasswordAsync(user, request.Token, request.Password);

        return result.Succeeded
            ? Results.Redirect("/admin/login?reset=1")
            : Results.Redirect(failureUrl);
    }

    private static async Task<string> BuildAbsoluteUrlAsync(
        HttpContext httpContext,
        SystemConfigurationService systemConfiguration,
        string path,
        IEnumerable<KeyValuePair<string, string?>> query)
    {
        var baseUrl = await systemConfiguration.GetPublicBaseUrlAsync()
            ?? $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";

        return baseUrl + path + QueryString.Create(query).ToUriComponent();
    }

    private static async Task<string> GenerateEmailConfirmationLinkAsync(
        HttpContext httpContext,
        SystemConfigurationService systemConfiguration,
        UserManager<ApplicationUser> userManager,
        ApplicationUser user,
        string email)
    {
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);

        return await BuildAbsoluteUrlAsync(
            httpContext,
            systemConfiguration,
            "/account/confirm-email",
            [
                new("email", email),
                new("token", token),
            ]);
    }

    private static string GetLocalReturnUrl(string? returnUrl, string fallback)
    {
        if (string.IsNullOrWhiteSpace(returnUrl)
            || returnUrl[0] != '/'
            || (returnUrl.Length > 1 && (returnUrl[1] == '/' || returnUrl[1] == '\\')))
        {
            return fallback;
        }

        return returnUrl;
    }
}

public sealed class RegisterRequest
{
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string Password { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
    public string? ReturnUrl { get; set; }
}

public sealed class ForgotPasswordRequest
{
    public string Email { get; set; } = string.Empty;
}

public sealed class ResetPasswordRequest
{
    public string Email { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
}

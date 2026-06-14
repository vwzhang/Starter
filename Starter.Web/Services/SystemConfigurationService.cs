using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Starter.Web.Data;

namespace Starter.Web.Services;

public sealed class SystemConfigurationService(
    IServiceScopeFactory scopeFactory,
    IHostEnvironment environment,
    IConfiguration configuration,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<SystemConfigurationService> logger)
{
    private const string ProtectedSecretPrefix = "protected:v1:";
    private readonly IDataProtector secretProtector =
        dataProtectionProvider.CreateProtector("Starter.SystemConfiguration.Secrets.v1");

    public async Task EnsureDefaultsAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        foreach (var definition in GetDefinitions())
        {
            var setting = await dbContext.Settings.SingleOrDefaultAsync(
                item => item.Key == definition.Key,
                cancellationToken);

            var defaultValue = GetDefaultValue(definition);

            if (setting is null)
            {
                dbContext.Settings.Add(new ApplicationSetting
                {
                    Key = definition.Key,
                    Name = definition.Name,
                    Category = definition.Category,
                    Value = GetStoredValue(defaultValue, definition.ValueType),
                    DefaultValue = defaultValue,
                    ValueType = definition.ValueType,
                    Description = definition.Description,
                });
                continue;
            }

            var shouldUseNewDefault = string.Equals(
                setting.Value,
                setting.DefaultValue,
                StringComparison.Ordinal);

            setting.Name = definition.Name;
            setting.Category = definition.Category;
            setting.DefaultValue = defaultValue;
            setting.ValueType = definition.ValueType;
            setting.Description = definition.Description;

            if (shouldUseNewDefault)
            {
                setting.Value = GetStoredValue(defaultValue, definition.ValueType);
            }
            else if (definition.ValueType == SystemConfigurationValueTypes.Secret)
            {
                setting.Value = GetStoredValue(setting.Value, setting.ValueType);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SystemConfigurationSummary>> GetSettingsAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var settings = await dbContext.Settings
            .AsNoTracking()
            .OrderBy(setting => setting.Category)
            .ThenBy(setting => setting.Name)
            .ToListAsync();

        return settings
            .Select(setting => new SystemConfigurationSummary(
                setting.Id,
                setting.Key,
                setting.Name,
                setting.Category,
                GetDisplayValue(setting.Value, setting.ValueType),
                setting.DefaultValue,
                setting.ValueType,
                setting.Description))
            .ToList();
    }

    public async Task<AdminMutationResult> SaveSettingAsync(SystemConfigurationFormModel model)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var setting = model.Id is null
            ? await dbContext.Settings.SingleOrDefaultAsync(item => item.Key == model.Key)
            : await dbContext.Settings.SingleOrDefaultAsync(item => item.Id == model.Id);

        if (setting is null)
        {
            return AdminMutationResult.Failure("Setting not found.");
        }

        var normalizedValue = NormalizeValue(model.Value, setting.ValueType);

        if (normalizedValue is null)
        {
            return AdminMutationResult.Failure("Value does not match the setting type.");
        }

        if (setting.ValueType == SystemConfigurationValueTypes.Secret
            && string.IsNullOrEmpty(normalizedValue))
        {
            return AdminMutationResult.Success("Setting saved.");
        }

        setting.Value = GetStoredValue(normalizedValue, setting.ValueType);
        setting.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync();
        return AdminMutationResult.Success("Setting saved.");
    }

    public async Task<bool> IsSelfRegistrationEnabledAsync()
    {
        return await GetBooleanAsync(SystemConfigurationKeys.SelfRegistrationEnabled);
    }

    public async Task<bool> IsEmailConfirmationRequiredAsync()
    {
        return await GetBooleanAsync(SystemConfigurationKeys.RequireConfirmedEmail);
    }

    public async Task<bool> ShouldDisplayEmailConfirmationLinkAsync()
    {
        return environment.IsDevelopment()
            && await GetBooleanAsync(SystemConfigurationKeys.DisplayEmailConfirmationLink);
    }

    public async Task<bool> ShouldDisplayPasswordResetLinkAsync()
    {
        return environment.IsDevelopment()
            && await GetBooleanAsync(SystemConfigurationKeys.DisplayPasswordResetLink);
    }

    public async Task<string?> GetPublicBaseUrlAsync()
    {
        var value = await GetValueAsync(SystemConfigurationKeys.PublicBaseUrl);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().TrimEnd('/');
    }

    public async Task<SmtpEmailSettings> GetSmtpEmailSettingsAsync()
    {
        var portValue = await GetValueAsync(SystemConfigurationKeys.EmailSmtpPort);
        var port = int.TryParse(portValue, out var parsedPort) ? parsedPort : 587;

        return new SmtpEmailSettings(
            await GetBooleanAsync(SystemConfigurationKeys.EmailDeliveryEnabled),
            await GetValueAsync(SystemConfigurationKeys.EmailFromAddress),
            await GetValueAsync(SystemConfigurationKeys.EmailFromName),
            await GetValueAsync(SystemConfigurationKeys.EmailSmtpHost),
            port,
            await GetBooleanAsync(SystemConfigurationKeys.EmailSmtpUseSsl),
            await GetValueAsync(SystemConfigurationKeys.EmailSmtpUsername),
            await GetValueAsync(SystemConfigurationKeys.EmailSmtpPassword));
    }

    public async Task<AiApiSettings> GetAiApiSettingsAsync()
    {
        var currentProvider = await GetValueAsync(SystemConfigurationKeys.AiCurrentProvider);

        return new AiApiSettings(
            currentProvider.Trim().ToLowerInvariant(),
            await GetValueAsync(SystemConfigurationKeys.AiSystemPrompt),
            [
                new(
                    AiApiProviderKeys.OpenAi,
                    "ChatGPT",
                    await GetValueAsync(SystemConfigurationKeys.AiOpenAiEndpoint),
                    await GetValueAsync(SystemConfigurationKeys.AiOpenAiModel),
                    await GetValueAsync(SystemConfigurationKeys.AiOpenAiApiKey)),
                new(
                    AiApiProviderKeys.Gemini,
                    "Gemini",
                    await GetValueAsync(SystemConfigurationKeys.AiGeminiEndpoint),
                    await GetValueAsync(SystemConfigurationKeys.AiGeminiModel),
                    await GetValueAsync(SystemConfigurationKeys.AiGeminiApiKey)),
                new(
                    AiApiProviderKeys.GitHub,
                    "GitHub Models",
                    await GetValueAsync(SystemConfigurationKeys.AiGitHubEndpoint),
                    await GetValueAsync(SystemConfigurationKeys.AiGitHubModel),
                    await GetValueAsync(SystemConfigurationKeys.AiGitHubApiKey)),
                new(
                    AiApiProviderKeys.Groq,
                    "Groq",
                    await GetValueAsync(SystemConfigurationKeys.AiGroqEndpoint),
                    await GetValueAsync(SystemConfigurationKeys.AiGroqModel),
                    await GetValueAsync(SystemConfigurationKeys.AiGroqApiKey)),
                new(
                    AiApiProviderKeys.AzureFoundry,
                    "Azure Foundry",
                    await GetValueAsync(SystemConfigurationKeys.AiAzureFoundryEndpoint),
                    await GetValueAsync(SystemConfigurationKeys.AiAzureFoundryModel),
                    await GetValueAsync(SystemConfigurationKeys.AiAzureFoundryApiKey)),
            ]);
    }

    public async Task<AdminMutationResult> SaveAiApiSettingsAsync(AiApiConfigurationUpdate update)
    {
        var providerKeys = update.Providers.Select(provider => provider.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!providerKeys.Contains(update.CurrentProvider))
        {
            return AdminMutationResult.Failure("Current AI provider is not valid.");
        }

        var updates = new List<(string Key, string Value)>
        {
            (SystemConfigurationKeys.AiCurrentProvider, update.CurrentProvider),
            (SystemConfigurationKeys.AiSystemPrompt, update.SystemPrompt),
        };

        foreach (var provider in update.Providers)
        {
            var keys = GetAiProviderSettingKeys(provider.Key);
            if (keys is null)
            {
                return AdminMutationResult.Failure($"Unknown AI provider '{provider.Key}'.");
            }

            updates.Add((keys.Value.EndpointKey, provider.Endpoint));
            updates.Add((keys.Value.ModelKey, provider.Model));
            updates.Add((keys.Value.ApiKeyKey, provider.ApiKey));
        }

        foreach (var item in updates)
        {
            var result = await SaveSettingValueAsync(item.Key, item.Value);
            if (!result.Succeeded)
            {
                return result;
            }
        }

        return AdminMutationResult.Success("AI API configuration saved.");
    }

    private async Task<bool> GetBooleanAsync(string key)
    {
        return bool.TryParse(await GetValueAsync(key), out var value) && value;
    }

    private async Task<AdminMutationResult> SaveSettingValueAsync(string key, string value)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var setting = await dbContext.Settings.SingleOrDefaultAsync(item => item.Key == key);

        if (setting is null)
        {
            return AdminMutationResult.Failure($"Setting '{key}' not found.");
        }

        var normalizedValue = NormalizeValue(value, setting.ValueType);

        if (normalizedValue is null)
        {
            return AdminMutationResult.Failure($"Value for '{setting.Name}' does not match the setting type.");
        }

        if (setting.ValueType == SystemConfigurationValueTypes.Secret
            && string.IsNullOrEmpty(normalizedValue))
        {
            return AdminMutationResult.Success("Setting saved.");
        }

        setting.Value = GetStoredValue(normalizedValue, setting.ValueType);
        setting.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync();
        return AdminMutationResult.Success("Setting saved.");
    }

    private async Task<string> GetValueAsync(string key)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var setting = await dbContext.Settings.AsNoTracking().SingleOrDefaultAsync(item => item.Key == key);

        if (setting is not null)
        {
            return GetRuntimeValue(setting.Value, setting.ValueType);
        }

        var definition = GetDefinitions().Single(item => item.Key == key);
        return GetDefaultValue(definition);
    }

    private SystemConfigurationDefinition[] GetDefinitions()
    {
        var smtpHost = configuration["Starter:Email:SmtpHost"] ?? string.Empty;
        var smtpPort = configuration["Starter:Email:SmtpPort"] ?? "587";
        var smtpEnabled = string.IsNullOrWhiteSpace(smtpHost) ? "false" : "true";

        return
        [
            new(
                SystemConfigurationKeys.AiCurrentProvider,
                "Current AI provider",
                "AI",
                SystemConfigurationValueTypes.Text,
                AiApiProviderKeys.OpenAi,
                AiApiProviderKeys.OpenAi,
                "Provider used by the AI chat page."),
            new(
                SystemConfigurationKeys.AiSystemPrompt,
                "AI system prompt",
                "AI",
                SystemConfigurationValueTypes.Text,
                "You are a helpful assistant for this .NET Aspire starter application.",
                "You are a helpful assistant for this .NET Aspire starter application.",
                "System prompt sent with every AI chat request."),
            new(
                SystemConfigurationKeys.AiOpenAiEndpoint,
                "ChatGPT endpoint",
                "AI",
                SystemConfigurationValueTypes.Text,
                "https://api.openai.com/v1/chat/completions",
                "https://api.openai.com/v1/chat/completions",
                "OpenAI Chat Completions endpoint."),
            new(
                SystemConfigurationKeys.AiOpenAiModel,
                "ChatGPT model",
                "AI",
                SystemConfigurationValueTypes.Text,
                "",
                "",
                "OpenAI model name to use for AI chat."),
            new(
                SystemConfigurationKeys.AiOpenAiApiKey,
                "ChatGPT API key",
                "AI",
                SystemConfigurationValueTypes.Secret,
                "",
                "",
                "OpenAI API key."),
            new(
                SystemConfigurationKeys.AiGeminiEndpoint,
                "Gemini endpoint",
                "AI",
                SystemConfigurationValueTypes.Text,
                "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions",
                "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions",
                "Gemini OpenAI-compatible Chat Completions endpoint."),
            new(
                SystemConfigurationKeys.AiGeminiModel,
                "Gemini model",
                "AI",
                SystemConfigurationValueTypes.Text,
                "",
                "",
                "Gemini model name to use for AI chat."),
            new(
                SystemConfigurationKeys.AiGeminiApiKey,
                "Gemini API key",
                "AI",
                SystemConfigurationValueTypes.Secret,
                "",
                "",
                "Gemini API key."),
            new(
                SystemConfigurationKeys.AiGitHubEndpoint,
                "GitHub Models endpoint",
                "AI",
                SystemConfigurationValueTypes.Text,
                "https://models.github.ai/inference/chat/completions",
                "https://models.github.ai/inference/chat/completions",
                "GitHub Models Chat Completions endpoint."),
            new(
                SystemConfigurationKeys.AiGitHubModel,
                "GitHub Models model",
                "AI",
                SystemConfigurationValueTypes.Text,
                "",
                "",
                "GitHub Models model name to use for AI chat."),
            new(
                SystemConfigurationKeys.AiGitHubApiKey,
                "GitHub Models API key",
                "AI",
                SystemConfigurationValueTypes.Secret,
                "",
                "",
                "GitHub personal access token or model access token."),
            new(
                SystemConfigurationKeys.AiGroqEndpoint,
                "Groq endpoint",
                "AI",
                SystemConfigurationValueTypes.Text,
                "https://api.groq.com/openai/v1/chat/completions",
                "https://api.groq.com/openai/v1/chat/completions",
                "Groq OpenAI-compatible Chat Completions endpoint."),
            new(
                SystemConfigurationKeys.AiGroqModel,
                "Groq model",
                "AI",
                SystemConfigurationValueTypes.Text,
                "",
                "",
                "Groq model name to use for AI chat."),
            new(
                SystemConfigurationKeys.AiGroqApiKey,
                "Groq API key",
                "AI",
                SystemConfigurationValueTypes.Secret,
                "",
                "",
                "Groq API key."),
            new(
                SystemConfigurationKeys.AiAzureFoundryEndpoint,
                "Azure Foundry endpoint",
                "AI",
                SystemConfigurationValueTypes.Text,
                "",
                "",
                "Azure Foundry or Azure OpenAI chat completions/responses endpoint."),
            new(
                SystemConfigurationKeys.AiAzureFoundryModel,
                "Azure Foundry model",
                "AI",
                SystemConfigurationValueTypes.Text,
                "",
                "",
                "Azure Foundry model or deployment name to use for AI chat."),
            new(
                SystemConfigurationKeys.AiAzureFoundryApiKey,
                "Azure Foundry API key",
                "AI",
                SystemConfigurationValueTypes.Secret,
                "",
                "",
                "Azure Foundry or Azure OpenAI API key."),
            new(
                SystemConfigurationKeys.SelfRegistrationEnabled,
                "Self registration",
                "Identity",
                SystemConfigurationValueTypes.Boolean,
                "false",
                "true",
                "Allow visitors to create their own baseline User account."),
            new(
                SystemConfigurationKeys.RequireConfirmedEmail,
                "Require email confirmation",
                "Identity",
                SystemConfigurationValueTypes.Boolean,
                "false",
                "false",
                "Require newly registered users to confirm their email before they can sign in."),
            new(
                SystemConfigurationKeys.DisplayEmailConfirmationLink,
                "Display confirmation link",
                "Identity",
                SystemConfigurationValueTypes.Boolean,
                "false",
                "true",
                "Show generated email confirmation links on screen for local development."),
            new(
                SystemConfigurationKeys.DisplayPasswordResetLink,
                "Display reset link",
                "Identity",
                SystemConfigurationValueTypes.Boolean,
                "false",
                "true",
                "Show generated password reset links on screen for local development until email delivery is configured."),
            new(
                SystemConfigurationKeys.PublicBaseUrl,
                "Public base URL",
                "Server",
                SystemConfigurationValueTypes.Text,
                "",
                "",
                "External application URL used for generated links when the app is behind a proxy."),
            new(
                SystemConfigurationKeys.EmailDeliveryEnabled,
                "Email delivery",
                "Email",
                SystemConfigurationValueTypes.Boolean,
                "false",
                smtpEnabled,
                "Enable outbound application email."),
            new(
                SystemConfigurationKeys.EmailFromAddress,
                "Email from address",
                "Email",
                SystemConfigurationValueTypes.Text,
                "no-reply@starter.local",
                "no-reply@starter.local",
                "Sender address to use when email delivery is added."),
            new(
                SystemConfigurationKeys.EmailFromName,
                "Email from name",
                "Email",
                SystemConfigurationValueTypes.Text,
                "Starter",
                "Starter",
                "Sender display name for outbound email."),
            new(
                SystemConfigurationKeys.EmailSmtpHost,
                "SMTP host",
                "Email",
                SystemConfigurationValueTypes.Text,
                "",
                smtpHost,
                "SMTP host for outbound email."),
            new(
                SystemConfigurationKeys.EmailSmtpPort,
                "SMTP port",
                "Email",
                SystemConfigurationValueTypes.Number,
                "587",
                smtpPort,
                "SMTP port for outbound email."),
            new(
                SystemConfigurationKeys.EmailSmtpUsername,
                "SMTP username",
                "Email",
                SystemConfigurationValueTypes.Text,
                "",
                "",
                "Username for authenticated SMTP delivery."),
            new(
                SystemConfigurationKeys.EmailSmtpPassword,
                "SMTP password",
                "Email",
                SystemConfigurationValueTypes.Secret,
                "",
                "",
                "Password or API key for authenticated SMTP delivery."),
            new(
                SystemConfigurationKeys.EmailSmtpUseSsl,
                "SMTP SSL",
                "Email",
                SystemConfigurationValueTypes.Boolean,
                "true",
                "false",
                "Use TLS/SSL for SMTP email delivery."),
        ];
    }

    private static AiProviderSettingKeys? GetAiProviderSettingKeys(string providerKey)
    {
        return providerKey.ToLowerInvariant() switch
        {
            AiApiProviderKeys.OpenAi => new(
                SystemConfigurationKeys.AiOpenAiEndpoint,
                SystemConfigurationKeys.AiOpenAiModel,
                SystemConfigurationKeys.AiOpenAiApiKey),
            AiApiProviderKeys.Gemini => new(
                SystemConfigurationKeys.AiGeminiEndpoint,
                SystemConfigurationKeys.AiGeminiModel,
                SystemConfigurationKeys.AiGeminiApiKey),
            AiApiProviderKeys.GitHub => new(
                SystemConfigurationKeys.AiGitHubEndpoint,
                SystemConfigurationKeys.AiGitHubModel,
                SystemConfigurationKeys.AiGitHubApiKey),
            AiApiProviderKeys.Groq => new(
                SystemConfigurationKeys.AiGroqEndpoint,
                SystemConfigurationKeys.AiGroqModel,
                SystemConfigurationKeys.AiGroqApiKey),
            AiApiProviderKeys.AzureFoundry => new(
                SystemConfigurationKeys.AiAzureFoundryEndpoint,
                SystemConfigurationKeys.AiAzureFoundryModel,
                SystemConfigurationKeys.AiAzureFoundryApiKey),
            _ => null,
        };
    }

    private readonly record struct AiProviderSettingKeys(
        string EndpointKey,
        string ModelKey,
        string ApiKeyKey);

    private string GetDefaultValue(SystemConfigurationDefinition definition)
    {
        return environment.IsDevelopment()
            ? definition.DevelopmentDefaultValue
            : definition.DefaultValue;
    }

    private string GetStoredValue(string value, string valueType)
    {
        if (valueType != SystemConfigurationValueTypes.Secret
            || string.IsNullOrEmpty(value)
            || value.StartsWith(ProtectedSecretPrefix, StringComparison.Ordinal))
        {
            return value;
        }

        return ProtectedSecretPrefix + secretProtector.Protect(value);
    }

    private string GetRuntimeValue(string value, string valueType)
    {
        if (valueType != SystemConfigurationValueTypes.Secret
            || string.IsNullOrEmpty(value)
            || !value.StartsWith(ProtectedSecretPrefix, StringComparison.Ordinal))
        {
            return value;
        }

        try
        {
            return secretProtector.Unprotect(value[ProtectedSecretPrefix.Length..]);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to unprotect system configuration secret.");
            return string.Empty;
        }
    }

    private static string GetDisplayValue(string value, string valueType)
    {
        if (valueType == SystemConfigurationValueTypes.Secret)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : "__configured__";
        }

        return value;
    }

    private static string? NormalizeValue(string value, string valueType)
    {
        if (valueType == SystemConfigurationValueTypes.Secret)
        {
            return value;
        }

        var trimmedValue = value.Trim();

        if (valueType == SystemConfigurationValueTypes.Boolean)
        {
            if (!bool.TryParse(trimmedValue, out var booleanValue))
            {
                return null;
            }

            return booleanValue.ToString().ToLowerInvariant();
        }

        if (valueType == SystemConfigurationValueTypes.Number
            && !int.TryParse(trimmedValue, out _))
        {
            return null;
        }

        return trimmedValue;
    }
}

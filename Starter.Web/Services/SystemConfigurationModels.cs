using System.ComponentModel.DataAnnotations;

namespace Starter.Web.Services;

public static class SystemConfigurationKeys
{
    public const string AiCurrentProvider = "ai.current_provider";
    public const string AiSystemPrompt = "ai.system_prompt";
    public const string AiOpenAiEndpoint = "ai.openai.endpoint";
    public const string AiOpenAiModel = "ai.openai.model";
    public const string AiOpenAiApiKey = "ai.openai.api_key";
    public const string AiDeepSeekEndpoint = "ai.deepseek.endpoint";
    public const string AiDeepSeekModel = "ai.deepseek.model";
    public const string AiDeepSeekApiKey = "ai.deepseek.api_key";
    public const string AiGeminiEndpoint = "ai.gemini.endpoint";
    public const string AiGeminiModel = "ai.gemini.model";
    public const string AiGeminiApiKey = "ai.gemini.api_key";
    public const string AiGitHubEndpoint = "ai.github.endpoint";
    public const string AiGitHubModel = "ai.github.model";
    public const string AiGitHubApiKey = "ai.github.api_key";
    public const string AiGroqEndpoint = "ai.groq.endpoint";
    public const string AiGroqModel = "ai.groq.model";
    public const string AiGroqApiKey = "ai.groq.api_key";
    public const string AiAzureFoundryEndpoint = "ai.azure_foundry.endpoint";
    public const string AiAzureFoundryModel = "ai.azure_foundry.model";
    public const string AiAzureFoundryApiKey = "ai.azure_foundry.api_key";
    public const string SelfRegistrationEnabled = "identity.registration.enabled";
    public const string RequireConfirmedEmail = "identity.email_confirmation.required";
    public const string DisplayEmailConfirmationLink = "identity.email_confirmation.display_link";
    public const string DisplayPasswordResetLink = "identity.password_reset.display_link";
    public const string PublicBaseUrl = "server.public_base_url";
    public const string EmailDeliveryEnabled = "email.delivery.enabled";
    public const string EmailFromAddress = "email.from_address";
    public const string EmailFromName = "email.from_name";
    public const string EmailSmtpHost = "email.smtp_host";
    public const string EmailSmtpPort = "email.smtp_port";
    public const string EmailSmtpUseSsl = "email.smtp_use_ssl";
    public const string EmailSmtpUsername = "email.smtp_username";
    public const string EmailSmtpPassword = "email.smtp_password";
}

public static class SystemConfigurationValueTypes
{
    public const string Boolean = "Boolean";
    public const string Number = "Number";
    public const string Secret = "Secret";
    public const string Text = "Text";
}

public sealed record SystemConfigurationDefinition(
    string Key,
    string Name,
    string Category,
    string ValueType,
    string DefaultValue,
    string DevelopmentDefaultValue,
    string? Description);

public sealed record SystemConfigurationSummary(
    int Id,
    string Key,
    string Name,
    string Category,
    string Value,
    string DefaultValue,
    string ValueType,
    string? Description);

public static class AiApiProviderKeys
{
    public const string OpenAi = "openai";
    public const string DeepSeek = "deepseek";
    public const string Gemini = "gemini";
    public const string GitHub = "github";
    public const string Groq = "groq";
    public const string AzureFoundry = "azure-foundry";
}

public sealed record AiApiProviderSettings(
    string Key,
    string DisplayName,
    string Endpoint,
    string Model,
    string ApiKey)
{
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint)
        && !string.IsNullOrWhiteSpace(Model)
        && !string.IsNullOrWhiteSpace(ApiKey);
}

public sealed record AiApiSettings(
    string CurrentProvider,
    string SystemPrompt,
    IReadOnlyList<AiApiProviderSettings> Providers)
{
    public AiApiProviderSettings? CurrentProviderSettings =>
        Providers.FirstOrDefault(provider => string.Equals(provider.Key, CurrentProvider, StringComparison.OrdinalIgnoreCase));
}

public sealed record AiApiConfigurationUpdate(
    string CurrentProvider,
    string SystemPrompt,
    IReadOnlyList<AiApiProviderConfigurationUpdate> Providers);

public sealed record AiApiProviderConfigurationUpdate(
    string Key,
    string Endpoint,
    string Model,
    string ApiKey);

public sealed record SmtpEmailSettings(
    bool DeliveryEnabled,
    string FromAddress,
    string FromName,
    string Host,
    int Port,
    bool UseSsl,
    string Username,
    string Password)
{
    public bool IsConfigured =>
        DeliveryEnabled
        && !string.IsNullOrWhiteSpace(FromAddress)
        && !string.IsNullOrWhiteSpace(Host)
        && Port > 0;
}

public sealed record EmailSendResult(bool Attempted, bool Succeeded, string Message)
{
    public static EmailSendResult Skipped(string message) => new(false, false, message);
    public static EmailSendResult Success() => new(true, true, "Email sent.");
    public static EmailSendResult Failure(string message) => new(true, false, message);
}

public sealed class SystemConfigurationFormModel
{
    public int? Id { get; set; }

    [Required]
    public string Key { get; set; } = string.Empty;

    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Category { get; set; } = string.Empty;

    [Required]
    public string Value { get; set; } = string.Empty;

    [Required]
    public string ValueType { get; set; } = SystemConfigurationValueTypes.Text;

    public string? Description { get; set; }
}

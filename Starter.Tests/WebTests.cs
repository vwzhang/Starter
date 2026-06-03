using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using Starter.Shared;

namespace Starter.Tests;

public class WebTests
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(300);

    [Fact]
    public async Task StarterResourcesSupportWebCrudAndEmailSmoke()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Starter_AppHost>(cancellationToken);
        appHost.Services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Debug);
            // Override the logging filters from the app's configuration
            logging.AddFilter(appHost.Environment.ApplicationName, LogLevel.Debug);
            logging.AddFilter("Aspire.", LogLevel.Debug);
            // To output logs to the xUnit.net ITestOutputHelper, consider adding a package from https://www.nuget.org/packages?q=xunit+logging
        });
        appHost.Services.ConfigureHttpClientDefaults(clientBuilder =>
        {
            clientBuilder.AddStandardResilienceHandler();
        });

        await using var app = await appHost.BuildAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        await app.StartAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);

        var httpClient = app.CreateHttpClient("webfrontend");
        var apiClient = app.CreateHttpClient("apiservice");
        var smtpClient = app.CreateHttpClient("smtp4dev");

        await app.ResourceNotifications.WaitForResourceHealthyAsync("webfrontend", cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("apiservice", cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("smtp4dev", cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);

        // Act + Assert: Web shell and admin login are reachable.
        var response = await httpClient.GetAsync("/", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var loginResponse = await httpClient.GetAsync("/admin/login", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        // Act + Assert: shared DTO CRUD API works against the migrated starter database.
        var createRequest = new DevTodoSaveRequest(
            "CI smoke todo",
            "Created by Starter.Tests",
            DevTodoStatus.Backlog,
            null,
            10);
        var createResponse = await apiClient.PostAsJsonAsync("/dev/todos", createRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var createdTodo = await createResponse.Content.ReadFromJsonAsync<DevTodoItemDto>(cancellationToken);
        Assert.NotNull(createdTodo);
        Assert.Equal(createRequest.Title, createdTodo.Title);

        var updateRequest = createRequest with
        {
            Status = DevTodoStatus.Done,
            Notes = "Updated by Starter.Tests",
        };
        var updateResponse = await apiClient.PutAsJsonAsync($"/dev/todos/{createdTodo.Id}", updateRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var updatedTodo = await updateResponse.Content.ReadFromJsonAsync<DevTodoItemDto>(cancellationToken);
        Assert.NotNull(updatedTodo);
        Assert.Equal(DevTodoStatus.Done, updatedTodo.Status);

        var list = await apiClient.GetFromJsonAsync<DevTodoItemDto[]>("/dev/todos?search=CI%20smoke", cancellationToken);
        Assert.Contains(list ?? [], todo => todo.Id == createdTodo.Id);

        var deleteResponse = await apiClient.DeleteAsync($"/dev/todos/{createdTodo.Id}", cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // Act + Assert: forgot-password uses the seeded SMTP settings and is captured by smtp4dev.
        var forgotResponse = await httpClient.PostAsync(
            "/account/forgot-password",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Email"] = "admin@starter.local",
            }),
            cancellationToken);
        Assert.True(forgotResponse.IsSuccessStatusCode || forgotResponse.StatusCode == HttpStatusCode.Redirect);

        await WaitForSmtpMessageAsync(smtpClient, "Reset your Starter password", cancellationToken);
    }

    private static async Task WaitForSmtpMessageAsync(
        HttpClient smtpClient,
        string expectedText,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        var lastResponse = string.Empty;

        while (DateTimeOffset.UtcNow < deadline)
        {
            var response = await smtpClient.GetAsync("/api/messages", cancellationToken);
            lastResponse = await response.Content.ReadAsStringAsync(cancellationToken);

            if (lastResponse.Contains(expectedText, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }

        Assert.True(
            lastResponse.Contains(expectedText, StringComparison.OrdinalIgnoreCase),
            $"smtp4dev did not receive a message containing '{expectedText}'. Last response: {lastResponse}");
    }
}

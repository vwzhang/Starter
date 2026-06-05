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

        // Act + Assert: shared DTO catalog API works against the migrated application database.
        var createCategoryRequest = new CatalogCategorySaveRequest(
            "CI Catalog",
            "Created by Starter.Tests",
            90);
        var createCategoryResponse = await apiClient.PostAsJsonAsync("/dev/catalog/categories", createCategoryRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, createCategoryResponse.StatusCode);

        var createdCategory = await createCategoryResponse.Content.ReadFromJsonAsync<CatalogCategoryDto>(cancellationToken);
        Assert.NotNull(createdCategory);
        Assert.Equal(createCategoryRequest.Name, createdCategory.Name);

        var createRequest = new CatalogProductSaveRequest(
            createdCategory.Id,
            "CI Smoke Product",
            "CI-SMOKE-PRODUCT",
            "Created by Starter.Tests",
            12.34m,
            5,
            true);
        var createResponse = await apiClient.PostAsJsonAsync("/dev/catalog/products", createRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var createdProduct = await createResponse.Content.ReadFromJsonAsync<CatalogProductDto>(cancellationToken);
        Assert.NotNull(createdProduct);
        Assert.Equal(createRequest.Name, createdProduct.Name);
        Assert.Equal(createCategoryRequest.Name, createdProduct.CategoryName);

        var updateRequest = createRequest with
        {
            Price = 23.45m,
            StockQuantity = 8,
        };
        var updateResponse = await apiClient.PutAsJsonAsync($"/dev/catalog/products/{createdProduct.Id}", updateRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var updatedProduct = await updateResponse.Content.ReadFromJsonAsync<CatalogProductDto>(cancellationToken);
        Assert.NotNull(updatedProduct);
        Assert.Equal(updateRequest.Price, updatedProduct.Price);

        var list = await apiClient.GetFromJsonAsync<CatalogProductDto[]>("/dev/catalog/products?search=CI%20Smoke", cancellationToken);
        Assert.Contains(list ?? [], product => product.Id == createdProduct.Id);

        var deleteResponse = await apiClient.DeleteAsync($"/dev/catalog/products/{createdProduct.Id}", cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var deleteCategoryResponse = await apiClient.DeleteAsync($"/dev/catalog/categories/{createdCategory.Id}", cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteCategoryResponse.StatusCode);

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

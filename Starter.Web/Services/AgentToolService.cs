using System.ComponentModel;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Starter.Shared;

namespace Starter.Web.Services;

public sealed class AgentToolService(
    HttpClient httpClient,
    CatalogApiClient catalogApi,
    ILogger<AgentToolService> logger)
{
    [Description("Search the public internet for current information. Returns concise result snippets and source URLs.")]
    public async Task<string> SearchInternetAsync([Description("The internet search query.")] string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return "Internet search skipped because the query was empty.";
        }

        var requestUri = $"https://api.duckduckgo.com/?q={Uri.EscapeDataString(query.Trim())}&format=json&no_html=1&skip_disambig=1";
        logger.LogInformation("Agents.AI internet search tool querying {RequestUri}", requestUri);

        var response = await httpClient.GetFromJsonAsync<DuckDuckGoInstantAnswerResponse>(requestUri);
        if (response is null)
        {
            return $"Internet search for '{query}' returned an empty response.";
        }

        var results = new List<string>();

        if (!string.IsNullOrWhiteSpace(response.AbstractText))
        {
            results.Add(FormatSearchResult(response.Heading, response.AbstractText, response.AbstractUrl));
        }

        foreach (var relatedTopic in FlattenRelatedTopics(response.RelatedTopics).Take(4))
        {
            if (!string.IsNullOrWhiteSpace(relatedTopic.Text))
            {
                results.Add(FormatSearchResult(relatedTopic.FirstUrl, relatedTopic.Text, relatedTopic.FirstUrl));
            }
        }

        return results.Count == 0
            ? $"Internet search for '{query}' completed but DuckDuckGo returned no instant-answer snippets."
            : string.Join("\n", results.Distinct());
    }

    [Description("Search the local PostgreSQL catalog database for matching products. Returns product, SKU, price, stock, status, and category.")]
    public async Task<string> SearchLocalCatalogAsync([Description("Product, SKU, category, or inventory search text.")] string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return "Local catalog search skipped because the query was empty.";
        }

        logger.LogInformation("Agents.AI local catalog search tool querying catalog API with search '{Query}'", query);
        var products = await catalogApi.GetProductsAsync(search: query.Trim());

        if (products.Count == 0)
        {
            return $"Local catalog database search for '{query}' returned no matching products.";
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Local catalog database search for '{query}' returned {products.Count} product(s):");

        foreach (var product in products.Take(6))
        {
            builder.AppendLine(FormatProduct(product));
        }

        return builder.ToString().Trim();
    }

    private static string FormatSearchResult(string? title, string text, string? url)
    {
        var label = string.IsNullOrWhiteSpace(title) ? "Result" : title.Trim();
        var source = string.IsNullOrWhiteSpace(url) ? "no source URL" : url.Trim();
        return $"- {label}: {text.Trim()} Source: {source}";
    }

    private static string FormatProduct(CatalogProductDto product)
    {
        var status = product.IsActive ? "active" : "inactive";
        return $"- {product.Name} ({product.Sku}) in {product.CategoryName}: {product.Price:C}, stock {product.StockQuantity}, {status}. {product.Description}";
    }

    private static IEnumerable<DuckDuckGoRelatedTopic> FlattenRelatedTopics(IEnumerable<DuckDuckGoRelatedTopic>? topics)
    {
        if (topics is null)
        {
            yield break;
        }

        foreach (var topic in topics)
        {
            if (!string.IsNullOrWhiteSpace(topic.Text))
            {
                yield return topic;
            }

            foreach (var child in FlattenRelatedTopics(topic.Topics))
            {
                yield return child;
            }
        }
    }

    private sealed class DuckDuckGoInstantAnswerResponse
    {
        [JsonPropertyName("Heading")]
        public string? Heading { get; set; }

        [JsonPropertyName("AbstractText")]
        public string? AbstractText { get; set; }

        [JsonPropertyName("AbstractURL")]
        public string? AbstractUrl { get; set; }

        [JsonPropertyName("RelatedTopics")]
        public List<DuckDuckGoRelatedTopic>? RelatedTopics { get; set; }
    }

    private sealed class DuckDuckGoRelatedTopic
    {
        [JsonPropertyName("Text")]
        public string? Text { get; set; }

        [JsonPropertyName("FirstURL")]
        public string? FirstUrl { get; set; }

        [JsonPropertyName("Topics")]
        public List<DuckDuckGoRelatedTopic>? Topics { get; set; }
    }
}

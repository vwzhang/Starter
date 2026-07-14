using Starter.Shared;

namespace Starter.Tests;

public class CatalogContractTests
{
    [Fact]
    public void ProductSaveRequestPreservesTheFullCatalogMutationContract()
    {
        var categoryId = Guid.NewGuid();
        var request = new CatalogProductSaveRequest(
            categoryId,
            "Starter product",
            "STARTER-001",
            "A reusable template product.",
            19.95m,
            4,
            true);

        Assert.Equal(categoryId, request.CategoryId);
        Assert.Equal("STARTER-001", request.Sku);
        Assert.Equal(19.95m, request.Price);
        Assert.True(request.IsActive);
    }
}

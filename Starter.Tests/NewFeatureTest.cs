using System.Threading.Tasks;
using Xunit;

namespace Starter.Tests;

public class NewFeatureTests
{
    [Fact]
    public async Task Simple_succeeds()
    {
        // Arrange

        // Act
        await Task.Delay(1, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(true);
    }
}

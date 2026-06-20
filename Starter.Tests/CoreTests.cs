using System.Threading.Tasks;
using Xunit;

namespace Starter.Tests;
using Core;

public class CoreTests
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

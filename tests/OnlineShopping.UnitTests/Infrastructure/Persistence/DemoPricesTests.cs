using OnlineShopping.Infrastructure.Persistence.Seeding.Demo;

namespace OnlineShopping.UnitTests.Infrastructure.Persistence;

public sealed class DemoPricesTests
{
    [Theory]
    [InlineData(349, "EUR", 349)]
    [InlineData(349, "USD", 383.90)]
    [InlineData(349, "AMD", 150_100)]
    [InlineData(15, "IRR", 15_000_000)]
    [InlineData(349, "XYZ", 349)]
    public void Converts_euros_and_rounds_to_the_currency_step(decimal euros, string currency, decimal expected) =>
        Assert.Equal(expected, DemoPrices.Convert(euros, currency));
}

using OnlineShop.Application.Catalog;

namespace OnlineShop.UnitTests.Application;

public sealed class SlugsTests
{
    [Theory]
    [InlineData("Electric Kettle 1.7 L", "electric-kettle-1-7-l")]
    [InlineData("گوشی موبایل نوا X1", "گوشی-موبایل-نوا-x1")]
    [InlineData("کتری برقی ۱٫۷ لیتری", "کتری-برقی-1-7-لیتری")]
    [InlineData("سماعات لاسلكية ٢٠٢٦", "سماعات-لاسلكية-2026")]
    [InlineData("Wasserkocher für Küche", "wasserkocher-fur-kuche")]
    [InlineData("Էլեկտրական թեյնիկ", "էլեկտրական-թեյնիկ")]
    [InlineData("  --Hello,   World!--  ", "hello-world")]
    [InlineData("کیف‌پول", "کیف-پول")]
    [InlineData("مُنَظِّم", "منظم")]
    [InlineData("آنلاین‌شاپ أحمد", "آنلاین-شاپ-أحمد")]
    public void Builds_readable_slugs_in_every_script(string text, string expected) =>
        Assert.Equal(expected, Slugs.From(text));

    [Fact]
    public void Limits_the_length_without_a_trailing_hyphen()
    {
        var slug = Slugs.From(string.Join(' ', Enumerable.Repeat("word", 40)));

        Assert.True(slug.Length <= Slugs.MaxLength);
        Assert.False(slug.EndsWith('-'));
    }

    [Fact]
    public void Text_without_letters_gives_an_empty_slug() => Assert.Equal("", Slugs.From(" ! ? "));
}

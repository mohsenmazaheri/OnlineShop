namespace OnlineShop.Infrastructure.Persistence.Seeding.Demo;

/// <summary>One demo text in every language the platform renders (written by hand, not machine-translated).</summary>
public sealed record DemoText(string Fa, string En, string Hy, string De, string Ar)
{
    public static IReadOnlyList<string> Locales { get; } = ["fa", "en", "hy", "de", "ar"];

    public string For(string locale) => locale switch
    {
        "fa" => Fa,
        "en" => En,
        "hy" => Hy,
        "de" => De,
        "ar" => Ar,
        _ => En,
    };
}

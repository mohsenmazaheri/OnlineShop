using OnlineShopping.Application.Common;
using OnlineShopping.Domain.Catalog;

namespace OnlineShopping.Application.Catalog.Admin;

/// <summary>
/// Maps the admin's per-language texts onto an item: the default language into its own columns, every other language
/// into <see cref="CatalogTranslation"/> rows (added, changed, or removed when its name is left empty).
/// </summary>
public static class LocalizedTexts
{
    /// <summary>Only the shop's languages, each once, and a name in the default language.</summary>
    public static AppError? Validate(IReadOnlyList<LocalizedText> texts, StoreOptions store)
    {
        if (texts.Any(t => !store.LocaleList.Contains(t.Locale, StringComparer.Ordinal)))
            return AppError.Validation(CatalogErrors.LocaleInvalid, "A text is in a language the shop does not offer.");
        if (texts.Select(t => t.Locale).Distinct(StringComparer.Ordinal).Count() != texts.Count)
            return AppError.Validation(CatalogErrors.LocaleRepeated, "A language appears twice.");
        if (string.IsNullOrWhiteSpace(texts.FirstOrDefault(t => t.Locale == store.DefaultLocale)?.Name))
            return AppError.Validation(CatalogErrors.DefaultTextRequired, $"A name in {store.DefaultLocale} is required.");
        return null;
    }

    /// <summary>The slug to store: the given one cleaned up, or one made from the name.</summary>
    public static string SlugFor(LocalizedText text) =>
        Slugs.From(string.IsNullOrWhiteSpace(text.Slug) ? text.Name : text.Slug) is { Length: > 0 } slug ? slug : Slugs.From(text.Name);

    /// <summary>The default-language text (validated to exist).</summary>
    public static LocalizedText Default(IReadOnlyList<LocalizedText> texts, string defaultLocale) =>
        Normalize(texts.Single(t => t.Locale == defaultLocale));

    /// <summary>Writes every non-default language into <paramref name="translations"/>.</summary>
    public static void ApplyTranslations(
        IReadOnlyList<LocalizedText> texts, string defaultLocale, ICollection<CatalogTranslation> translations, bool withSlug, bool withDescription)
    {
        foreach (var text in texts.Where(t => t.Locale != defaultLocale).Select(Normalize))
        {
            var existing = translations.FirstOrDefault(t => t.Locale == text.Locale);
            if (text.Name.Length == 0)
            {
                if (existing is not null)
                    translations.Remove(existing);
                continue;
            }

            var target = existing ?? new CatalogTranslation { Locale = text.Locale, Name = text.Name };
            target.Name = text.Name;
            target.Slug = withSlug ? SlugFor(text) : null;
            target.Description = withDescription ? text.Description : null;
            if (existing is null)
                translations.Add(target);
        }
    }

    /// <summary>The texts as the admin edits them: the default language first, then one entry per other shop language.</summary>
    public static List<LocalizedText> Read(
        StoreOptions store, string name, string? slug, string? description, IEnumerable<CatalogTranslation> translations)
    {
        var byLocale = translations.ToDictionary(t => t.Locale, StringComparer.Ordinal);
        return store.LocaleList
            .OrderBy(l => l == store.DefaultLocale ? 0 : 1)
            .Select(l => l == store.DefaultLocale
                ? new LocalizedText(l, name, slug, description)
                : byLocale.TryGetValue(l, out var t)
                    ? new LocalizedText(l, t.Name, t.Slug, t.Description)
                    : new LocalizedText(l, "", null, null))
            .ToList();
    }

    /// <summary>Shop languages (other than the default) without a translation.</summary>
    public static List<string> Missing(StoreOptions store, IEnumerable<string> translatedLocales)
    {
        var translated = translatedLocales.ToHashSet(StringComparer.Ordinal);
        return store.LocaleList.Where(l => l != store.DefaultLocale && !translated.Contains(l)).ToList();
    }

    private static LocalizedText Normalize(LocalizedText text) => text with
    {
        Name = text.Name?.Trim() ?? "",
        Slug = string.IsNullOrWhiteSpace(text.Slug) ? null : text.Slug.Trim(),
        Description = string.IsNullOrWhiteSpace(text.Description) ? null : text.Description.Trim(),
    };
}

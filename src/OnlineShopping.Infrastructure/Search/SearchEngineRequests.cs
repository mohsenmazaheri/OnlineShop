using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using OnlineShopping.Application.Search;
using OnlineShopping.Domain.Search;

namespace OnlineShopping.Infrastructure.Search;

/// <summary>
/// The request bodies the shop sends to OpenSearch or Elasticsearch (both accept them unchanged) and the reading of
/// their answers. Kept apart from the HTTP calls so every body is tested on its own.
/// </summary>
public static class SearchEngineRequests
{
    /// <summary>Fields and weights, as in the database engines: title over keywords over details/codes over description.</summary>
    public static readonly string[] Fields = ["title^4", "keywords^2", "details", "codes", "description^0.5"];

    /// <summary>The engine's built-in analyzer for a language (stemming, stop words), "standard" when it has none.</summary>
    public static string LanguageAnalyzer(string locale)
    {
        ArgumentNullException.ThrowIfNull(locale);
        return locale.Split('-')[0].ToLowerInvariant() switch
        {
            "fa" => "persian",
            "ar" => "arabic",
            "hy" => "armenian",
            "de" => "german",
            "en" => "english",
            "fr" => "french",
            "es" => "spanish",
            "it" => "italian",
            "ru" => "russian",
            "tr" => "turkish",
            _ => "standard",
        };
    }

    /// <summary>
    /// A language's index: one shard, the language analyzer on the texts, codes split on spaces only, and a "words"
    /// field (title and keywords as written) for "did you mean". Unknown fields are refused.
    /// </summary>
    public static JsonObject IndexDefinition(string locale, int replicas)
    {
        var language = LanguageAnalyzer(locale);
        static JsonObject Text(string analyzer, bool words = false)
        {
            var field = new JsonObject { ["type"] = "text", ["analyzer"] = analyzer };
            if (words)
                field["copy_to"] = "words";
            return field;
        }
        return new JsonObject
        {
            ["settings"] = new JsonObject
            {
                ["number_of_shards"] = 1,
                ["number_of_replicas"] = replicas,
                ["analysis"] = new JsonObject
                {
                    ["analyzer"] = new JsonObject
                    {
                        ["plain"] = new JsonObject { ["type"] = "custom", ["tokenizer"] = "whitespace", ["filter"] = new JsonArray("lowercase") },
                    },
                },
            },
            ["mappings"] = new JsonObject
            {
                ["dynamic"] = "strict",
                ["properties"] = new JsonObject
                {
                    ["title"] = Text(language, words: true),
                    ["keywords"] = Text(language, words: true),
                    ["details"] = Text(language),
                    ["codes"] = Text("plain"),
                    ["description"] = Text(language),
                    ["words"] = Text("plain"),
                    ["updated"] = new JsonObject { ["type"] = "date" },
                },
            },
        };
    }

    /// <summary>
    /// Every word must match through one of its alternatives (synonyms), each as a prefix ("sams" finds "samsung") or
    /// with a typo or two (fuzziness AUTO). Only ids and scores come back.
    /// </summary>
    public static JsonObject Match(SearchTerms terms, int limit)
    {
        ArgumentNullException.ThrowIfNull(terms);
        var must = new JsonArray();
        foreach (var alternatives in terms.Words)
        {
            var should = new JsonArray();
            foreach (var phrase in alternatives)
            {
                should.Add(new JsonObject
                {
                    ["multi_match"] = new JsonObject { ["query"] = phrase, ["type"] = "bool_prefix", ["fields"] = FieldList(), ["operator"] = "and" },
                });
                should.Add(new JsonObject
                {
                    ["multi_match"] = new JsonObject { ["query"] = phrase, ["fields"] = FieldList(), ["fuzziness"] = "AUTO", ["prefix_length"] = 1, ["operator"] = "and" },
                });
            }
            must.Add(new JsonObject { ["bool"] = new JsonObject { ["should"] = should, ["minimum_should_match"] = 1 } });
        }
        return new JsonObject
        {
            ["size"] = limit,
            ["_source"] = false,
            ["track_total_hits"] = false,
            ["query"] = new JsonObject { ["bool"] = new JsonObject { ["must"] = must } },
        };
    }

    /// <summary>The closest word of the titles and keywords (one edit for short words, two from seven letters).</summary>
    public static JsonObject SimilarWord(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        return new JsonObject
        {
            ["size"] = 0,
            ["suggest"] = new JsonObject
            {
                ["word"] = new JsonObject
                {
                    ["text"] = word,
                    ["term"] = new JsonObject
                    {
                        ["field"] = "words",
                        ["suggest_mode"] = "always",
                        ["min_word_length"] = 3,
                        ["max_edits"] = word.Length >= 7 ? 2 : 1,
                        ["sort"] = "score",
                    },
                },
            },
        };
    }

    /// <summary>A bulk body (NDJSON): every upsert as an index action with its document, every delete by product id.</summary>
    public static string Bulk(string index, IEnumerable<SearchDocument> upserts, IEnumerable<Guid> deletes)
    {
        ArgumentNullException.ThrowIfNull(upserts);
        ArgumentNullException.ThrowIfNull(deletes);
        var body = new StringBuilder();
        foreach (var d in upserts)
        {
            body.Append(new JsonObject { ["index"] = new JsonObject { ["_index"] = index, ["_id"] = Id(d.ProductId) } }.ToJsonString()).Append('\n');
            body.Append(new JsonObject
            {
                ["title"] = d.Title,
                ["keywords"] = d.Keywords ?? "",
                ["details"] = d.Details ?? "",
                ["codes"] = d.Codes ?? "",
                ["description"] = d.Description ?? "",
                ["updated"] = d.UpdatedUtc.ToString("O", CultureInfo.InvariantCulture),
            }.ToJsonString()).Append('\n');
        }
        foreach (var id in deletes)
            body.Append(new JsonObject { ["delete"] = new JsonObject { ["_index"] = index, ["_id"] = Id(id) } }.ToJsonString()).Append('\n');
        return body.ToString();
    }

    /// <summary>Moves the alias to the new index and removes the older ones, in one atomic step.</summary>
    public static JsonObject Publish(string alias, string index, IEnumerable<string> older)
    {
        ArgumentNullException.ThrowIfNull(older);
        var actions = new JsonArray { new JsonObject { ["add"] = new JsonObject { ["index"] = index, ["alias"] = alias } } };
        foreach (var old in older.Where(o => o != index))
            actions.Add(new JsonObject { ["remove_index"] = new JsonObject { ["index"] = old } });
        return new JsonObject { ["actions"] = actions };
    }

    /// <summary>Product ids and scores of a search answer.</summary>
    public static IReadOnlyDictionary<Guid, double> ReadHits(JsonNode? answer)
    {
        var hits = new Dictionary<Guid, double>();
        if (answer?["hits"]?["hits"] is not JsonArray list)
            throw new SearchEngineException("The search answer has no hits.");
        foreach (var hit in list)
        {
            if (Guid.TryParse(hit?["_id"]?.GetValue<string>(), out var id))
                hits[id] = hit?["_score"]?.GetValue<double>() ?? 0;
        }
        return hits;
    }

    /// <summary>The best suggestion that is not the word itself, or null.</summary>
    public static string? ReadSuggestion(JsonNode? answer, string word)
    {
        if (answer?["suggest"]?["word"] is not JsonArray entries)
            return null;
        return entries.OfType<JsonObject>()
            .SelectMany(e => e["options"] as JsonArray ?? [])
            .Select(o => o?["text"]?.GetValue<string>())
            .FirstOrDefault(t => !string.IsNullOrEmpty(t) && t != word);
    }

    /// <summary>Throws when a bulk write refused an item (a delete of a missing document is fine).</summary>
    public static void EnsureBulkSucceeded(JsonNode? answer)
    {
        if (answer?["errors"]?.GetValue<bool>() != true)
            return;
        foreach (var item in answer["items"] as JsonArray ?? [])
        {
            if (item is not JsonObject action || action.Count == 0)
                continue;
            var (kind, result) = action.First();
            var status = result?["status"]?.GetValue<int>() ?? 0;
            if (status < 300 || (kind == "delete" && status == 404))
                continue;
            var error = result?["error"];
            throw new SearchEngineException($"The search engine refused a {kind} ({status}): {error?["type"]} {error?["reason"]}".TrimEnd());
        }
    }

    /// <summary>The names in a "_cat/indices" answer that are physical indexes of this alias ({alias}-{17 digits}).</summary>
    public static IReadOnlyList<string> ReadIndexNames(JsonNode? answer, string alias)
    {
        ArgumentNullException.ThrowIfNull(alias);
        return (answer as JsonArray ?? [])
            .Select(i => i?["index"]?.GetValue<string>())
            .OfType<string>()
            .Where(name => name.Length == alias.Length + 18 && name.StartsWith(alias + "-", StringComparison.Ordinal) && IsPhysicalIndex(name))
            .ToList();
    }

    /// <summary>Whether the name is a physical index ({alias}-{17-digit timestamp}) rather than an alias.</summary>
    public static bool IsPhysicalIndex(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Length > 18 && name[^18] == '-' && name[^17..].All(char.IsAsciiDigit);
    }

    private static string Id(Guid id) => id.ToString("D");

    private static JsonArray FieldList() => new(Fields.Select(f => (JsonNode?)JsonValue.Create(f)).ToArray());
}

namespace Lilia.Api.Services;

/// <summary>
/// Service for generating Lorem Ipsum placeholder text.
/// </summary>
public interface ILoremIpsumService
{
    /// <summary>
    /// Generate Lorem Ipsum paragraphs.
    /// </summary>
    string GenerateParagraphs(int count, bool startWithLoremIpsum = true);

    /// <summary>
    /// Generate Lorem Ipsum sentences.
    /// </summary>
    string GenerateSentences(int count, bool startWithLoremIpsum = true);

    /// <summary>
    /// Generate Lorem Ipsum words.
    /// </summary>
    string GenerateWords(int count, bool startWithLoremIpsum = true);
}

/// <summary>
/// Lorem Ipsum text generator implementation.
/// </summary>
public class LoremIpsumService : ILoremIpsumService
{
    private static readonly string[] LoremWords =
    [
        "lorem", "ipsum", "dolor", "sit", "amet", "consectetur", "adipiscing", "elit",
        "sed", "do", "eiusmod", "tempor", "incididunt", "ut", "labore", "et", "dolore",
        "magna", "aliqua", "enim", "ad", "minim", "veniam", "quis", "nostrud",
        "exercitation", "ullamco", "laboris", "nisi", "aliquip", "ex", "ea", "commodo",
        "consequat", "duis", "aute", "irure", "in", "reprehenderit", "voluptate",
        "velit", "esse", "cillum", "fugiat", "nulla", "pariatur", "excepteur", "sint",
        "occaecat", "cupidatat", "non", "proident", "sunt", "culpa", "qui", "officia",
        "deserunt", "mollit", "anim", "id", "est", "laborum", "at", "vero", "eos",
        "accusamus", "iusto", "odio", "dignissimos", "ducimus", "blanditiis",
        "praesentium", "voluptatum", "deleniti", "atque", "corrupti", "quos", "dolores",
        "quas", "molestias", "excepturi", "obcaecati", "cupiditate", "provident",
        "similique", "mollitia", "animi", "dolorum", "fuga", "harum", "quidem", "rerum",
        "facilis", "expedita", "distinctio", "nam", "libero", "tempore", "cum", "soluta",
        "nobis", "eligendi", "optio", "cumque", "nihil", "impedit", "quo", "minus",
        "quod", "maxime", "placeat", "facere", "possimus", "omnis", "voluptas",
        "assumenda", "repellendus", "temporibus", "autem", "quibusdam", "officiis",
        "debitis", "aut", "necessitatibus", "saepe", "eveniet", "voluptates",
        "repudiandae", "recusandae", "itaque", "earum", "hic", "tenetur", "sapiente",
        "delectus", "reiciendis", "voluptatibus", "maiores", "alias", "perferendis",
        "doloribus", "asperiores", "repellat"
    ];

    private static readonly string ClassicOpening =
        "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.";

    private readonly Random _random = new();

    public string GenerateParagraphs(int count, bool startWithLoremIpsum = true)
    {
        if (count <= 0) return string.Empty;

        var paragraphs = new List<string>();

        for (int i = 0; i < count; i++)
        {
            var sentenceCount = _random.Next(4, 8);
            var paragraph = GenerateSentences(sentenceCount, startWithLoremIpsum && i == 0);
            paragraphs.Add(paragraph);
        }

        return string.Join("\n\n", paragraphs);
    }

    public string GenerateSentences(int count, bool startWithLoremIpsum = true)
    {
        if (count <= 0) return string.Empty;

        var sentences = new List<string>();

        for (int i = 0; i < count; i++)
        {
            if (startWithLoremIpsum && i == 0)
            {
                sentences.Add(ClassicOpening);
            }
            else
            {
                sentences.Add(GenerateRandomSentence());
            }
        }

        return string.Join(" ", sentences);
    }

    public string GenerateWords(int count, bool startWithLoremIpsum = true)
    {
        if (count <= 0) return string.Empty;

        var words = new List<string>();

        if (startWithLoremIpsum)
        {
            words.AddRange(["Lorem", "ipsum", "dolor", "sit", "amet"]);
            count -= 5;
        }

        for (int i = 0; i < count; i++)
        {
            words.Add(GetRandomWord());
        }

        return string.Join(" ", words);
    }

    private string GenerateRandomSentence()
    {
        var wordCount = _random.Next(8, 16);
        var words = new List<string>();

        for (int i = 0; i < wordCount; i++)
        {
            words.Add(GetRandomWord());
        }

        // Capitalize first word
        if (words.Count > 0)
        {
            words[0] = char.ToUpper(words[0][0]) + words[0][1..];
        }

        // Add random comma
        if (wordCount > 6 && _random.Next(3) == 0)
        {
            var commaPos = _random.Next(3, wordCount - 2);
            words[commaPos] += ",";
        }

        return string.Join(" ", words) + ".";
    }

    private string GetRandomWord()
    {
        return LoremWords[_random.Next(LoremWords.Length)];
    }
}

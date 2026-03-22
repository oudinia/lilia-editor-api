using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Lilia.Core.Entities;
using Lilia.Core.Interfaces;
using Lilia.Core.Models.Epub;

namespace Lilia.Api.Services;

public partial class EpubService : IEpubService
{
    private static readonly XNamespace OpfNs = "http://www.idpf.org/2007/opf";
    private static readonly XNamespace DcNs = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace ContainerNs = "urn:oasis:names:tc:opendocument:xmlns:container";
    private static readonly XNamespace XhtmlNs = "http://www.w3.org/1999/xhtml";
    private static readonly XNamespace EpubTypeNs = "http://www.idpf.org/2007/ops";

    private readonly ILogger<EpubService> _logger;

    public EpubService(ILogger<EpubService> logger)
    {
        _logger = logger;
    }

    // ────────────────────────── Import ──────────────────────────

    public async Task<(EpubMetadata Metadata, List<Block> Blocks, List<string> Warnings)> ImportAsync(Stream epubStream)
    {
        var warnings = new List<string>();
        using var ms = await CopyToMemoryStreamAsync(epubStream);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        var opfPath = FindOpfPath(zip);
        if (opfPath == null)
        {
            warnings.Add("Could not find OPF file via META-INF/container.xml");
            return (new EpubMetadata("Unknown"), new List<Block>(), warnings);
        }

        var opfDir = GetDirectoryPath(opfPath);
        var opfEntry = zip.GetEntry(opfPath);
        if (opfEntry == null)
        {
            warnings.Add($"OPF file not found in archive: {opfPath}");
            return (new EpubMetadata("Unknown"), new List<Block>(), warnings);
        }

        var opfDoc = await LoadXmlAsync(opfEntry);
        var metadata = ExtractMetadata(opfDoc);
        var spineItems = GetSpineItems(opfDoc, opfDir);

        var blocks = new List<Block>();
        var sortOrder = 0;
        var isFirstFile = true;

        foreach (var itemPath in spineItems)
        {
            var entry = zip.GetEntry(itemPath);
            if (entry == null)
            {
                warnings.Add($"Spine item not found: {itemPath}");
                continue;
            }

            if (!isFirstFile)
            {
                blocks.Add(CreateBlock(BlockTypes.ChapterBreak, new { }, sortOrder++));
            }
            isFirstFile = false;

            try
            {
                var xhtmlDoc = await LoadXmlAsync(entry);
                var body = xhtmlDoc.Descendants(XhtmlNs + "body").FirstOrDefault()
                           ?? xhtmlDoc.Descendants("body").FirstOrDefault();

                if (body == null)
                {
                    warnings.Add($"No <body> found in {itemPath}");
                    continue;
                }

                var epubType = body.Attribute(EpubTypeNs + "type")?.Value
                               ?? GetPrefixedAttribute(body, "epub:type");

                if (epubType != null && epubType.Contains("frontmatter"))
                {
                    blocks.Add(CreateBlock(BlockTypes.FrontMatter, new { text = GetInnerText(body) }, sortOrder++));
                    continue;
                }
                if (epubType != null && epubType.Contains("backmatter"))
                {
                    blocks.Add(CreateBlock(BlockTypes.BackMatter, new { text = GetInnerText(body) }, sortOrder++));
                    continue;
                }

                ExtractBlocksFromElement(body, blocks, ref sortOrder, warnings);
            }
            catch (Exception ex)
            {
                warnings.Add($"Error parsing {itemPath}: {ex.Message}");
            }
        }

        return (metadata, blocks, warnings);
    }

    // ────────────────────────── Analyze ──────────────────────────

    public async Task<EpubAnalysisResult> AnalyzeAsync(Stream epubStream)
    {
        using var ms = await CopyToMemoryStreamAsync(epubStream);
        var fileSize = ms.Length;

        ms.Position = 0;
        var (metadata, blocks, warnings) = await ImportAsync(ms);

        var issues = new List<EpubIssue>();

        // Metadata checks
        if (string.IsNullOrWhiteSpace(metadata.Title) || metadata.Title == "Unknown")
            issues.Add(new EpubIssue("metadata", "error", "Missing document title"));
        if (string.IsNullOrWhiteSpace(metadata.Author))
            issues.Add(new EpubIssue("metadata", "warning", "Missing author metadata"));
        if (string.IsNullOrWhiteSpace(metadata.Language))
            issues.Add(new EpubIssue("metadata", "warning", "Missing language metadata"));

        // Structure checks
        var headings = blocks.Where(b => b.Type == BlockTypes.Heading).ToList();
        if (headings.Count == 0)
            issues.Add(new EpubIssue("structure", "warning", "No headings found in document"));

        // Check heading hierarchy
        CheckHeadingHierarchy(headings, issues);

        // Check for ToC
        var hasToc = blocks.Any(b => b.Type == BlockTypes.TableOfContents);
        if (!hasToc)
            issues.Add(new EpubIssue("structure", "info", "No table of contents detected"));

        // Accessibility checks — images without alt text
        var imageCount = 0;
        foreach (var block in blocks.Where(b => b.Type == BlockTypes.Figure))
        {
            imageCount++;
            var alt = GetJsonProperty(block.Content, "alt");
            if (string.IsNullOrWhiteSpace(alt))
                issues.Add(new EpubIssue("accessibility", "warning", "Image missing alt text", $"Block {block.SortOrder}"));
        }

        // Formatting checks — look for inline styles in warnings
        var inlineStyleWarnings = warnings.Count(w => w.Contains("inline style"));
        if (inlineStyleWarnings > 0)
            issues.Add(new EpubIssue("formatting", "warning", $"Found {inlineStyleWarnings} elements with inline styles"));

        // Add import warnings as issues
        foreach (var w in warnings)
        {
            if (!w.Contains("inline style"))
                issues.Add(new EpubIssue("encoding", "info", w));
        }

        var chapterCount = blocks.Count(b => b.Type == BlockTypes.ChapterBreak) + 1;

        return new EpubAnalysisResult(metadata, issues, chapterCount, blocks.Count, imageCount, fileSize);
    }

    // ────────────────────────── Export ──────────────────────────

    public async Task<byte[]> ExportAsync(List<Block> blocks, EpubExportOptions options)
    {
        using var outputMs = new MemoryStream();
        using (var zip = new ZipArchive(outputMs, ZipArchiveMode.Create, leaveOpen: true))
        {
            // mimetype must be first entry, uncompressed
            await AddEntryAsync(zip, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);

            // META-INF/container.xml
            await AddEntryAsync(zip, "META-INF/container.xml", GenerateContainerXml());

            // Split blocks into chapters
            var chapters = SplitIntoChapters(blocks);

            // Generate CSS
            var css = options.CssContent ?? GetDefaultCss();
            await AddEntryAsync(zip, "OEBPS/style.css", css);

            // Generate chapter XHTML files
            var manifestItems = new List<(string Id, string Href, string MediaType)>();
            var spineItems = new List<string>();

            manifestItems.Add(("style", "style.css", "text/css"));
            manifestItems.Add(("nav", "nav.xhtml", "application/xhtml+xml"));

            for (var i = 0; i < chapters.Count; i++)
            {
                var chapterId = $"chapter{i + 1}";
                var fileName = $"{chapterId}.xhtml";
                var xhtml = GenerateChapterXhtml(chapters[i], options.Title, css != null);
                await AddEntryAsync(zip, $"OEBPS/{fileName}", xhtml);
                manifestItems.Add((chapterId, fileName, "application/xhtml+xml"));
                spineItems.Add(chapterId);
            }

            // Generate nav.xhtml
            var navXhtml = GenerateNavXhtml(blocks, chapters, options.Title);
            await AddEntryAsync(zip, "OEBPS/nav.xhtml", navXhtml);

            // Generate toc.ncx (EPUB 2 compat)
            var tocNcx = GenerateTocNcx(blocks, chapters, options.Title);
            await AddEntryAsync(zip, "OEBPS/toc.ncx", tocNcx);
            manifestItems.Add(("ncx", "toc.ncx", "application/x-dtbncx+xml"));

            // Generate content.opf
            var opf = GenerateContentOpf(options, manifestItems, spineItems);
            await AddEntryAsync(zip, "OEBPS/content.opf", opf);
        }

        return outputMs.ToArray();
    }

    // ────────────────────────── Clean & Repackage ──────────────────────────

    public async Task<byte[]> CleanAndRepackageAsync(Stream epubStream, EpubExportOptions? options = null)
    {
        var (metadata, blocks, _) = await ImportAsync(epubStream);

        // Clean blocks: strip inline styles from content
        foreach (var block in blocks)
        {
            CleanBlockContent(block);
        }

        var exportOptions = options ?? new EpubExportOptions(
            Title: metadata.Title,
            Author: metadata.Author,
            Language: metadata.Language,
            Publisher: metadata.Publisher,
            Isbn: metadata.Isbn
        );

        return await ExportAsync(blocks, exportOptions);
    }

    // ────────────────────────── XHTML Parsing ──────────────────────────

    internal static void ExtractBlocksFromElement(XElement element, List<Block> blocks, ref int sortOrder, List<string> warnings)
    {
        foreach (var child in element.Elements())
        {
            var localName = child.Name.LocalName.ToLowerInvariant();
            var style = child.Attribute("style")?.Value;
            if (!string.IsNullOrEmpty(style))
                warnings.Add($"Detected inline style on <{localName}>");

            var epubType = child.Attribute(EpubTypeNs + "type")?.Value
                           ?? GetPrefixedAttribute(child, "epub:type");

            switch (localName)
            {
                case "h1": case "h2": case "h3": case "h4": case "h5": case "h6":
                    var level = int.Parse(localName[1..]);
                    blocks.Add(CreateBlock(BlockTypes.Heading, new { text = GetInnerText(child), level }, sortOrder++));
                    break;

                case "p":
                    var text = GetInnerText(child);
                    if (!string.IsNullOrWhiteSpace(text))
                        blocks.Add(CreateBlock(BlockTypes.Paragraph, new { text }, sortOrder++));
                    break;

                case "figure":
                    var img = child.Descendants(XhtmlNs + "img").FirstOrDefault()
                              ?? child.Descendants("img").FirstOrDefault();
                    var figcaption = child.Descendants(XhtmlNs + "figcaption").FirstOrDefault()
                                    ?? child.Descendants("figcaption").FirstOrDefault();
                    blocks.Add(CreateBlock(BlockTypes.Figure, new
                    {
                        src = img?.Attribute("src")?.Value ?? "",
                        caption = figcaption != null ? GetInnerText(figcaption) : "",
                        alt = img?.Attribute("alt")?.Value ?? ""
                    }, sortOrder++));
                    break;

                case "img":
                    blocks.Add(CreateBlock(BlockTypes.Figure, new
                    {
                        src = child.Attribute("src")?.Value ?? "",
                        caption = "",
                        alt = child.Attribute("alt")?.Value ?? ""
                    }, sortOrder++));
                    break;

                case "ul":
                    var ulItems = child.Elements()
                        .Where(li => li.Name.LocalName.Equals("li", StringComparison.OrdinalIgnoreCase))
                        .Select(li => GetInnerText(li))
                        .ToArray();
                    blocks.Add(CreateBlock(BlockTypes.List, new { items = ulItems, ordered = false }, sortOrder++));
                    break;

                case "ol":
                    var olItems = child.Elements()
                        .Where(li => li.Name.LocalName.Equals("li", StringComparison.OrdinalIgnoreCase))
                        .Select(li => GetInnerText(li))
                        .ToArray();
                    blocks.Add(CreateBlock(BlockTypes.List, new { items = olItems, ordered = true }, sortOrder++));
                    break;

                case "pre":
                    var codeEl = child.Descendants(XhtmlNs + "code").FirstOrDefault()
                                 ?? child.Descendants("code").FirstOrDefault();
                    var code = codeEl != null ? GetInnerText(codeEl) : GetInnerText(child);
                    blocks.Add(CreateBlock(BlockTypes.Code, new { code, language = "text" }, sortOrder++));
                    break;

                case "blockquote":
                    blocks.Add(CreateBlock(BlockTypes.Blockquote, new { text = GetInnerText(child) }, sortOrder++));
                    break;

                case "table":
                    var tableData = ParseTable(child);
                    blocks.Add(CreateBlock(BlockTypes.Table, tableData, sortOrder++));
                    break;

                case "aside":
                    if (epubType != null && epubType.Contains("annotation"))
                        blocks.Add(CreateBlock(BlockTypes.Annotation, new { text = GetInnerText(child) }, sortOrder++));
                    else
                        blocks.Add(CreateBlock(BlockTypes.Aside, new { text = GetInnerText(child) }, sortOrder++));
                    break;

                case "section":
                    if (epubType != null && epubType.Contains("frontmatter"))
                        blocks.Add(CreateBlock(BlockTypes.FrontMatter, new { text = GetInnerText(child) }, sortOrder++));
                    else if (epubType != null && epubType.Contains("backmatter"))
                        blocks.Add(CreateBlock(BlockTypes.BackMatter, new { text = GetInnerText(child) }, sortOrder++));
                    else
                        ExtractBlocksFromElement(child, blocks, ref sortOrder, warnings);
                    break;

                case "div":
                    // Recurse into divs
                    ExtractBlocksFromElement(child, blocks, ref sortOrder, warnings);
                    break;

                default:
                    // For verse-like elements or unknown, try to extract text
                    var unknownText = GetInnerText(child);
                    if (!string.IsNullOrWhiteSpace(unknownText))
                        blocks.Add(CreateBlock(BlockTypes.Paragraph, new { text = unknownText }, sortOrder++));
                    break;
            }
        }
    }

    // ────────────────────────── OPF / Container Parsing ──────────────────────────

    internal static string? FindOpfPath(ZipArchive zip)
    {
        var containerEntry = zip.GetEntry("META-INF/container.xml");
        if (containerEntry == null) return null;

        using var stream = containerEntry.Open();
        var doc = XDocument.Load(stream);
        var rootfile = doc.Descendants(ContainerNs + "rootfile").FirstOrDefault()
                       ?? doc.Descendants("rootfile").FirstOrDefault();
        return rootfile?.Attribute("full-path")?.Value;
    }

    internal static EpubMetadata ExtractMetadata(XDocument opfDoc)
    {
        var metadataEl = opfDoc.Descendants(OpfNs + "metadata").FirstOrDefault()
                         ?? opfDoc.Descendants("metadata").FirstOrDefault();

        if (metadataEl == null)
            return new EpubMetadata("Unknown");

        string GetDcValue(string name) =>
            metadataEl.Descendants(DcNs + name).FirstOrDefault()?.Value
            ?? metadataEl.Descendants(name).FirstOrDefault()?.Value
            ?? "";

        var title = GetDcValue("title");
        var author = GetDcValue("creator");
        var language = GetDcValue("language");
        var publisher = GetDcValue("publisher");
        var description = GetDcValue("description");

        // Look for ISBN in dc:identifier
        string? isbn = null;
        foreach (var id in metadataEl.Descendants(DcNs + "identifier")
                     .Concat(metadataEl.Descendants("identifier")))
        {
            var val = id.Value;
            var scheme = id.Attribute(OpfNs + "scheme")?.Value ?? id.Attribute("scheme")?.Value;
            if (scheme?.Equals("ISBN", StringComparison.OrdinalIgnoreCase) == true || IsbnRegex().IsMatch(val))
            {
                isbn = val;
                break;
            }
        }

        return new EpubMetadata(
            Title: string.IsNullOrWhiteSpace(title) ? "Unknown" : title,
            Author: string.IsNullOrWhiteSpace(author) ? null : author,
            Language: string.IsNullOrWhiteSpace(language) ? null : language,
            Publisher: string.IsNullOrWhiteSpace(publisher) ? null : publisher,
            Isbn: isbn,
            Description: string.IsNullOrWhiteSpace(description) ? null : description
        );
    }

    internal static List<string> GetSpineItems(XDocument opfDoc, string opfDir)
    {
        var manifest = opfDoc.Descendants(OpfNs + "manifest").FirstOrDefault()
                       ?? opfDoc.Descendants("manifest").FirstOrDefault();
        var spine = opfDoc.Descendants(OpfNs + "spine").FirstOrDefault()
                    ?? opfDoc.Descendants("spine").FirstOrDefault();

        if (manifest == null || spine == null) return new List<string>();

        var itemMap = new Dictionary<string, string>();
        foreach (var item in manifest.Elements(OpfNs + "item").Concat(manifest.Elements("item")))
        {
            var id = item.Attribute("id")?.Value;
            var href = item.Attribute("href")?.Value;
            if (id != null && href != null)
                itemMap[id] = href;
        }

        var result = new List<string>();
        foreach (var itemref in spine.Elements(OpfNs + "itemref").Concat(spine.Elements("itemref")))
        {
            var idref = itemref.Attribute("idref")?.Value;
            if (idref != null && itemMap.TryGetValue(idref, out var href))
            {
                var fullPath = string.IsNullOrEmpty(opfDir) ? href : $"{opfDir}/{href}";
                result.Add(fullPath);
            }
        }

        return result;
    }

    // ────────────────────────── Export Helpers ──────────────────────────

    internal static List<List<Block>> SplitIntoChapters(List<Block> blocks)
    {
        var chapters = new List<List<Block>>();
        var current = new List<Block>();

        foreach (var block in blocks)
        {
            if (block.Type == BlockTypes.ChapterBreak ||
                (block.Type == BlockTypes.Heading && GetJsonPropertyInt(block.Content, "level") == 1 && current.Count > 0))
            {
                if (current.Count > 0)
                    chapters.Add(current);
                current = new List<Block>();

                if (block.Type == BlockTypes.ChapterBreak)
                    continue;
            }

            current.Add(block);
        }

        if (current.Count > 0)
            chapters.Add(current);

        if (chapters.Count == 0)
            chapters.Add(new List<Block>());

        return chapters;
    }

    internal static string GenerateChapterXhtml(List<Block> blocks, string title, bool hasStylesheet)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\">");
        sb.AppendLine("<head>");
        sb.AppendLine($"  <title>{EscapeXml(title)}</title>");
        if (hasStylesheet)
            sb.AppendLine("  <link rel=\"stylesheet\" type=\"text/css\" href=\"style.css\"/>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        foreach (var block in blocks)
        {
            sb.AppendLine(BlockToXhtml(block));
        }

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    internal static string BlockToXhtml(Block block)
    {
        var content = block.Content;

        return block.Type switch
        {
            BlockTypes.Heading => $"<h{GetJsonPropertyInt(content, "level", 1)}>{EscapeXml(GetJsonProperty(content, "text"))}</h{GetJsonPropertyInt(content, "level", 1)}>",
            BlockTypes.Paragraph => $"<p>{EscapeXml(GetJsonProperty(content, "text"))}</p>",
            BlockTypes.Figure => GenerateFigureXhtml(content),
            BlockTypes.Code => $"<pre><code>{EscapeXml(GetJsonProperty(content, "code"))}</code></pre>",
            BlockTypes.Blockquote => $"<blockquote><p>{EscapeXml(GetJsonProperty(content, "text"))}</p></blockquote>",
            BlockTypes.List => GenerateListXhtml(content),
            BlockTypes.Table => GenerateTableXhtml(content),
            BlockTypes.FrontMatter => $"<section epub:type=\"frontmatter\"><p>{EscapeXml(GetJsonProperty(content, "text"))}</p></section>",
            BlockTypes.BackMatter => $"<section epub:type=\"backmatter\"><p>{EscapeXml(GetJsonProperty(content, "text"))}</p></section>",
            BlockTypes.Verse => $"<p class=\"verse\">{EscapeXml(GetJsonProperty(content, "text"))}</p>",
            BlockTypes.Aside => $"<aside>{EscapeXml(GetJsonProperty(content, "text"))}</aside>",
            BlockTypes.Annotation => $"<aside epub:type=\"annotation\">{EscapeXml(GetJsonProperty(content, "text"))}</aside>",
            BlockTypes.Cover => $"<div class=\"cover\"><img src=\"{EscapeXml(GetJsonProperty(content, "src"))}\" alt=\"Cover\"/></div>",
            BlockTypes.Abstract => $"<section class=\"abstract\"><h2>{EscapeXml(GetJsonProperty(content, "title", "Abstract"))}</h2><p>{EscapeXml(GetJsonProperty(content, "text"))}</p></section>",
            BlockTypes.Equation => $"<p class=\"equation\">{EscapeXml(GetJsonProperty(content, "latex"))}</p>",
            _ => $"<p>{EscapeXml(GetJsonProperty(content, "text"))}</p>"
        };
    }

    internal static string GenerateContentOpf(EpubExportOptions options, List<(string Id, string Href, string MediaType)> manifestItems, List<string> spineItems)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"bookid\">");
        sb.AppendLine("  <metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\">");
        sb.AppendLine($"    <dc:title>{EscapeXml(options.Title)}</dc:title>");
        if (!string.IsNullOrEmpty(options.Author))
            sb.AppendLine($"    <dc:creator>{EscapeXml(options.Author)}</dc:creator>");
        sb.AppendLine($"    <dc:language>{EscapeXml(options.Language ?? "en")}</dc:language>");
        if (!string.IsNullOrEmpty(options.Publisher))
            sb.AppendLine($"    <dc:publisher>{EscapeXml(options.Publisher)}</dc:publisher>");
        if (!string.IsNullOrEmpty(options.Isbn))
            sb.AppendLine($"    <dc:identifier id=\"bookid\">{EscapeXml(options.Isbn)}</dc:identifier>");
        else
            sb.AppendLine($"    <dc:identifier id=\"bookid\">urn:uuid:{Guid.NewGuid()}</dc:identifier>");
        sb.AppendLine($"    <meta property=\"dcterms:modified\">{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</meta>");
        sb.AppendLine("  </metadata>");

        sb.AppendLine("  <manifest>");
        foreach (var (id, href, mediaType) in manifestItems)
        {
            var props = id == "nav" ? " properties=\"nav\"" : "";
            sb.AppendLine($"    <item id=\"{EscapeXml(id)}\" href=\"{EscapeXml(href)}\" media-type=\"{EscapeXml(mediaType)}\"{props}/>");
        }
        sb.AppendLine("  </manifest>");

        sb.AppendLine("  <spine toc=\"ncx\">");
        foreach (var id in spineItems)
        {
            sb.AppendLine($"    <itemref idref=\"{EscapeXml(id)}\"/>");
        }
        sb.AppendLine("  </spine>");

        sb.AppendLine("</package>");
        return sb.ToString();
    }

    internal static string GenerateNavXhtml(List<Block> allBlocks, List<List<Block>> chapters, string title)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\">");
        sb.AppendLine("<head><title>Table of Contents</title></head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<nav epub:type=\"toc\" id=\"toc\">");
        sb.AppendLine($"<h1>{EscapeXml(title)}</h1>");
        sb.AppendLine("<ol>");

        for (var i = 0; i < chapters.Count; i++)
        {
            var firstHeading = chapters[i].FirstOrDefault(b => b.Type == BlockTypes.Heading);
            var chapterTitle = firstHeading != null
                ? GetJsonProperty(firstHeading.Content, "text")
                : $"Chapter {i + 1}";
            sb.AppendLine($"  <li><a href=\"chapter{i + 1}.xhtml\">{EscapeXml(chapterTitle)}</a></li>");
        }

        sb.AppendLine("</ol>");
        sb.AppendLine("</nav>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    internal static string GenerateTocNcx(List<Block> allBlocks, List<List<Block>> chapters, string title)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<ncx xmlns=\"http://www.daisy.org/z3986/2005/ncx/\" version=\"2005-1\">");
        sb.AppendLine("<head><meta name=\"dtb:uid\" content=\"bookid\"/></head>");
        sb.AppendLine($"<docTitle><text>{EscapeXml(title)}</text></docTitle>");
        sb.AppendLine("<navMap>");

        for (var i = 0; i < chapters.Count; i++)
        {
            var firstHeading = chapters[i].FirstOrDefault(b => b.Type == BlockTypes.Heading);
            var chapterTitle = firstHeading != null
                ? GetJsonProperty(firstHeading.Content, "text")
                : $"Chapter {i + 1}";
            sb.AppendLine($"  <navPoint id=\"navpoint-{i + 1}\" playOrder=\"{i + 1}\">");
            sb.AppendLine($"    <navLabel><text>{EscapeXml(chapterTitle)}</text></navLabel>");
            sb.AppendLine($"    <content src=\"chapter{i + 1}.xhtml\"/>");
            sb.AppendLine("  </navPoint>");
        }

        sb.AppendLine("</navMap>");
        sb.AppendLine("</ncx>");
        return sb.ToString();
    }

    // ────────────────────────── Utility Methods ──────────────────────────

    internal static Block CreateBlock(string type, object content, int sortOrder)
    {
        var json = JsonSerializer.Serialize(content);
        return new Block
        {
            Id = Guid.NewGuid(),
            Type = type,
            Content = JsonDocument.Parse(json),
            SortOrder = sortOrder,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static void CheckHeadingHierarchy(List<Block> headings, List<EpubIssue> issues)
    {
        var previousLevel = 0;
        foreach (var h in headings)
        {
            var level = GetJsonPropertyInt(h.Content, "level", 1);
            if (previousLevel > 0 && level > previousLevel + 1)
            {
                issues.Add(new EpubIssue("structure", "warning",
                    $"Heading level jumps from h{previousLevel} to h{level}",
                    $"Block {h.SortOrder}"));
            }
            previousLevel = level;
        }
    }

    private static void CleanBlockContent(Block block)
    {
        var rawText = block.Content.RootElement.GetRawText();
        // Strip any style-related JSON properties (simplified cleaning)
        // The real cleaning happens during export by regenerating clean XHTML
        // Here we just ensure content is valid
        try
        {
            using var doc = JsonDocument.Parse(rawText);
            // Content is valid, nothing to clean at JSON level
        }
        catch
        {
            block.Content = JsonDocument.Parse("{}");
        }
    }

    private static object ParseTable(XElement tableEl)
    {
        var headers = new List<string>();
        var rows = new List<string[]>();

        var thead = tableEl.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("thead", StringComparison.OrdinalIgnoreCase));
        if (thead != null)
        {
            var headerRow = thead.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("tr", StringComparison.OrdinalIgnoreCase));
            if (headerRow != null)
            {
                headers = headerRow.Elements()
                    .Where(e => e.Name.LocalName.Equals("th", StringComparison.OrdinalIgnoreCase) ||
                                e.Name.LocalName.Equals("td", StringComparison.OrdinalIgnoreCase))
                    .Select(GetInnerText)
                    .ToList();
            }
        }

        var tbody = tableEl.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("tbody", StringComparison.OrdinalIgnoreCase))
                    ?? tableEl;
        foreach (var tr in tbody.Elements().Where(e => e.Name.LocalName.Equals("tr", StringComparison.OrdinalIgnoreCase)))
        {
            // Skip if this is a header row we already processed
            if (thead != null && tr.Parent?.Name.LocalName.Equals("thead", StringComparison.OrdinalIgnoreCase) == true)
                continue;

            var cells = tr.Elements()
                .Where(e => e.Name.LocalName.Equals("td", StringComparison.OrdinalIgnoreCase) ||
                            e.Name.LocalName.Equals("th", StringComparison.OrdinalIgnoreCase))
                .Select(GetInnerText)
                .ToArray();

            if (headers.Count == 0 && rows.Count == 0)
            {
                headers = cells.ToList();
                continue;
            }

            rows.Add(cells);
        }

        return new { headers = headers.ToArray(), rows = rows.Select(r => r).ToArray() };
    }

    private static string GenerateFigureXhtml(JsonDocument content)
    {
        var src = GetJsonProperty(content, "src");
        var caption = GetJsonProperty(content, "caption");
        var alt = GetJsonProperty(content, "alt");

        var sb = new StringBuilder();
        sb.Append("<figure>");
        sb.Append($"<img src=\"{EscapeXml(src)}\" alt=\"{EscapeXml(alt)}\"/>");
        if (!string.IsNullOrEmpty(caption))
            sb.Append($"<figcaption>{EscapeXml(caption)}</figcaption>");
        sb.Append("</figure>");
        return sb.ToString();
    }

    private static string GenerateListXhtml(JsonDocument content)
    {
        var ordered = false;
        if (content.RootElement.TryGetProperty("ordered", out var orderedProp))
            ordered = orderedProp.GetBoolean();

        var tag = ordered ? "ol" : "ul";
        var sb = new StringBuilder();
        sb.Append($"<{tag}>");

        if (content.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                sb.Append($"<li>{EscapeXml(item.GetString() ?? "")}</li>");
            }
        }

        sb.Append($"</{tag}>");
        return sb.ToString();
    }

    private static string GenerateTableXhtml(JsonDocument content)
    {
        var sb = new StringBuilder();
        sb.Append("<table>");

        if (content.RootElement.TryGetProperty("headers", out var headers) && headers.ValueKind == JsonValueKind.Array)
        {
            sb.Append("<thead><tr>");
            foreach (var h in headers.EnumerateArray())
                sb.Append($"<th>{EscapeXml(h.GetString() ?? "")}</th>");
            sb.Append("</tr></thead>");
        }

        if (content.RootElement.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
        {
            sb.Append("<tbody>");
            foreach (var row in rows.EnumerateArray())
            {
                sb.Append("<tr>");
                if (row.ValueKind == JsonValueKind.Array)
                {
                    foreach (var cell in row.EnumerateArray())
                        sb.Append($"<td>{EscapeXml(cell.GetString() ?? "")}</td>");
                }
                sb.Append("</tr>");
            }
            sb.Append("</tbody>");
        }

        sb.Append("</table>");
        return sb.ToString();
    }

    private static string GenerateContainerXml()
    {
        return """
               <?xml version="1.0" encoding="UTF-8"?>
               <container xmlns="urn:oasis:names:tc:opendocument:xmlns:container" version="1.0">
                 <rootfiles>
                   <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
                 </rootfiles>
               </container>
               """;
    }

    private static string GetDefaultCss()
    {
        return """
               body {
                 font-family: Georgia, "Times New Roman", serif;
                 margin: 1em;
                 line-height: 1.6;
               }
               h1, h2, h3, h4, h5, h6 {
                 margin-top: 1.5em;
                 margin-bottom: 0.5em;
               }
               p { margin: 0.5em 0; }
               blockquote {
                 margin: 1em 2em;
                 font-style: italic;
                 border-left: 3px solid #ccc;
                 padding-left: 1em;
               }
               pre {
                 background: #f4f4f4;
                 padding: 1em;
                 overflow-x: auto;
                 font-family: monospace;
               }
               figure { text-align: center; margin: 1em 0; }
               figcaption { font-style: italic; font-size: 0.9em; }
               table { border-collapse: collapse; width: 100%; margin: 1em 0; }
               th, td { border: 1px solid #ccc; padding: 0.5em; text-align: left; }
               th { background: #f4f4f4; }
               aside { background: #f9f9f9; padding: 1em; margin: 1em 0; border-left: 3px solid #999; }
               .verse { white-space: pre-wrap; font-style: italic; }
               .cover { text-align: center; }
               .cover img { max-width: 100%; }
               .abstract { margin: 2em 1em; }
               .equation { text-align: center; font-family: monospace; }
               """;
    }

    /// <summary>
    /// Safely gets an attribute value by prefixed name (e.g. "epub:type").
    /// XName constructor throws on colons, so we search attributes manually.
    /// </summary>
    private static string? GetPrefixedAttribute(XElement element, string prefixedName)
    {
        return element.Attributes()
            .FirstOrDefault(a => a.Name.LocalName == prefixedName
                                 || $"{a.Name.NamespaceName}:{a.Name.LocalName}" == prefixedName
                                 || a.ToString().StartsWith($"{prefixedName}=", StringComparison.OrdinalIgnoreCase))
            ?.Value;
    }

    internal static string GetInnerText(XElement element)
    {
        return string.Concat(element.DescendantNodes()
            .OfType<XText>()
            .Select(t => t.Value))
            .Trim();
    }

    internal static string GetJsonProperty(JsonDocument doc, string propertyName, string defaultValue = "")
    {
        if (doc.RootElement.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString() ?? defaultValue;
        return defaultValue;
    }

    internal static int GetJsonPropertyInt(JsonDocument doc, string propertyName, int defaultValue = 0)
    {
        if (doc.RootElement.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number)
            return prop.GetInt32();
        return defaultValue;
    }

    internal static string EscapeXml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }

    private static string GetDirectoryPath(string path)
    {
        var idx = path.LastIndexOf('/');
        return idx >= 0 ? path[..idx] : "";
    }

    private static async Task<MemoryStream> CopyToMemoryStreamAsync(Stream stream)
    {
        var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        ms.Position = 0;
        return ms;
    }

    private static async Task<XDocument> LoadXmlAsync(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return await XDocument.LoadAsync(stream, LoadOptions.None, CancellationToken.None);
    }

    private static async Task AddEntryAsync(ZipArchive zip, string entryName, string content, CompressionLevel compressionLevel = CompressionLevel.Optimal)
    {
        var entry = zip.CreateEntry(entryName, compressionLevel);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        await writer.WriteAsync(content);
    }

    [GeneratedRegex(@"^(97[89])?\d{9}[\dX]$")]
    private static partial Regex IsbnRegex();
}

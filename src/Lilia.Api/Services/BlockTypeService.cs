using System.Text.Json;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;

namespace Lilia.Api.Services;

public class BlockTypeService : IBlockTypeService
{
    public const string CategoryDocument = "document";
    public const string CategoryPresentation = "presentation";
    public const string CategoryInvoice = "invoice";
    public const string CategoryEpub = "epub";

    private static readonly List<BlockTypeMetadataDto> BlockTypeDefinitions = BuildBlockTypes();

    private static List<BlockTypeMetadataDto> BuildBlockTypes()
    {
        return
        [
            // Document block types
            MakeBlockType(BlockTypes.Paragraph, "Paragraph", "Plain text paragraph", "paragraphMark", CategoryDocument, new { text = "" }),
            MakeBlockType(BlockTypes.Heading, "Heading", "Section heading (H1-H4)", "fontFamily", CategoryDocument, new { text = "", level = 1 }),
            MakeBlockType(BlockTypes.Equation, "Equation", "LaTeX math equation", "calculator", CategoryDocument, new { latex = "", displayMode = true, equationMode = "display", numbered = true }),
            MakeBlockType(BlockTypes.Figure, "Figure", "Image with caption", "image", CategoryDocument, new { src = "", caption = "", alt = "", width = 0.8, position = "center", placement = "auto" }),
            MakeBlockType(BlockTypes.Code, "Code", "Code block with syntax highlighting", "code", CategoryDocument, new { code = "", language = "javascript" }),
            MakeBlockType(BlockTypes.List, "List", "Numbered or bulleted list", "listOrdered", CategoryDocument, new { items = Array.Empty<string>(), ordered = false, start = 1, labelFormat = "number" }),
            MakeBlockType(BlockTypes.Blockquote, "Quote", "Block quotation", "rightDoubleQuotes", CategoryDocument, new { text = "" }),
            MakeBlockType(BlockTypes.Table, "Table", "Data table", "table", CategoryDocument, new { headers = new[] { "Column 1", "Column 2", "Column 3" }, rows = new[] { new[] { "", "", "" }, new[] { "", "", "" } }, columnAlign = Array.Empty<string>(), caption = "" }),
            MakeBlockType(BlockTypes.Theorem, "Theorem", "Theorem, definition, lemma, proof", "stickyNote", CategoryDocument, new { theoremType = "theorem", title = "", text = "", label = "" }),
            MakeBlockType(BlockTypes.Abstract, "Abstract", "Document abstract section", "file", CategoryDocument, new { title = "Abstract", text = "" }),
            MakeBlockType(BlockTypes.Bibliography, "Bibliography", "Reference list", "book", CategoryDocument, new { title = "References", style = "apa", entries = Array.Empty<object>() }),
            MakeBlockType(BlockTypes.TableOfContents, "Table of Contents", "Auto-generated contents from headings", "listUnordered", CategoryDocument, new { title = "Table of Contents" }),
            MakeBlockType(BlockTypes.PageBreak, "Page Break", "Force content to start on new page", "horizontalRule", CategoryDocument, new { }),
            MakeBlockType(BlockTypes.ColumnBreak, "Column Break", "Force content to next column", "layoutSideByLarge", CategoryDocument, new { }),
            MakeBlockType(BlockTypes.Embed, "Embed", "Raw LaTeX or Typst code block", "terminal2", CategoryDocument, new { engine = "latex", code = "", label = "", caption = "" }),
            MakeBlockType(BlockTypes.Footnote, "Footnote", "Footnote annotation at page bottom", "superscript", CategoryDocument, new { text = "" }),
            MakeBlockType(BlockTypes.Algorithm, "Algorithm", "Pseudocode algorithm block", "cpu", CategoryDocument, new { title = "", language = "pseudocode", code = "", label = "", caption = "" }),
            MakeBlockType(BlockTypes.Callout, "Callout", "Admonition or callout box (note, tip, warning, important, example)", "alert", CategoryDocument, new { variant = "note", title = "", text = "" }),

            // Presentation
            MakeBlockType(BlockTypes.Slide, "Slide", "Presentation slide — Beamer frame with title and content", "presentation", CategoryPresentation, new
            {
                title = "",
                subtitle = "",
                content = "",          // Markdown-ish body
                notes = "",            // Speaker notes (exported to Beamer \note{...})
                transition = "none",   // none | fade | push
                layout = "default",    // default | title-only | two-column | centered
            }),

            // CV / resume block types
            MakeBlockType(BlockTypes.PersonalInfo, "Personal Info", "Name, contact, and social profile — typically at the top of a CV", "idBadge2", CategoryDocument, new
            {
                name = "",
                headline = "",
                email = "",
                phones = Array.Empty<object>(),
                homepage = "",
                location = "",
                socials = Array.Empty<object>(),
                extra = ""
            }),
            MakeBlockType(BlockTypes.Photo, "Photo", "Profile photo / avatar with geometry controls", "photo", CategoryDocument, new
            {
                src = "",
                alt = "",
                shape = "square",
                size = 64,
                position = "right",
                border = 0
            }),
            MakeBlockType(BlockTypes.CvSection, "CV Section", "Named section container for a CV (e.g. Experience, Education)", "layoutList", CategoryDocument, new { title = "" }),
            MakeBlockType(BlockTypes.CvEntry, "CV Entry", "Dated role / education / project entry for a CV section", "list", CategoryDocument, new
            {
                period = "",
                role = "",
                org = "",
                location = "",
                highlight = "",
                description = "",
                tech = Array.Empty<string>()
            }),

            // ePub block types
            MakeBlockType(BlockTypes.FrontMatter, "Front Matter", "Book front matter (title page, preface, etc.)", "bookOpen", CategoryEpub, new { text = "" }),
            MakeBlockType(BlockTypes.BackMatter, "Back Matter", "Book back matter (appendix, index, etc.)", "bookEnd", CategoryEpub, new { text = "" }),
            MakeBlockType(BlockTypes.Verse, "Verse", "Poetry or verse block", "quote", CategoryEpub, new { text = "" }),
            MakeBlockType(BlockTypes.Aside, "Aside", "Sidebar or supplementary content", "layoutSidebar", CategoryEpub, new { text = "" }),
            MakeBlockType(BlockTypes.Annotation, "Annotation", "Annotation or footnote-like remark", "pencil", CategoryEpub, new { text = "" }),
            MakeBlockType(BlockTypes.Cover, "Cover", "Book cover image", "image", CategoryEpub, new { src = "", alt = "Cover" }),
            MakeBlockType(BlockTypes.ChapterBreak, "Chapter Break", "Marks a chapter boundary", "horizontalRule", CategoryEpub, new { }),

            // Invoice block types
            MakeBlockType(BlockTypes.InvHeader, "Invoice Header", "Invoice metadata: number, dates, currency, type", "fileInvoice", CategoryInvoice, new
            {
                invoiceNumber = "",
                issueDate = "",
                dueDate = "",
                currency = "EUR",
                invoiceType = "380",
                taxCurrencyCode = (string?)null,
                buyerReference = (string?)null,
                contractReference = (string?)null,
                projectReference = (string?)null,
                precedingInvoiceRef = (string?)null,
                note = (string?)null,
                taxPointDate = (string?)null,
                periodStart = (string?)null,
                periodEnd = (string?)null
            }),
            MakeBlockType(BlockTypes.InvParty, "Invoice Party", "Seller or buyer party with address and tax IDs", "users", CategoryInvoice, new
            {
                role = "seller",
                name = "",
                tradingName = (string?)null,
                street = "",
                streetAdditional = (string?)null,
                city = "",
                postalCode = "",
                countrySubdivision = (string?)null,
                countryCode = "",
                vatId = (string?)null,
                taxRegistrationId = (string?)null,
                legalRegistrationId = (string?)null,
                legalRegistrationScheme = (string?)null,
                email = (string?)null,
                phone = (string?)null,
                contactName = (string?)null,
                contactPhone = (string?)null,
                contactEmail = (string?)null,
                electronicAddress = (string?)null,
                electronicAddressScheme = (string?)null,
                logo = (string?)null,
                bankAccount = (object?)null
            }),
            MakeBlockType(BlockTypes.InvLineItems, "Line Items", "Invoice line item table", "listOrdered", CategoryInvoice, new
            {
                items = Array.Empty<object>(),
                columns = new[] { "description", "quantity", "unitCode", "unitPrice", "taxCategoryCode", "taxRate", "netAmount" }
            }),
            MakeBlockType(BlockTypes.InvTaxSummary, "Tax Summary", "Per-rate VAT/tax breakdown", "percentage", CategoryInvoice, new
            {
                breakdowns = Array.Empty<object>(),
                taxTotalAmount = 0m,
                taxCurrencyTotalAmount = (decimal?)null
            }),
            MakeBlockType(BlockTypes.InvTotals, "Invoice Totals", "Monetary totals: subtotal, tax, amount due", "currencyEuro", CategoryInvoice, new
            {
                sumOfLineNetAmounts = 0m,
                sumOfAllowances = 0m,
                sumOfCharges = 0m,
                invoiceSubtotal = 0m,
                totalTaxAmount = 0m,
                invoiceTotal = 0m,
                prepaidAmount = 0m,
                roundingAmount = 0m,
                amountDue = 0m
            }),
            MakeBlockType(BlockTypes.InvPayment, "Payment Information", "Payment method, bank details, terms", "creditCard", CategoryInvoice, new
            {
                paymentMeansCode = "58",
                paymentMeansText = (string?)null,
                paymentId = (string?)null,
                iban = (string?)null,
                bic = (string?)null,
                bankName = (string?)null,
                accountName = (string?)null,
                paymentTerms = (string?)null,
                earlyPaymentDiscount = (object?)null,
                directDebitMandateId = (string?)null,
                directDebitCreditorId = (string?)null,
                cardPan = (string?)null
            }),
            MakeBlockType(BlockTypes.InvAllowanceCharge, "Allowance/Charge", "Document-level discount or surcharge", "discount", CategoryInvoice, new
            {
                isCharge = false,
                reason = "",
                reasonCode = (string?)null,
                percentage = (decimal?)null,
                baseAmount = (decimal?)null,
                amount = 0m,
                taxCategoryCode = (string?)null,
                taxRate = (decimal?)null
            }),
            MakeBlockType(BlockTypes.InvDelivery, "Delivery Information", "Delivery date, location, and party", "truck", CategoryInvoice, new
            {
                deliveryDate = (string?)null,
                deliveryLocationId = (string?)null,
                deliveryLocationScheme = (string?)null,
                deliveryStreet = (string?)null,
                deliveryCity = (string?)null,
                deliveryPostalCode = (string?)null,
                deliveryCountryCode = (string?)null,
                deliveryCountrySubdivision = (string?)null,
                deliveryPartyName = (string?)null
            }),
            MakeBlockType(BlockTypes.InvNote, "Invoice Note", "Free-text note or attachment reference", "stickyNote", CategoryInvoice, new
            {
                text = "",
                subjectCode = (string?)null,
                attachmentFilename = (string?)null,
                attachmentMimeType = (string?)null,
                attachmentUrl = (string?)null
            })
        ];
    }

    private static BlockTypeMetadataDto MakeBlockType(string type, string label, string description, string iconName, string category, object defaultContent)
    {
        var json = JsonSerializer.Serialize(defaultContent);
        using var doc = JsonDocument.Parse(json);
        return new BlockTypeMetadataDto(type, label, description, iconName, category, doc.RootElement.Clone());
    }

    public List<BlockTypeMetadataDto> GetAllBlockTypes()
    {
        return BlockTypeDefinitions;
    }

    public List<BlockTypeMetadataDto> GetBlockTypesByCategory(string category)
    {
        return BlockTypeDefinitions
            .Where(b => b.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public List<BlockTypeMetadataDto> SearchBlockTypes(string query, string? category = null)
    {
        if (string.IsNullOrWhiteSpace(query) && string.IsNullOrWhiteSpace(category))
            return BlockTypeDefinitions;

        IEnumerable<BlockTypeMetadataDto> results = BlockTypeDefinitions;

        if (!string.IsNullOrWhiteSpace(category))
            results = results.Where(b => b.Category.Equals(category, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim().ToLowerInvariant();
            results = results.Where(b =>
                b.Label.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                b.Type.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                b.Description.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        return results.ToList();
    }

    public JsonDocument GetDefaultContent(string blockType)
    {
        var meta = BlockTypeDefinitions.FirstOrDefault(b =>
            b.Type.Equals(blockType, StringComparison.OrdinalIgnoreCase));

        if (meta == null)
            return JsonDocument.Parse("{}");

        return JsonDocument.Parse(meta.DefaultContent.GetRawText());
    }

    public bool IsValidBlockType(string blockType)
    {
        return BlockTypeDefinitions.Any(b =>
            b.Type.Equals(blockType, StringComparison.OrdinalIgnoreCase));
    }
}

using System.Text;
using FluentAssertions;
using Lilia.Api.Services;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>
/// The review of API #670 (20 Nov 2026): a PDF's pages are counted exactly before Mathpix, never with
/// <see cref="PdfPages.Estimate"/>, whose own doc calls it "never a reason to refuse a real PDF" and which read a 2-page
/// PDF as 21 (#65). <see cref="PdfPages.Exact"/> asks poppler's pdfinfo, else reads the page tree (inside compressed object
/// streams too), else says it doesn't know; an unknown count is never a refusal.
///
/// <para>pdfinfo is on the server (poppler-utils, in the Linux install guide); the fallback is tested with a runner that
/// isn't there.</para>
/// </summary>
public class PdfPagesExactTests
{
    /// <summary>
    /// A real pdflatex PDF of two empty pages (made on the server, 20 Nov 2026): its page tree is inside a compressed object
    /// stream, so <see cref="PdfPages.Estimate"/> sees no page at all and says 1.
    /// </summary>
    internal static readonly byte[] TwoPagePdflatex = Convert.FromBase64String("JVBERi0xLjcKJdDUxdgKMyAwIG9iago8PAovTGVuZ3RoIDggICAgICAgICAKL0ZpbHRlciAvRmxhdGVEZWNvZGUKPj4Kc3RyZWFtCnjaAwAAAAABCmVuZHN0cmVhbQplbmRvYmoKOCAwIG9iago8PAovTGVuZ3RoIDggICAgICAgICAKL0ZpbHRlciAvRmxhdGVEZWNvZGUKPj4Kc3RyZWFtCnjaAwAAAAABCmVuZHN0cmVhbQplbmRvYmoKMTAgMCBvYmoKPDwKL1Byb2R1Y2VyIChwZGZUZVgtMS40MC4yOSkKL0NyZWF0b3IgKFRlWCkKL0NyZWF0aW9uRGF0ZSAoRDoyMDI2MTEyMDEyNTAzMiswMScwMCcpCi9Nb2REYXRlIChEOjIwMjYxMTIwMTI1MDMyKzAxJzAwJykKL1RyYXBwZWQgL0ZhbHNlCi9QVEVYLkZ1bGxiYW5uZXIgKFRoaXMgaXMgcGRmVGVYLCBWZXJzaW9uIDMuMTQxNTkyNjUzLTIuNi0xLjQwLjI5IChUZVggTGl2ZSAyMDI2KSBrcGF0aHNlYSB2ZXJzaW9uIDYuNC4yKQo+PgplbmRvYmoKNCAwIG9iago8PAovVHlwZSAvT2JqU3RtCi9OIDYKL0ZpcnN0IDMzCi9MZW5ndGggMTc5ICAgICAgIAovRmlsdGVyIC9GbGF0ZURlY29kZQo+PgpzdHJlYW0KeNq1kMEKgkAURffzFfcLHGfScQRxkdEmArF24kJ0ECGccBTq73saEUjbNm9xueceeBI+BGKFCEL6UJBCIYQMfMSQWrMkYfz6vBvwvO4M45kdJjNMDjsiC8YL4+w8NsbRzBqcTdvXe/tA6VMQxqEnIwUdCE/HFaOVkXBSLOU0Xffz0TYXM6EkyeGI6pP/9OqtV/3R6xbxTJxk/NS3DqVcBuhbdDf9rJ7qm+3YG/yaXgPMWqoKZW5kc3RyZWFtCmVuZG9iagoxMSAwIG9iago8PAovVHlwZSAvWFJlZgovSW5kZXggWzAgMTJdCi9TaXplIDEyCi9XIFsxIDIgMV0KL1Jvb3QgOSAwIFIKL0luZm8gMTAgMCBSCi9JRCBbPDc4MTU1OUU2OTA4RDkxQjY0NzIzMjUzNTY4RDQ4Q0JEPiA8NzgxNTU5RTY5MDhEOTFCNjQ3MjMyNTM1NjhENDhDQkQ+XQovTGVuZ3RoIDQ2ICAgICAgICAKL0ZpbHRlciAvRmxhdGVEZWNvZGUKPj4Kc3RyZWFtCnjaFcexDQAgEMNAJzw1W7IlDZswCYTiJBu4phSIgbRJV7SwmP+7WMiHB1bhBBAKZW5kc3RyZWFtCmVuZG9iagpzdGFydHhyZWYKNzM3CiUlRU9GCg==");

    /// <summary>
    /// A 21-page PDF edited down to 2 (an incremental update with a new page tree): the old tree still says /Count 21,
    /// the latest says 2. A viewer, and pdfinfo, show 2 pages.
    /// </summary>
    internal static readonly byte[] EditedFrom21To2 = Convert.FromBase64String("JVBERi0xLjQKMSAwIG9iago8PCAvVHlwZSAvQ2F0YWxvZyAvUGFnZXMgMiAwIFIgPj4KZW5kb2JqCjIgMCBvYmoKPDwgL1R5cGUgL1BhZ2VzIC9LaWRzIFszIDAgUiA0IDAgUiA1IDAgUiA2IDAgUiA3IDAgUiA4IDAgUiA5IDAgUiAxMCAwIFIgMTEgMCBSIDEyIDAgUiAxMyAwIFIgMTQgMCBSIDE1IDAgUiAxNiAwIFIgMTcgMCBSIDE4IDAgUiAxOSAwIFIgMjAgMCBSIDIxIDAgUiAyMiAwIFIgMjMgMCBSXSAvQ291bnQgMjEgPj4KZW5kb2JqCjMgMCBvYmoKPDwgL1R5cGUgL1BhZ2UgL1BhcmVudCAyIDAgUiAvTWVkaWFCb3ggWzAgMCA2MTIgNzkyXSA+PgplbmRvYmoKNCAwIG9iago8PCAvVHlwZSAvUGFnZSAvUGFyZW50IDIgMCBSIC9NZWRpYUJveCBbMCAwIDYxMiA3OTJdID4+CmVuZG9iago1IDAgb2JqCjw8IC9UeXBlIC9QYWdlIC9QYXJlbnQgMiAwIFIgL01lZGlhQm94IFswIDAgNjEyIDc5Ml0gPj4KZW5kb2JqCjYgMCBvYmoKPDwgL1R5cGUgL1BhZ2UgL1BhcmVudCAyIDAgUiAvTWVkaWFCb3ggWzAgMCA2MTIgNzkyXSA+PgplbmRvYmoKNyAwIG9iago8PCAvVHlwZSAvUGFnZSAvUGFyZW50IDIgMCBSIC9NZWRpYUJveCBbMCAwIDYxMiA3OTJdID4+CmVuZG9iago4IDAgb2JqCjw8IC9UeXBlIC9QYWdlIC9QYXJlbnQgMiAwIFIgL01lZGlhQm94IFswIDAgNjEyIDc5Ml0gPj4KZW5kb2JqCjkgMCBvYmoKPDwgL1R5cGUgL1BhZ2UgL1BhcmVudCAyIDAgUiAvTWVkaWFCb3ggWzAgMCA2MTIgNzkyXSA+PgplbmRvYmoKMTAgMCBvYmoKPDwgL1R5cGUgL1BhZ2UgL1BhcmVudCAyIDAgUiAvTWVkaWFCb3ggWzAgMCA2MTIgNzkyXSA+PgplbmRvYmoKMTEgMCBvYmoKPDwgL1R5cGUgL1BhZ2UgL1BhcmVudCAyIDAgUiAvTWVkaWFCb3ggWzAgMCA2MTIgNzkyXSA+PgplbmRvYmoKMTIgMCBvYmoKPDwgL1R5cGUgL1BhZ2UgL1BhcmVudCAyIDAgUiAvTWVkaWFCb3ggWzAgMCA2MTIgNzkyXSA+PgplbmRvYmoKMTMgMCBvYmoKPDwgL1R5cGUgL1BhZ2UgL1BhcmVudCAyIDAgUiAvTWVkaWFCb3ggWzAgMCA2MTIgNzkyXSA+PgplbmRvYmoKMTQgMCBvYmoKPDwgL1R5cGUgL1BhZ2UgL1BhcmVudCAyIDAgUiAvTWVkaWFCb3ggWzAgMCA2MTIgNzkyXSA+PgplbmRvYmoKMTUgMCBvYmoKPDwgL1R5cGUgL1BhZ2UgL1BhcmVudCAyIDAgUiAvTWVkaWFCb3ggWzAgMCA2MTIgNzkyXSA+PgplbmRvYmoKMTYgMCBvYmoKPDwgL1R5cGUgL1BhZ2UgL1BhcmVudCAyIDAgUiAvTWVkaWFCb3ggWzAgMCA2MTIgNzkyXSA+PgplbmRvYmoKMTcgMCBvYmoKPDwgL1R5cGUgL1BhZ2UgL1BhcmVudCAyIDAgUiAvTWVkaWFCb3ggWzAgMCA2MTIgNzkyXSA+PgplbmRvYmoKMTggMCBvYmoKPDwgL1R5cGUgL1BhZ2UgL1BhcmVudCAyIDAgUiAvTWVkaWFCb3ggWzAgMCA2MTIgNzkyXSA+PgplbmRvYmoKMTkgMCBvYmoKPDwgL1R5cGUgL1BhZ2UgL1BhcmVudCAyIDAgUiAvTWVkaWFCb3ggWzAgMCA2MTIgNzkyXSA+PgplbmRvYmoKMjAgMCBvYmoKPDwgL1R5cGUgL1BhZ2UgL1BhcmVudCAyIDAgUiAvTWVkaWFCb3ggWzAgMCA2MTIgNzkyXSA+PgplbmRvYmoKMjEgMCBvYmoKPDwgL1R5cGUgL1BhZ2UgL1BhcmVudCAyIDAgUiAvTWVkaWFCb3ggWzAgMCA2MTIgNzkyXSA+PgplbmRvYmoKMjIgMCBvYmoKPDwgL1R5cGUgL1BhZ2UgL1BhcmVudCAyIDAgUiAvTWVkaWFCb3ggWzAgMCA2MTIgNzkyXSA+PgplbmRvYmoKMjMgMCBvYmoKPDwgL1R5cGUgL1BhZ2UgL1BhcmVudCAyIDAgUiAvTWVkaWFCb3ggWzAgMCA2MTIgNzkyXSA+PgplbmRvYmoKeHJlZgowIDI0CjAwMDAwMDAwMDAgNjU1MzUgZiAKMDAwMDAwMDAwOSAwMDAwMCBuIAowMDAwMDAwMDU4IDAwMDAwIG4gCjAwMDAwMDAyNTAgMDAwMDAgbiAKMDAwMDAwMDMyMSAwMDAwMCBuIAowMDAwMDAwMzkyIDAwMDAwIG4gCjAwMDAwMDA0NjMgMDAwMDAgbiAKMDAwMDAwMDUzNCAwMDAwMCBuIAowMDAwMDAwNjA1IDAwMDAwIG4gCjAwMDAwMDA2NzYgMDAwMDAgbiAKMDAwMDAwMDc0NyAwMDAwMCBuIAowMDAwMDAwODE5IDAwMDAwIG4gCjAwMDAwMDA4OTEgMDAwMDAgbiAKMDAwMDAwMDk2MyAwMDAwMCBuIAowMDAwMDAxMDM1IDAwMDAwIG4gCjAwMDAwMDExMDcgMDAwMDAgbiAKMDAwMDAwMTE3OSAwMDAwMCBuIAowMDAwMDAxMjUxIDAwMDAwIG4gCjAwMDAwMDEzMjMgMDAwMDAgbiAKMDAwMDAwMTM5NSAwMDAwMCBuIAowMDAwMDAxNDY3IDAwMDAwIG4gCjAwMDAwMDE1MzkgMDAwMDAgbiAKMDAwMDAwMTYxMSAwMDAwMCBuIAowMDAwMDAxNjgzIDAwMDAwIG4gCnRyYWlsZXIKPDwgL1NpemUgMjQgL1Jvb3QgMSAwIFIgPj4Kc3RhcnR4cmVmCjE3NTUKJSVFT0YKMiAwIG9iago8PCAvVHlwZSAvUGFnZXMgL0tpZHMgWzMgMCBSIDQgMCBSXSAvQ291bnQgMiA+PgplbmRvYmoKeHJlZgoyIDEKMDAwMDAwMjMwMSAwMDAwMCBuIAp0cmFpbGVyCjw8IC9TaXplIDI0IC9Sb290IDEgMCBSIC9QcmV2IDE3NTUgPj4Kc3RhcnR4cmVmCjIzNjQKJSVFT0YK");

    [Fact]
    public void A_2_page_pdflatex_PDF_with_a_compressed_page_tree_is_counted_2()
    {
        PdfPages.Estimate(TwoPagePdflatex).Should().Be(1, "the estimate can't see inside the object stream");
        PdfPages.Exact(TwoPagePdflatex).Should().Be(2);
    }

    [Fact]
    public void A_PDF_the_estimate_reads_as_21_is_counted_right()
    {
        PdfPages.Estimate(EditedFrom21To2).Should().Be(21);
        PdfPages.Exact(EditedFrom21To2).Should().Be(2);
    }

    [Fact]
    public void Without_pdfinfo_the_count_falls_back_to_the_page_tree()
    {
        PdfPages.PdfInfoPages(TwoPagePdflatex, executable: "/nonexistent/pdfinfo").Should().BeNull();
        PdfPages.Exact(TwoPagePdflatex, pdfinfo: _ => null).Should().Be(2, "the page tree is read inside the object stream");
    }

    [Fact]
    public void A_PDF_nothing_can_count_is_unknown_not_one_page()
    {
        PdfPages.Exact(Encoding.ASCII.GetBytes("%PDF-1.4 fake")).Should().BeNull();
    }

    [Fact]
    public void Pdfinfo_reads_the_pages_itself()
    {
        PdfPages.PdfInfoPages(TwoPagePdflatex).Should().Be(2);
        PdfPages.PdfInfoPages(Encoding.ASCII.GetBytes("%PDF-1.4 fake")).Should().BeNull();
    }
}

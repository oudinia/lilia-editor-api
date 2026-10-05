using System.Text.Json;
using FluentAssertions;
using Lilia.Core.DTOs;
using Microsoft.Extensions.AI;
using Moq;
using Xunit;
using static Lilia.Api.Tests.Services.AskLiliaHarness;

namespace Lilia.Api.Tests.Services;

public class AskLiliaSettingsToolTests
{
    private static (AskLiliaHarness H, DocumentDto Doc) Setup()
    {
        var h = new AskLiliaHarness();
        var doc = Doc() with
        {
            PaperSize = "a4", Columns = 2, FontSize = 11, MarginTop = "2cm", HeaderLeft = "Lecture 3",
            PageNumbering = "roman", CustomPreamble = "\\newcommand{\\R}{x}", Orientation = "portrait",
        };
        return (h, doc);
    }

    [Fact]
    public void Settings_tools_exist_and_the_write_tool_is_gated()
    {
        var (h, doc) = Setup();
        var ro = h.Tools(doc, allowWrite: false).OfType<AIFunction>().Select(f => f.Name).ToList();
        ro.Should().Contain("get_document_settings").And.NotContain("set_document_settings");
        var rw = h.Tools(doc, allowWrite: true).OfType<AIFunction>().Select(f => f.Name).ToList();
        rw.Should().Contain(new[] { "get_document_settings", "set_document_settings" });
    }

    [Fact]
    public void Every_setting_is_optional_in_the_schema()
    {
        var (h, doc) = Setup();
        var f = Fn(h.Tools(doc, true), "set_document_settings");
        var props = f.JsonSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToList();
        props.Should().Contain(new[]
        {
            "paperSize", "orientation", "marginTop", "marginBottom", "marginLeft", "marginRight", "columns", "columnGap",
            "columnSeparator", "balancedColumns", "fontFamily", "fontSize", "lineSpacing", "paragraphIndent", "pageNumbering",
            "headerLeft", "headerCenter", "headerRight", "footerLeft", "footerCenter", "footerRight", "customPreamble",
        });
        (f.JsonSchema.TryGetProperty("required", out var r) ? r.GetArrayLength() : 0).Should().Be(0);
        f.Description.Should().Contain("ONLY");
        f.JsonSchema.GetProperty("properties").GetProperty("customPreamble")
            .GetProperty("description").GetString().Should().Contain("ADVANCED");
    }

    [Fact]
    public async Task An_older_document_landscape_only_in_its_class_options_reads_as_landscape()
    {
        var (h, doc) = Setup();
        doc = doc with { Orientation = null, LatexDocumentClassOptions = "twoside,landscape" };
        var r = await Call(Fn(h.Tools(doc, false), "get_document_settings"));
        r.GetProperty("orientation").GetString().Should().Be("landscape");
    }

    [Fact]
    public async Task Get_returns_the_stored_settings()
    {
        var (h, doc) = Setup();
        var r = await Call(Fn(h.Tools(doc, false), "get_document_settings"));
        r.GetProperty("paperSize").GetString().Should().Be("a4");
        r.GetProperty("columns").GetInt32().Should().Be(2);
        r.GetProperty("marginTop").GetString().Should().Be("2cm");
        r.GetProperty("headerLeft").GetString().Should().Be("Lecture 3");
        r.GetProperty("pageNumbering").GetString().Should().Be("roman");
        r.GetProperty("marginLeft").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Set_sends_only_the_provided_fields_through_the_document_service_and_marks_the_change()
    {
        var (h, doc) = Setup();
        UpdateDocumentDto? sent = null;
        h.Documents.Setup(d => d.UpdateDocumentAsync(doc.Id, "u1", It.IsAny<UpdateDocumentDto>()))
            .Callback<Guid, string, UpdateDocumentDto>((_, _, dto) => sent = dto)
            .ReturnsAsync(doc);
        var meta = 0;
        var tools = h.Tools(doc, true, meta: () => meta++);

        var r = await Call(Fn(tools, "set_document_settings"),
            ("orientation", "landscape"), ("columns", 1), ("footerCenter", "Page"), ("marginLeft", "3cm"));

        r.GetProperty("ok").GetBoolean().Should().BeTrue();
        r.GetProperty("changed").EnumerateArray().Select(e => e.GetString()).Should()
            .BeEquivalentTo(new[] { "orientation", "columns", "marginLeft", "footerCenter" });
        r.GetProperty("settings").GetProperty("paperSize").GetString().Should().Be("a4"); // untouched, read back
        sent.Should().NotBeNull();
        sent!.Orientation.Should().Be("landscape");
        sent.Columns.Should().Be(1);
        sent.FooterCenter.Should().Be("Page");
        sent.MarginLeft.Should().Be("3cm");
        sent.PaperSize.Should().BeNull();
        sent.FontSize.Should().BeNull();
        sent.HeaderLeft.Should().BeNull();
        meta.Should().Be(1, "the client must be told a write happened so the editor refreshes and Undo is offered");
    }

    [Fact]
    public async Task Invalid_values_change_nothing_and_list_the_valid_ones()
    {
        var (h, doc) = Setup();
        var tools = h.Tools(doc, true);
        var meta = 0;
        tools = h.Tools(doc, true, meta: () => meta++);
        var r = await Call(Fn(tools, "set_document_settings"), ("paperSize", "tabloid"), ("fontSize", 9));
        var msg = r.GetProperty("error").GetString()!;
        msg.Should().StartWith("No settings were changed").And.Contain("a4, letter, legal").And.Contain("10, 11, 12");
        h.Documents.Verify(d => d.UpdateDocumentAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<UpdateDocumentDto>()), Times.Never);
        meta.Should().Be(0);
    }

    [Fact]
    public async Task Empty_call_and_no_access_are_reported()
    {
        var (h, doc) = Setup();
        var tools = h.Tools(doc, true);
        (await Call(Fn(tools, "set_document_settings"))).GetProperty("error").GetString()
            .Should().Contain("at least one setting");
        h.Documents.Setup(d => d.UpdateDocumentAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<UpdateDocumentDto>()))
            .ReturnsAsync((DocumentDto?)null);
        (await Call(Fn(tools, "set_document_settings"), ("columns", 2))).GetProperty("error").GetString()
            .Should().Contain("no write access");
    }
}

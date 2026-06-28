using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace VendingManager.Tests.DesignV3;

public class TokensCssTests
{
    private static string WebWwwRoot => Path.GetFullPath(
        Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "VendingManager.Web", "wwwroot"));

    private static string CssPath(params string[] parts) => Path.Combine([WebWwwRoot, "css", ..parts]);

    private static string IndexHtmlPath => Path.Combine(WebWwwRoot, "index.html");

    private static readonly string[] TokenFiles =
    {
        "colors.css",
        "typography.css",
        "spacing.css",
        "effects.css",
        "fonts.css"
    };

    [Fact]
    public void TokensCss_EntryPoint_Exists()
    {
        File.Exists(CssPath("vendingmanager.css")).Should().BeTrue("vendingmanager.css entry point must exist");
    }

    [Fact]
    public void TokensCss_EntryPoint_ImportsAllTokenFiles()
    {
        var entry = File.ReadAllText(CssPath("vendingmanager.css"));

        foreach (var token in TokenFiles)
        {
            entry.Should().Contain($"@import url('tokens/{token}')", $"entry point must import tokens/{token}");
        }
    }

    [Fact]
    public void TokensCss_AllTokenFiles_Exist()
    {
        foreach (var token in TokenFiles)
        {
            File.Exists(CssPath("tokens", token)).Should().BeTrue($"token file tokens/{token} must exist");
        }
    }

    [Fact]
    public void TokensCss_AllTokenFiles_NonEmpty()
    {
        foreach (var token in TokenFiles)
        {
            var path = CssPath("tokens", token);
            File.Exists(path).Should().BeTrue($"token file tokens/{token} must exist");
            var lines = File.ReadAllLines(path);
            lines.Length.Should().BeGreaterThan(0, $"token file tokens/{token} must have content");
        }
    }

    [Fact]
    public void TokensCss_Colors_DefinesInkPaperAndSignal()
    {
        var colors = File.ReadAllText(CssPath("tokens", "colors.css"));

        colors.Should().Contain("--ink-900");
        colors.Should().Contain("--paper-100");
        colors.Should().Contain("--signal-success");
    }

    [Fact]
    public void TokensCss_Typography_DefinesFontMonoAndSans()
    {
        var typography = File.ReadAllText(CssPath("tokens", "typography.css"));

        typography.Should().Contain("--font-mono");
        typography.Should().Contain("--font-sans");
    }

    [Fact]
    public void TokensCss_Effects_DefinesShadow()
    {
        var effects = File.ReadAllText(CssPath("tokens", "effects.css"));

        effects.Should().Contain("--shadow-card");
    }

    [Fact]
    public void TokensCss_IndexHtml_LinksVendingManagerCss()
    {
        var html = File.ReadAllText(IndexHtmlPath);

        html.Should().MatchRegex("<link[^>]+href=\"css/vendingmanager.css\"", "index.html must link css/vendingmanager.css");
    }

    [Fact]
    public void TokensCss_IndexHtml_HasPreconnectForGoogleFonts()
    {
        var html = File.ReadAllText(IndexHtmlPath);

        html.Should().Contain("<link rel=\"preconnect\" href=\"https://fonts.googleapis.com\">");
        html.Should().Contain("<link rel=\"preconnect\" href=\"https://fonts.gstatic.com\" crossorigin>");
    }
}

using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Infrastructure.Data.Seeds;

public static class SystemSnippetSeeder
{
    public static async Task SeedAsync(LiliaDbContext context)
    {
        var existingSystemSnippets = await context.Snippets
            .Where(s => s.IsSystem)
            .ToListAsync();

        if (existingSystemSnippets.Any())
        {
            context.Snippets.RemoveRange(existingSystemSnippets);
            await context.SaveChangesAsync();
        }

        var snippets = GetSystemSnippets();
        context.Snippets.AddRange(snippets);
        await context.SaveChangesAsync();
    }

    private static List<Snippet> GetSystemSnippets()
    {
        return new List<Snippet>
        {
            // ================================================================
            // TABLES
            // ================================================================
            CreateSnippet("2x2 Table", "A simple 2-column, 2-row table with headers",
                SnippetBlockTypes.Table, SnippetCategories.Tables,
                "\\begin{table}[htbp]\n\\centering\n\\begin{tabular}{ll}\n\\hline\nColumn 1 & Column 2 \\\\\n\\hline\nData 1 & Data 2 \\\\\nData 3 & Data 4 \\\\\n\\hline\n\\end{tabular}\n\\end{table}",
                new List<string>(), null, new List<string> { "simple", "basic" }),

            CreateSnippet("3x3 Table", "A 3-column, 3-row table with headers",
                SnippetBlockTypes.Table, SnippetCategories.Tables,
                "\\begin{table}[htbp]\n\\centering\n\\begin{tabular}{lll}\n\\hline\nColumn 1 & Column 2 & Column 3 \\\\\n\\hline\nA1 & B1 & C1 \\\\\nA2 & B2 & C2 \\\\\nA3 & B3 & C3 \\\\\n\\hline\n\\end{tabular}\n\\end{table}",
                new List<string>(), null, new List<string> { "medium", "data" }),

            CreateSnippet("4x4 Table", "A 4-column, 4-row table with headers",
                SnippetBlockTypes.Table, SnippetCategories.Tables,
                "\\begin{table}[htbp]\n\\centering\n\\begin{tabular}{llll}\n\\hline\nColumn 1 & Column 2 & Column 3 & Column 4 \\\\\n\\hline\nA1 & B1 & C1 & D1 \\\\\nA2 & B2 & C2 & D2 \\\\\nA3 & B3 & C3 & D3 \\\\\nA4 & B4 & C4 & D4 \\\\\n\\hline\n\\end{tabular}\n\\end{table}",
                new List<string>(), null, new List<string> { "large", "data" }),

            CreateSnippet("Comparison Table", "Two-column table for feature comparisons",
                SnippetBlockTypes.Table, SnippetCategories.Tables,
                "\\begin{table}[htbp]\n\\centering\n\\begin{tabular}{ll}\n\\hline\nFeature & Description \\\\\n\\hline\nFeature 1 & Description of feature 1 \\\\\nFeature 2 & Description of feature 2 \\\\\nFeature 3 & Description of feature 3 \\\\\n\\hline\n\\end{tabular}\n\\end{table}",
                new List<string>(), null, new List<string> { "comparison", "features" }),

            CreateSnippet("Data Table", "Four-column table for numeric data",
                SnippetBlockTypes.Table, SnippetCategories.Tables,
                "\\begin{table}[htbp]\n\\centering\n\\begin{tabular}{llll}\n\\hline\nCategory & Value 1 & Value 2 & Total \\\\\n\\hline\nRow 1 & 10 & 20 & 30 \\\\\nRow 2 & 15 & 25 & 40 \\\\\nRow 3 & 20 & 30 & 50 \\\\\n\\hline\n\\end{tabular}\n\\end{table}",
                new List<string>(), null, new List<string> { "numeric", "data" }),

            CreateSnippet("Schedule / Timetable", "Weekly schedule with time slots",
                SnippetBlockTypes.Table, SnippetCategories.Tables,
                "\\begin{table}[htbp]\n\\centering\n\\begin{tabular}{llllll}\n\\hline\nTime & Monday & Tuesday & Wednesday & Thursday & Friday \\\\\n\\hline\n09:00 & Math & Physics & Chemistry & Biology & English \\\\\n10:00 & English & Math & Physics & Chemistry & Biology \\\\\n11:00 & Physics & Chemistry & Biology & English & Math \\\\\n\\hline\n\\end{tabular}\n\\end{table}",
                new List<string>(), null, new List<string> { "schedule", "timetable" }),

            // ================================================================
            // FIGURES
            // ================================================================
            CreateSnippet("Figure with Caption", "Image placeholder with caption text",
                SnippetBlockTypes.Figure, SnippetCategories.Figures,
                "\\begin{figure}[htbp]\n\\centering\n\\includegraphics[width=0.8\\textwidth]{example-image}\n\\caption{An example figure with caption}\n\\label{fig:example}\n\\end{figure}",
                new List<string> { "graphicx" }, null, new List<string> { "image", "caption" }),

            CreateSnippet("Side-by-Side Figures", "Two figures arranged side by side",
                SnippetBlockTypes.Figure, SnippetCategories.Figures,
                "\\begin{figure}[htbp]\n\\centering\n\\includegraphics[width=0.8\\textwidth]{example-image}\n\\caption{Left image and right image comparison}\n\\label{fig:comparison}\n\\end{figure}",
                new List<string> { "graphicx" }, null, new List<string> { "side-by-side", "comparison" }),

            CreateSnippet("Figure with Source", "Image with caption and source attribution",
                SnippetBlockTypes.Figure, SnippetCategories.Figures,
                "\\begin{figure}[htbp]\n\\centering\n\\includegraphics[width=0.8\\textwidth]{example-image}\n\\caption{Description of figure. Source: Author (Year)}\n\\label{fig:sourced}\n\\end{figure}",
                new List<string> { "graphicx" }, null, new List<string> { "source", "attribution" }),

            // ================================================================
            // ACADEMIC
            // ================================================================
            CreateSnippet("Theorem", "Numbered theorem statement",
                SnippetBlockTypes.Theorem, SnippetCategories.Academic,
                "\\begin{theorem}[Pythagorean Theorem]\n\\label{thm:pythagoras}\nIn a right triangle, the square of the hypotenuse equals the sum of the squares of the other two sides: $a^2 + b^2 = c^2$.\n\\end{theorem}",
                new List<string> { "amsthm" },
                "\\newtheorem{theorem}{Theorem}\n\\newtheorem{lemma}{Lemma}\n\\newtheorem{corollary}{Corollary}\n\\theoremstyle{definition}\n\\newtheorem{definition}{Definition}\n\\newtheorem{example}{Example}\n\\theoremstyle{remark}\n\\newtheorem{remark}{Remark}",
                new List<string> { "theorem", "math" }),

            CreateSnippet("Definition", "Formal definition block",
                SnippetBlockTypes.Theorem, SnippetCategories.Academic,
                "\\begin{definition}[Continuous Function]\n\\label{def:continuous}\nA function f is continuous at a point c if the limit of f(x) as x approaches c equals f(c).\n\\end{definition}",
                new List<string> { "amsthm" },
                "\\newtheorem{theorem}{Theorem}\n\\newtheorem{lemma}{Lemma}\n\\newtheorem{corollary}{Corollary}\n\\theoremstyle{definition}\n\\newtheorem{definition}{Definition}\n\\newtheorem{example}{Example}\n\\theoremstyle{remark}\n\\newtheorem{remark}{Remark}",
                new List<string> { "definition", "analysis" }),

            CreateSnippet("Lemma", "Supporting lemma block",
                SnippetBlockTypes.Theorem, SnippetCategories.Academic,
                "\\begin{lemma}\n\\label{lem:even-square}\nIf n is an even integer, then n squared is also even.\n\\end{lemma}",
                new List<string> { "amsthm" },
                "\\newtheorem{theorem}{Theorem}\n\\newtheorem{lemma}{Lemma}\n\\newtheorem{corollary}{Corollary}\n\\theoremstyle{definition}\n\\newtheorem{definition}{Definition}\n\\newtheorem{example}{Example}\n\\theoremstyle{remark}\n\\newtheorem{remark}{Remark}",
                new List<string> { "lemma", "proof" }),

            CreateSnippet("Proof", "Mathematical proof block",
                SnippetBlockTypes.Theorem, SnippetCategories.Academic,
                "\\begin{proof}\nLet n = 2k for some integer k. Then n\\^{}2 = (2k)\\^{}2 = 4k\\^{}2 = 2(2k\\^{}2). Since 2k\\^{}2 is an integer, n\\^{}2 is even.\n\\end{proof}",
                new List<string> { "amsthm" }, null,
                new List<string> { "proof", "math" }),

            CreateSnippet("Example", "Worked example block",
                SnippetBlockTypes.Theorem, SnippetCategories.Academic,
                "\\begin{example}\nConsider the function f(x) = x\\^{}2. The derivative is f'(x) = 2x.\n\\end{example}",
                new List<string> { "amsthm" },
                "\\newtheorem{theorem}{Theorem}\n\\newtheorem{lemma}{Lemma}\n\\newtheorem{corollary}{Corollary}\n\\theoremstyle{definition}\n\\newtheorem{definition}{Definition}\n\\newtheorem{example}{Example}\n\\theoremstyle{remark}\n\\newtheorem{remark}{Remark}",
                new List<string> { "example", "worked" }),

            CreateSnippet("Abstract", "Document abstract section",
                SnippetBlockTypes.Abstract, SnippetCategories.Academic,
                "\\begin{abstract}\nThis paper presents a comprehensive study of the subject. We introduce novel methods and demonstrate their effectiveness through extensive experiments.\n\\end{abstract}",
                new List<string>(), null,
                new List<string> { "abstract", "paper" }),

            // ================================================================
            // CODE
            // ================================================================
            CreateSnippet("JavaScript / TypeScript", "JavaScript or TypeScript code snippet",
                SnippetBlockTypes.Code, SnippetCategories.Code,
                "% Language: javascript\n\\begin{verbatim}\nfunction fibonacci(n) {\n  if (n <= 1) return n;\n  return fibonacci(n - 1) + fibonacci(n - 2);\n}\n\nconsole.log(fibonacci(10));\n\\end{verbatim}",
                new List<string>(), null,
                new List<string> { "javascript", "typescript", "fibonacci" }),

            CreateSnippet("Python", "Python code snippet",
                SnippetBlockTypes.Code, SnippetCategories.Code,
                "% Language: python\n\\begin{verbatim}\ndef quicksort(arr):\n    if len(arr) <= 1:\n        return arr\n    pivot = arr[len(arr) // 2]\n    left = [x for x in arr if x < pivot]\n    middle = [x for x in arr if x == pivot]\n    right = [x for x in arr if x > pivot]\n    return quicksort(left) + middle + quicksort(right)\n\\end{verbatim}",
                new List<string>(), null,
                new List<string> { "python", "sorting", "quicksort" }),

            CreateSnippet("LaTeX", "LaTeX source snippet",
                SnippetBlockTypes.Code, SnippetCategories.Code,
                "% Language: latex\n\\begin{verbatim}\n\\documentclass{article}\n\\usepackage{amsmath}\n\\begin{document}\n  Hello, \\LaTeX!\n\\end{document}\n\\end{verbatim}",
                new List<string>(), null,
                new List<string> { "latex", "template" }),

            CreateSnippet("Algorithm Pseudocode", "Pseudocode for algorithm description",
                SnippetBlockTypes.Code, SnippetCategories.Code,
                "% Language: plaintext\n\\begin{verbatim}\nAlgorithm: BinarySearch\nInput: sorted array A, target value T\nOutput: index of T in A, or -1\n\n1. Set low = 0, high = length(A) - 1\n2. While low <= high:\n   a. mid = (low + high) / 2\n   b. If A[mid] == T, return mid\n   c. If A[mid] < T, set low = mid + 1\n   d. Else set high = mid - 1\n3. Return -1\n\\end{verbatim}",
                new List<string>(), null,
                new List<string> { "algorithm", "pseudocode", "binary-search" }),

            // ================================================================
            // STRUCTURE
            // ================================================================
            CreateSnippet("Numbered List", "Ordered list with numbered items",
                SnippetBlockTypes.List, SnippetCategories.Structure,
                "\\begin{enumerate}\n\\item First item\n\\item Second item\n\\item Third item\n\\end{enumerate}",
                new List<string>(), null,
                new List<string> { "ordered", "numbered" }),

            CreateSnippet("Bullet List", "Unordered list with bullet points",
                SnippetBlockTypes.List, SnippetCategories.Structure,
                "\\begin{itemize}\n\\item First item\n\\item Second item\n\\item Third item\n\\end{itemize}",
                new List<string>(), null,
                new List<string> { "unordered", "bullet" }),

            CreateSnippet("Block Quote", "Quoted text block",
                SnippetBlockTypes.Blockquote, SnippetCategories.Structure,
                "\\begin{quote}\nThe only way to do great work is to love what you do.\n\\end{quote}",
                new List<string>(), null,
                new List<string> { "quote", "citation" }),

            CreateSnippet("Table of Contents", "Auto-generated from document headings",
                SnippetBlockTypes.TableOfContents, SnippetCategories.Structure,
                "\\tableofcontents",
                new List<string>(), null,
                new List<string> { "toc", "navigation" }),

            CreateSnippet("Page Break", "Force content to start on a new page",
                SnippetBlockTypes.PageBreak, SnippetCategories.Structure,
                "\\newpage",
                new List<string>(), null,
                new List<string> { "page", "break" }),
        };
    }

    private static Snippet CreateSnippet(string name, string description, string blockType,
        string category, string latexContent, List<string> requiredPackages,
        string? preamble, List<string> tags)
    {
        return new Snippet
        {
            Id = Guid.NewGuid(),
            UserId = null,
            Name = name,
            Description = description,
            LatexContent = latexContent,
            BlockType = blockType,
            Category = category,
            RequiredPackages = requiredPackages,
            Preamble = preamble,
            Tags = tags,
            IsFavorite = false,
            IsSystem = true,
            UsageCount = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }
}

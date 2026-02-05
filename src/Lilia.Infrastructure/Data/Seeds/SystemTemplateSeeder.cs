using System.Text.Json;
using System.Text.Json.Serialization;
using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Infrastructure.Data.Seeds;

public static class SystemTemplateSeeder
{
    public static async Task SeedAsync(LiliaDbContext context)
    {
        // Remove existing system templates and re-seed to ensure data is correct
        var existingSystemTemplates = await context.Templates
            .Where(t => t.IsSystem)
            .ToListAsync();

        if (existingSystemTemplates.Any())
        {
            context.Templates.RemoveRange(existingSystemTemplates);
            await context.SaveChangesAsync();
        }

        var templates = GetSystemTemplates();
        context.Templates.AddRange(templates);
        await context.SaveChangesAsync();
    }

    private static List<Template> GetSystemTemplates()
    {
        return new List<Template>
        {
            CreateTemplate(
                "IEEE Conference Paper",
                "Standard IEEE conference paper format with abstract, introduction, methodology, results, and conclusion sections.",
                TemplateCategories.Academic,
                new
                {
                    language = "en",
                    paperSize = "letter",
                    fontFamily = "times",
                    fontSize = 10,
                    blocks = new object[]
                    {
                        new { type = "heading", content = new { text = "Paper Title", level = 1 }, sortOrder = "000000" },
                        new { type = "paragraph", content = new { text = "<em>Author Name</em><br/>Affiliation<br/>email@example.com" }, sortOrder = "000001" },
                        new { type = "abstract", content = new { text = "This paper presents..." }, sortOrder = "000002" },
                        new { type = "heading", content = new { text = "Introduction", level = 2 }, sortOrder = "000003" },
                        new { type = "paragraph", content = new { text = "The introduction provides background and motivation for the research..." }, sortOrder = "000004" },
                        new { type = "heading", content = new { text = "Related Work", level = 2 }, sortOrder = "000005" },
                        new { type = "paragraph", content = new { text = "Previous research in this area includes..." }, sortOrder = "000006" },
                        new { type = "heading", content = new { text = "Methodology", level = 2 }, sortOrder = "000007" },
                        new { type = "paragraph", content = new { text = "Our approach consists of the following steps..." }, sortOrder = "000008" },
                        new { type = "heading", content = new { text = "Results", level = 2 }, sortOrder = "000009" },
                        new { type = "paragraph", content = new { text = "The experimental results demonstrate..." }, sortOrder = "000010" },
                        new { type = "heading", content = new { text = "Conclusion", level = 2 }, sortOrder = "000011" },
                        new { type = "paragraph", content = new { text = "In this paper, we have presented..." }, sortOrder = "000012" },
                        new { type = "heading", content = new { text = "References", level = 2 }, sortOrder = "000013" },
                        new { type = "bibliography", content = new { }, sortOrder = "000014" }
                    }
                }
            ),
            CreateTemplate(
                "Academic Research Article",
                "Comprehensive research article template with literature review and discussion sections.",
                TemplateCategories.Academic,
                new
                {
                    language = "en",
                    paperSize = "a4",
                    fontFamily = "charter",
                    fontSize = 11,
                    blocks = new object[]
                    {
                        new { type = "heading", content = new { text = "Research Article Title", level = 1 }, sortOrder = "000000" },
                        new { type = "abstract", content = new { text = "Abstract: Provide a concise summary of the research objectives, methods, results, and conclusions (150-250 words)." }, sortOrder = "000001" },
                        new { type = "paragraph", content = new { text = "<strong>Keywords:</strong> keyword1, keyword2, keyword3" }, sortOrder = "000002" },
                        new { type = "heading", content = new { text = "1. Introduction", level = 2 }, sortOrder = "000003" },
                        new { type = "paragraph", content = new { text = "Introduce the research problem, its significance, and the objectives of this study..." }, sortOrder = "000004" },
                        new { type = "heading", content = new { text = "2. Literature Review", level = 2 }, sortOrder = "000005" },
                        new { type = "paragraph", content = new { text = "Review relevant prior work and theoretical background..." }, sortOrder = "000006" },
                        new { type = "heading", content = new { text = "3. Methodology", level = 2 }, sortOrder = "000007" },
                        new { type = "heading", content = new { text = "3.1 Research Design", level = 3 }, sortOrder = "000008" },
                        new { type = "paragraph", content = new { text = "Describe the research design and approach..." }, sortOrder = "000009" },
                        new { type = "heading", content = new { text = "3.2 Data Collection", level = 3 }, sortOrder = "000010" },
                        new { type = "paragraph", content = new { text = "Explain data collection methods and procedures..." }, sortOrder = "000011" },
                        new { type = "heading", content = new { text = "4. Results", level = 2 }, sortOrder = "000012" },
                        new { type = "paragraph", content = new { text = "Present the findings of the research..." }, sortOrder = "000013" },
                        new { type = "heading", content = new { text = "5. Discussion", level = 2 }, sortOrder = "000014" },
                        new { type = "paragraph", content = new { text = "Interpret the results in context of the research questions and prior literature..." }, sortOrder = "000015" },
                        new { type = "heading", content = new { text = "6. Conclusion", level = 2 }, sortOrder = "000016" },
                        new { type = "paragraph", content = new { text = "Summarize key findings and their implications..." }, sortOrder = "000017" },
                        new { type = "heading", content = new { text = "References", level = 2 }, sortOrder = "000018" },
                        new { type = "bibliography", content = new { }, sortOrder = "000019" }
                    }
                }
            ),
            CreateTemplate(
                "Thesis / Dissertation",
                "Complete thesis structure with chapters for introduction, literature review, methodology, results, and appendices.",
                TemplateCategories.Thesis,
                new
                {
                    language = "en",
                    paperSize = "a4",
                    fontFamily = "charter",
                    fontSize = 12,
                    blocks = new object[]
                    {
                        new { type = "heading", content = new { text = "Thesis Title", level = 1 }, sortOrder = "000000" },
                        new { type = "paragraph", content = new { text = "<em>A thesis submitted in partial fulfillment of the requirements for the degree of...</em>" }, sortOrder = "000001" },
                        new { type = "paragraph", content = new { text = "<strong>Author:</strong> Your Name<br/><strong>Supervisor:</strong> Prof. Name<br/><strong>Date:</strong> Month Year" }, sortOrder = "000002" },
                        new { type = "heading", content = new { text = "Abstract", level = 2 }, sortOrder = "000003" },
                        new { type = "abstract", content = new { text = "Provide a comprehensive summary of your thesis (300-500 words)..." }, sortOrder = "000004" },
                        new { type = "heading", content = new { text = "Acknowledgments", level = 2 }, sortOrder = "000005" },
                        new { type = "paragraph", content = new { text = "I would like to thank..." }, sortOrder = "000006" },
                        new { type = "heading", content = new { text = "Chapter 1: Introduction", level = 2 }, sortOrder = "000007" },
                        new { type = "heading", content = new { text = "1.1 Background", level = 3 }, sortOrder = "000008" },
                        new { type = "paragraph", content = new { text = "Provide context and background for your research..." }, sortOrder = "000009" },
                        new { type = "heading", content = new { text = "1.2 Problem Statement", level = 3 }, sortOrder = "000010" },
                        new { type = "paragraph", content = new { text = "Clearly state the research problem..." }, sortOrder = "000011" },
                        new { type = "heading", content = new { text = "1.3 Research Objectives", level = 3 }, sortOrder = "000012" },
                        new { type = "paragraph", content = new { text = "List the main objectives of your research..." }, sortOrder = "000013" },
                        new { type = "heading", content = new { text = "Chapter 2: Literature Review", level = 2 }, sortOrder = "000014" },
                        new { type = "paragraph", content = new { text = "Review and synthesize relevant literature..." }, sortOrder = "000015" },
                        new { type = "heading", content = new { text = "Chapter 3: Methodology", level = 2 }, sortOrder = "000016" },
                        new { type = "paragraph", content = new { text = "Describe your research methodology in detail..." }, sortOrder = "000017" },
                        new { type = "heading", content = new { text = "Chapter 4: Results", level = 2 }, sortOrder = "000018" },
                        new { type = "paragraph", content = new { text = "Present your research findings..." }, sortOrder = "000019" },
                        new { type = "heading", content = new { text = "Chapter 5: Discussion", level = 2 }, sortOrder = "000020" },
                        new { type = "paragraph", content = new { text = "Discuss the implications of your findings..." }, sortOrder = "000021" },
                        new { type = "heading", content = new { text = "Chapter 6: Conclusion", level = 2 }, sortOrder = "000022" },
                        new { type = "paragraph", content = new { text = "Summarize conclusions and future work..." }, sortOrder = "000023" },
                        new { type = "heading", content = new { text = "References", level = 2 }, sortOrder = "000024" },
                        new { type = "bibliography", content = new { }, sortOrder = "000025" }
                    }
                }
            ),
            CreateTemplate(
                "Academic CV",
                "Curriculum vitae template for academic positions with sections for education, publications, and teaching.",
                TemplateCategories.Other,
                new
                {
                    language = "en",
                    paperSize = "a4",
                    fontFamily = "charter",
                    fontSize = 11,
                    blocks = new object[]
                    {
                        new { type = "heading", content = new { text = "Curriculum Vitae", level = 1 }, sortOrder = "000000" },
                        new { type = "heading", content = new { text = "Personal Information", level = 2 }, sortOrder = "000001" },
                        new { type = "paragraph", content = new { text = "<strong>Name:</strong> Your Full Name<br/><strong>Email:</strong> email@university.edu<br/><strong>Phone:</strong> +1 (555) 123-4567<br/><strong>Address:</strong> Department, University" }, sortOrder = "000002" },
                        new { type = "heading", content = new { text = "Education", level = 2 }, sortOrder = "000003" },
                        new { type = "paragraph", content = new { text = "<strong>Ph.D. in Field</strong>, University Name, Year<br/>Dissertation: \"Title\"<br/>Advisor: Prof. Name" }, sortOrder = "000004" },
                        new { type = "paragraph", content = new { text = "<strong>M.S. in Field</strong>, University Name, Year" }, sortOrder = "000005" },
                        new { type = "paragraph", content = new { text = "<strong>B.S. in Field</strong>, University Name, Year" }, sortOrder = "000006" },
                        new { type = "heading", content = new { text = "Research Interests", level = 2 }, sortOrder = "000007" },
                        new { type = "paragraph", content = new { text = "List your main research interests and areas of expertise..." }, sortOrder = "000008" },
                        new { type = "heading", content = new { text = "Publications", level = 2 }, sortOrder = "000009" },
                        new { type = "heading", content = new { text = "Journal Articles", level = 3 }, sortOrder = "000010" },
                        new { type = "paragraph", content = new { text = "1. Author, A., Author, B. (Year). \"Article Title.\" <em>Journal Name</em>, Volume(Issue), pages." }, sortOrder = "000011" },
                        new { type = "heading", content = new { text = "Conference Papers", level = 3 }, sortOrder = "000012" },
                        new { type = "paragraph", content = new { text = "1. Author, A., Author, B. (Year). \"Paper Title.\" In <em>Conference Name</em>, Location." }, sortOrder = "000013" },
                        new { type = "heading", content = new { text = "Teaching Experience", level = 2 }, sortOrder = "000014" },
                        new { type = "paragraph", content = new { text = "<strong>Course Name</strong>, University, Semester Year<br/>Role: Instructor/TA" }, sortOrder = "000015" },
                        new { type = "heading", content = new { text = "Awards and Honors", level = 2 }, sortOrder = "000016" },
                        new { type = "paragraph", content = new { text = "List awards, fellowships, and honors..." }, sortOrder = "000017" },
                        new { type = "heading", content = new { text = "Professional Service", level = 2 }, sortOrder = "000018" },
                        new { type = "paragraph", content = new { text = "List reviewing, committee work, and other service..." }, sortOrder = "000019" }
                    }
                }
            ),
            CreateTemplate(
                "Technical Report",
                "Technical report template with executive summary, methodology, and recommendations.",
                TemplateCategories.Report,
                new
                {
                    language = "en",
                    paperSize = "a4",
                    fontFamily = "charter",
                    fontSize = 11,
                    blocks = new object[]
                    {
                        new { type = "heading", content = new { text = "Technical Report Title", level = 1 }, sortOrder = "000000" },
                        new { type = "paragraph", content = new { text = "<strong>Report Number:</strong> TR-2024-001<br/><strong>Date:</strong> Month Year<br/><strong>Author(s):</strong> Name(s)<br/><strong>Organization:</strong> Company/Institution" }, sortOrder = "000001" },
                        new { type = "heading", content = new { text = "Executive Summary", level = 2 }, sortOrder = "000002" },
                        new { type = "paragraph", content = new { text = "Provide a brief overview of the report's purpose, methods, key findings, and recommendations (1-2 paragraphs)..." }, sortOrder = "000003" },
                        new { type = "heading", content = new { text = "1. Introduction", level = 2 }, sortOrder = "000004" },
                        new { type = "heading", content = new { text = "1.1 Background", level = 3 }, sortOrder = "000005" },
                        new { type = "paragraph", content = new { text = "Provide context for the technical work..." }, sortOrder = "000006" },
                        new { type = "heading", content = new { text = "1.2 Objectives", level = 3 }, sortOrder = "000007" },
                        new { type = "paragraph", content = new { text = "State the objectives of this technical work..." }, sortOrder = "000008" },
                        new { type = "heading", content = new { text = "1.3 Scope", level = 3 }, sortOrder = "000009" },
                        new { type = "paragraph", content = new { text = "Define the scope and limitations..." }, sortOrder = "000010" },
                        new { type = "heading", content = new { text = "2. Methodology", level = 2 }, sortOrder = "000011" },
                        new { type = "paragraph", content = new { text = "Describe the technical approach and methods used..." }, sortOrder = "000012" },
                        new { type = "heading", content = new { text = "3. Results and Analysis", level = 2 }, sortOrder = "000013" },
                        new { type = "paragraph", content = new { text = "Present technical results and analysis..." }, sortOrder = "000014" },
                        new { type = "heading", content = new { text = "4. Discussion", level = 2 }, sortOrder = "000015" },
                        new { type = "paragraph", content = new { text = "Discuss implications and interpretations..." }, sortOrder = "000016" },
                        new { type = "heading", content = new { text = "5. Conclusions", level = 2 }, sortOrder = "000017" },
                        new { type = "paragraph", content = new { text = "Summarize key conclusions..." }, sortOrder = "000018" },
                        new { type = "heading", content = new { text = "6. Recommendations", level = 2 }, sortOrder = "000019" },
                        new { type = "paragraph", content = new { text = "Provide actionable recommendations..." }, sortOrder = "000020" },
                        new { type = "heading", content = new { text = "References", level = 2 }, sortOrder = "000021" },
                        new { type = "bibliography", content = new { }, sortOrder = "000022" },
                        new { type = "heading", content = new { text = "Appendices", level = 2 }, sortOrder = "000023" },
                        new { type = "paragraph", content = new { text = "Include supplementary technical data, calculations, or documentation..." }, sortOrder = "000024" }
                    }
                }
            ),
            CreateTemplate(
                "Math Problem Set",
                "Template for mathematical problem sets with theorem environments and equation support.",
                TemplateCategories.Academic,
                new
                {
                    language = "en",
                    paperSize = "a4",
                    fontFamily = "charter",
                    fontSize = 11,
                    blocks = new object[]
                    {
                        new { type = "heading", content = new { text = "Problem Set Title", level = 1 }, sortOrder = "000000" },
                        new { type = "paragraph", content = new { text = "<strong>Course:</strong> MATH 101<br/><strong>Due Date:</strong> Month Day, Year<br/><strong>Name:</strong> Your Name" }, sortOrder = "000001" },
                        new { type = "heading", content = new { text = "Problem 1", level = 2 }, sortOrder = "000002" },
                        new { type = "paragraph", content = new { text = "<strong>Statement:</strong> Let $f: \\mathbb{R} \\to \\mathbb{R}$ be a continuous function. Prove that..." }, sortOrder = "000003" },
                        new { type = "paragraph", content = new { text = "<strong>Solution:</strong>" }, sortOrder = "000004" },
                        new { type = "paragraph", content = new { text = "We begin by noting that..." }, sortOrder = "000005" },
                        new { type = "equation", content = new { latex = "\\int_a^b f(x) \\, dx = F(b) - F(a)", equationMode = "display" }, sortOrder = "000006" },
                        new { type = "paragraph", content = new { text = "Therefore, we conclude that... $\\square$" }, sortOrder = "000007" },
                        new { type = "heading", content = new { text = "Problem 2", level = 2 }, sortOrder = "000008" },
                        new { type = "paragraph", content = new { text = "<strong>Statement:</strong> Consider the following theorem:" }, sortOrder = "000009" },
                        new { type = "theorem", content = new { theoremType = "theorem", title = "Fundamental Theorem", text = "If $f$ is continuous on $[a,b]$, then..." }, sortOrder = "000010" },
                        new { type = "paragraph", content = new { text = "<strong>Solution:</strong>" }, sortOrder = "000011" },
                        new { type = "theorem", content = new { theoremType = "proof", text = "By definition of continuity, for all $\\epsilon > 0$, there exists $\\delta > 0$ such that... $\\square$" }, sortOrder = "000012" },
                        new { type = "heading", content = new { text = "Problem 3", level = 2 }, sortOrder = "000013" },
                        new { type = "paragraph", content = new { text = "<strong>Statement:</strong> Compute the following integral:" }, sortOrder = "000014" },
                        new { type = "equation", content = new { latex = "\\int_0^\\infty e^{-x^2} \\, dx", equationMode = "display" }, sortOrder = "000015" },
                        new { type = "paragraph", content = new { text = "<strong>Solution:</strong>" }, sortOrder = "000016" },
                        new { type = "paragraph", content = new { text = "Using the Gaussian integral formula..." }, sortOrder = "000017" }
                    }
                }
            ),

            // === MORE ACADEMIC PAPERS ===
            CreateTemplate(
                "ACM Conference Paper",
                "ACM SIG proceedings format for computer science conferences.",
                TemplateCategories.Academic,
                new
                {
                    language = "en",
                    paperSize = "letter",
                    fontFamily = "libertine",
                    fontSize = 10,
                    blocks = new object[]
                    {
                        new { type = "heading", content = new { text = "Paper Title: A Subtitle if Needed", level = 1 }, sortOrder = "000000" },
                        new { type = "paragraph", content = new { text = "<strong>Author Name</strong><br/>Institution<br/>City, Country<br/>email@domain.com" }, sortOrder = "000001" },
                        new { type = "abstract", content = new { text = "A short abstract (150-200 words) describing the problem, approach, and key results." }, sortOrder = "000002" },
                        new { type = "paragraph", content = new { text = "<strong>CCS Concepts:</strong> • Computing methodologies → Machine learning<br/><strong>Keywords:</strong> keyword1, keyword2, keyword3" }, sortOrder = "000003" },
                        new { type = "heading", content = new { text = "1 Introduction", level = 2 }, sortOrder = "000004" },
                        new { type = "paragraph", content = new { text = "Introduce the problem and motivation..." }, sortOrder = "000005" },
                        new { type = "heading", content = new { text = "2 Related Work", level = 2 }, sortOrder = "000006" },
                        new { type = "paragraph", content = new { text = "Discuss relevant prior work..." }, sortOrder = "000007" },
                        new { type = "heading", content = new { text = "3 Approach", level = 2 }, sortOrder = "000008" },
                        new { type = "paragraph", content = new { text = "Describe your method or system..." }, sortOrder = "000009" },
                        new { type = "heading", content = new { text = "4 Evaluation", level = 2 }, sortOrder = "000010" },
                        new { type = "paragraph", content = new { text = "Present experimental setup and results..." }, sortOrder = "000011" },
                        new { type = "heading", content = new { text = "5 Discussion", level = 2 }, sortOrder = "000012" },
                        new { type = "paragraph", content = new { text = "Discuss limitations and implications..." }, sortOrder = "000013" },
                        new { type = "heading", content = new { text = "6 Conclusion", level = 2 }, sortOrder = "000014" },
                        new { type = "paragraph", content = new { text = "Summarize contributions and future work..." }, sortOrder = "000015" },
                        new { type = "heading", content = new { text = "Acknowledgments", level = 2 }, sortOrder = "000016" },
                        new { type = "paragraph", content = new { text = "This work was supported by..." }, sortOrder = "000017" },
                        new { type = "heading", content = new { text = "References", level = 2 }, sortOrder = "000018" },
                        new { type = "bibliography", content = new { }, sortOrder = "000019" }
                    }
                }
            ),
            CreateTemplate(
                "Elsevier Journal Article",
                "Standard Elsevier journal format with graphical abstract support.",
                TemplateCategories.Academic,
                new
                {
                    language = "en",
                    paperSize = "a4",
                    fontFamily = "times",
                    fontSize = 11,
                    blocks = new object[]
                    {
                        new { type = "heading", content = new { text = "Article Title", level = 1 }, sortOrder = "000000" },
                        new { type = "paragraph", content = new { text = "Author A$^{a,*}$, Author B$^{b}$, Author C$^{a}$" }, sortOrder = "000001" },
                        new { type = "paragraph", content = new { text = "$^{a}$Department, University, City, Country<br/>$^{b}$Department, Institution, City, Country<br/>$^{*}$Corresponding author: email@domain.com" }, sortOrder = "000002" },
                        new { type = "heading", content = new { text = "Highlights", level = 2 }, sortOrder = "000003" },
                        new { type = "paragraph", content = new { text = "• First major finding or contribution<br/>• Second key result<br/>• Third important outcome<br/>• Fourth highlight (3-5 bullet points)" }, sortOrder = "000004" },
                        new { type = "abstract", content = new { text = "Abstract (max 300 words): State objectives, methods, results, and conclusions..." }, sortOrder = "000005" },
                        new { type = "paragraph", content = new { text = "<strong>Keywords:</strong> keyword1; keyword2; keyword3; keyword4; keyword5" }, sortOrder = "000006" },
                        new { type = "heading", content = new { text = "1. Introduction", level = 2 }, sortOrder = "000007" },
                        new { type = "paragraph", content = new { text = "Background and motivation for the research..." }, sortOrder = "000008" },
                        new { type = "heading", content = new { text = "2. Materials and Methods", level = 2 }, sortOrder = "000009" },
                        new { type = "paragraph", content = new { text = "Describe materials, equipment, and experimental procedures..." }, sortOrder = "000010" },
                        new { type = "heading", content = new { text = "3. Results", level = 2 }, sortOrder = "000011" },
                        new { type = "paragraph", content = new { text = "Present findings with figures and tables..." }, sortOrder = "000012" },
                        new { type = "heading", content = new { text = "4. Discussion", level = 2 }, sortOrder = "000013" },
                        new { type = "paragraph", content = new { text = "Interpret results and compare with existing literature..." }, sortOrder = "000014" },
                        new { type = "heading", content = new { text = "5. Conclusions", level = 2 }, sortOrder = "000015" },
                        new { type = "paragraph", content = new { text = "Summarize main conclusions and implications..." }, sortOrder = "000016" },
                        new { type = "heading", content = new { text = "Declaration of Competing Interest", level = 2 }, sortOrder = "000017" },
                        new { type = "paragraph", content = new { text = "The authors declare that they have no known competing financial interests..." }, sortOrder = "000018" },
                        new { type = "heading", content = new { text = "Acknowledgements", level = 2 }, sortOrder = "000019" },
                        new { type = "paragraph", content = new { text = "This research was funded by..." }, sortOrder = "000020" },
                        new { type = "heading", content = new { text = "References", level = 2 }, sortOrder = "000021" },
                        new { type = "bibliography", content = new { }, sortOrder = "000022" }
                    }
                }
            ),
            CreateTemplate(
                "Short Paper / Extended Abstract",
                "2-4 page format for workshop papers or extended abstracts.",
                TemplateCategories.Academic,
                new
                {
                    language = "en",
                    paperSize = "a4",
                    fontFamily = "charter",
                    fontSize = 10,
                    blocks = new object[]
                    {
                        new { type = "heading", content = new { text = "Short Paper Title", level = 1 }, sortOrder = "000000" },
                        new { type = "paragraph", content = new { text = "<em>Author Name</em>, Institution, email@domain.com" }, sortOrder = "000001" },
                        new { type = "abstract", content = new { text = "Brief abstract (100-150 words) summarizing the contribution." }, sortOrder = "000002" },
                        new { type = "heading", content = new { text = "Introduction", level = 2 }, sortOrder = "000003" },
                        new { type = "paragraph", content = new { text = "Motivate the problem and state contributions..." }, sortOrder = "000004" },
                        new { type = "heading", content = new { text = "Approach", level = 2 }, sortOrder = "000005" },
                        new { type = "paragraph", content = new { text = "Describe your method briefly..." }, sortOrder = "000006" },
                        new { type = "heading", content = new { text = "Preliminary Results", level = 2 }, sortOrder = "000007" },
                        new { type = "paragraph", content = new { text = "Present initial findings..." }, sortOrder = "000008" },
                        new { type = "heading", content = new { text = "Conclusion and Future Work", level = 2 }, sortOrder = "000009" },
                        new { type = "paragraph", content = new { text = "Summarize and outline next steps..." }, sortOrder = "000010" },
                        new { type = "heading", content = new { text = "References", level = 2 }, sortOrder = "000011" },
                        new { type = "bibliography", content = new { }, sortOrder = "000012" }
                    }
                }
            ),
            CreateTemplate(
                "Literature Review",
                "Template for systematic literature reviews and survey papers.",
                TemplateCategories.Academic,
                new
                {
                    language = "en",
                    paperSize = "a4",
                    fontFamily = "charter",
                    fontSize = 11,
                    blocks = new object[]
                    {
                        new { type = "heading", content = new { text = "A Systematic Review of [Topic]", level = 1 }, sortOrder = "000000" },
                        new { type = "abstract", content = new { text = "This paper presents a systematic review of... We analyzed N papers published between YYYY and YYYY..." }, sortOrder = "000001" },
                        new { type = "paragraph", content = new { text = "<strong>Keywords:</strong> systematic review, literature survey, [topic keywords]" }, sortOrder = "000002" },
                        new { type = "heading", content = new { text = "1. Introduction", level = 2 }, sortOrder = "000003" },
                        new { type = "paragraph", content = new { text = "Motivation for the review and research questions..." }, sortOrder = "000004" },
                        new { type = "heading", content = new { text = "2. Methodology", level = 2 }, sortOrder = "000005" },
                        new { type = "heading", content = new { text = "2.1 Search Strategy", level = 3 }, sortOrder = "000006" },
                        new { type = "paragraph", content = new { text = "Databases searched, search terms, date range..." }, sortOrder = "000007" },
                        new { type = "heading", content = new { text = "2.2 Inclusion/Exclusion Criteria", level = 3 }, sortOrder = "000008" },
                        new { type = "paragraph", content = new { text = "Papers were included if... Papers were excluded if..." }, sortOrder = "000009" },
                        new { type = "heading", content = new { text = "2.3 Data Extraction", level = 3 }, sortOrder = "000010" },
                        new { type = "paragraph", content = new { text = "Information extracted from each paper..." }, sortOrder = "000011" },
                        new { type = "heading", content = new { text = "3. Results", level = 2 }, sortOrder = "000012" },
                        new { type = "heading", content = new { text = "3.1 Overview of Selected Studies", level = 3 }, sortOrder = "000013" },
                        new { type = "paragraph", content = new { text = "Summary statistics of the corpus..." }, sortOrder = "000014" },
                        new { type = "heading", content = new { text = "3.2 Thematic Analysis", level = 3 }, sortOrder = "000015" },
                        new { type = "paragraph", content = new { text = "Key themes identified in the literature..." }, sortOrder = "000016" },
                        new { type = "heading", content = new { text = "4. Discussion", level = 2 }, sortOrder = "000017" },
                        new { type = "paragraph", content = new { text = "Synthesis of findings, gaps in literature..." }, sortOrder = "000018" },
                        new { type = "heading", content = new { text = "5. Conclusion", level = 2 }, sortOrder = "000019" },
                        new { type = "paragraph", content = new { text = "Summary and directions for future research..." }, sortOrder = "000020" },
                        new { type = "heading", content = new { text = "References", level = 2 }, sortOrder = "000021" },
                        new { type = "bibliography", content = new { }, sortOrder = "000022" }
                    }
                }
            ),

            // === CVs & RESUMES ===
            CreateTemplate(
                "Modern Resume",
                "Clean, professional one-page resume for industry positions.",
                TemplateCategories.Other,
                new
                {
                    language = "en",
                    paperSize = "a4",
                    fontFamily = "helvetica",
                    fontSize = 10,
                    blocks = new object[]
                    {
                        new { type = "heading", content = new { text = "Your Name", level = 1 }, sortOrder = "000000" },
                        new { type = "paragraph", content = new { text = "City, State • (555) 123-4567 • email@domain.com • linkedin.com/in/yourname • github.com/yourname" }, sortOrder = "000001" },
                        new { type = "heading", content = new { text = "Summary", level = 2 }, sortOrder = "000002" },
                        new { type = "paragraph", content = new { text = "Experienced [role] with X+ years in [industry]. Skilled in [key skills]. Proven track record of [achievements]." }, sortOrder = "000003" },
                        new { type = "heading", content = new { text = "Experience", level = 2 }, sortOrder = "000004" },
                        new { type = "paragraph", content = new { text = "<strong>Job Title</strong> | Company Name | City, State | Month YYYY – Present" }, sortOrder = "000005" },
                        new { type = "paragraph", content = new { text = "• Accomplished [X] by implementing [Y], resulting in [Z]<br/>• Led team of N to deliver [project] on time and under budget<br/>• Improved [metric] by X% through [action]" }, sortOrder = "000006" },
                        new { type = "paragraph", content = new { text = "<strong>Previous Job Title</strong> | Company Name | City, State | Month YYYY – Month YYYY" }, sortOrder = "000007" },
                        new { type = "paragraph", content = new { text = "• Achievement with quantifiable result<br/>• Another accomplishment<br/>• Third bullet point" }, sortOrder = "000008" },
                        new { type = "heading", content = new { text = "Education", level = 2 }, sortOrder = "000009" },
                        new { type = "paragraph", content = new { text = "<strong>Degree Name</strong>, Major | University Name | Graduation Year<br/>GPA: X.XX (if notable) • Relevant coursework: Course 1, Course 2" }, sortOrder = "000010" },
                        new { type = "heading", content = new { text = "Skills", level = 2 }, sortOrder = "000011" },
                        new { type = "paragraph", content = new { text = "<strong>Technical:</strong> Skill 1, Skill 2, Skill 3, Skill 4<br/><strong>Tools:</strong> Tool 1, Tool 2, Tool 3<br/><strong>Languages:</strong> English (Native), Spanish (Conversational)" }, sortOrder = "000012" },
                        new { type = "heading", content = new { text = "Projects", level = 2 }, sortOrder = "000013" },
                        new { type = "paragraph", content = new { text = "<strong>Project Name</strong> – Brief description of the project and your role. Technologies used." }, sortOrder = "000014" }
                    }
                }
            ),
            CreateTemplate(
                "Simple CV",
                "Minimal, elegant CV template suitable for any profession.",
                TemplateCategories.Other,
                new
                {
                    language = "en",
                    paperSize = "a4",
                    fontFamily = "charter",
                    fontSize = 11,
                    blocks = new object[]
                    {
                        new { type = "heading", content = new { text = "Full Name", level = 1 }, sortOrder = "000000" },
                        new { type = "paragraph", content = new { text = "Address Line 1, City, Postal Code<br/>Phone: +1 234 567 8900 | Email: your.email@domain.com" }, sortOrder = "000001" },
                        new { type = "heading", content = new { text = "Professional Profile", level = 2 }, sortOrder = "000002" },
                        new { type = "paragraph", content = new { text = "A brief professional summary highlighting your expertise and career goals..." }, sortOrder = "000003" },
                        new { type = "heading", content = new { text = "Work Experience", level = 2 }, sortOrder = "000004" },
                        new { type = "paragraph", content = new { text = "<strong>Position Title</strong><br/><em>Company Name, Location</em> | YYYY – Present<br/>Description of responsibilities and achievements." }, sortOrder = "000005" },
                        new { type = "paragraph", content = new { text = "<strong>Previous Position</strong><br/><em>Company Name, Location</em> | YYYY – YYYY<br/>Description of responsibilities and achievements." }, sortOrder = "000006" },
                        new { type = "heading", content = new { text = "Education", level = 2 }, sortOrder = "000007" },
                        new { type = "paragraph", content = new { text = "<strong>Degree Title</strong><br/><em>University Name, Location</em> | YYYY<br/>Relevant details, honors, thesis title if applicable." }, sortOrder = "000008" },
                        new { type = "heading", content = new { text = "Skills", level = 2 }, sortOrder = "000009" },
                        new { type = "paragraph", content = new { text = "• Skill category 1: specific skills<br/>• Skill category 2: specific skills<br/>• Languages: List with proficiency levels" }, sortOrder = "000010" },
                        new { type = "heading", content = new { text = "References", level = 2 }, sortOrder = "000011" },
                        new { type = "paragraph", content = new { text = "Available upon request." }, sortOrder = "000012" }
                    }
                }
            ),

            // === LETTERS ===
            CreateTemplate(
                "Cover Letter",
                "Professional cover letter for job applications.",
                TemplateCategories.Other,
                new
                {
                    language = "en",
                    paperSize = "a4",
                    fontFamily = "charter",
                    fontSize = 11,
                    blocks = new object[]
                    {
                        new { type = "paragraph", content = new { text = "Your Name<br/>Your Address<br/>City, State ZIP<br/>your.email@domain.com<br/>(555) 123-4567" }, sortOrder = "000000" },
                        new { type = "paragraph", content = new { text = "Date" }, sortOrder = "000001" },
                        new { type = "paragraph", content = new { text = "Hiring Manager's Name<br/>Company Name<br/>Company Address<br/>City, State ZIP" }, sortOrder = "000002" },
                        new { type = "paragraph", content = new { text = "Dear Hiring Manager," }, sortOrder = "000003" },
                        new { type = "paragraph", content = new { text = "I am writing to express my interest in the [Position Title] role at [Company Name], as advertised on [where you found the job]. With my background in [relevant field] and [X years] of experience in [relevant area], I am confident in my ability to contribute effectively to your team." }, sortOrder = "000004" },
                        new { type = "paragraph", content = new { text = "In my current role at [Current Company], I have [key achievement 1]. Additionally, I [key achievement 2]. These experiences have equipped me with [relevant skills] that align well with the requirements of this position." }, sortOrder = "000005" },
                        new { type = "paragraph", content = new { text = "I am particularly drawn to [Company Name] because of [specific reason - company values, projects, reputation]. I believe my skills in [specific skills] would allow me to [how you would contribute]." }, sortOrder = "000006" },
                        new { type = "paragraph", content = new { text = "I would welcome the opportunity to discuss how my experience and enthusiasm can benefit [Company Name]. Thank you for considering my application. I look forward to hearing from you." }, sortOrder = "000007" },
                        new { type = "paragraph", content = new { text = "Sincerely,<br/><br/><br/>Your Name" }, sortOrder = "000008" }
                    }
                }
            ),
            CreateTemplate(
                "Recommendation Letter",
                "Academic or professional recommendation letter template.",
                TemplateCategories.Other,
                new
                {
                    language = "en",
                    paperSize = "a4",
                    fontFamily = "charter",
                    fontSize = 11,
                    blocks = new object[]
                    {
                        new { type = "paragraph", content = new { text = "[Your Name]<br/>[Your Title]<br/>[Department/Organization]<br/>[Address]<br/>[Email] | [Phone]" }, sortOrder = "000000" },
                        new { type = "paragraph", content = new { text = "[Date]" }, sortOrder = "000001" },
                        new { type = "paragraph", content = new { text = "To Whom It May Concern," }, sortOrder = "000002" },
                        new { type = "paragraph", content = new { text = "I am writing to recommend [Candidate Name] for [position/program/opportunity]. I have known [him/her/them] for [duration] in my capacity as [your relationship - professor, supervisor, colleague]." }, sortOrder = "000003" },
                        new { type = "paragraph", content = new { text = "[Candidate Name] has demonstrated exceptional [qualities] during [his/her/their] time at [organization/institution]. Specifically, [he/she/they] [specific example of achievement or skill]. This exemplifies [his/her/their] [relevant quality]." }, sortOrder = "000004" },
                        new { type = "paragraph", content = new { text = "In addition to [his/her/their] technical abilities, [Candidate Name] possesses excellent [soft skills - communication, teamwork, leadership]. [He/She/They] [another specific example]." }, sortOrder = "000005" },
                        new { type = "paragraph", content = new { text = "[Optional: Compare to other candidates] Among the [number] students/employees I have worked with in my [X] years at [organization], [Candidate Name] ranks in the top [percentage/number]." }, sortOrder = "000006" },
                        new { type = "paragraph", content = new { text = "I give [Candidate Name] my highest recommendation without reservation. [He/She/They] would be a valuable addition to [program/organization]. Please feel free to contact me if you require any additional information." }, sortOrder = "000007" },
                        new { type = "paragraph", content = new { text = "Sincerely,<br/><br/><br/>[Your Name]<br/>[Your Title]" }, sortOrder = "000008" }
                    }
                }
            ),
            CreateTemplate(
                "Formal Letter",
                "General formal letter template for official correspondence.",
                TemplateCategories.Other,
                new
                {
                    language = "en",
                    paperSize = "a4",
                    fontFamily = "charter",
                    fontSize = 11,
                    blocks = new object[]
                    {
                        new { type = "paragraph", content = new { text = "[Your Name/Organization]<br/>[Address Line 1]<br/>[Address Line 2]<br/>[City, State/Province, Postal Code]" }, sortOrder = "000000" },
                        new { type = "paragraph", content = new { text = "[Date]" }, sortOrder = "000001" },
                        new { type = "paragraph", content = new { text = "[Recipient's Name]<br/>[Title]<br/>[Organization]<br/>[Address]<br/>[City, State/Province, Postal Code]" }, sortOrder = "000002" },
                        new { type = "paragraph", content = new { text = "<strong>Re: [Subject Line]</strong>" }, sortOrder = "000003" },
                        new { type = "paragraph", content = new { text = "Dear [Mr./Ms./Dr.] [Last Name]," }, sortOrder = "000004" },
                        new { type = "paragraph", content = new { text = "[Opening paragraph: State the purpose of your letter clearly and concisely.]" }, sortOrder = "000005" },
                        new { type = "paragraph", content = new { text = "[Body paragraph(s): Provide relevant details, context, or supporting information.]" }, sortOrder = "000006" },
                        new { type = "paragraph", content = new { text = "[Closing paragraph: Summarize key points and state any required actions or next steps.]" }, sortOrder = "000007" },
                        new { type = "paragraph", content = new { text = "Thank you for your attention to this matter. Please do not hesitate to contact me if you have any questions." }, sortOrder = "000008" },
                        new { type = "paragraph", content = new { text = "Sincerely,<br/><br/><br/>[Your Name]<br/>[Your Title]<br/><br/>Enclosures: [if applicable]<br/>cc: [if applicable]" }, sortOrder = "000009" }
                    }
                }
            ),

            // === REPORTS ===
            CreateTemplate(
                "Lab Report",
                "Scientific laboratory report with standard sections.",
                TemplateCategories.Report,
                new
                {
                    language = "en",
                    paperSize = "a4",
                    fontFamily = "charter",
                    fontSize = 11,
                    blocks = new object[]
                    {
                        new { type = "heading", content = new { text = "Lab Report: [Experiment Title]", level = 1 }, sortOrder = "000000" },
                        new { type = "paragraph", content = new { text = "<strong>Course:</strong> [Course Name and Number]<br/><strong>Date:</strong> [Date of Experiment]<br/><strong>Name:</strong> [Your Name]<br/><strong>Lab Partner(s):</strong> [Partner Names]<br/><strong>Instructor:</strong> [Instructor Name]" }, sortOrder = "000001" },
                        new { type = "heading", content = new { text = "1. Objective", level = 2 }, sortOrder = "000002" },
                        new { type = "paragraph", content = new { text = "State the purpose of the experiment and what you aim to demonstrate or measure..." }, sortOrder = "000003" },
                        new { type = "heading", content = new { text = "2. Introduction/Theory", level = 2 }, sortOrder = "000004" },
                        new { type = "paragraph", content = new { text = "Provide background theory and relevant equations..." }, sortOrder = "000005" },
                        new { type = "equation", content = new { latex = "F = ma", equationMode = "display" }, sortOrder = "000006" },
                        new { type = "heading", content = new { text = "3. Materials and Equipment", level = 2 }, sortOrder = "000007" },
                        new { type = "paragraph", content = new { text = "• Item 1<br/>• Item 2<br/>• Item 3<br/>• Measuring instruments used" }, sortOrder = "000008" },
                        new { type = "heading", content = new { text = "4. Procedure", level = 2 }, sortOrder = "000009" },
                        new { type = "paragraph", content = new { text = "1. First step in the procedure<br/>2. Second step<br/>3. Third step<br/>4. Continue as needed..." }, sortOrder = "000010" },
                        new { type = "heading", content = new { text = "5. Data and Observations", level = 2 }, sortOrder = "000011" },
                        new { type = "paragraph", content = new { text = "Record raw data in tables. Include units and uncertainties..." }, sortOrder = "000012" },
                        new { type = "heading", content = new { text = "6. Analysis and Calculations", level = 2 }, sortOrder = "000013" },
                        new { type = "paragraph", content = new { text = "Show sample calculations and data analysis..." }, sortOrder = "000014" },
                        new { type = "heading", content = new { text = "7. Results", level = 2 }, sortOrder = "000015" },
                        new { type = "paragraph", content = new { text = "Present final results with uncertainties and compare to expected values..." }, sortOrder = "000016" },
                        new { type = "heading", content = new { text = "8. Discussion", level = 2 }, sortOrder = "000017" },
                        new { type = "paragraph", content = new { text = "Discuss sources of error, limitations, and how results compare to theory..." }, sortOrder = "000018" },
                        new { type = "heading", content = new { text = "9. Conclusion", level = 2 }, sortOrder = "000019" },
                        new { type = "paragraph", content = new { text = "Summarize what was learned and whether objectives were met..." }, sortOrder = "000020" },
                        new { type = "heading", content = new { text = "References", level = 2 }, sortOrder = "000021" },
                        new { type = "bibliography", content = new { }, sortOrder = "000022" }
                    }
                }
            ),
            CreateTemplate(
                "Project Proposal",
                "Template for academic or business project proposals.",
                TemplateCategories.Report,
                new
                {
                    language = "en",
                    paperSize = "a4",
                    fontFamily = "charter",
                    fontSize = 11,
                    blocks = new object[]
                    {
                        new { type = "heading", content = new { text = "Project Proposal: [Project Title]", level = 1 }, sortOrder = "000000" },
                        new { type = "paragraph", content = new { text = "<strong>Submitted by:</strong> [Your Name/Team]<br/><strong>Date:</strong> [Date]<br/><strong>Submitted to:</strong> [Recipient/Organization]" }, sortOrder = "000001" },
                        new { type = "heading", content = new { text = "Executive Summary", level = 2 }, sortOrder = "000002" },
                        new { type = "paragraph", content = new { text = "Brief overview of the project, its goals, and expected outcomes (1-2 paragraphs)..." }, sortOrder = "000003" },
                        new { type = "heading", content = new { text = "1. Introduction", level = 2 }, sortOrder = "000004" },
                        new { type = "heading", content = new { text = "1.1 Background", level = 3 }, sortOrder = "000005" },
                        new { type = "paragraph", content = new { text = "Context and motivation for the project..." }, sortOrder = "000006" },
                        new { type = "heading", content = new { text = "1.2 Problem Statement", level = 3 }, sortOrder = "000007" },
                        new { type = "paragraph", content = new { text = "Clearly define the problem or opportunity being addressed..." }, sortOrder = "000008" },
                        new { type = "heading", content = new { text = "2. Objectives", level = 2 }, sortOrder = "000009" },
                        new { type = "paragraph", content = new { text = "• Primary objective<br/>• Secondary objective 1<br/>• Secondary objective 2" }, sortOrder = "000010" },
                        new { type = "heading", content = new { text = "3. Scope", level = 2 }, sortOrder = "000011" },
                        new { type = "paragraph", content = new { text = "Define what is included and excluded from the project..." }, sortOrder = "000012" },
                        new { type = "heading", content = new { text = "4. Methodology/Approach", level = 2 }, sortOrder = "000013" },
                        new { type = "paragraph", content = new { text = "Describe how you will accomplish the objectives..." }, sortOrder = "000014" },
                        new { type = "heading", content = new { text = "5. Timeline", level = 2 }, sortOrder = "000015" },
                        new { type = "paragraph", content = new { text = "Phase 1: [Description] - [Duration]<br/>Phase 2: [Description] - [Duration]<br/>Phase 3: [Description] - [Duration]" }, sortOrder = "000016" },
                        new { type = "heading", content = new { text = "6. Budget", level = 2 }, sortOrder = "000017" },
                        new { type = "paragraph", content = new { text = "Itemized budget with justifications..." }, sortOrder = "000018" },
                        new { type = "heading", content = new { text = "7. Expected Outcomes", level = 2 }, sortOrder = "000019" },
                        new { type = "paragraph", content = new { text = "Describe deliverables and success metrics..." }, sortOrder = "000020" },
                        new { type = "heading", content = new { text = "8. Risks and Mitigation", level = 2 }, sortOrder = "000021" },
                        new { type = "paragraph", content = new { text = "Identify potential risks and how they will be addressed..." }, sortOrder = "000022" },
                        new { type = "heading", content = new { text = "9. Conclusion", level = 2 }, sortOrder = "000023" },
                        new { type = "paragraph", content = new { text = "Summarize the proposal and call to action..." }, sortOrder = "000024" },
                        new { type = "heading", content = new { text = "References", level = 2 }, sortOrder = "000025" },
                        new { type = "bibliography", content = new { }, sortOrder = "000026" }
                    }
                }
            ),
            CreateTemplate(
                "Business Report",
                "Professional business report with analysis and recommendations.",
                TemplateCategories.Report,
                new
                {
                    language = "en",
                    paperSize = "a4",
                    fontFamily = "helvetica",
                    fontSize = 11,
                    blocks = new object[]
                    {
                        new { type = "heading", content = new { text = "Business Report: [Title]", level = 1 }, sortOrder = "000000" },
                        new { type = "paragraph", content = new { text = "<strong>Prepared for:</strong> [Recipient/Department]<br/><strong>Prepared by:</strong> [Your Name/Team]<br/><strong>Date:</strong> [Date]<br/><strong>Report Period:</strong> [Time Period Covered]" }, sortOrder = "000001" },
                        new { type = "heading", content = new { text = "Executive Summary", level = 2 }, sortOrder = "000002" },
                        new { type = "paragraph", content = new { text = "High-level summary of key findings, conclusions, and recommendations. This section should stand alone and provide decision-makers with the essential information." }, sortOrder = "000003" },
                        new { type = "heading", content = new { text = "1. Introduction", level = 2 }, sortOrder = "000004" },
                        new { type = "paragraph", content = new { text = "Purpose of the report, scope, and methodology used..." }, sortOrder = "000005" },
                        new { type = "heading", content = new { text = "2. Background", level = 2 }, sortOrder = "000006" },
                        new { type = "paragraph", content = new { text = "Relevant context and background information..." }, sortOrder = "000007" },
                        new { type = "heading", content = new { text = "3. Findings", level = 2 }, sortOrder = "000008" },
                        new { type = "heading", content = new { text = "3.1 Key Finding 1", level = 3 }, sortOrder = "000009" },
                        new { type = "paragraph", content = new { text = "Description and supporting data..." }, sortOrder = "000010" },
                        new { type = "heading", content = new { text = "3.2 Key Finding 2", level = 3 }, sortOrder = "000011" },
                        new { type = "paragraph", content = new { text = "Description and supporting data..." }, sortOrder = "000012" },
                        new { type = "heading", content = new { text = "4. Analysis", level = 2 }, sortOrder = "000013" },
                        new { type = "paragraph", content = new { text = "Interpretation of findings, trends, and implications..." }, sortOrder = "000014" },
                        new { type = "heading", content = new { text = "5. Recommendations", level = 2 }, sortOrder = "000015" },
                        new { type = "paragraph", content = new { text = "1. First recommendation with rationale<br/>2. Second recommendation with rationale<br/>3. Third recommendation with rationale" }, sortOrder = "000016" },
                        new { type = "heading", content = new { text = "6. Conclusion", level = 2 }, sortOrder = "000017" },
                        new { type = "paragraph", content = new { text = "Summary of main points and next steps..." }, sortOrder = "000018" },
                        new { type = "heading", content = new { text = "Appendices", level = 2 }, sortOrder = "000019" },
                        new { type = "paragraph", content = new { text = "Supporting data, charts, detailed calculations..." }, sortOrder = "000020" }
                    }
                }
            ),

            // === OTHER USEFUL TEMPLATES ===
            CreateTemplate(
                "Meeting Minutes",
                "Template for recording meeting notes and action items.",
                TemplateCategories.Other,
                new
                {
                    language = "en",
                    paperSize = "a4",
                    fontFamily = "helvetica",
                    fontSize = 11,
                    blocks = new object[]
                    {
                        new { type = "heading", content = new { text = "Meeting Minutes", level = 1 }, sortOrder = "000000" },
                        new { type = "paragraph", content = new { text = "<strong>Meeting Title:</strong> [Title]<br/><strong>Date:</strong> [Date]<br/><strong>Time:</strong> [Start Time] – [End Time]<br/><strong>Location:</strong> [Location/Virtual Platform]<br/><strong>Facilitator:</strong> [Name]<br/><strong>Note Taker:</strong> [Name]" }, sortOrder = "000001" },
                        new { type = "heading", content = new { text = "Attendees", level = 2 }, sortOrder = "000002" },
                        new { type = "paragraph", content = new { text = "<strong>Present:</strong> Name 1, Name 2, Name 3<br/><strong>Absent:</strong> Name 4<br/><strong>Guests:</strong> Name 5" }, sortOrder = "000003" },
                        new { type = "heading", content = new { text = "Agenda", level = 2 }, sortOrder = "000004" },
                        new { type = "paragraph", content = new { text = "1. Agenda item 1<br/>2. Agenda item 2<br/>3. Agenda item 3" }, sortOrder = "000005" },
                        new { type = "heading", content = new { text = "Discussion", level = 2 }, sortOrder = "000006" },
                        new { type = "heading", content = new { text = "Item 1: [Topic]", level = 3 }, sortOrder = "000007" },
                        new { type = "paragraph", content = new { text = "Summary of discussion points and decisions made..." }, sortOrder = "000008" },
                        new { type = "heading", content = new { text = "Item 2: [Topic]", level = 3 }, sortOrder = "000009" },
                        new { type = "paragraph", content = new { text = "Summary of discussion points and decisions made..." }, sortOrder = "000010" },
                        new { type = "heading", content = new { text = "Action Items", level = 2 }, sortOrder = "000011" },
                        new { type = "paragraph", content = new { text = "| Action | Owner | Due Date | Status |<br/>|--------|-------|----------|--------|<br/>| Task 1 | Name | Date | Pending |<br/>| Task 2 | Name | Date | Pending |" }, sortOrder = "000012" },
                        new { type = "heading", content = new { text = "Next Meeting", level = 2 }, sortOrder = "000013" },
                        new { type = "paragraph", content = new { text = "<strong>Date:</strong> [Next Date]<br/><strong>Time:</strong> [Time]<br/><strong>Location:</strong> [Location]<br/><strong>Tentative Agenda:</strong> Items to discuss" }, sortOrder = "000014" }
                    }
                }
            ),
            CreateTemplate(
                "Lecture Notes",
                "Structured template for taking and organizing lecture notes.",
                TemplateCategories.Academic,
                new
                {
                    language = "en",
                    paperSize = "a4",
                    fontFamily = "charter",
                    fontSize = 11,
                    blocks = new object[]
                    {
                        new { type = "heading", content = new { text = "Lecture Notes: [Topic]", level = 1 }, sortOrder = "000000" },
                        new { type = "paragraph", content = new { text = "<strong>Course:</strong> [Course Name]<br/><strong>Date:</strong> [Date]<br/><strong>Instructor:</strong> [Instructor Name]<br/><strong>Lecture #:</strong> [Number]" }, sortOrder = "000001" },
                        new { type = "heading", content = new { text = "Learning Objectives", level = 2 }, sortOrder = "000002" },
                        new { type = "paragraph", content = new { text = "• Objective 1<br/>• Objective 2<br/>• Objective 3" }, sortOrder = "000003" },
                        new { type = "heading", content = new { text = "Key Concepts", level = 2 }, sortOrder = "000004" },
                        new { type = "heading", content = new { text = "1. [First Main Topic]", level = 3 }, sortOrder = "000005" },
                        new { type = "paragraph", content = new { text = "Notes and explanations..." }, sortOrder = "000006" },
                        new { type = "theorem", content = new { theoremType = "definition", title = "Key Term", text = "Definition of an important concept..." }, sortOrder = "000007" },
                        new { type = "heading", content = new { text = "2. [Second Main Topic]", level = 3 }, sortOrder = "000008" },
                        new { type = "paragraph", content = new { text = "Notes and explanations..." }, sortOrder = "000009" },
                        new { type = "equation", content = new { latex = "\\text{Important equation here}", equationMode = "display" }, sortOrder = "000010" },
                        new { type = "heading", content = new { text = "Examples", level = 2 }, sortOrder = "000011" },
                        new { type = "theorem", content = new { theoremType = "example", text = "Worked example from lecture..." }, sortOrder = "000012" },
                        new { type = "heading", content = new { text = "Questions", level = 2 }, sortOrder = "000013" },
                        new { type = "paragraph", content = new { text = "• Question to follow up on<br/>• Concept that needs clarification" }, sortOrder = "000014" },
                        new { type = "heading", content = new { text = "Summary", level = 2 }, sortOrder = "000015" },
                        new { type = "paragraph", content = new { text = "Key takeaways from this lecture..." }, sortOrder = "000016" },
                        new { type = "heading", content = new { text = "Readings & Assignments", level = 2 }, sortOrder = "000017" },
                        new { type = "paragraph", content = new { text = "• Textbook: Chapter X, pages Y-Z<br/>• Assignment: Problem set due [date]" }, sortOrder = "000018" }
                    }
                }
            ),
            CreateTemplate(
                "Book Chapter",
                "Template for writing book chapters or long-form content.",
                TemplateCategories.Other,
                new
                {
                    language = "en",
                    paperSize = "a4",
                    fontFamily = "charter",
                    fontSize = 12,
                    blocks = new object[]
                    {
                        new { type = "heading", content = new { text = "Chapter [Number]: [Chapter Title]", level = 1 }, sortOrder = "000000" },
                        new { type = "paragraph", content = new { text = "<em>[Optional epigraph or opening quote]</em>" }, sortOrder = "000001" },
                        new { type = "heading", content = new { text = "[First Section Title]", level = 2 }, sortOrder = "000002" },
                        new { type = "paragraph", content = new { text = "Opening paragraph that introduces the chapter's main themes and hooks the reader..." }, sortOrder = "000003" },
                        new { type = "paragraph", content = new { text = "Continue developing the narrative or argument. Each paragraph should flow logically to the next..." }, sortOrder = "000004" },
                        new { type = "heading", content = new { text = "[Subsection Title]", level = 3 }, sortOrder = "000005" },
                        new { type = "paragraph", content = new { text = "More detailed exploration of a specific aspect..." }, sortOrder = "000006" },
                        new { type = "heading", content = new { text = "[Second Section Title]", level = 2 }, sortOrder = "000007" },
                        new { type = "paragraph", content = new { text = "Transition to new topic or aspect of the chapter..." }, sortOrder = "000008" },
                        new { type = "paragraph", content = new { text = "Development of ideas with examples, evidence, or narrative..." }, sortOrder = "000009" },
                        new { type = "heading", content = new { text = "[Third Section Title]", level = 2 }, sortOrder = "000010" },
                        new { type = "paragraph", content = new { text = "Continue building the chapter's argument or story..." }, sortOrder = "000011" },
                        new { type = "paragraph", content = new { text = "Concluding thoughts that tie together the chapter's themes and perhaps foreshadow what comes next..." }, sortOrder = "000012" },
                        new { type = "heading", content = new { text = "Notes", level = 2 }, sortOrder = "000013" },
                        new { type = "paragraph", content = new { text = "1. Endnote with additional context or citation.<br/>2. Another endnote." }, sortOrder = "000014" }
                    }
                }
            ),
            CreateTemplate(
                "Blank Document",
                "Start with a clean slate - no predefined structure.",
                TemplateCategories.Other,
                new
                {
                    language = "en",
                    paperSize = "a4",
                    fontFamily = "charter",
                    fontSize = 11,
                    blocks = new object[]
                    {
                        new { type = "heading", content = new { text = "Untitled Document", level = 1 }, sortOrder = "000000" },
                        new { type = "paragraph", content = new { text = "Start writing here..." }, sortOrder = "000001" }
                    }
                }
            )
        };
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static Template CreateTemplate(string name, string description, string category, object content)
    {
        return new Template
        {
            Id = Guid.NewGuid(),
            UserId = null, // System template
            Name = name,
            Description = description,
            Category = category,
            Thumbnail = null,
            Content = JsonDocument.Parse(JsonSerializer.Serialize(content, JsonOptions)),
            IsPublic = true,
            IsSystem = true,
            UsageCount = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }
}

-- Batch 2: Social Sciences & Humanities Sample Thesis Documents (11-20)
-- 10 AI-generated sample documents showcasing Lilia editor features

-- Ensure the sample-content user exists (idempotent)
INSERT INTO users (id, email, name, created_at)
VALUES ('sample-content', 'sample@lilia.app', 'Sample Content', NOW())
ON CONFLICT (id) DO NOTHING;

-- ============================================================================
-- Document 11: Economic Impact of AI
-- Features: tables, statistics, citations, figures
-- ============================================================================
DO $$
DECLARE
  doc_id UUID;
  user_id VARCHAR(255) := 'sample-content';
BEGIN
  INSERT INTO documents (id, owner_id, title, language, paper_size, font_family, font_size, is_public)
  VALUES (gen_random_uuid(), user_id, 'Economic Impact of Artificial Intelligence on Labor Markets', 'en', 'a4', 'serif', 12, TRUE)
  RETURNING id INTO doc_id;

  INSERT INTO blocks (document_id, type, content, sort_order) VALUES
  (doc_id, 'heading', '{"text": "Economic Impact of Artificial Intelligence on Labor Markets", "level": 1}', 0),
  (doc_id, 'abstract', '{"text": "This thesis examines the economic implications of artificial intelligence adoption on employment, wages, and occupational structure. Through econometric analysis of industry-level data, we quantify the effects of automation on labor market outcomes and project future workforce transitions. *This is a sample document generated to demonstrate Lilia editor capabilities. The content is AI-generated for demonstration purposes only.*"}', 1),
  (doc_id, 'pagebreak', '{}', 2),
  (doc_id, 'heading', '{"text": "1. Introduction", "level": 2}', 3),
  (doc_id, 'paragraph', '{"text": "The rapid advancement of artificial intelligence technologies is transforming the global economy, raising fundamental questions about the future of work \\cite{brynjolfsson2014}. Unlike previous waves of automation, AI systems can perform cognitive tasks traditionally requiring human intelligence, potentially affecting a broader range of occupations."}', 4),
  (doc_id, 'paragraph', '{"text": "This thesis provides empirical analysis of AI''s economic impact, examining both displacement effects and productivity gains. We draw on labor economics theory and recent empirical studies to develop a comprehensive framework for understanding technological unemployment \\cite{acemoglu2020}."}', 5),
  (doc_id, 'heading', '{"text": "2. Literature Review", "level": 2}', 6),
  (doc_id, 'paragraph', '{"text": "The economic literature on automation and employment spans several decades. Autor et al. \\cite{autor2003} established the \"routine-biased technical change\" hypothesis, showing that computerization displaced middle-skill occupations while complementing high-skill cognitive work."}', 7),
  (doc_id, 'paragraph', '{"text": "Recent studies have focused specifically on AI and machine learning. Frey and Osborne \\cite{frey2017} estimated that 47% of US employment is at high risk of automation, sparking both academic debate and public concern about technological unemployment."}', 8),
  (doc_id, 'list', '{"items": ["Skill-biased technological change theories", "Task-based models of labor markets", "General-purpose technology diffusion", "Job polarization and wage inequality"], "ordered": false}', 9),
  (doc_id, 'pagebreak', '{}', 10),
  (doc_id, 'heading', '{"text": "3. Methodology", "level": 2}', 11),
  (doc_id, 'heading', '{"text": "3.1 Data Sources", "level": 3}', 12),
  (doc_id, 'paragraph', '{"text": "Our analysis utilizes multiple data sources to capture AI adoption and labor market outcomes:"}', 13),
  (doc_id, 'table', '{"headers": ["Dataset", "Source", "Period", "Observations"], "rows": [["Employment Statistics", "Bureau of Labor Statistics", "2010-2023", "850 occupations"], ["AI Patent Data", "USPTO", "2010-2023", "124,500 patents"], ["Job Postings", "Burning Glass", "2015-2023", "42M postings"], ["Wage Data", "CPS/ACS", "2010-2023", "3.2M individuals"], ["Industry AI Adoption", "Census Annual Survey", "2018-2023", "45,000 firms"]]}', 14),
  (doc_id, 'heading', '{"text": "3.2 Empirical Strategy", "level": 3}', 15),
  (doc_id, 'paragraph', '{"text": "We estimate the impact of AI exposure on employment using a difference-in-differences framework:"}', 16),
  (doc_id, 'equation', '{"latex": "\\Delta E_{ot} = \\alpha + \\beta \\cdot AIExposure_o \\times Post_t + \\gamma X_{ot} + \\delta_o + \\lambda_t + \\varepsilon_{ot}", "equationMode": "display", "label": "eq:did"}', 17),
  (doc_id, 'paragraph', '{"text": "where $\\Delta E_{ot}$ is employment change in occupation $o$ at time $t$, $AIExposure_o$ measures occupational exposure to AI automation, $Post_t$ indicates the post-2015 AI acceleration period, and $X_{ot}$ includes control variables."}', 18),
  (doc_id, 'heading', '{"text": "4. Results", "level": 2}', 19),
  (doc_id, 'heading', '{"text": "4.1 Employment Effects", "level": 3}', 20),
  (doc_id, 'paragraph', '{"text": "Table 2 presents our main findings on employment effects by occupation category:"}', 21),
  (doc_id, 'table', '{"headers": ["Occupation Category", "AI Exposure", "Employment Δ", "Wage Δ", "New Jobs Created"], "rows": [["Data Entry/Processing", "High", "-18.3%", "-4.2%", "+2.1%"], ["Customer Service", "High", "-12.7%", "-2.8%", "+8.4%"], ["Financial Analysis", "Medium", "-4.2%", "+6.3%", "+15.2%"], ["Software Development", "Medium", "+23.5%", "+18.7%", "+45.3%"], ["Healthcare", "Low", "+8.2%", "+5.1%", "+12.8%"], ["Creative/Design", "Low", "+4.8%", "+7.2%", "+22.1%"]]}', 22),
  (doc_id, 'paragraph', '{"text": "Our estimates indicate heterogeneous effects across occupations. High-exposure routine cognitive occupations experienced significant employment declines, while complementary occupations saw growth."}', 23),
  (doc_id, 'figure', '{"src": "/api/placeholder/600/350", "caption": "Figure 1: Employment changes by AI exposure quintile, 2015-2023. Error bars represent 95% confidence intervals.", "alt": "Employment changes by AI exposure"}', 24),
  (doc_id, 'pagebreak', '{}', 25),
  (doc_id, 'heading', '{"text": "4.2 Wage Distribution Effects", "level": 3}', 26),
  (doc_id, 'paragraph', '{"text": "AI adoption has contributed to wage polarization, with differential effects across the income distribution:"}', 27),
  (doc_id, 'table', '{"headers": ["Wage Percentile", "2010-2015 Growth", "2015-2023 Growth", "AI Contribution"], "rows": [["10th", "+3.2%", "+1.8%", "-0.4%"], ["25th", "+5.1%", "+2.9%", "-0.8%"], ["50th", "+6.8%", "+4.2%", "-1.2%"], ["75th", "+8.4%", "+9.1%", "+2.4%"], ["90th", "+11.2%", "+15.8%", "+5.2%"], ["99th", "+18.7%", "+28.4%", "+8.9%"]]}', 28),
  (doc_id, 'heading', '{"text": "5. Discussion", "level": 2}', 29),
  (doc_id, 'paragraph', '{"text": "Our findings support the hypothesis that AI represents a continuation and intensification of skill-biased technological change \\cite{autor2020}. Key implications include:"}', 30),
  (doc_id, 'list', '{"items": ["Workforce transition programs need significant expansion", "Education systems must emphasize AI-complementary skills", "Social safety nets require adaptation for more frequent job transitions", "Geographic concentration of AI jobs exacerbates regional inequality"], "ordered": true}', 31),
  (doc_id, 'pagebreak', '{}', 32),
  (doc_id, 'heading', '{"text": "6. Conclusion", "level": 2}', 33),
  (doc_id, 'paragraph', '{"text": "This thesis has documented significant but heterogeneous labor market effects of AI adoption. While aggregate employment impacts have been modest thus far, distributional consequences are substantial. Policy responses should focus on facilitating worker transitions and ensuring the gains from AI-driven productivity are broadly shared."}', 34),
  (doc_id, 'pagebreak', '{}', 35),
  (doc_id, 'heading', '{"text": "References", "level": 2}', 36),
  (doc_id, 'bibliography', '{}', 37);

  INSERT INTO bibliography_entries (document_id, cite_key, entry_type, data) VALUES
  (doc_id, 'brynjolfsson2014', 'book', '{"author": "Brynjolfsson, Erik and McAfee, Andrew", "title": "The Second Machine Age: Work, Progress, and Prosperity in a Time of Brilliant Technologies", "year": "2014", "publisher": "W. W. Norton"}'),
  (doc_id, 'acemoglu2020', 'article', '{"author": "Acemoglu, Daron and Restrepo, Pascual", "title": "Robots and Jobs: Evidence from US Labor Markets", "journal": "Journal of Political Economy", "year": "2020", "volume": "128", "pages": "2188-2244"}'),
  (doc_id, 'autor2003', 'article', '{"author": "Autor, David H. and Levy, Frank and Murnane, Richard J.", "title": "The Skill Content of Recent Technological Change", "journal": "Quarterly Journal of Economics", "year": "2003", "volume": "118", "pages": "1279-1333"}'),
  (doc_id, 'frey2017', 'article', '{"author": "Frey, Carl Benedikt and Osborne, Michael A.", "title": "The Future of Employment: How Susceptible Are Jobs to Computerisation?", "journal": "Technological Forecasting and Social Change", "year": "2017", "volume": "114", "pages": "254-280"}'),
  (doc_id, 'autor2020', 'article', '{"author": "Autor, David H.", "title": "The Work of the Future: Building Better Jobs in an Age of Intelligent Machines", "journal": "MIT Work of the Future", "year": "2020"}');
END $$;

-- ============================================================================
-- Document 12: Linguistic Analysis of Social Media
-- Features: tables, quotes, citations, qualitative analysis
-- ============================================================================
DO $$
DECLARE
  doc_id UUID;
  user_id VARCHAR(255) := 'sample-content';
BEGIN
  INSERT INTO documents (id, owner_id, title, language, paper_size, font_family, font_size, is_public)
  VALUES (gen_random_uuid(), user_id, 'Linguistic Analysis of Social Media Discourse', 'en', 'a4', 'serif', 12, TRUE)
  RETURNING id INTO doc_id;

  INSERT INTO blocks (document_id, type, content, sort_order) VALUES
  (doc_id, 'heading', '{"text": "Linguistic Analysis of Social Media Discourse", "level": 1}', 0),
  (doc_id, 'abstract', '{"text": "This thesis examines the linguistic characteristics of social media communication, analyzing how digital platforms shape language use, identity construction, and discourse patterns. Through corpus linguistics methods and critical discourse analysis, we investigate the emergence of new linguistic norms in online spaces. *This is a sample document generated to demonstrate Lilia editor capabilities. The content is AI-generated for demonstration purposes only.*"}', 1),
  (doc_id, 'pagebreak', '{}', 2),
  (doc_id, 'heading', '{"text": "1. Introduction", "level": 2}', 3),
  (doc_id, 'paragraph', '{"text": "Social media platforms have created new contexts for language use, fundamentally altering how people communicate \\cite{crystal2011}. The constraints and affordances of platforms like Twitter, Instagram, and TikTok have given rise to distinctive linguistic practices that merit scholarly attention."}', 4),
  (doc_id, 'paragraph', '{"text": "This thesis investigates how users adapt language to digital contexts, focusing on **linguistic innovation**, **identity performance**, and **community formation** through discourse \\cite{androutsopoulos2006}."}', 5),
  (doc_id, 'heading', '{"text": "2. Theoretical Framework", "level": 2}', 6),
  (doc_id, 'paragraph', '{"text": "Our analysis draws on multiple theoretical traditions:"}', 7),
  (doc_id, 'list', '{"items": ["Computer-mediated communication (CMC) theory", "Sociolinguistics and language variation", "Critical discourse analysis (CDA)", "Multimodal discourse analysis"], "ordered": false}', 8),
  (doc_id, 'paragraph', '{"text": "Following Herring''s \\cite{herring2013} faceted classification scheme, we analyze social media discourse across technological, situational, and participatory dimensions."}', 9),
  (doc_id, 'pagebreak', '{}', 10),
  (doc_id, 'heading', '{"text": "3. Methodology", "level": 2}', 11),
  (doc_id, 'heading', '{"text": "3.1 Corpus Construction", "level": 3}', 12),
  (doc_id, 'paragraph', '{"text": "We compiled a corpus of social media posts across multiple platforms:"}', 13),
  (doc_id, 'table', '{"headers": ["Platform", "Posts", "Words", "Users", "Time Period"], "rows": [["Twitter/X", "2.4M", "48.2M", "125,000", "2020-2023"], ["Instagram", "850K", "12.4M", "45,000", "2021-2023"], ["Reddit", "1.2M", "89.5M", "78,000", "2019-2023"], ["TikTok (captions)", "320K", "4.8M", "28,000", "2022-2023"]]}', 14),
  (doc_id, 'heading', '{"text": "3.2 Analytical Methods", "level": 3}', 15),
  (doc_id, 'paragraph', '{"text": "We employed mixed methods combining quantitative corpus analysis with qualitative close reading:"}', 16),
  (doc_id, 'list', '{"items": ["Frequency analysis and keyword extraction", "Collocation and concordance analysis", "Sentiment and stance detection", "Thematic coding of discourse strategies"], "ordered": true}', 17),
  (doc_id, 'heading', '{"text": "4. Findings", "level": 2}', 18),
  (doc_id, 'heading', '{"text": "4.1 Lexical Innovation", "level": 3}', 19),
  (doc_id, 'paragraph', '{"text": "Social media has accelerated lexical change. Table 2 shows emerging terms and their frequency patterns:"}', 20),
  (doc_id, 'table', '{"headers": ["Term", "Type", "First Attested", "2023 Frequency", "Meaning"], "rows": [["slay", "Semantic shift", "2016", "12.4 per 10K", "Excel, succeed impressively"], ["unalive", "Euphemism", "2020", "3.2 per 10K", "Die/kill (censorship avoidance)"], ["ate", "Semantic extension", "2018", "8.7 per 10K", "Performed excellently"], ["understood the assignment", "Phrasal", "2020", "2.1 per 10K", "Met expectations perfectly"], ["main character", "Metaphorical", "2019", "5.4 per 10K", "Central, important person"]]}', 21),
  (doc_id, 'heading', '{"text": "4.2 Platform-Specific Registers", "level": 3}', 22),
  (doc_id, 'paragraph', '{"text": "Different platforms foster distinct linguistic registers. Twitter encourages brevity and wit; Instagram favors aspirational and aesthetic language; Reddit supports extended argumentation."}', 23),
  (doc_id, 'paragraph', '{"text": "Consider this example of platform-adapted discourse:"}', 24),
  (doc_id, 'paragraph', '{"text": "*Twitter*: \"hot take: pineapple on pizza is actually good and you''re all just cowards 🍍🍕 fight me\""}', 25),
  (doc_id, 'paragraph', '{"text": "*Reddit*: \"I know this is controversial, but I genuinely enjoy pineapple on pizza. The sweetness contrasts nicely with the savory cheese and tomato sauce. Here''s my reasoning: [detailed argument follows]\""}', 26),
  (doc_id, 'pagebreak', '{}', 27),
  (doc_id, 'heading', '{"text": "4.3 Identity and Stance", "level": 3}', 28),
  (doc_id, 'paragraph', '{"text": "Users construct identity through linguistic choices. We identified several key strategies:"}', 29),
  (doc_id, 'table', '{"headers": ["Strategy", "Example", "Function"], "rows": [["Code-switching", "\"That outfit is giving cottagecore vibes fr fr\"", "In-group solidarity"], ["Ironic distancing", "\"It''s giving... confused\"", "Hedged evaluation"], ["Authenticity markers", "\"No but actually though\"", "Sincerity claim"], ["Metacommentary", "\"I can''t believe I''m saying this but\"", "Stance modulation"]]}', 30),
  (doc_id, 'heading', '{"text": "5. Discussion", "level": 2}', 31),
  (doc_id, 'paragraph', '{"text": "Our findings reveal that social media language is not degraded or simplified, as popular discourse sometimes suggests, but rather represents creative adaptation to new communicative contexts \\cite{tagliamonte2016}. Users demonstrate sophisticated metalinguistic awareness and deploy varied registers strategically."}', 32),
  (doc_id, 'paragraph', '{"text": "The rapid spread of linguistic innovations through social networks challenges traditional models of language change, suggesting the need for updated theoretical frameworks \\cite{eisenstein2014}."}', 33),
  (doc_id, 'pagebreak', '{}', 34),
  (doc_id, 'heading', '{"text": "6. Conclusion", "level": 2}', 35),
  (doc_id, 'paragraph', '{"text": "This thesis has documented the distinctive linguistic features of social media discourse, demonstrating that digital platforms serve as sites of significant linguistic innovation and identity work. Future research should continue tracking these rapidly evolving practices and their potential influence on broader language change."}', 36),
  (doc_id, 'pagebreak', '{}', 37),
  (doc_id, 'heading', '{"text": "References", "level": 2}', 38),
  (doc_id, 'bibliography', '{}', 39);

  INSERT INTO bibliography_entries (document_id, cite_key, entry_type, data) VALUES
  (doc_id, 'crystal2011', 'book', '{"author": "Crystal, David", "title": "Internet Linguistics: A Student Guide", "year": "2011", "publisher": "Routledge"}'),
  (doc_id, 'androutsopoulos2006', 'article', '{"author": "Androutsopoulos, Jannis", "title": "Introduction: Sociolinguistics and Computer-Mediated Communication", "journal": "Journal of Sociolinguistics", "year": "2006", "volume": "10", "pages": "419-438"}'),
  (doc_id, 'herring2013', 'incollection', '{"author": "Herring, Susan C.", "title": "Discourse in Web 2.0: Familiar, Reconfigured, and Emergent", "booktitle": "Discourse 2.0: Language and New Media", "year": "2013", "publisher": "Georgetown University Press"}'),
  (doc_id, 'tagliamonte2016', 'article', '{"author": "Tagliamonte, Sali A.", "title": "Teen Talk: The Language of Adolescents", "journal": "Language in Society", "year": "2016", "volume": "45", "pages": "605-607"}'),
  (doc_id, 'eisenstein2014', 'inproceedings', '{"author": "Eisenstein, Jacob and others", "title": "Diffusion of Lexical Change in Social Media", "booktitle": "PLoS ONE", "year": "2014", "volume": "9"}');
END $$;

-- ============================================================================
-- Document 13: Psychology of Decision Making
-- Features: citations, figures, research methodology
-- ============================================================================
DO $$
DECLARE
  doc_id UUID;
  user_id VARCHAR(255) := 'sample-content';
BEGIN
  INSERT INTO documents (id, owner_id, title, language, paper_size, font_family, font_size, is_public)
  VALUES (gen_random_uuid(), user_id, 'Psychology of Decision Making Under Uncertainty', 'en', 'a4', 'serif', 12, TRUE)
  RETURNING id INTO doc_id;

  INSERT INTO blocks (document_id, type, content, sort_order) VALUES
  (doc_id, 'heading', '{"text": "Psychology of Decision Making Under Uncertainty", "level": 1}', 0),
  (doc_id, 'abstract', '{"text": "This thesis investigates cognitive processes underlying human decision making when outcomes are uncertain. Through experimental studies, we examine the roles of heuristics, biases, and emotional factors in shaping choices, contributing to both theoretical understanding and practical applications in behavioral economics. *This is a sample document generated to demonstrate Lilia editor capabilities. The content is AI-generated for demonstration purposes only.*"}', 1),
  (doc_id, 'pagebreak', '{}', 2),
  (doc_id, 'heading', '{"text": "1. Introduction", "level": 2}', 3),
  (doc_id, 'paragraph', '{"text": "Human decision making rarely conforms to the rational agent model of classical economics \\cite{kahneman2011}. When facing uncertainty, people rely on mental shortcuts that can lead to systematic deviations from optimal choices. Understanding these patterns has implications for fields ranging from medicine to finance."}', 4),
  (doc_id, 'paragraph', '{"text": "This thesis presents three experimental studies examining how people make decisions under risk and ambiguity, with particular attention to the interaction between cognitive and affective processes \\cite{loewenstein2001}."}', 5),
  (doc_id, 'heading', '{"text": "2. Theoretical Background", "level": 2}', 6),
  (doc_id, 'heading', '{"text": "2.1 Prospect Theory", "level": 3}', 7),
  (doc_id, 'paragraph', '{"text": "Kahneman and Tversky''s \\cite{tversky1979} prospect theory revolutionized understanding of risky choice. Key features include:"}', 8),
  (doc_id, 'list', '{"items": ["**Reference dependence**: Outcomes evaluated relative to a reference point", "**Loss aversion**: Losses loom larger than equivalent gains (λ ≈ 2.25)", "**Diminishing sensitivity**: Marginal impact decreases with distance from reference", "**Probability weighting**: Overweighting of small probabilities, underweighting of large"], "ordered": false}', 9),
  (doc_id, 'heading', '{"text": "2.2 Dual-Process Theory", "level": 3}', 10),
  (doc_id, 'paragraph', '{"text": "Contemporary models distinguish between automatic (System 1) and deliberative (System 2) processes \\cite{evans2008}. Decisions under uncertainty often reflect a competition between these systems."}', 11),
  (doc_id, 'pagebreak', '{}', 12),
  (doc_id, 'heading', '{"text": "3. Study 1: Framing Effects", "level": 2}', 13),
  (doc_id, 'heading', '{"text": "3.1 Method", "level": 3}', 14),
  (doc_id, 'paragraph', '{"text": "Participants (N = 248) completed a medical decision task with logically equivalent options presented in gain vs. loss frames."}', 15),
  (doc_id, 'table', '{"headers": ["Measure", "Value"], "rows": [["Sample Size", "248"], ["Age (Mean ± SD)", "34.2 ± 12.8"], ["Gender", "52% female"], ["Education", "68% college degree"]]}', 16),
  (doc_id, 'heading', '{"text": "3.2 Results", "level": 3}', 17),
  (doc_id, 'paragraph', '{"text": "Strong framing effects emerged, replicating classic findings:"}', 18),
  (doc_id, 'table', '{"headers": ["Frame", "Risk-Seeking Choice", "χ²", "p-value"], "rows": [["Gain (\"200 saved\")", "28%", "-", "-"], ["Loss (\"400 die\")", "71%", "46.2", "< .001"]]}', 19),
  (doc_id, 'figure', '{"src": "/api/placeholder/550/350", "caption": "Figure 1: Risk preference by frame condition. Error bars indicate 95% CI.", "alt": "Framing effects bar chart"}', 20),
  (doc_id, 'heading', '{"text": "4. Study 2: Emotion and Risk", "level": 2}', 21),
  (doc_id, 'paragraph', '{"text": "Study 2 examined how incidental emotions affect risk taking. Participants (N = 312) were randomly assigned to anger, fear, or neutral mood inductions before completing investment decisions."}', 22),
  (doc_id, 'table', '{"headers": ["Emotion", "Risk Allocation", "Confidence", "Decision Time"], "rows": [["Neutral", "48.2%", "5.4", "12.3s"], ["Anger", "61.7%", "6.2", "9.8s"], ["Fear", "34.1%", "4.1", "15.6s"]]}', 23),
  (doc_id, 'paragraph', '{"text": "ANOVA revealed significant main effects of emotion on risk allocation, F(2, 309) = 18.42, p < .001, η²p = .11. Post-hoc comparisons confirmed that angry participants took significantly more risk than fearful participants (p < .001)."}', 24),
  (doc_id, 'pagebreak', '{}', 25),
  (doc_id, 'heading', '{"text": "5. Study 3: Debiasing Interventions", "level": 2}', 26),
  (doc_id, 'paragraph', '{"text": "Study 3 tested whether cognitive reflection training could reduce susceptibility to decision biases. We developed a brief intervention and evaluated its effectiveness across multiple bias paradigms."}', 27),
  (doc_id, 'table', '{"headers": ["Bias", "Control Group", "Intervention", "Reduction"], "rows": [["Framing", "42% susceptible", "28% susceptible", "33%"], ["Anchoring", "Mean error: 34%", "Mean error: 22%", "35%"], ["Sunk Cost", "61% fallacy", "44% fallacy", "28%"], ["Overconfidence", "82% overconfident", "71% overconfident", "13%"]]}', 28),
  (doc_id, 'heading', '{"text": "6. General Discussion", "level": 2}', 29),
  (doc_id, 'paragraph', '{"text": "Across three studies, we demonstrate that decision making under uncertainty is shaped by contextual factors (framing), affective states (emotion), and cognitive capacity (training). These findings have practical implications:"}', 30),
  (doc_id, 'list', '{"items": ["Medical communication should avoid unnecessary loss framing", "Financial decisions should not be made in heightened emotional states", "Education in critical thinking can partially mitigate cognitive biases", "Choice architecture can leverage these insights for better outcomes"], "ordered": true}', 31),
  (doc_id, 'pagebreak', '{}', 32),
  (doc_id, 'heading', '{"text": "7. Conclusion", "level": 2}', 33),
  (doc_id, 'paragraph', '{"text": "This thesis contributes to understanding the psychological mechanisms underlying human choice under uncertainty. Our findings underscore that rationality is bounded by cognitive architecture and situational factors, with important implications for policy and practice."}', 34),
  (doc_id, 'pagebreak', '{}', 35),
  (doc_id, 'heading', '{"text": "References", "level": 2}', 36),
  (doc_id, 'bibliography', '{}', 37);

  INSERT INTO bibliography_entries (document_id, cite_key, entry_type, data) VALUES
  (doc_id, 'kahneman2011', 'book', '{"author": "Kahneman, Daniel", "title": "Thinking, Fast and Slow", "year": "2011", "publisher": "Farrar, Straus and Giroux"}'),
  (doc_id, 'loewenstein2001', 'article', '{"author": "Loewenstein, George F. and others", "title": "Risk as Feelings", "journal": "Psychological Bulletin", "year": "2001", "volume": "127", "pages": "267-286"}'),
  (doc_id, 'tversky1979', 'article', '{"author": "Tversky, Amos and Kahneman, Daniel", "title": "Prospect Theory: An Analysis of Decision under Risk", "journal": "Econometrica", "year": "1979", "volume": "47", "pages": "263-291"}'),
  (doc_id, 'evans2008', 'article', '{"author": "Evans, Jonathan St B. T.", "title": "Dual-Processing Accounts of Reasoning, Judgment, and Social Cognition", "journal": "Annual Review of Psychology", "year": "2008", "volume": "59", "pages": "255-278"}'),
  (doc_id, 'gigerenzer2011', 'article', '{"author": "Gigerenzer, Gerd and Gaissmaier, Wolfgang", "title": "Heuristic Decision Making", "journal": "Annual Review of Psychology", "year": "2011", "volume": "62", "pages": "451-482"}');
END $$;

-- ============================================================================
-- Document 14: Historical Analysis of Pandemics
-- Features: citations, timeline tables, historical analysis
-- ============================================================================
DO $$
DECLARE
  doc_id UUID;
  user_id VARCHAR(255) := 'sample-content';
BEGIN
  INSERT INTO documents (id, owner_id, title, language, paper_size, font_family, font_size, is_public)
  VALUES (gen_random_uuid(), user_id, 'Historical Analysis of Pandemics: Lessons for the Present', 'en', 'a4', 'serif', 12, TRUE)
  RETURNING id INTO doc_id;

  INSERT INTO blocks (document_id, type, content, sort_order) VALUES
  (doc_id, 'heading', '{"text": "Historical Analysis of Pandemics: Lessons for the Present", "level": 1}', 0),
  (doc_id, 'abstract', '{"text": "This thesis examines major pandemics throughout history, analyzing their causes, spread patterns, societal impacts, and policy responses. By comparing historical cases with the COVID-19 pandemic, we identify recurring patterns and derive lessons for future public health preparedness. *This is a sample document generated to demonstrate Lilia editor capabilities. The content is AI-generated for demonstration purposes only.*"}', 1),
  (doc_id, 'pagebreak', '{}', 2),
  (doc_id, 'heading', '{"text": "1. Introduction", "level": 2}', 3),
  (doc_id, 'paragraph', '{"text": "Infectious disease outbreaks have shaped human history, toppling empires, transforming societies, and redirecting the course of civilization \\cite{mcneill1976}. Understanding this history is essential for navigating present and future health crises."}', 4),
  (doc_id, 'paragraph', '{"text": "This thesis adopts a comparative historical approach, examining pandemics from the Antonine Plague to COVID-19. We focus on the interplay between disease biology, social conditions, and institutional responses \\cite{snowden2019}."}', 5),
  (doc_id, 'heading', '{"text": "2. Major Historical Pandemics", "level": 2}', 6),
  (doc_id, 'paragraph', '{"text": "Table 1 provides an overview of major pandemic events in human history:"}', 7),
  (doc_id, 'table', '{"headers": ["Pandemic", "Date", "Pathogen", "Deaths (Est.)", "Population %"], "rows": [["Antonine Plague", "165-180 CE", "Likely smallpox", "5-10 million", "3-6%"], ["Plague of Justinian", "541-542 CE", "Yersinia pestis", "25-50 million", "13-26%"], ["Black Death", "1347-1351", "Yersinia pestis", "75-200 million", "30-60%"], ["Smallpox (Americas)", "1520-1600", "Variola major", "56 million", "90% indigenous"], ["1918 Influenza", "1918-1920", "H1N1 virus", "50-100 million", "3-5%"], ["HIV/AIDS", "1981-present", "HIV", "40+ million", "Ongoing"], ["COVID-19", "2019-present", "SARS-CoV-2", "7+ million", "0.09%"]]}', 8),
  (doc_id, 'pagebreak', '{}', 9),
  (doc_id, 'heading', '{"text": "3. The Black Death", "level": 2}', 10),
  (doc_id, 'heading', '{"text": "3.1 Origins and Spread", "level": 3}', 11),
  (doc_id, 'paragraph', '{"text": "The Black Death originated in Central Asia and spread westward along trade routes, reaching Europe in 1347 via Genoese trading ships \\cite{benedictow2004}. The plague bacterium *Yersinia pestis* was transmitted primarily through flea bites, with pneumonic transmission enabling rapid person-to-person spread."}', 12),
  (doc_id, 'heading', '{"text": "3.2 Social and Economic Impact", "level": 3}', 13),
  (doc_id, 'paragraph', '{"text": "The demographic catastrophe transformed European society:"}', 14),
  (doc_id, 'list', '{"items": ["Labor shortages led to increased peasant bargaining power", "Wage increases of 50-100% documented in many regions", "Accelerated decline of feudal institutions", "Persecution of minority groups (Jews, lepers, foreigners)", "Religious upheaval and flagellant movements"], "ordered": false}', 15),
  (doc_id, 'heading', '{"text": "4. The 1918 Influenza Pandemic", "level": 2}', 16),
  (doc_id, 'paragraph', '{"text": "The 1918 pandemic emerged during World War I, spreading rapidly through military camps and civilian populations \\cite{barry2004}. Its distinctive \"W-shaped\" mortality curve, with peak deaths among young adults, remains incompletely understood."}', 17),
  (doc_id, 'heading', '{"text": "4.1 Public Health Responses", "level": 3}', 18),
  (doc_id, 'paragraph', '{"text": "Responses varied dramatically across cities, providing natural experiments for studying intervention effectiveness:"}', 19),
  (doc_id, 'table', '{"headers": ["City", "Response Timing", "Measures", "Peak Death Rate"], "rows": [["Philadelphia", "Late, minimal", "Parade allowed", "257 per 100K"], ["St. Louis", "Early, aggressive", "Closures, masks", "31 per 100K"], ["San Francisco", "Early, then relaxed", "Mandatory masks", "67 per 100K"], ["Pittsburgh", "Moderate", "Partial closures", "89 per 100K"]]}', 20),
  (doc_id, 'paragraph', '{"text": "Cities implementing early, sustained non-pharmaceutical interventions experienced significantly lower mortality \\cite{hatchett2007}."}', 21),
  (doc_id, 'pagebreak', '{}', 22),
  (doc_id, 'heading', '{"text": "5. COVID-19 in Historical Perspective", "level": 2}', 23),
  (doc_id, 'paragraph', '{"text": "The COVID-19 pandemic shares features with historical precedents while also presenting novel characteristics:"}', 24),
  (doc_id, 'table', '{"headers": ["Feature", "Historical Pattern", "COVID-19"], "rows": [["Emergence", "Zoonotic spillover", "Likely bat origin"], ["Spread", "Trade/travel routes", "Global air travel"], ["Containment", "Quarantine, isolation", "Lockdowns, testing"], ["Treatment", "Limited options", "Vaccines in 1 year"], ["Information", "Slow, unreliable", "Rapid, misinformation"]]}', 25),
  (doc_id, 'heading', '{"text": "6. Lessons Learned", "level": 2}', 26),
  (doc_id, 'paragraph', '{"text": "Comparative analysis reveals recurring patterns with implications for policy:"}', 27),
  (doc_id, 'list', '{"items": ["Early intervention is crucial—delayed responses consistently result in higher mortality", "Transparent communication builds public trust and compliance", "Economic disruption accompanies all major pandemics regardless of policy", "Social solidarity can emerge but so can scapegoating and division", "Preparedness degrades rapidly after threat perception diminishes"], "ordered": true}', 28),
  (doc_id, 'pagebreak', '{}', 29),
  (doc_id, 'heading', '{"text": "7. Conclusion", "level": 2}', 30),
  (doc_id, 'paragraph', '{"text": "History does not repeat, but it rhymes. This thesis has demonstrated that pandemic patterns recur across centuries, shaped by both biological constants and social variables. As climate change and habitat destruction increase zoonotic spillover risk, these historical lessons become ever more urgent."}', 31),
  (doc_id, 'pagebreak', '{}', 32),
  (doc_id, 'heading', '{"text": "References", "level": 2}', 33),
  (doc_id, 'bibliography', '{}', 34);

  INSERT INTO bibliography_entries (document_id, cite_key, entry_type, data) VALUES
  (doc_id, 'mcneill1976', 'book', '{"author": "McNeill, William H.", "title": "Plagues and Peoples", "year": "1976", "publisher": "Anchor Press"}'),
  (doc_id, 'snowden2019', 'book', '{"author": "Snowden, Frank M.", "title": "Epidemics and Society: From the Black Death to the Present", "year": "2019", "publisher": "Yale University Press"}'),
  (doc_id, 'benedictow2004', 'book', '{"author": "Benedictow, Ole J.", "title": "The Black Death, 1346-1353: The Complete History", "year": "2004", "publisher": "Boydell Press"}'),
  (doc_id, 'barry2004', 'book', '{"author": "Barry, John M.", "title": "The Great Influenza: The Story of the Deadliest Pandemic in History", "year": "2004", "publisher": "Penguin Books"}'),
  (doc_id, 'hatchett2007', 'article', '{"author": "Hatchett, Richard J. and others", "title": "Public Health Interventions and Epidemic Intensity During the 1918 Influenza Pandemic", "journal": "PNAS", "year": "2007", "volume": "104", "pages": "7582-7587"}');
END $$;

-- ============================================================================
-- Document 15: Urban Planning and Sustainability
-- Features: figures, tables, case studies
-- ============================================================================
DO $$
DECLARE
  doc_id UUID;
  user_id VARCHAR(255) := 'sample-content';
BEGIN
  INSERT INTO documents (id, owner_id, title, language, paper_size, font_family, font_size, is_public)
  VALUES (gen_random_uuid(), user_id, 'Urban Planning and Environmental Sustainability', 'en', 'a4', 'serif', 12, TRUE)
  RETURNING id INTO doc_id;

  INSERT INTO blocks (document_id, type, content, sort_order) VALUES
  (doc_id, 'heading', '{"text": "Urban Planning and Environmental Sustainability", "level": 1}', 0),
  (doc_id, 'abstract', '{"text": "This thesis examines sustainable urban planning strategies, analyzing how city design influences environmental outcomes. Through case studies of leading sustainable cities, we identify best practices in land use, transportation, and green infrastructure that reduce urban carbon footprints while improving quality of life. *This is a sample document generated to demonstrate Lilia editor capabilities. The content is AI-generated for demonstration purposes only.*"}', 1),
  (doc_id, 'pagebreak', '{}', 2),
  (doc_id, 'heading', '{"text": "1. Introduction", "level": 2}', 3),
  (doc_id, 'paragraph', '{"text": "Cities are responsible for over 70% of global carbon emissions, yet they also represent our greatest opportunity for sustainable transformation \\cite{ipcc2022}. Urban planning decisions made today will determine environmental outcomes for generations."}', 4),
  (doc_id, 'paragraph', '{"text": "This thesis investigates how urban form and policy interventions affect sustainability metrics, drawing lessons from cities that have achieved significant environmental improvements \\cite{newman2015}."}', 5),
  (doc_id, 'heading', '{"text": "2. Theoretical Framework", "level": 2}', 6),
  (doc_id, 'paragraph', '{"text": "Sustainable urban development integrates three pillars:"}', 7),
  (doc_id, 'list', '{"items": ["**Environmental**: Carbon reduction, biodiversity, resource efficiency", "**Social**: Equity, accessibility, health, community", "**Economic**: Productivity, innovation, green jobs"], "ordered": false}', 8),
  (doc_id, 'paragraph', '{"text": "We adopt Raworth''s \\cite{raworth2017} \"doughnut economics\" framework, seeking development within planetary boundaries while meeting social foundations."}', 9),
  (doc_id, 'pagebreak', '{}', 10),
  (doc_id, 'heading', '{"text": "3. Case Studies", "level": 2}', 11),
  (doc_id, 'heading', '{"text": "3.1 Copenhagen, Denmark", "level": 3}', 12),
  (doc_id, 'paragraph', '{"text": "Copenhagen aims to become the world''s first carbon-neutral capital by 2025. Key strategies include:"}', 13),
  (doc_id, 'table', '{"headers": ["Sector", "Strategy", "2023 Achievement"], "rows": [["Transport", "Cycling infrastructure", "62% bike commute share"], ["Energy", "District heating", "98% renewable heat"], ["Buildings", "Retrofit program", "35% energy reduction"], ["Waste", "Waste-to-energy", "2% to landfill"], ["Green Space", "Pocket parks", "+18% green area"]]}', 14),
  (doc_id, 'figure', '{"src": "/api/placeholder/600/350", "caption": "Figure 1: Copenhagen''s cycling infrastructure network showing protected bike lanes (green) and bike highways (blue).", "alt": "Copenhagen cycling map"}', 15),
  (doc_id, 'heading', '{"text": "3.2 Singapore", "level": 3}', 16),
  (doc_id, 'paragraph', '{"text": "Despite high density, Singapore has achieved significant sustainability gains through integrated planning:"}', 17),
  (doc_id, 'list', '{"items": ["Vertical greenery requirements on new buildings", "Comprehensive public transit network (85% of trips)", "Water recycling and desalination (40% self-sufficiency)", "Urban heat island mitigation through strategic planting"], "ordered": false}', 18),
  (doc_id, 'heading', '{"text": "3.3 Medellín, Colombia", "level": 3}', 19),
  (doc_id, 'paragraph', '{"text": "Medellín demonstrates that sustainability planning can address social equity. The city''s \"social urbanism\" approach connects informal settlements to opportunity:"}', 20),
  (doc_id, 'table', '{"headers": ["Initiative", "Year", "Impact"], "rows": [["MetroCable", "2004", "Reduced commute 85%, crime -79%"], ["Biblioteca España", "2007", "Cultural access to marginalized areas"], ["Green corridors", "2016", "-2°C local temperature"], ["Escalators", "2011", "Mobility for hillside communities"]]}', 21),
  (doc_id, 'pagebreak', '{}', 22),
  (doc_id, 'heading', '{"text": "4. Comparative Analysis", "level": 2}', 23),
  (doc_id, 'paragraph', '{"text": "Table 3 compares sustainability metrics across case study cities:"}', 24),
  (doc_id, 'table', '{"headers": ["City", "CO₂/capita", "Transit Share", "Green Space/capita", "Air Quality"], "rows": [["Copenhagen", "2.1 tons", "41%", "89 m²", "Good"], ["Singapore", "4.8 tons", "66%", "66 m²", "Moderate"], ["Medellín", "1.4 tons", "58%", "3.5 m²", "Improving"], ["Global Average", "4.7 tons", "~25%", "~20 m²", "Variable"]]}', 25),
  (doc_id, 'figure', '{"src": "/api/placeholder/600/300", "caption": "Figure 2: Relationship between urban density and per-capita emissions across world cities. Case study cities highlighted.", "alt": "Density vs emissions scatter plot"}', 26),
  (doc_id, 'heading', '{"text": "5. Policy Recommendations", "level": 2}', 27),
  (doc_id, 'paragraph', '{"text": "Based on our analysis, we recommend the following policy priorities:"}', 28),
  (doc_id, 'list', '{"items": ["Prioritize transit-oriented development over car-centric sprawl", "Mandate green building standards for all new construction", "Invest in cycling and pedestrian infrastructure", "Integrate equity considerations into all sustainability planning", "Establish binding carbon budgets with regular reporting"], "ordered": true}', 29),
  (doc_id, 'pagebreak', '{}', 30),
  (doc_id, 'heading', '{"text": "6. Conclusion", "level": 2}', 31),
  (doc_id, 'paragraph', '{"text": "Sustainable urban planning is possible at various scales and income levels, as demonstrated by our diverse case studies. Success requires political will, integrated planning, and sustained investment. The examples analyzed here provide roadmaps for cities worldwide seeking to reduce their environmental impact while improving livability."}', 32),
  (doc_id, 'pagebreak', '{}', 33),
  (doc_id, 'heading', '{"text": "References", "level": 2}', 34),
  (doc_id, 'bibliography', '{}', 35);

  INSERT INTO bibliography_entries (document_id, cite_key, entry_type, data) VALUES
  (doc_id, 'ipcc2022', 'book', '{"author": "IPCC", "title": "Climate Change 2022: Mitigation of Climate Change", "year": "2022", "publisher": "Cambridge University Press"}'),
  (doc_id, 'newman2015', 'book', '{"author": "Newman, Peter and Kenworthy, Jeffrey", "title": "The End of Automobile Dependence", "year": "2015", "publisher": "Island Press"}'),
  (doc_id, 'raworth2017', 'book', '{"author": "Raworth, Kate", "title": "Doughnut Economics: Seven Ways to Think Like a 21st-Century Economist", "year": "2017", "publisher": "Chelsea Green Publishing"}'),
  (doc_id, 'gehl2010', 'book', '{"author": "Gehl, Jan", "title": "Cities for People", "year": "2010", "publisher": "Island Press"}'),
  (doc_id, 'un2016', 'book', '{"author": "UN-Habitat", "title": "New Urban Agenda", "year": "2016", "publisher": "United Nations"}');
END $$;

-- ============================================================================
-- Document 16: Educational Technology Effectiveness
-- Features: data tables, citations, methodology
-- ============================================================================
DO $$
DECLARE
  doc_id UUID;
  user_id VARCHAR(255) := 'sample-content';
BEGIN
  INSERT INTO documents (id, owner_id, title, language, paper_size, font_family, font_size, is_public)
  VALUES (gen_random_uuid(), user_id, 'Educational Technology Effectiveness: A Meta-Analysis', 'en', 'a4', 'serif', 12, TRUE)
  RETURNING id INTO doc_id;

  INSERT INTO blocks (document_id, type, content, sort_order) VALUES
  (doc_id, 'heading', '{"text": "Educational Technology Effectiveness: A Meta-Analysis", "level": 1}', 0),
  (doc_id, 'abstract', '{"text": "This thesis presents a meta-analysis of educational technology interventions, synthesizing findings from 287 randomized controlled trials to estimate average effect sizes and identify moderating factors. We examine technology-enhanced learning across subjects, age groups, and implementation contexts. *This is a sample document generated to demonstrate Lilia editor capabilities. The content is AI-generated for demonstration purposes only.*"}', 1),
  (doc_id, 'pagebreak', '{}', 2),
  (doc_id, 'heading', '{"text": "1. Introduction", "level": 2}', 3),
  (doc_id, 'paragraph', '{"text": "Educational technology (EdTech) has been heralded as transformative for learning, yet evidence on its effectiveness remains mixed \\cite{escueta2020}. As schools invest billions in technology, rigorous evaluation of impact is essential."}', 4),
  (doc_id, 'paragraph', '{"text": "This meta-analysis synthesizes the experimental literature on EdTech interventions, providing pooled effect estimates and exploring heterogeneity across contexts \\cite{cheung2013}."}', 5),
  (doc_id, 'heading', '{"text": "2. Methodology", "level": 2}', 6),
  (doc_id, 'heading', '{"text": "2.1 Search Strategy", "level": 3}', 7),
  (doc_id, 'paragraph', '{"text": "We conducted systematic searches across six databases using the following criteria:"}', 8),
  (doc_id, 'list', '{"items": ["Randomized or quasi-experimental design", "Technology-based intervention in K-12 or higher education", "Cognitive learning outcome measures", "Published 2010-2023", "Available effect size or calculable from statistics"], "ordered": false}', 9),
  (doc_id, 'heading', '{"text": "2.2 Study Characteristics", "level": 3}', 10),
  (doc_id, 'paragraph', '{"text": "Our final sample includes 287 studies meeting inclusion criteria:"}', 11),
  (doc_id, 'table', '{"headers": ["Characteristic", "N", "%"], "rows": [["RCT Design", "198", "69%"], ["Quasi-Experimental", "89", "31%"], ["K-12 Setting", "201", "70%"], ["Higher Education", "86", "30%"], ["STEM Subject", "156", "54%"], ["Humanities/Social", "131", "46%"]]}', 12),
  (doc_id, 'pagebreak', '{}', 13),
  (doc_id, 'heading', '{"text": "3. Results", "level": 2}', 14),
  (doc_id, 'heading', '{"text": "3.1 Overall Effect Size", "level": 3}', 15),
  (doc_id, 'paragraph', '{"text": "The pooled effect size across all studies was g = 0.35 (95% CI: 0.28-0.42), a small-to-medium effect \\cite{cohen1988}. Significant heterogeneity existed between studies (I² = 78%)."}', 16),
  (doc_id, 'figure', '{"src": "/api/placeholder/600/400", "caption": "Figure 1: Forest plot of effect sizes by technology type. Diamond represents pooled estimate.", "alt": "Forest plot of EdTech effects"}', 17),
  (doc_id, 'heading', '{"text": "3.2 Moderator Analysis", "level": 3}', 18),
  (doc_id, 'paragraph', '{"text": "Table 2 presents effect sizes by key moderating variables:"}', 18),
  (doc_id, 'table', '{"headers": ["Moderator", "Category", "g", "95% CI", "k"], "rows": [["Technology Type", "Intelligent Tutoring", "0.54", "0.41-0.67", "48"], ["", "Educational Games", "0.38", "0.25-0.51", "62"], ["", "Video/Multimedia", "0.28", "0.18-0.38", "84"], ["", "General Software", "0.22", "0.12-0.32", "93"], ["Subject", "Mathematics", "0.42", "0.32-0.52", "98"], ["", "Science", "0.38", "0.26-0.50", "58"], ["", "Reading/Writing", "0.29", "0.19-0.39", "76"], ["", "Social Studies", "0.24", "0.10-0.38", "55"], ["Grade Level", "Elementary", "0.41", "0.30-0.52", "112"], ["", "Middle School", "0.34", "0.23-0.45", "89"], ["", "High School", "0.28", "0.17-0.39", "86"]]}', 19),
  (doc_id, 'heading', '{"text": "3.3 Implementation Factors", "level": 3}', 20),
  (doc_id, 'paragraph', '{"text": "Teacher training and implementation fidelity emerged as significant moderators:"}', 21),
  (doc_id, 'table', '{"headers": ["Factor", "Low", "High", "Difference"], "rows": [["Teacher Training", "g = 0.21", "g = 0.48", "+0.27***"], ["Implementation Fidelity", "g = 0.18", "g = 0.45", "+0.27***"], ["Student-Teacher Ratio", "g = 0.44 (low)", "g = 0.26 (high)", "-0.18**"], ["Supplemental vs. Replace", "g = 0.39", "g = 0.28", "-0.11*"]]}', 22),
  (doc_id, 'pagebreak', '{}', 23),
  (doc_id, 'heading', '{"text": "4. Discussion", "level": 2}', 24),
  (doc_id, 'paragraph', '{"text": "Our findings suggest that educational technology can be effective, but effect sizes are modest and highly context-dependent. Key insights include:"}', 25),
  (doc_id, 'list', '{"items": ["Intelligent tutoring systems show the largest effects, particularly in mathematics", "Technology works best as a supplement to, not replacement for, teacher instruction", "Implementation quality matters more than technology type", "Effects are larger for younger students", "Publication bias likely inflates estimates; trim-and-fill suggests true effect around g = 0.28"], "ordered": true}', 26),
  (doc_id, 'heading', '{"text": "5. Conclusion", "level": 2}', 27),
  (doc_id, 'paragraph', '{"text": "Educational technology shows promise but is not a silver bullet. Our meta-analysis finds meaningful but modest effects that depend critically on how technology is implemented. Investment in teacher professional development and implementation support may yield greater returns than hardware and software alone."}', 28),
  (doc_id, 'pagebreak', '{}', 29),
  (doc_id, 'heading', '{"text": "References", "level": 2}', 30),
  (doc_id, 'bibliography', '{}', 31);

  INSERT INTO bibliography_entries (document_id, cite_key, entry_type, data) VALUES
  (doc_id, 'escueta2020', 'article', '{"author": "Escueta, Maya and others", "title": "Upgrading Education with Technology: Insights from Experimental Research", "journal": "Journal of Economic Literature", "year": "2020", "volume": "58", "pages": "897-996"}'),
  (doc_id, 'cheung2013', 'article', '{"author": "Cheung, Alan C. K. and Slavin, Robert E.", "title": "The Effectiveness of Educational Technology Applications for Enhancing Mathematics Achievement in K-12 Classrooms", "journal": "Educational Research Review", "year": "2013", "volume": "9", "pages": "88-113"}'),
  (doc_id, 'cohen1988', 'book', '{"author": "Cohen, Jacob", "title": "Statistical Power Analysis for the Behavioral Sciences", "year": "1988", "publisher": "Lawrence Erlbaum Associates", "edition": "2nd"}'),
  (doc_id, 'steenbergen2016', 'article', '{"author": "Steenbergen-Hu, Saiying and Cooper, Harris", "title": "A Meta-Analysis of the Effectiveness of Intelligent Tutoring Systems on College Students'' Academic Learning", "journal": "Journal of Educational Psychology", "year": "2014", "volume": "106", "pages": "331-347"}'),
  (doc_id, 'kulik2016', 'article', '{"author": "Kulik, James A. and Fletcher, J. D.", "title": "Effectiveness of Intelligent Tutoring Systems: A Meta-Analytic Review", "journal": "Review of Educational Research", "year": "2016", "volume": "86", "pages": "42-78"}');
END $$;

-- ============================================================================
-- Document 17: Philosophy of Consciousness
-- Features: citations, quotes, theorems/propositions
-- ============================================================================
DO $$
DECLARE
  doc_id UUID;
  user_id VARCHAR(255) := 'sample-content';
BEGIN
  INSERT INTO documents (id, owner_id, title, language, paper_size, font_family, font_size, is_public)
  VALUES (gen_random_uuid(), user_id, 'The Philosophy of Consciousness: Mind, Matter, and Meaning', 'en', 'a4', 'serif', 12, TRUE)
  RETURNING id INTO doc_id;

  INSERT INTO blocks (document_id, type, content, sort_order) VALUES
  (doc_id, 'heading', '{"text": "The Philosophy of Consciousness: Mind, Matter, and Meaning", "level": 1}', 0),
  (doc_id, 'abstract', '{"text": "This thesis examines central problems in the philosophy of consciousness, including the hard problem, the explanatory gap, and the possibility of machine consciousness. Through analysis of competing theories—physicalism, dualism, and panpsychism—we develop a novel framework for understanding the place of mind in nature. *This is a sample document generated to demonstrate Lilia editor capabilities. The content is AI-generated for demonstration purposes only.*"}', 1),
  (doc_id, 'pagebreak', '{}', 2),
  (doc_id, 'heading', '{"text": "1. Introduction", "level": 2}', 3),
  (doc_id, 'paragraph', '{"text": "Consciousness presents what Chalmers \\cite{chalmers1996} calls \"the hard problem\"—explaining why physical processes give rise to subjective experience at all. This question has occupied philosophers since antiquity and has gained renewed urgency as artificial intelligence advances."}', 4),
  (doc_id, 'paragraph', '{"text": "This thesis surveys major theories of consciousness and argues for a nuanced position that takes seriously both the reality of subjective experience and its relationship to physical processes."}', 5),
  (doc_id, 'heading', '{"text": "2. The Problem of Consciousness", "level": 2}', 6),
  (doc_id, 'heading', '{"text": "2.1 Defining Consciousness", "level": 3}', 7),
  (doc_id, 'theorem', '{"theoremType": "definition", "title": "Phenomenal Consciousness", "text": "**Phenomenal consciousness** refers to the subjective, qualitative character of experience—what it is *like* to be in a mental state. This includes sensory experiences (seeing red, feeling pain), emotions, and thoughts.", "label": "def:phenomenal"}', 8),
  (doc_id, 'theorem', '{"theoremType": "definition", "title": "Access Consciousness", "text": "**Access consciousness** refers to information being available for reasoning, reporting, and guiding behavior. A state is access-conscious when its content can be used by cognitive processes.", "label": "def:access"}', 9),
  (doc_id, 'paragraph', '{"text": "The hard problem concerns phenomenal consciousness specifically. As Nagel \\cite{nagel1974} famously asked: What is it like to be a bat? This question points to an irreducibly subjective dimension of consciousness."}', 10),
  (doc_id, 'heading', '{"text": "2.2 The Explanatory Gap", "level": 3}', 11),
  (doc_id, 'paragraph', '{"text": "Even complete knowledge of neural correlates leaves unexplained why those processes are accompanied by experience \\cite{levine1983}. Consider the argument:"}', 12),
  (doc_id, 'list', '{"items": ["P1: Complete physical knowledge of the brain is possible in principle", "P2: Such knowledge would not include knowledge of what experiences feel like", "C: Therefore, there is an explanatory gap between physical and phenomenal facts"], "ordered": true}', 13),
  (doc_id, 'pagebreak', '{}', 14),
  (doc_id, 'heading', '{"text": "3. Theories of Consciousness", "level": 2}', 15),
  (doc_id, 'heading', '{"text": "3.1 Physicalism", "level": 3}', 16),
  (doc_id, 'paragraph', '{"text": "Physicalists maintain that consciousness is entirely physical. Strong versions identify mental states with brain states; weaker versions claim only supervenience. Dennett \\cite{dennett1991} argues the hard problem dissolves under proper analysis."}', 17),
  (doc_id, 'theorem', '{"theoremType": "proposition", "title": "Identity Theory", "text": "For every type of mental state M, there exists a type of physical state P such that M = P. Consciousness is nothing over and above certain brain processes.", "label": "prop:identity"}', 18),
  (doc_id, 'paragraph', '{"text": "*Objection*: Jackson''s \\cite{jackson1982} Mary thought experiment suggests physical knowledge is incomplete. Mary, a color scientist raised in a black-and-white room, seems to learn something new when she first sees red."}', 19),
  (doc_id, 'heading', '{"text": "3.2 Dualism", "level": 3}', 20),
  (doc_id, 'paragraph', '{"text": "Dualists posit that consciousness involves non-physical properties or substances. Property dualism accepts physical substances while denying that mental properties reduce to physical properties."}', 21),
  (doc_id, 'theorem', '{"theoremType": "proposition", "title": "Property Dualism", "text": "There exist irreducibly mental properties (qualia) that supervene on but are not identical to physical properties. Zombies—physical duplicates lacking consciousness—are conceivable.", "label": "prop:dualism"}', 22),
  (doc_id, 'paragraph', '{"text": "*Objection*: Conceivability may not entail possibility. Moreover, causal interaction between non-physical and physical properties remains mysterious."}', 23),
  (doc_id, 'heading', '{"text": "3.3 Panpsychism", "level": 3}', 24),
  (doc_id, 'paragraph', '{"text": "Panpsychists propose that consciousness is fundamental and ubiquitous \\cite{goff2017}. On this view, even fundamental particles have proto-conscious properties that combine into full consciousness in complex systems."}', 25),
  (doc_id, 'theorem', '{"theoremType": "proposition", "title": "Panpsychism", "text": "Phenomenal properties are fundamental features of reality. Complex consciousness emerges from the combination of simpler experiential properties present at all levels of physical organization.", "label": "prop:panpsychism"}', 26),
  (doc_id, 'paragraph', '{"text": "*Objection*: The combination problem—how micro-experiences combine into unified macro-consciousness—is as difficult as the hard problem it aims to solve."}', 27),
  (doc_id, 'pagebreak', '{}', 28),
  (doc_id, 'heading', '{"text": "4. Machine Consciousness", "level": 2}', 29),
  (doc_id, 'paragraph', '{"text": "Recent AI advances raise the question: Could machines be conscious? The answer depends on which theory is correct:"}', 30),
  (doc_id, 'table', '{"headers": ["Theory", "Machine Consciousness?", "Conditions"], "rows": [["Functionalism", "Yes, in principle", "Functional organization"], ["Biological Naturalism", "No", "Requires biological substrate"], ["Integrated Information", "Possibly", "High Φ value"], ["Panpsychism", "Yes, trivially", "All matter is proto-conscious"]]}', 31),
  (doc_id, 'heading', '{"text": "5. Conclusion", "level": 2}', 32),
  (doc_id, 'paragraph', '{"text": "The problem of consciousness remains unsolved, but progress has been made in clarifying the issues. We have argued that any adequate theory must respect both the reality of subjective experience and its deep connection to physical processes. The emergence of AI systems that behave intelligently adds practical urgency to these ancient questions."}', 33),
  (doc_id, 'pagebreak', '{}', 34),
  (doc_id, 'heading', '{"text": "References", "level": 2}', 35),
  (doc_id, 'bibliography', '{}', 36);

  INSERT INTO bibliography_entries (document_id, cite_key, entry_type, data) VALUES
  (doc_id, 'chalmers1996', 'book', '{"author": "Chalmers, David J.", "title": "The Conscious Mind: In Search of a Fundamental Theory", "year": "1996", "publisher": "Oxford University Press"}'),
  (doc_id, 'nagel1974', 'article', '{"author": "Nagel, Thomas", "title": "What Is It Like to Be a Bat?", "journal": "Philosophical Review", "year": "1974", "volume": "83", "pages": "435-450"}'),
  (doc_id, 'levine1983', 'article', '{"author": "Levine, Joseph", "title": "Materialism and Qualia: The Explanatory Gap", "journal": "Pacific Philosophical Quarterly", "year": "1983", "volume": "64", "pages": "354-361"}'),
  (doc_id, 'dennett1991', 'book', '{"author": "Dennett, Daniel C.", "title": "Consciousness Explained", "year": "1991", "publisher": "Little, Brown and Company"}'),
  (doc_id, 'jackson1982', 'article', '{"author": "Jackson, Frank", "title": "Epiphenomenal Qualia", "journal": "Philosophical Quarterly", "year": "1982", "volume": "32", "pages": "127-136"}'),
  (doc_id, 'goff2017', 'book', '{"author": "Goff, Philip", "title": "Consciousness and Fundamental Reality", "year": "2017", "publisher": "Oxford University Press"}');
END $$;

-- ============================================================================
-- Document 18: Sociology of Remote Work
-- Features: statistics tables, citations, survey methodology
-- ============================================================================
DO $$
DECLARE
  doc_id UUID;
  user_id VARCHAR(255) := 'sample-content';
BEGIN
  INSERT INTO documents (id, owner_id, title, language, paper_size, font_family, font_size, is_public)
  VALUES (gen_random_uuid(), user_id, 'The Sociology of Remote Work: Organization, Identity, and Inequality', 'en', 'a4', 'serif', 12, TRUE)
  RETURNING id INTO doc_id;

  INSERT INTO blocks (document_id, type, content, sort_order) VALUES
  (doc_id, 'heading', '{"text": "The Sociology of Remote Work: Organization, Identity, and Inequality", "level": 1}', 0),
  (doc_id, 'abstract', '{"text": "This thesis examines the social implications of remote work, analyzing how distributed work arrangements reshape organizational structures, professional identities, and social inequalities. Through survey research and in-depth interviews, we explore the experiences of remote workers during and after the COVID-19 pandemic. *This is a sample document generated to demonstrate Lilia editor capabilities. The content is AI-generated for demonstration purposes only.*"}', 1),
  (doc_id, 'pagebreak', '{}', 2),
  (doc_id, 'heading', '{"text": "1. Introduction", "level": 2}', 3),
  (doc_id, 'paragraph', '{"text": "The COVID-19 pandemic triggered the largest natural experiment in remote work history, with millions of workers suddenly shifted to home-based arrangements \\cite{brynjolfsson2020}. While emergency conditions have receded, remote and hybrid work persist at far higher levels than pre-pandemic norms."}', 4),
  (doc_id, 'paragraph', '{"text": "This thesis investigates the sociological dimensions of this transformation, examining how remote work restructures power relations, professional identities, and social stratification \\cite{kossek2016}."}', 5),
  (doc_id, 'heading', '{"text": "2. Literature Review", "level": 2}', 6),
  (doc_id, 'paragraph', '{"text": "Sociological research on work has long emphasized the workplace as a site of identity formation, social interaction, and power negotiation \\cite{hochschild1983}. Remote work challenges these dynamics by:"}', 7),
  (doc_id, 'list', '{"items": ["Dissolving boundaries between work and home spheres", "Reducing informal social interaction and relationship building", "Making worker performance more visible through digital monitoring", "Enabling geographic arbitrage but exacerbating digital divides"], "ordered": false}', 8),
  (doc_id, 'pagebreak', '{}', 9),
  (doc_id, 'heading', '{"text": "3. Methodology", "level": 2}', 10),
  (doc_id, 'heading', '{"text": "3.1 Survey Design", "level": 3}', 11),
  (doc_id, 'paragraph', '{"text": "We administered an online survey to a nationally representative sample (N = 2,847) of US knowledge workers:"}', 12),
  (doc_id, 'table', '{"headers": ["Demographic", "Sample", "Population"], "rows": [["Female", "48%", "47%"], ["Age 25-44", "52%", "51%"], ["College Degree+", "89%", "85%"], ["White", "62%", "63%"], ["Fully Remote", "34%", "-"], ["Hybrid", "48%", "-"], ["Fully On-site", "18%", "-"]]}', 13),
  (doc_id, 'heading', '{"text": "3.2 Qualitative Interviews", "level": 3}', 14),
  (doc_id, 'paragraph', '{"text": "We conducted semi-structured interviews with 45 remote workers across industries, using thematic analysis to identify patterns."}', 15),
  (doc_id, 'heading', '{"text": "4. Findings", "level": 2}', 16),
  (doc_id, 'heading', '{"text": "4.1 Work-Life Boundaries", "level": 3}', 17),
  (doc_id, 'paragraph', '{"text": "Survey results reveal persistent boundary challenges:"}', 18),
  (doc_id, 'table', '{"headers": ["Statement", "Agree/Strongly Agree"], "rows": [["I work longer hours when remote", "58%"], ["I struggle to disconnect from work", "64%"], ["I have more flexibility for personal needs", "72%"], ["My work-life balance has improved", "41%"], ["I feel pressure to be always available", "53%"]]}', 19),
  (doc_id, 'paragraph', '{"text": "Interview data reveals gendered patterns. As one participant noted:"}', 20),
  (doc_id, 'paragraph', '{"text": "*\"When I''m home, everyone assumes I''m available. My husband can close his door, but if I do that, I''m a bad mother. So I''m simultaneously working and supervising homework and starting dinner.\"* (P23, female, 38)"}', 21),
  (doc_id, 'heading', '{"text": "4.2 Professional Identity", "level": 3}', 22),
  (doc_id, 'paragraph', '{"text": "Remote work disrupts traditional markers of professional identity:"}', 23),
  (doc_id, 'table', '{"headers": ["Identity Dimension", "Impact of Remote Work"], "rows": [["Physical appearance", "Diminished importance (\"Zoom professional\")"], ["Office presence", "Replaced by digital visibility metrics"], ["Relationship building", "Requires more intentional effort"], ["Career advancement", "Perceived disadvantage for remote workers"], ["Organizational belonging", "Weakened for newer employees"]]}', 24),
  (doc_id, 'pagebreak', '{}', 25),
  (doc_id, 'heading', '{"text": "4.3 Inequality Patterns", "level": 3}', 26),
  (doc_id, 'paragraph', '{"text": "Remote work capability is strongly stratified by occupation, education, and race:"}', 27),
  (doc_id, 'table', '{"headers": ["Group", "Remote-Capable", "Actually Remote", "Preference Gap"], "rows": [["Management/Professional", "78%", "52%", "-26%"], ["Sales/Office", "45%", "28%", "-17%"], ["Service Occupations", "12%", "4%", "-8%"], ["White Workers", "58%", "42%", "-16%"], ["Black Workers", "39%", "24%", "-15%"], ["Hispanic Workers", "34%", "21%", "-13%"]]}', 28),
  (doc_id, 'paragraph', '{"text": "The \"remote work premium\" benefits already-advantaged groups, potentially widening labor market inequalities \\cite{mongey2020}."}', 29),
  (doc_id, 'heading', '{"text": "5. Discussion", "level": 2}', 30),
  (doc_id, 'paragraph', '{"text": "Our findings suggest that remote work transforms rather than eliminates workplace dynamics. Power, status, and inequality persist in new forms mediated by digital technology. Organizations must intentionally design remote work policies that address emerging challenges:"}', 31),
  (doc_id, 'list', '{"items": ["Combat proximity bias in promotion decisions", "Establish clear norms for availability and boundaries", "Invest in virtual community building", "Ensure equitable access to remote work opportunities"], "ordered": true}', 32),
  (doc_id, 'pagebreak', '{}', 33),
  (doc_id, 'heading', '{"text": "6. Conclusion", "level": 2}', 34),
  (doc_id, 'paragraph', '{"text": "Remote work represents a fundamental restructuring of the employment relationship with profound sociological implications. This thesis has documented both the benefits and challenges of distributed work, highlighting how existing inequalities are reproduced in new contexts. Future research should track these dynamics as hybrid models stabilize."}', 35),
  (doc_id, 'pagebreak', '{}', 36),
  (doc_id, 'heading', '{"text": "References", "level": 2}', 37),
  (doc_id, 'bibliography', '{}', 38);

  INSERT INTO bibliography_entries (document_id, cite_key, entry_type, data) VALUES
  (doc_id, 'brynjolfsson2020', 'article', '{"author": "Brynjolfsson, Erik and others", "title": "COVID-19 and Remote Work: An Early Look at US Data", "journal": "NBER Working Paper", "year": "2020"}'),
  (doc_id, 'kossek2016', 'article', '{"author": "Kossek, Ellen Ernst and Thompson, Rebecca J.", "title": "Workplace Flexibility: Integrating Employer and Employee Perspectives to Close the Research-Practice Implementation Gap", "journal": "Oxford Handbook of Work and Family", "year": "2016"}'),
  (doc_id, 'hochschild1983', 'book', '{"author": "Hochschild, Arlie Russell", "title": "The Managed Heart: Commercialization of Human Feeling", "year": "1983", "publisher": "University of California Press"}'),
  (doc_id, 'mongey2020', 'article', '{"author": "Mongey, Simon and others", "title": "Which Workers Bear the Burden of Social Distancing Policies?", "journal": "Journal of Economic Inequality", "year": "2020", "volume": "18", "pages": "509-526"}'),
  (doc_id, 'choudhury2021', 'article', '{"author": "Choudhury, Prithwiraj and others", "title": "Work-from-Anywhere: The Productivity Effects of Geographic Flexibility", "journal": "Strategic Management Journal", "year": "2021", "volume": "42", "pages": "655-683"}');
END $$;

-- ============================================================================
-- Document 19: Political Science: Voting Systems
-- Features: equations, tables, comparative analysis
-- ============================================================================
DO $$
DECLARE
  doc_id UUID;
  user_id VARCHAR(255) := 'sample-content';
BEGIN
  INSERT INTO documents (id, owner_id, title, language, paper_size, font_family, font_size, is_public)
  VALUES (gen_random_uuid(), user_id, 'Voting Systems and Democratic Representation', 'en', 'a4', 'serif', 12, TRUE)
  RETURNING id INTO doc_id;

  INSERT INTO blocks (document_id, type, content, sort_order) VALUES
  (doc_id, 'heading', '{"text": "Voting Systems and Democratic Representation", "level": 1}', 0),
  (doc_id, 'abstract', '{"text": "This thesis analyzes the mathematical and political properties of electoral systems, comparing how different voting rules translate citizen preferences into representation. Using social choice theory and empirical analysis, we evaluate systems against normative criteria including proportionality, accountability, and fairness. *This is a sample document generated to demonstrate Lilia editor capabilities. The content is AI-generated for demonstration purposes only.*"}', 1),
  (doc_id, 'pagebreak', '{}', 2),
  (doc_id, 'heading', '{"text": "1. Introduction", "level": 2}', 3),
  (doc_id, 'paragraph', '{"text": "The choice of electoral system is among the most consequential institutional decisions in democratic design \\cite{lijphart1994}. Different voting rules produce systematically different outcomes from identical voter preferences, affecting party systems, representation of minorities, and government stability."}', 4),
  (doc_id, 'paragraph', '{"text": "This thesis applies social choice theory to evaluate electoral systems, identifying trade-offs between desirable properties and analyzing real-world outcomes \\cite{arrow1951}."}', 5),
  (doc_id, 'heading', '{"text": "2. Social Choice Framework", "level": 2}', 6),
  (doc_id, 'heading', '{"text": "2.1 Preference Aggregation", "level": 3}', 7),
  (doc_id, 'paragraph', '{"text": "An electoral system aggregates individual preferences into a collective choice. Formally, for $n$ voters and $m$ candidates, a voting rule is a function:"}', 8),
  (doc_id, 'equation', '{"latex": "f: \\mathcal{L}^n \\rightarrow W", "equationMode": "display", "label": "eq:voting-rule"}', 9),
  (doc_id, 'paragraph', '{"text": "where $\\mathcal{L}$ is the set of linear orders over candidates and $W$ is the set of possible winners."}', 10),
  (doc_id, 'heading', '{"text": "2.2 Arrow''s Impossibility Theorem", "level": 3}', 11),
  (doc_id, 'paragraph', '{"text": "Arrow \\cite{arrow1951} proved that no voting system can simultaneously satisfy all of the following reasonable criteria:"}', 12),
  (doc_id, 'list', '{"items": ["**Unrestricted Domain**: All preference orderings are admissible", "**Non-Dictatorship**: No single voter determines the outcome", "**Pareto Efficiency**: If all prefer A to B, society prefers A to B", "**Independence of Irrelevant Alternatives**: Adding candidate C doesn''t change relative ranking of A and B"], "ordered": false}', 13),
  (doc_id, 'equation', '{"latex": "\\nexists f \\text{ s.t. } f \\text{ satisfies UD} \\land \\text{ND} \\land \\text{PE} \\land \\text{IIA}", "equationMode": "display", "label": "eq:arrow"}', 14),
  (doc_id, 'pagebreak', '{}', 15),
  (doc_id, 'heading', '{"text": "3. Electoral System Types", "level": 2}', 16),
  (doc_id, 'heading', '{"text": "3.1 Plurality Systems", "level": 3}', 17),
  (doc_id, 'paragraph', '{"text": "First-past-the-post (FPTP) awards seats to candidates with the most votes, regardless of majority:"}', 18),
  (doc_id, 'equation', '{"latex": "w = \\arg\\max_c |\\{i : c \\succ_i c'' \\, \\forall c'' \\neq c\\}|", "equationMode": "display", "label": "eq:fptp"}', 19),
  (doc_id, 'paragraph', '{"text": "FPTP tends toward two-party systems (Duverger''s Law) and produces disproportional outcomes but clear accountability."}', 20),
  (doc_id, 'heading', '{"text": "3.2 Proportional Systems", "level": 3}', 21),
  (doc_id, 'paragraph', '{"text": "Proportional representation (PR) aims to allocate seats in proportion to vote shares. The D''Hondt method divides votes by successive integers:"}', 22),
  (doc_id, 'equation', '{"latex": "\\text{quot}_{p,s} = \\frac{V_p}{s+1}, \\quad s = 0, 1, 2, \\ldots", "equationMode": "display", "label": "eq:dhondt"}', 23),
  (doc_id, 'paragraph', '{"text": "Seats are allocated to parties with the highest quotients until all seats are filled."}', 24),
  (doc_id, 'heading', '{"text": "4. Comparative Analysis", "level": 2}', 25),
  (doc_id, 'paragraph', '{"text": "Table 1 compares electoral systems across key dimensions:"}', 26),
  (doc_id, 'table', '{"headers": ["System", "Proportionality", "Accountability", "Simplicity", "Stability"], "rows": [["FPTP", "Low", "High", "High", "High"], ["Two-Round", "Low", "High", "Medium", "High"], ["AV/IRV", "Low", "High", "Low", "Medium"], ["Party List PR", "High", "Low", "Medium", "Low"], ["MMP", "High", "Medium", "Low", "Medium"], ["STV", "High", "Medium", "Low", "Medium"]]}', 27),
  (doc_id, 'heading', '{"text": "4.1 Disproportionality Measurement", "level": 3}', 28),
  (doc_id, 'paragraph', '{"text": "The Gallagher index measures disproportionality between vote and seat shares:"}', 29),
  (doc_id, 'equation', '{"latex": "G = \\sqrt{\\frac{1}{2}\\sum_{i=1}^{n}(v_i - s_i)^2}", "equationMode": "display", "label": "eq:gallagher"}', 30),
  (doc_id, 'table', '{"headers": ["Country", "System", "Gallagher Index"], "rows": [["Netherlands", "List PR", "1.2"], ["Germany", "MMP", "2.5"], ["Ireland", "STV", "3.8"], ["Australia (House)", "AV", "8.1"], ["United Kingdom", "FPTP", "15.2"], ["United States", "FPTP", "17.8"]]}', 31),
  (doc_id, 'pagebreak', '{}', 32),
  (doc_id, 'heading', '{"text": "5. Discussion", "level": 2}', 33),
  (doc_id, 'paragraph', '{"text": "No electoral system is universally optimal. The choice involves trade-offs between:"}', 34),
  (doc_id, 'list', '{"items": ["Proportionality vs. governability", "Party discipline vs. individual accountability", "Simplicity vs. sophisticated preference expression", "Stability vs. responsiveness to new movements"], "ordered": false}', 35),
  (doc_id, 'heading', '{"text": "6. Conclusion", "level": 2}', 36),
  (doc_id, 'paragraph', '{"text": "Electoral system design involves fundamental trade-offs that cannot be resolved by mathematical analysis alone. This thesis has provided tools for systematic comparison, but the ultimate choice depends on which democratic values a society prioritizes. Arrow''s theorem reminds us that no system is perfect—the goal is to find acceptable compromises."}', 37),
  (doc_id, 'pagebreak', '{}', 38),
  (doc_id, 'heading', '{"text": "References", "level": 2}', 39),
  (doc_id, 'bibliography', '{}', 40);

  INSERT INTO bibliography_entries (document_id, cite_key, entry_type, data) VALUES
  (doc_id, 'lijphart1994', 'book', '{"author": "Lijphart, Arend", "title": "Electoral Systems and Party Systems: A Study of Twenty-Seven Democracies", "year": "1994", "publisher": "Oxford University Press"}'),
  (doc_id, 'arrow1951', 'book', '{"author": "Arrow, Kenneth J.", "title": "Social Choice and Individual Values", "year": "1951", "publisher": "John Wiley & Sons"}'),
  (doc_id, 'duverger1954', 'book', '{"author": "Duverger, Maurice", "title": "Political Parties: Their Organization and Activity in the Modern State", "year": "1954", "publisher": "Wiley"}'),
  (doc_id, 'gallagher1991', 'article', '{"author": "Gallagher, Michael", "title": "Proportionality, Disproportionality and Electoral Systems", "journal": "Electoral Studies", "year": "1991", "volume": "10", "pages": "33-51"}'),
  (doc_id, 'norris2004', 'book', '{"author": "Norris, Pippa", "title": "Electoral Engineering: Voting Rules and Political Behavior", "year": "2004", "publisher": "Cambridge University Press"}');
END $$;

-- ============================================================================
-- Document 20: Archaeological Dating Methods
-- Features: equations, figures, tables, scientific methods
-- ============================================================================
DO $$
DECLARE
  doc_id UUID;
  user_id VARCHAR(255) := 'sample-content';
BEGIN
  INSERT INTO documents (id, owner_id, title, language, paper_size, font_family, font_size, is_public)
  VALUES (gen_random_uuid(), user_id, 'Archaeological Dating Methods: Principles and Applications', 'en', 'a4', 'serif', 12, TRUE)
  RETURNING id INTO doc_id;

  INSERT INTO blocks (document_id, type, content, sort_order) VALUES
  (doc_id, 'heading', '{"text": "Archaeological Dating Methods: Principles and Applications", "level": 1}', 0),
  (doc_id, 'abstract', '{"text": "This thesis provides a comprehensive survey of dating methods in archaeology, from radiocarbon dating to emerging techniques. We examine the physical principles underlying each method, evaluate accuracy and precision, and demonstrate applications through case studies. *This is a sample document generated to demonstrate Lilia editor capabilities. The content is AI-generated for demonstration purposes only.*"}', 1),
  (doc_id, 'pagebreak', '{}', 2),
  (doc_id, 'heading', '{"text": "1. Introduction", "level": 2}', 3),
  (doc_id, 'paragraph', '{"text": "Establishing chronology is fundamental to archaeological interpretation \\cite{renfrew2016}. Dating methods range from relative techniques that establish sequence to absolute methods providing calendar ages. The choice of method depends on material type, age range, and required precision."}', 4),
  (doc_id, 'paragraph', '{"text": "This thesis surveys major dating techniques, emphasizing the physical principles that enable age determination and the practical considerations affecting reliability \\cite{taylor2014}."}', 5),
  (doc_id, 'heading', '{"text": "2. Relative Dating Methods", "level": 2}', 6),
  (doc_id, 'heading', '{"text": "2.1 Stratigraphy", "level": 3}', 7),
  (doc_id, 'paragraph', '{"text": "Stratigraphic analysis establishes relative chronology based on superposition: lower layers are older than upper layers (absent disturbance). While providing no absolute dates, stratigraphy remains essential for establishing depositional sequences."}', 8),
  (doc_id, 'heading', '{"text": "2.2 Typological Seriation", "level": 3}', 9),
  (doc_id, 'paragraph', '{"text": "Artifact styles change over time in patterned ways. Seriation arranges assemblages by similarity, with the assumption that similar assemblages are temporally close \\cite{obrien2005}."}', 10),
  (doc_id, 'pagebreak', '{}', 11),
  (doc_id, 'heading', '{"text": "3. Radiometric Dating", "level": 2}', 12),
  (doc_id, 'heading', '{"text": "3.1 Radiocarbon Dating", "level": 3}', 13),
  (doc_id, 'paragraph', '{"text": "Radiocarbon dating, developed by Libby \\cite{libby1955}, measures the decay of ¹⁴C in organic materials. The decay follows first-order kinetics:"}', 14),
  (doc_id, 'equation', '{"latex": "N(t) = N_0 e^{-\\lambda t}", "equationMode": "display", "label": "eq:decay"}', 15),
  (doc_id, 'paragraph', '{"text": "where $N_0$ is initial ¹⁴C abundance, $\\lambda$ is the decay constant, and $t$ is time. The half-life of ¹⁴C is 5,730 ± 40 years. Age is calculated as:"}', 16),
  (doc_id, 'equation', '{"latex": "t = -\\frac{1}{\\lambda}\\ln\\left(\\frac{N}{N_0}\\right) = -8033 \\ln\\left(\\frac{A}{A_0}\\right)", "equationMode": "display", "label": "eq:age"}', 17),
  (doc_id, 'paragraph', '{"text": "Table 1 summarizes radiocarbon dating characteristics:"}', 18),
  (doc_id, 'table', '{"headers": ["Parameter", "Value"], "rows": [["Effective range", "300 - 50,000 years BP"], ["Typical precision", "± 20-50 years (AMS)"], ["Sample size (AMS)", "1-50 mg"], ["Materials dated", "Organic: wood, bone, charcoal, shell"]]}', 19),
  (doc_id, 'heading', '{"text": "3.2 Calibration", "level": 3}', 20),
  (doc_id, 'paragraph', '{"text": "Raw radiocarbon ages must be calibrated to account for variations in atmospheric ¹⁴C over time. The IntCal20 calibration curve relates radiocarbon age to calendar age through tree-ring dated samples \\cite{reimer2020}."}', 21),
  (doc_id, 'figure', '{"src": "/api/placeholder/600/300", "caption": "Figure 1: IntCal20 calibration curve showing relationship between radiocarbon age (BP) and calendar age (cal BP).", "alt": "Radiocarbon calibration curve"}', 22),
  (doc_id, 'pagebreak', '{}', 23),
  (doc_id, 'heading', '{"text": "3.3 Other Radiometric Methods", "level": 3}', 24),
  (doc_id, 'paragraph', '{"text": "Several other radiometric techniques complement radiocarbon:"}', 25),
  (doc_id, 'table', '{"headers": ["Method", "Isotope System", "Half-life", "Range (years)", "Materials"], "rows": [["K-Ar", "⁴⁰K → ⁴⁰Ar", "1.25 Ga", "100K - billions", "Volcanic rocks"], ["U-Series", "²³⁸U → ²³⁴U → ²³⁰Th", "Various", "10 - 500K", "Carbonates, bone"], ["Luminescence", "Trapped electrons", "N/A", "100 - 500K", "Sediments, ceramics"], ["Cosmogenic", "Various", "Various", "100 - millions", "Surface rocks"]]}', 26),
  (doc_id, 'heading', '{"text": "4. Luminescence Dating", "level": 2}', 27),
  (doc_id, 'paragraph', '{"text": "Luminescence dating measures energy stored in mineral crystals from environmental radiation. The age equation is:"}', 28),
  (doc_id, 'equation', '{"latex": "\\text{Age} = \\frac{\\text{Equivalent Dose (Gy)}}{\\text{Dose Rate (Gy/yr)}}", "equationMode": "display", "label": "eq:luminescence"}', 29),
  (doc_id, 'paragraph', '{"text": "Optically stimulated luminescence (OSL) dates the last exposure to sunlight, making it ideal for sediment dating."}', 30),
  (doc_id, 'heading', '{"text": "5. Case Study: Dating Human Migration", "level": 2}', 31),
  (doc_id, 'paragraph', '{"text": "Multiple dating methods have refined understanding of human dispersal:"}', 32),
  (doc_id, 'table', '{"headers": ["Site", "Location", "Method", "Age", "Significance"], "rows": [["Jebel Irhoud", "Morocco", "U-series, TL", "~315,000 BP", "Earliest H. sapiens"], ["Skhul/Qafzeh", "Israel", "ESR, TL", "100-120,000 BP", "Early Out of Africa"], ["Madjedbebe", "Australia", "OSL", "~65,000 BP", "Early Australian arrival"], ["Monte Verde", "Chile", "¹⁴C", "~14,500 BP", "Early American site"]]}', 33),
  (doc_id, 'pagebreak', '{}', 34),
  (doc_id, 'heading', '{"text": "6. Conclusion", "level": 2}', 35),
  (doc_id, 'paragraph', '{"text": "Archaeological dating has been transformed by advances in measurement technology and calibration. Modern best practice involves multiple independent methods to cross-check results. As analytical precision improves, dating continues to refine our understanding of human prehistory and cultural change."}', 36),
  (doc_id, 'pagebreak', '{}', 37),
  (doc_id, 'heading', '{"text": "References", "level": 2}', 38),
  (doc_id, 'bibliography', '{}', 39);

  INSERT INTO bibliography_entries (document_id, cite_key, entry_type, data) VALUES
  (doc_id, 'renfrew2016', 'book', '{"author": "Renfrew, Colin and Bahn, Paul", "title": "Archaeology: Theories, Methods, and Practice", "year": "2016", "publisher": "Thames & Hudson", "edition": "7th"}'),
  (doc_id, 'taylor2014', 'book', '{"author": "Taylor, R. E. and Bar-Yosef, Ofer", "title": "Radiocarbon Dating: An Archaeological Perspective", "year": "2014", "publisher": "Routledge", "edition": "2nd"}'),
  (doc_id, 'obrien2005', 'book', '{"author": "O''Brien, Michael J. and Lyman, R. Lee", "title": "Seriation, Stratigraphy, and Index Fossils", "year": "2005", "publisher": "Springer"}'),
  (doc_id, 'libby1955', 'book', '{"author": "Libby, Willard F.", "title": "Radiocarbon Dating", "year": "1955", "publisher": "University of Chicago Press"}'),
  (doc_id, 'reimer2020', 'article', '{"author": "Reimer, Paula J. and others", "title": "The IntCal20 Northern Hemisphere Radiocarbon Age Calibration Curve", "journal": "Radiocarbon", "year": "2020", "volume": "62", "pages": "725-757"}');
END $$;

-- ============================================================================
-- Verification query for Batch 2
-- ============================================================================
-- Run this to verify the documents were created:
-- SELECT d.title, COUNT(b.id) as block_count, COUNT(be.id) as citation_count
-- FROM documents d
-- LEFT JOIN blocks b ON d.id = b.document_id
-- LEFT JOIN bibliography_entries be ON d.id = be.document_id
-- WHERE d.owner_id = 'sample-content'
-- GROUP BY d.id, d.title
-- ORDER BY d.created_at;

-- Seed system templates

INSERT INTO templates (id, user_id, name, description, category, content, is_public, is_system, usage_count) VALUES
(
    '10000000-0000-0000-0000-000000000001',
    NULL,
    'Blank Document',
    'Start with a blank document',
    'other',
    '{"language": "en", "paperSize": "a4", "fontFamily": "serif", "fontSize": 12, "blocks": [{"type": "heading", "content": {"level": 1, "text": ""}, "sortOrder": 0}], "bibliography": []}',
    TRUE,
    TRUE,
    0
),
(
    '10000000-0000-0000-0000-000000000002',
    NULL,
    'Academic Paper',
    'Template for academic research papers with abstract, sections, and bibliography',
    'academic',
    '{"language": "en", "paperSize": "a4", "fontFamily": "serif", "fontSize": 12, "blocks": [{"type": "heading", "content": {"level": 1, "text": "Paper Title"}, "sortOrder": 0}, {"type": "abstract", "content": {"text": "Enter your abstract here..."}, "sortOrder": 1}, {"type": "heading", "content": {"level": 2, "text": "1. Introduction"}, "sortOrder": 2}, {"type": "paragraph", "content": {"text": ""}, "sortOrder": 3}, {"type": "heading", "content": {"level": 2, "text": "2. Methods"}, "sortOrder": 4}, {"type": "paragraph", "content": {"text": ""}, "sortOrder": 5}, {"type": "heading", "content": {"level": 2, "text": "3. Results"}, "sortOrder": 6}, {"type": "paragraph", "content": {"text": ""}, "sortOrder": 7}, {"type": "heading", "content": {"level": 2, "text": "4. Discussion"}, "sortOrder": 8}, {"type": "paragraph", "content": {"text": ""}, "sortOrder": 9}, {"type": "heading", "content": {"level": 2, "text": "5. Conclusion"}, "sortOrder": 10}, {"type": "paragraph", "content": {"text": ""}, "sortOrder": 11}, {"type": "heading", "content": {"level": 2, "text": "References"}, "sortOrder": 12}, {"type": "bibliography", "content": {}, "sortOrder": 13}], "bibliography": []}',
    TRUE,
    TRUE,
    0
),
(
    '10000000-0000-0000-0000-000000000003',
    NULL,
    'Physics Homework',
    'Template for physics problem sets with equation support',
    'academic',
    '{"language": "en", "paperSize": "a4", "fontFamily": "serif", "fontSize": 12, "blocks": [{"type": "heading", "content": {"level": 1, "text": "Physics Homework"}, "sortOrder": 0}, {"type": "paragraph", "content": {"text": "Name: ________________  Date: ________________"}, "sortOrder": 1}, {"type": "divider", "content": {}, "sortOrder": 2}, {"type": "heading", "content": {"level": 2, "text": "Problem 1"}, "sortOrder": 3}, {"type": "paragraph", "content": {"text": ""}, "sortOrder": 4}, {"type": "heading", "content": {"level": 3, "text": "Solution"}, "sortOrder": 5}, {"type": "equation", "content": {"latex": "", "numbered": true}, "sortOrder": 6}], "bibliography": []}',
    TRUE,
    TRUE,
    0
),
(
    '10000000-0000-0000-0000-000000000004',
    NULL,
    'Thesis Chapter',
    'Template for thesis or dissertation chapters',
    'thesis',
    '{"language": "en", "paperSize": "a4", "fontFamily": "serif", "fontSize": 12, "blocks": [{"type": "heading", "content": {"level": 1, "text": "Chapter N: Title"}, "sortOrder": 0}, {"type": "paragraph", "content": {"text": ""}, "sortOrder": 1}, {"type": "heading", "content": {"level": 2, "text": "N.1 Introduction"}, "sortOrder": 2}, {"type": "paragraph", "content": {"text": ""}, "sortOrder": 3}, {"type": "heading", "content": {"level": 2, "text": "N.2 Background"}, "sortOrder": 4}, {"type": "paragraph", "content": {"text": ""}, "sortOrder": 5}, {"type": "heading", "content": {"level": 2, "text": "N.3 Main Content"}, "sortOrder": 6}, {"type": "paragraph", "content": {"text": ""}, "sortOrder": 7}, {"type": "heading", "content": {"level": 2, "text": "N.4 Summary"}, "sortOrder": 8}, {"type": "paragraph", "content": {"text": ""}, "sortOrder": 9}], "bibliography": []}',
    TRUE,
    TRUE,
    0
),
(
    '10000000-0000-0000-0000-000000000005',
    NULL,
    'Math Proof',
    'Template for mathematical proofs with theorem environments',
    'academic',
    '{"language": "en", "paperSize": "a4", "fontFamily": "serif", "fontSize": 12, "blocks": [{"type": "heading", "content": {"level": 1, "text": "Theorem Title"}, "sortOrder": 0}, {"type": "theorem", "content": {"type": "theorem", "title": "", "text": "State the theorem here."}, "sortOrder": 1}, {"type": "theorem", "content": {"type": "proof", "title": "", "text": "Write the proof here..."}, "sortOrder": 2}], "bibliography": []}',
    TRUE,
    TRUE,
    0
),
(
    '10000000-0000-0000-0000-000000000006',
    NULL,
    'Lab Report',
    'Template for scientific lab reports',
    'report',
    '{"language": "en", "paperSize": "a4", "fontFamily": "serif", "fontSize": 12, "blocks": [{"type": "heading", "content": {"level": 1, "text": "Lab Report: Experiment Title"}, "sortOrder": 0}, {"type": "paragraph", "content": {"text": "Author: ________________  Date: ________________"}, "sortOrder": 1}, {"type": "heading", "content": {"level": 2, "text": "Objective"}, "sortOrder": 2}, {"type": "paragraph", "content": {"text": ""}, "sortOrder": 3}, {"type": "heading", "content": {"level": 2, "text": "Materials and Methods"}, "sortOrder": 4}, {"type": "paragraph", "content": {"text": ""}, "sortOrder": 5}, {"type": "heading", "content": {"level": 2, "text": "Results"}, "sortOrder": 6}, {"type": "paragraph", "content": {"text": ""}, "sortOrder": 7}, {"type": "heading", "content": {"level": 2, "text": "Analysis"}, "sortOrder": 8}, {"type": "paragraph", "content": {"text": ""}, "sortOrder": 9}, {"type": "heading", "content": {"level": 2, "text": "Conclusion"}, "sortOrder": 10}, {"type": "paragraph", "content": {"text": ""}, "sortOrder": 11}], "bibliography": []}',
    TRUE,
    TRUE,
    0
);

-- =====================================================================
--  E2E scenario catalogue seed — modules, surfaces, block types,
--  block actions, entry points.
--
--  Idempotent: ON CONFLICT on the natural keys. Safe to re-run after
--  the schema changes (additive only).
--
--  Source: lilia-docs/launch-readiness/2026-05-18-e2e-scenario-db.md
--  §13 (block-deep seed strategy) + §6 (surface inventory).
-- =====================================================================

BEGIN;

-- ---------------------------------------------------------------------
--  Modules
-- ---------------------------------------------------------------------

INSERT INTO e2e.module (slug, name, description, criticality) VALUES
  ('studio',         'Studio (block editor)',           'The block-editing canvas + toolbar + side panels.', 'p0'),
  ('dashboard',      'Documents dashboard',             'Doc grid, search, sort, pagination, "new doc" button.', 'p0'),
  ('auth',           'Authentication',                  'Sign-in, sign-up, password reset, magic links, OAuth.', 'p0'),
  ('mobile',         'Mobile editor',                   'StudioMobile — mobile-only views + bottom nav.', 'p0'),
  ('export',         'Export (LaTeX/PDF/HTML/MD)',      'Document export pipeline + dialog.', 'p0'),
  ('import',         'Import (DOCX/LaTeX/Overleaf)',    'Import flow + review session + finalise.', 'p1'),
  ('templates',      'Templates gallery',               'Starter docs + template management.', 'p1'),
  ('bibliography',   'Bibliography',                    'Citation library, DOI lookup, bib export.', 'p1'),
  ('formulas',       'Formulas',                        'Equation editor, formula library.', 'p1'),
  ('settings',       'User settings',                   'Profile, preferences, theme, language.', 'p2'),
  ('share',          'Document sharing',                'Share drawer, public links, collaborator invites.', 'p1'),
  ('admin',          'Admin / internal',                'Internal-only routes (e.g. /internal/scenarios).', 'p2')
ON CONFLICT (slug) DO UPDATE
  SET name = EXCLUDED.name,
      description = EXCLUDED.description,
      criticality = EXCLUDED.criticality,
      updated_at = NOW();

-- ---------------------------------------------------------------------
--  Block types — canonical list from CLAUDE.md (C# API is authoritative)
-- ---------------------------------------------------------------------

INSERT INTO e2e.block_type (slug, name, category, latex_role, description) VALUES
  ('paragraph',       'Paragraph',         'text',      'paragraph',         'Default text block. Rendered as a plain paragraph in LaTeX.'),
  ('heading',         'Heading',           'structure', 'section',           'Section / subsection / subsubsection by level.'),
  ('equation',        'Equation',          'math',      'equation',          'Display-mode LaTeX equation block, KaTeX-rendered preview.'),
  ('figure',          'Figure',            'media',     'figure',            'Image with optional caption + label.'),
  ('code',            'Code',              'code',      'verbatim',          'Monospace code block. Lstlisting / minted in LaTeX export.'),
  ('list',            'List',              'text',      'itemize',           'Ordered or unordered list.'),
  ('blockquote',      'Block quote',       'text',      'quote',             'Indented quotation. Legacy alias: "quote".'),
  ('table',           'Table',             'structure', 'tabular',           'Tabular data. Supports headers + alignment.'),
  ('theorem',         'Theorem',           'math',      'theorem',           'Math environment (theorem, lemma, definition, etc.).'),
  ('abstract',        'Abstract',          'structure', 'abstract',          'Document abstract section.'),
  ('bibliography',    'Bibliography',      'reference', 'thebibliography',   'Auto-generated from BibliographyEntry rows.'),
  ('tableOfContents', 'Table of contents', 'structure', 'tableofcontents',   'Auto-generated TOC.'),
  ('pageBreak',       'Page break',        'structure', 'newpage',           'Forces a new page in LaTeX export. Legacy alias: "divider".')
ON CONFLICT (slug) DO UPDATE
  SET name = EXCLUDED.name,
      category = EXCLUDED.category,
      latex_role = EXCLUDED.latex_role,
      description = EXCLUDED.description;

-- ---------------------------------------------------------------------
--  Block actions — drives the cartesian-product scenario stub list
-- ---------------------------------------------------------------------

INSERT INTO e2e.block_action (slug, name, description, expected_surface_kind) VALUES
  ('create-via-plus',      'Create via + button',           'Click the inline + button between blocks → BlockTypeMenu opens → choose type.', 'popover'),
  ('create-via-slash',     'Create via slash command',      'Type "/" in an empty paragraph → SlashCommandMenu → choose type.', 'popover'),
  ('create-via-drag-drop', 'Create via drag and drop',      'Drop a file (image / DOCX) onto the canvas to create a block.', 'inline'),
  ('edit-inline',          'Edit inline',                   'Click into the block content area and type.', 'inline'),
  ('edit-via-drawer',      'Edit via settings drawer',      'Open block settings drawer; modify properties; save.', 'drawer'),
  ('convert-via-slash',    'Convert via slash command',     'In a non-empty block, type "/typename" to convert.', 'popover'),
  ('convert-via-kebab',    'Convert via kebab menu',        'Block kebab → Convert → choose new type.', 'popover'),
  ('delete-via-kebab',     'Delete via kebab menu',         'Block kebab → Delete.', 'popover'),
  ('delete-via-keyboard',  'Delete via keyboard shortcut',  'Ctrl/Cmd+Backspace on an empty block.', 'inline'),
  ('reorder-via-drag',     'Reorder via drag handle',       'Grab the left-edge drag handle, move up/down, drop.', 'inline'),
  ('export-to-latex',      'Export to LaTeX',               'Document export → LaTeX → assert block appears with right environment.', 'page'),
  ('export-to-markdown',   'Export to Markdown',            'Document export → Markdown → assert block round-trips.', 'page'),
  ('render-preview',       'Render in preview pane',        'Open preview tab → assert block renders correctly.', 'inline')
ON CONFLICT (slug) DO UPDATE
  SET name = EXCLUDED.name,
      description = EXCLUDED.description,
      expected_surface_kind = EXCLUDED.expected_surface_kind;

-- ---------------------------------------------------------------------
--  Surfaces — the 15 from the design doc's inventory
--  We resolve module IDs by slug to keep this resilient to re-orders.
-- ---------------------------------------------------------------------

WITH m AS (SELECT id, slug FROM e2e.module)
INSERT INTO e2e.surface (module_id, slug, name, description, surface_kind, route_pattern, source_file, criticality)
SELECT m.id, v.slug, v.name, v.description, v.surface_kind, v.route_pattern, v.source_file, v.criticality
FROM (VALUES
  -- studio
  ('studio', 'studio-page',              'Studio page',                   'Top-level /studio/:id route, hosts the full editor.', 'page',    '/studio/:id', 'src/pages/StudioPage.tsx', 'p0'),
  ('studio', 'block-canvas',             'Block canvas',                  'The scrollable list of block cards in studio.',       'inline',   NULL, 'src/components/editor/BlockCanvas.tsx', 'p0'),
  ('studio', 'block-card',               'Block card',                    'Individual block container with kebab + drag handle.', 'inline',   NULL, 'src/components/editor/BlockWrapper.tsx', 'p0'),
  ('studio', 'block-type-menu',          'Block type menu',               'Popover from the + button, lists available block types.', 'popover', NULL, 'src/components/editor/BlockTypeMenu.tsx', 'p0'),
  ('studio', 'slash-command-menu',       'Slash command menu',            'Popover triggered by "/" in an empty paragraph.', 'popover', NULL, 'src/components/editor/SlashCommandMenu.tsx', 'p0'),
  ('studio', 'topbar',                   'Studio top bar',                'Title input, mode toggle, share, settings, save badge.', 'inline',  NULL, 'src/components/editor/TopBar.tsx', 'p0'),
  ('studio', 'ribbon-home',              'Ribbon — Home tab',             'Format/style/size/colour/spacing toolbar (current mode).', 'inline',  NULL, 'src/components/editor/EditorRibbon.tsx', 'p1'),
  ('studio', 'ribbon-cite',              'Ribbon — Cite tab',             'Citation insert + bibliography pane shortcut.',          'inline',  NULL, 'src/components/editor/EditorRibbon.tsx', 'p1'),
  ('studio', 'ribbon-review',            'Ribbon — Review tab',           'Comments, validation, version history.',                  'inline',  NULL, 'src/components/editor/EditorRibbon.tsx', 'p1'),
  ('studio', 'ribbon-view',              'Ribbon — View tab',             'Outline + preview + dark mode toggle.',                   'inline',  NULL, 'src/components/editor/EditorRibbon.tsx', 'p1'),
  ('studio', 'activity-bar',             'Activity bar',                  'Left rail with module icons (Outline, Comments, etc.).', 'inline',  NULL, 'src/components/editor/ActivityBar.tsx', 'p1'),
  ('studio', 'docs-panel',               'Documentation drawer',          'Right-rail help drawer.',                                 'drawer',  NULL, 'src/components/studio/DocsPanel.tsx', 'p2'),
  ('studio', 'block-settings-drawer',    'Block settings drawer',         'Per-block configuration via the kebab → settings path.', 'drawer',  NULL, 'src/components/studio/BlockSettingsDrawer.tsx', 'p1'),
  ('studio', 'document-settings-modal',  'Document settings modal',       'Class / font / paper / columns picker.',                  'modal',   NULL, 'src/components/studio/DocumentSettingsModal.tsx', 'p0'),
  ('studio', 'packages-modal',           'LaTeX packages modal',          'Browse + install LaTeX packages for the document.',       'modal',   NULL, 'src/components/studio/PackagesModal.tsx', 'p1'),
  ('studio', 'formula-editor',           'Formula editor',                'Modal for editing equation block content.',               'modal',   NULL, 'src/components/studio/FormulaEditor.tsx', 'p0'),
  ('studio', 'validation-report',        'Validation report',             'Drawer listing validation issues per block.',             'drawer',  NULL, 'src/components/studio/ValidationReport.tsx', 'p1'),
  ('studio', 'command-palette',          'Command palette (⌘K)',          'Global keyboard-driven action palette.',                  'modal',   NULL, 'src/components/shared/CommandPalette.tsx', 'p0'),

  -- dashboard
  ('dashboard', 'dashboard-page',        'Dashboard page',                'Top-level /latex/docs route.',                             'page',    '/latex/docs', 'src/pages/DocumentsPage.tsx', 'p0'),
  ('dashboard', 'doc-grid',              'Document grid',                 'Card grid of recent docs.',                                'inline',  NULL, 'src/components/dashboard/DocGrid.tsx', 'p0'),
  ('dashboard', 'doc-card',              'Document card',                 'Per-doc card with title, outline preview, kebab.',         'inline',  NULL, 'src/components/dashboard/DocCard.tsx', 'p0'),
  ('dashboard', 'new-doc-modal',         'New document modal',            'Title input + template picker.',                           'modal',   NULL, 'src/components/dashboard/NewDocumentDialog.tsx', 'p0'),
  ('dashboard', 'sort-menu',             'Sort + density menu',           'Last-updated / alphabetical / created sort + density toggle.', 'popover', NULL, 'src/components/dashboard/DashboardControls.tsx', 'p2'),

  -- auth
  ('auth', 'sign-in-page',               'Sign-in page',                  'Email + password form, OAuth buttons, magic link.',        'page',    '/sign-in', 'src/views/auth/SignIn/SignIn.tsx', 'p0'),
  ('auth', 'sign-up-page',               'Sign-up page',                  'Email + display name + password.',                         'page',    '/sign-up', 'src/views/auth/SignUp/SignUp.tsx', 'p0'),
  ('auth', 'verify-email-page',          'Verify-email landing',          'Post-signup "check your inbox" screen.',                   'page',    '/verify-email', 'src/views/auth/VerifyEmail.tsx', 'p0'),
  ('auth', 'forgot-password-page',       'Forgot-password page',          'Email entry for password reset.',                          'page',    '/forgot-password', 'src/views/auth/ForgotPassword.tsx', 'p1'),
  ('auth', 'oauth-callback',             'OAuth callback',                'Stytch /auth/callback handler.',                           'page',    '/auth/callback', 'src/views/auth/Callback.tsx', 'p0'),

  -- mobile
  ('mobile', 'mobile-studio',            'Mobile studio',                 'StudioMobile shell — card list + bottom-rail buttons.',    'page',    '/studio/:id', 'src/components/studio/StudioMobile.tsx', 'p0'),
  ('mobile', 'mobile-bottom-rail',       'Mobile bottom-rail buttons',    'Document panels, Add, Validation, Share buttons cluster.', 'inline',  NULL, 'src/components/studio/StudioMobile.tsx', 'p0'),
  ('mobile', 'mobile-action-sheet',      'Mobile block action sheet',     'Long-press on a card opens the action sheet.',             'sheet',   NULL, 'src/components/studio/MobileBlockActionSheet.tsx', 'p1'),
  ('mobile', 'mobile-edit-page',         'Mobile block edit page',        'Full-screen editor for one block on mobile.',              'page',    NULL, 'src/components/studio/MobileFullScreenEditor.tsx', 'p0'),
  ('mobile', 'mobile-settings-sheet',    'Mobile settings sheet',         'Bottom sheet with class/font/paper + sharing row.',        'sheet',   NULL, 'src/components/studio/MobileSettingsSheet.tsx', 'p1'),

  -- export / import
  ('export', 'export-dialog',            'Export dialog',                 'Format picker + generate button.',                         'dialog',  NULL, 'src/components/export/ExportDialog.tsx', 'p0'),
  ('import', 'import-dialog',            'Import dialog',                 'File picker + format guidance.',                           'dialog',  NULL, 'src/components/import/ImportDialog.tsx', 'p1'),
  ('import', 'import-review-page',       'Import review page',            'Per-block approve/reject/edit screen.',                    'page',    '/import-review/:sessionId', 'src/pages/ImportReviewPage.tsx', 'p1'),

  -- share
  ('share', 'share-drawer',              'Share drawer',                  'Public toggle, link, collaborator invites.',               'drawer',  NULL, 'src/components/share/ShareDrawer.tsx', 'p0'),

  -- templates / bibliography / formulas
  ('templates',    'templates-page',     'Templates gallery',             'Browse + preview starter docs.',                           'page',    '/latex/templates', 'src/pages/TemplatesPage.tsx', 'p1'),
  ('bibliography', 'bibliography-panel', 'Bibliography panel',            'Library + DOI lookup + bib export.',                       'drawer',  NULL, 'src/components/bibliography/BibliographyPanel.tsx', 'p1'),
  ('formulas',     'formulas-page',      'Formulas page',                 'Personal formula library.',                                'page',    '/latex/formulas', 'src/pages/FormulasPage.tsx', 'p2'),

  -- settings
  ('settings', 'settings-page',          'Settings page',                 'Profile, preferences, billing.',                           'page',    '/settings', 'src/pages/SettingsPage.tsx', 'p2'),

  -- admin
  ('admin', 'scenarios-admin',           'Scenarios admin route',         'Internal e2e.* CRUD UI gated to owner.',                   'page',    '/internal/scenarios', 'src/views/internal/ScenariosAdmin.tsx', 'p2')
) AS v(module_slug, slug, name, description, surface_kind, route_pattern, source_file, criticality)
JOIN m ON m.slug = v.module_slug
ON CONFLICT (module_id, slug) DO UPDATE
  SET name = EXCLUDED.name,
      description = EXCLUDED.description,
      surface_kind = EXCLUDED.surface_kind,
      route_pattern = EXCLUDED.route_pattern,
      source_file = EXCLUDED.source_file,
      criticality = EXCLUDED.criticality,
      updated_at = NOW();

-- ---------------------------------------------------------------------
--  Entry points — how each surface is opened
--  These are the openers we know about. The admin UI can add more.
-- ---------------------------------------------------------------------

WITH s AS (
  SELECT su.id, m.slug AS module_slug, su.slug AS surface_slug
  FROM e2e.surface su JOIN e2e.module m ON m.id = su.module_id
)
INSERT INTO e2e.entry_point (target_surface_id, slug, description, opener_kind, shortcut_keys, criticality)
SELECT s.id, v.ep_slug, v.description, v.opener_kind, v.shortcut, v.criticality
FROM (VALUES
  -- ShareDrawer openers
  ('share', 'share-drawer', 'topbar-share-btn',       'Topbar Share button',                'toolbar_button',    NULL,  'p0'),
  ('share', 'share-drawer', 'cmd-k-share',            'Command palette ⌘K → Share',         'command_palette',  '⌘K',   'p1'),
  ('share', 'share-drawer', 'mobile-settings-share',  'Mobile settings sheet → Share row',  'toolbar_button',    NULL,  'p0'),

  -- ExportDialog openers
  ('export', 'export-dialog', 'topbar-export-btn',    'Topbar Export button',               'toolbar_button',    NULL,  'p0'),
  ('export', 'export-dialog', 'cmd-k-export',         'Command palette ⌘K → Export',        'command_palette',  '⌘K',   'p1'),

  -- ImportDialog openers
  ('import', 'import-dialog', 'dashboard-import-btn', 'Dashboard Import button',            'toolbar_button',    NULL,  'p1'),
  ('import', 'import-dialog', 'drag-drop-dashboard',  'Drop file on dashboard',             'drag_drop',         NULL,  'p1'),

  -- BlockTypeMenu openers
  ('studio', 'block-type-menu', 'plus-button',        '+ button between blocks',            'toolbar_button',    NULL,  'p0'),

  -- SlashCommandMenu openers
  ('studio', 'slash-command-menu', 'slash-empty-para','"/" key in empty paragraph',         'keyboard_shortcut', '/',   'p0'),

  -- PackagesModal openers
  ('studio', 'packages-modal', 'topbar-packages',     'Studio toolbar Packages button',     'toolbar_button',    NULL,  'p1'),
  ('studio', 'packages-modal', 'cmd-k-packages',      'Command palette ⌘K → Packages',      'command_palette',  '⌘K',   'p2'),

  -- DocsPanel openers
  ('studio', 'docs-panel', 'activity-bar-docs',       'Activity bar Docs icon',             'toolbar_button',    NULL,  'p2'),
  ('studio', 'docs-panel', 'question-shortcut',       'Press "?" key',                      'keyboard_shortcut', '?',   'p2'),

  -- DocumentSettingsModal
  ('studio', 'document-settings-modal', 'stamp-click','Click document stamp in topbar',     'toolbar_button',    NULL,  'p0'),

  -- FormulaEditor
  ('studio', 'formula-editor', 'equation-edit',       'Click an equation block → edit',     'toolbar_button',    NULL,  'p0'),
  ('studio', 'formula-editor', 'slash-equation',      'Slash menu → Equation',              'command_palette',   NULL,  'p0'),

  -- ValidationReport
  ('studio', 'validation-report', 'validate-all-btn', 'Validate all blocks button',         'toolbar_button',    NULL,  'p1'),
  ('studio', 'validation-report', 'cmd-k-validate',   'Command palette ⌘K → Validate',      'command_palette',  '⌘K',   'p2'),

  -- Command palette is itself a surface; just the keyboard opener.
  ('studio', 'command-palette', 'cmd-k',              '⌘K / Ctrl+K anywhere',               'keyboard_shortcut', '⌘K',   'p0'),

  -- Mobile action sheet
  ('mobile', 'mobile-action-sheet', 'long-press-card','Long-press on a block card',         'long_press',        NULL,  'p1'),

  -- Mobile settings sheet
  ('mobile', 'mobile-settings-sheet', 'mobile-cog',   'Mobile topbar cog',                  'toolbar_button',    NULL,  'p1'),
  ('mobile', 'mobile-settings-sheet', 'canvas-stamp', 'Canvas stamp tap',                   'toolbar_button',    NULL,  'p1'),

  -- New doc modal
  ('dashboard', 'new-doc-modal', 'new-doc-btn',       '"New document" button',              'toolbar_button',    NULL,  'p0'),
  ('dashboard', 'new-doc-modal', 'cmd-k-new',         'Command palette ⌘K → New document',  'command_palette',  '⌘K',   'p1')
) AS v(module_slug, surface_slug, ep_slug, description, opener_kind, shortcut, criticality)
JOIN s ON s.module_slug = v.module_slug AND s.surface_slug = v.surface_slug
ON CONFLICT (target_surface_id, slug) DO UPDATE
  SET description = EXCLUDED.description,
      opener_kind = EXCLUDED.opener_kind,
      shortcut_keys = EXCLUDED.shortcut_keys,
      criticality = EXCLUDED.criticality;

-- ---------------------------------------------------------------------
--  Tags — initial set for filtering scenarios in the admin UI
-- ---------------------------------------------------------------------

INSERT INTO e2e.tag (slug, name, description, color) VALUES
  ('launch-gate',        'Launch gate',         'Must be green before shipping.',                       '#DC2626'),
  ('regression',         'Regression',          'Added to cover a previously-fixed bug.',                '#F59E0B'),
  ('accessibility',      'Accessibility',       'Keyboard / a11y focused.',                              '#2563EB'),
  ('mobile-only',        'Mobile only',         'Exercises the mobile-chrome project.',                  '#7C3AED'),
  ('visual',             'Visual',              'Pixel-level / layout-sensitive.',                       '#0891B2'),
  ('export-fidelity',    'Export fidelity',     'LaTeX / Markdown / HTML output checks.',                '#16A34A'),
  ('keyboard',           'Keyboard shortcuts',  'Exercises a keyboard binding.',                         '#475569'),
  ('p0-pre-launch',      'P0 pre-launch',       'Subset of p0 we ALWAYS run before deploy.',             '#B91C1C')
ON CONFLICT (slug) DO NOTHING;

-- Quick sanity count for the migrator.
DO $$
DECLARE
  m INTEGER; s INTEGER; bt INTEGER; ba INTEGER; ep INTEGER; t INTEGER;
BEGIN
  SELECT COUNT(*) INTO m  FROM e2e.module;
  SELECT COUNT(*) INTO s  FROM e2e.surface;
  SELECT COUNT(*) INTO bt FROM e2e.block_type;
  SELECT COUNT(*) INTO ba FROM e2e.block_action;
  SELECT COUNT(*) INTO ep FROM e2e.entry_point;
  SELECT COUNT(*) INTO t  FROM e2e.tag;
  RAISE NOTICE 'e2e catalogue seeded: % modules, % surfaces, % block_types, % block_actions, % entry_points, % tags',
    m, s, bt, ba, ep, t;
END
$$;

COMMIT;

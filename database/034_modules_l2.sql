-- =====================================================================
--  Scenario System — Phase 2: promote the spec-covered scenarios of
--  every NON-flow module to L2, in one batch.
--
--  Same recipe as the flow seeds (032 / 033): steps authored from the
--  executable tests in lilia-web-editor/e2e/tests/*.spec.ts. This batch
--  covers auth, blocks, documents, export, import, mobile and save —
--  the 20 scenarios that carry a scenario()-fingerprinted test.
--
--  As with flow, only spec-covered scenarios are promoted — a scenario
--  with no test stays L1 rather than getting a walkthrough for an
--  unverified path.
--
--  Idempotent: each row guarded by NOT EXISTS on version_number = 2.
-- =====================================================================

BEGIN;

WITH authored (slug, title, descr, steps) AS (
  VALUES
  -- ── auth ──────────────────────────────────────────────────────────
  ('auth.session-jwt-minted',
   'A valid session JWT is minted at sign-in',
   'Verify the authentication setup mints a valid, unexpired session JWT.',
   '[
     {"step_kind":"setup","description":"Run the authentication setup — it signs the test user in against Stytch.","user_visible_outcome":"A session JWT is stored for the run to use."},
     {"step_kind":"assert","description":"The token is non-empty and decodes into a JWT header and payload.","user_visible_outcome":"The header names an RS-family signing algorithm and a key id."},
     {"step_kind":"assert","description":"The token issuer references the expected Stytch project and its expiry is in the future.","user_visible_outcome":"The JWT is valid and not expired."}
   ]'::jsonb),

  ('auth.cookies-loaded',
   'Session cookies load into the browser',
   'Verify the Stytch session cookies survive into the browser.',
   '[
     {"step_kind":"setup","description":"Open the app with the saved authenticated session.","user_visible_outcome":"The app loads as a signed-in user."},
     {"step_kind":"assert","description":"Check the browser cookies.","user_visible_outcome":"Both the stytch_session and stytch_session_jwt cookies are present — the saved sign-in state loaded."}
   ]'::jsonb),

  ('auth.dashboard-route-resolves',
   'The dashboard resolves for a signed-in user',
   'Verify an authenticated user reaches the dashboard without being bounced to sign-in.',
   '[
     {"step_kind":"setup","description":"While signed in, navigate to the dashboard route at /latex/docs.","user_visible_outcome":"The dashboard route begins loading."},
     {"step_kind":"assert","description":"The dashboard chrome renders.","user_visible_outcome":"The page shows document-related content and stays on /latex/docs — no redirect to sign-in or unauthorized."}
   ]'::jsonb),

  -- ── blocks ────────────────────────────────────────────────────────
  ('blocks.create-all-canonical-types',
   'Every canonical block type can be created',
   'Create one block of each canonical type and confirm each is accepted.',
   '[
     {"step_kind":"setup","description":"Start from a seeded document.","user_visible_outcome":"The document is ready to take blocks."},
     {"step_kind":"action","description":"Create one block of each canonical type — paragraph, heading, equation, figure, code, list, blockquote, table, theorem, abstract, bibliography, table of contents, page break."},
     {"step_kind":"assert","description":"List the document blocks.","user_visible_outcome":"Every requested block was created with its type — the list holds one of each canonical type."}
   ]'::jsonb),

  ('blocks.list-reflects-ordering',
   'The block list reflects sort order',
   'Confirm blocks come back in their sort-order sequence.',
   '[
     {"step_kind":"setup","description":"Start from a seeded document.","user_visible_outcome":"The document is ready to take blocks."},
     {"step_kind":"action","description":"Add a heading then a paragraph, each with an increasing sort order."},
     {"step_kind":"assert","description":"List the document blocks.","user_visible_outcome":"The blocks come back in sort order — the heading precedes the paragraph."}
   ]'::jsonb),

  ('blocks.studio-renders-multiple-blocks',
   'The Studio renders multiple blocks',
   'Confirm the block-cards Studio shows every block in the document.',
   '[
     {"step_kind":"setup","description":"Seed a document with a heading and a paragraph.","user_visible_outcome":"The document holds two blocks."},
     {"step_kind":"action","action_kind":"click","description":"Open the document in the Studio at /studio/<id>."},
     {"step_kind":"assert","description":"The Studio renders the blocks.","user_visible_outcome":"Both the heading and the paragraph text appear as block cards."}
   ]'::jsonb),

  -- ── documents ─────────────────────────────────────────────────────
  ('documents.crud-roundtrip',
   'A document survives a create / read / delete round-trip',
   'Create, read back, then delete a document and confirm each step.',
   '[
     {"step_kind":"action","description":"Create a new document with a known title.","user_visible_outcome":"The document is created with an id and the title you gave it."},
     {"step_kind":"assert","description":"Read the document back by its id.","user_visible_outcome":"The fetched document matches the one created."},
     {"step_kind":"action","description":"Delete the document."},
     {"step_kind":"assert","description":"Try to read the deleted document.","user_visible_outcome":"The document is gone — fetching its id returns a 404."}
   ]'::jsonb),

  ('documents.dashboard-renders-new-doc',
   'A new document appears on the dashboard',
   'Confirm a freshly-created document shows up on the dashboard.',
   '[
     {"step_kind":"setup","description":"Create a document with a unique title.","user_visible_outcome":"The document exists."},
     {"step_kind":"action","action_kind":"click","description":"Open the dashboard at /latex/docs."},
     {"step_kind":"assert","description":"Look for the new document.","user_visible_outcome":"A card with the document title is visible on the dashboard."}
   ]'::jsonb),

  ('documents.studio-mounts-for-id',
   'The Studio mounts for a document id',
   'Confirm the Studio page loads when navigated to directly by document id.',
   '[
     {"step_kind":"setup","description":"Start from a seeded document.","user_visible_outcome":"A document id is ready."},
     {"step_kind":"action","action_kind":"click","description":"Navigate straight to the Studio URL at /studio/<id>."},
     {"step_kind":"assert","description":"The Studio shell mounts.","user_visible_outcome":"The Studio loads at /studio/<id> with its Cards toggle visible."}
   ]'::jsonb),

  -- ── export ────────────────────────────────────────────────────────
  ('export.latex-returns-non-empty',
   'LaTeX export returns content',
   'Export a document to LaTeX and confirm the result is non-empty.',
   '[
     {"step_kind":"setup","description":"Start from a document with a heading and a paragraph.","user_visible_outcome":"The document has exportable content."},
     {"step_kind":"action","description":"Request a LaTeX export of the document."},
     {"step_kind":"assert","description":"Inspect the export response.","user_visible_outcome":"The export returns a non-empty LaTeX artefact carrying the document content."}
   ]'::jsonb),

  ('export.markdown-returns-non-empty',
   'Markdown export returns content',
   'Export a document to Markdown and confirm the result is non-empty.',
   '[
     {"step_kind":"setup","description":"Start from a document with a heading and a paragraph.","user_visible_outcome":"The document has exportable content."},
     {"step_kind":"action","description":"Request a Markdown export of the document."},
     {"step_kind":"assert","description":"Inspect the export response.","user_visible_outcome":"The export returns a non-empty Markdown artefact carrying the document content."}
   ]'::jsonb),

  ('export.html-returns-non-empty',
   'HTML export returns content',
   'Export a document to HTML and confirm the result is non-empty.',
   '[
     {"step_kind":"setup","description":"Start from a document with a heading and a paragraph.","user_visible_outcome":"The document has exportable content."},
     {"step_kind":"action","description":"Request an HTML export of the document."},
     {"step_kind":"assert","description":"Inspect the export response.","user_visible_outcome":"The export returns a non-empty HTML artefact carrying the document content."}
   ]'::jsonb),

  -- ── import ────────────────────────────────────────────────────────
  ('import.docx-upload-returns-id',
   'Uploading a DOCX starts an import',
   'Upload a Word document and confirm the import job starts.',
   '[
     {"step_kind":"setup","description":"Have a DOCX file ready to import.","user_visible_outcome":"The file is ready to upload."},
     {"step_kind":"action","description":"Upload the DOCX file to the import endpoint."},
     {"step_kind":"assert","description":"Inspect the response.","user_visible_outcome":"The import accepts the file and returns a job or session id — the conversion has started."}
   ]'::jsonb),

  ('import.review-sessions-reachable',
   'The import-review sessions endpoint is reachable',
   'Confirm the import-review sessions route exists.',
   '[
     {"step_kind":"action","description":"Request the import-review sessions list endpoint."},
     {"step_kind":"assert","description":"Inspect the response status.","user_visible_outcome":"The route exists — it responds with any status other than 404, so the review flow has somewhere to read from."}
   ]'::jsonb),

  -- ── mobile ────────────────────────────────────────────────────────
  ('mobile.dashboard-mounts',
   'The dashboard mounts on a mobile viewport',
   'Confirm the dashboard renders on a phone-sized screen.',
   '[
     {"step_kind":"setup","description":"Open the app on a mobile viewport (Pixel 7 profile).","user_visible_outcome":"The app loads at phone width."},
     {"step_kind":"action","action_kind":"click","description":"Navigate to the dashboard at /latex/docs."},
     {"step_kind":"assert","description":"The dashboard renders.","user_visible_outcome":"The page shows document-related content laid out for a phone."}
   ]'::jsonb),

  ('mobile.studio-mounts-no-error',
   'The Studio mounts on mobile without errors',
   'Confirm the mobile Studio loads without throwing a runtime error.',
   '[
     {"step_kind":"setup","description":"Seed a document and open the app on a mobile viewport.","user_visible_outcome":"The app is ready at phone width."},
     {"step_kind":"action","action_kind":"click","description":"Navigate to the Studio at /studio/<id>."},
     {"step_kind":"assert","description":"The mobile Studio mounts.","user_visible_outcome":"The document content renders and no property-access runtime error is thrown."}
   ]'::jsonb),

  ('mobile.chrome-elements-present',
   'The mobile Studio chrome is present',
   'Confirm the launch-critical mobile Studio controls render.',
   '[
     {"step_kind":"setup","description":"Seed a document and open the Studio on a mobile viewport.","user_visible_outcome":"The mobile Studio loads."},
     {"step_kind":"assert","description":"Check for the mobile Studio chrome.","user_visible_outcome":"The Document panels drawer trigger and the Add button are both visible."}
   ]'::jsonb),

  -- ── save ──────────────────────────────────────────────────────────
  ('save.block-update-persists',
   'A block update persists',
   'Edit a block and confirm the change is saved.',
   '[
     {"step_kind":"setup","description":"Start from a seeded document with a paragraph block.","user_visible_outcome":"The block holds its first version of content."},
     {"step_kind":"action","description":"Update the block content."},
     {"step_kind":"assert","description":"Read the block back.","user_visible_outcome":"The block holds the updated text — the edit persisted."}
   ]'::jsonb),

  ('save.document-title-persists',
   'A document title change persists',
   'Rename a document and confirm the new title is saved.',
   '[
     {"step_kind":"setup","description":"Start from a seeded document.","user_visible_outcome":"The document has its original title."},
     {"step_kind":"action","description":"Update the document title."},
     {"step_kind":"assert","description":"Read the document back.","user_visible_outcome":"The document carries the renamed title."}
   ]'::jsonb),

  ('save.studio-renders-seeded-block',
   'Seeded block content renders in the Studio',
   'Confirm saved block content shows up when the Studio loads.',
   '[
     {"step_kind":"setup","description":"Seed a document with a paragraph block.","user_visible_outcome":"The document holds a saved paragraph."},
     {"step_kind":"action","action_kind":"click","description":"Open the document in the Studio at /studio/<id>."},
     {"step_kind":"assert","description":"The Studio renders the saved block.","user_visible_outcome":"The paragraph text appears in the Studio."}
   ]'::jsonb)
)
INSERT INTO e2e.scenario_version (
  scenario_id, version_number, detail_level, title, description,
  steps, generation_provenance, generated_by
)
SELECT s.id, 2, 'l2', a.title, a.descr, a.steps, 'llm_draft', 'claude-phase2'
FROM authored a
JOIN e2e.scenario s ON s.slug = a.slug
WHERE NOT EXISTS (
  SELECT 1 FROM e2e.scenario_version v
  WHERE v.scenario_id = s.id AND v.version_number = 2
);

UPDATE e2e.scenario s
SET detail_level = 'l2',
    description = v.description,
    current_version_id = v.id,
    updated_at = NOW()
FROM e2e.scenario_version v
WHERE v.scenario_id = s.id
  AND v.version_number = 2
  AND s.slug IN (
    'auth.session-jwt-minted','auth.cookies-loaded','auth.dashboard-route-resolves',
    'blocks.create-all-canonical-types','blocks.list-reflects-ordering','blocks.studio-renders-multiple-blocks',
    'documents.crud-roundtrip','documents.dashboard-renders-new-doc','documents.studio-mounts-for-id',
    'export.latex-returns-non-empty','export.markdown-returns-non-empty','export.html-returns-non-empty',
    'import.docx-upload-returns-id','import.review-sessions-reachable',
    'mobile.dashboard-mounts','mobile.studio-mounts-no-error','mobile.chrome-elements-present',
    'save.block-update-persists','save.document-title-persists','save.studio-renders-seeded-block'
  );

DO $$
DECLARE n INT;
BEGIN
  SELECT count(*) INTO n FROM e2e.scenario WHERE detail_level = 'l2';
  RAISE NOTICE 'total scenarios at L2 after all-modules batch: %', n;
END$$;

COMMIT;

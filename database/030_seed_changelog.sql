-- Seed the changelog_entries table with the current What's New content
-- (en/fr/es). Idempotent: only seeds when the table is empty, so re-running is
-- safe. New fixes are appended going forward (status='shipped').
INSERT INTO changelog_entries (entry_date, area, kind, status, title, detail, verified, shot_url, sort)
SELECT * FROM (VALUES
  -- ── 2026-06-27 ──
  (DATE '2026-06-27','Theme','feature','shipped',
   '{"en":"Theme picker (5 themes)","fr":"Sélecteur de thème (5 thèmes)","es":"Selector de tema (5 temas)"}'::jsonb,
   '{"en":"Pick Light, Dark, Dim, Forest or Sepia — each with a color swatch — from one picker in the app and the editor.","fr":"Choisissez Clair, Sombre, Tamisé, Forêt ou Sépia — chacun avec sa pastille — depuis un seul sélecteur dans l''app et l''éditeur.","es":"Elige Claro, Oscuro, Atenuado, Bosque o Sepia — cada uno con su muestra — desde un único selector en la app y el editor."}'::jsonb,
   false,'/whatsnew/theme-picker.png',70),
  (DATE '2026-06-27','Block mode','feature','shipped',
   '{"en":"Per-block “+” add button","fr":"Bouton « + » par bloc","es":"Botón « + » por bloque"}'::jsonb,
   '{"en":"Each block shows a “+” next to the ⋮ menu — Add block above / below.","fr":"Chaque bloc affiche un « + » à côté du menu ⋮ — Ajouter un bloc au-dessus / en dessous.","es":"Cada bloque muestra un « + » junto al menú ⋮ — Añadir bloque arriba / abajo."}'::jsonb,
   false,'/whatsnew/add-block.png',60),
  (DATE '2026-06-27','Block mode','feature','shipped',
   '{"en":"Cleaner toolbar + readable chips","fr":"Barre épurée + pastilles lisibles","es":"Barra más limpia + etiquetas legibles"}'::jsonb,
   '{"en":"Three stacked toolbar rows are now one compact bar; each block shows a readable type chip (H1, TXT, EQ…).","fr":"Les trois rangées deviennent une barre compacte ; chaque bloc affiche une pastille de type lisible (H1, TXT, EQ…).","es":"Las tres filas ahora son una barra compacta; cada bloque muestra una etiqueta de tipo legible (H1, TXT, EQ…)."}'::jsonb,
   false,'/whatsnew/block-toolbar.png',50),
  (DATE '2026-06-27','Ask Lilia','feature','shipped',
   '{"en":"Launch editor commands from chat","fr":"Lancer des commandes depuis le chat","es":"Lanzar comandos desde el chat"}'::jsonb,
   '{"en":"Ask Lilia can run actions — Validate, Open PDF preview, Export — as one-tap chips under its reply.","fr":"Ask Lilia peut exécuter des actions — Valider, Aperçu PDF, Exporter — en un clic sous sa réponse.","es":"Ask Lilia puede ejecutar acciones — Validar, Vista PDF, Exportar — con un toque bajo su respuesta."}'::jsonb,
   false,'/whatsnew/ask-lilia-commands.png',40),
  (DATE '2026-06-27','LaTeX','fix','shipped',
   '{"en":"Theorem blocks validate again","fr":"Les blocs théorème se valident à nouveau","es":"Los bloques de teorema vuelven a validar"}'::jsonb,
   '{"en":"Theorem/lemma blocks no longer fail “No counter ''Theorem'' defined”.","fr":"Les blocs théorème/lemme n''échouent plus avec « No counter ''Theorem'' defined ».","es":"Los bloques de teorema/lema ya no fallan con « No counter ''Theorem'' defined »."}'::jsonb,
   true,NULL,30),
  (DATE '2026-06-27','LaTeX','fix','shipped',
   '{"en":"Greek & math symbols in text compile","fr":"Les symboles grecs et mathématiques compilent","es":"Los símbolos griegos y matemáticos compilan"}'::jsonb,
   '{"en":"Literal Unicode in prose (γ, Δ, μ, ², →…) no longer aborts the build.","fr":"L''Unicode littéral dans le texte (γ, Δ, μ, ², →…) n''interrompt plus la compilation.","es":"El Unicode literal en el texto (γ, Δ, μ, ², →…) ya no aborta la compilación."}'::jsonb,
   true,NULL,20),
  (DATE '2026-06-27','LaTeX','fix','shipped',
   '{"en":"Code blocks with any language compile","fr":"Les blocs de code de tout langage compilent","es":"Los bloques de código de cualquier lenguaje compilan"}'::jsonb,
   '{"en":"An unknown code language now falls back to plain code instead of failing.","fr":"Un langage de code inconnu bascule en code brut au lieu d''échouer.","es":"Un lenguaje de código desconocido pasa a código plano en vez de fallar."}'::jsonb,
   true,NULL,10),
  -- ── 2026-06-26 ──
  (DATE '2026-06-26','Home','feature','shipped',
   '{"en":"Redesigned home (desktop + mobile)","fr":"Accueil redessiné (bureau + mobile)","es":"Inicio rediseñado (escritorio + móvil)"}'::jsonb,
   '{"en":"A cleaner landing once you log in, with the mobile layout fixed.","fr":"Un accueil plus clair après connexion, avec la mise en page mobile corrigée.","es":"Un inicio más claro tras iniciar sesión, con el diseño móvil corregido."}'::jsonb,
   false,NULL,30),
  (DATE '2026-06-26','Editor','feature','shipped',
   '{"en":"Block ⇆ Flow creation picker","fr":"Choix création Bloc ⇆ Flux","es":"Selector de creación Bloque ⇆ Flujo"}'::jsonb,
   '{"en":"After an AI draft and when creating a document, you choose Block or Flow up front.","fr":"Après un brouillon IA et à la création d''un document, vous choisissez Bloc ou Flux d''emblée.","es":"Tras un borrador de IA y al crear un documento, eliges Bloque o Flujo desde el principio."}'::jsonb,
   false,NULL,20),
  (DATE '2026-06-26','AI','fix','shipped',
   '{"en":"AI documents get a real title","fr":"Les documents IA ont un vrai titre","es":"Los documentos de IA reciben un título real"}'::jsonb,
   '{"en":"Documents from an Ask Lilia draft are named from their content instead of “Untitled”.","fr":"Les documents issus d''un brouillon Ask Lilia sont nommés d''après leur contenu plutôt que « Untitled ».","es":"Los documentos de un borrador de Ask Lilia se nombran por su contenido en vez de « Untitled »."}'::jsonb,
   false,NULL,10),
  -- ── Known issues ──
  (DATE '2026-06-27','Editor','fix','known',
   '{"en":"Sign-in / login errors","fr":"Erreurs de connexion","es":"Errores de inicio de sesión"}'::jsonb,
   '{"en":"Some sign-in / shared-link flows can bounce or error on a cold load. Shared links are fixed; the broader sign-in race is being worked on.","fr":"Certains parcours de connexion / lien partagé peuvent rebondir ou échouer au premier chargement. Les liens sont corrigés ; la course à la connexion est en cours.","es":"Algunos flujos de inicio de sesión / enlace compartido pueden rebotar o fallar en frío. Los enlaces están corregidos; la carrera del inicio de sesión está en proceso."}'::jsonb,
   false,NULL,20),
  (DATE '2026-06-27','Block mode','fix','known',
   '{"en":"Add / move block sometimes needs a second try","fr":"L''ajout / déplacement d''un bloc nécessite parfois un second essai","es":"Añadir / mover un bloque a veces requiere un segundo intento"}'::jsonb,
   '{"en":"Adding a block or dragging to reorder can require a second action the first time. Under investigation.","fr":"Ajouter un bloc ou le glisser pour réordonner peut demander une seconde action la première fois. En cours d''analyse.","es":"Añadir un bloque o arrastrarlo para reordenar puede requerir una segunda acción la primera vez. En investigación."}'::jsonb,
   false,NULL,10)
) AS seed(entry_date, area, kind, status, title, detail, verified, shot_url, sort)
WHERE NOT EXISTS (SELECT 1 FROM changelog_entries);

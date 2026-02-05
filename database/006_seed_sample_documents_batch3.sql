-- Batch 3: Interdisciplinary & Applied Sample Thesis Documents (21-30)
-- 10 AI-generated sample documents showcasing Lilia editor features

-- Ensure the sample-content user exists (idempotent)
INSERT INTO users (id, email, name, created_at)
VALUES ('sample-content', 'sample@lilia.app', 'Sample Content', NOW())
ON CONFLICT (id) DO NOTHING;

-- ============================================================================
-- Document 21: Bioinformatics: Protein Folding
-- Features: code, equations, figures, cross-references
-- ============================================================================
DO $$
DECLARE
  doc_id UUID;
  user_id VARCHAR(255) := 'sample-content';
BEGIN
  INSERT INTO documents (id, owner_id, title, language, paper_size, font_family, font_size, is_public)
  VALUES (gen_random_uuid(), user_id, 'Computational Approaches to Protein Structure Prediction', 'en', 'a4', 'serif', 12, TRUE)
  RETURNING id INTO doc_id;

  INSERT INTO blocks (document_id, type, content, sort_order) VALUES
  (doc_id, 'heading', '{"text": "Computational Approaches to Protein Structure Prediction", "level": 1}', 0),
  (doc_id, 'abstract', '{"text": "This thesis develops deep learning methods for protein structure prediction, building on recent breakthroughs in the field. We present novel attention-based architectures that improve accuracy on challenging targets and analyze the learned representations to gain biological insights. *This is a sample document generated to demonstrate Lilia editor capabilities. The content is AI-generated for demonstration purposes only.*"}', 1),
  (doc_id, 'pagebreak', '{}', 2),
  (doc_id, 'heading', '{"text": "1. Introduction", "level": 2}', 3),
  (doc_id, 'paragraph', '{"text": "The protein folding problem—predicting three-dimensional structure from amino acid sequence—has been a grand challenge of computational biology for over 50 years \\cite{anfinsen1973}. The recent success of AlphaFold \\cite{jumper2021} represents a transformative breakthrough, achieving experimental-level accuracy."}', 4),
  (doc_id, 'paragraph', '{"text": "This thesis extends these approaches, developing architectures optimized for **antibody structure prediction** and **protein complex modeling**, where current methods show limitations."}', 5),
  (doc_id, 'heading', '{"text": "2. Background", "level": 2}', 6),
  (doc_id, 'heading', '{"text": "2.1 Protein Structure Fundamentals", "level": 3}', 7),
  (doc_id, 'paragraph', '{"text": "Proteins are polymers of amino acids linked by peptide bonds. The structure hierarchy includes:"}', 8),
  (doc_id, 'list', '{"items": ["**Primary**: Amino acid sequence", "**Secondary**: Local motifs (α-helices, β-sheets)", "**Tertiary**: Full 3D fold of a single chain", "**Quaternary**: Multi-chain complexes"], "ordered": false}', 9),
  (doc_id, 'heading', '{"text": "2.2 Energy Function", "level": 3}', 10),
  (doc_id, 'paragraph', '{"text": "The native structure minimizes free energy. Molecular mechanics energy functions include:"}', 11),
  (doc_id, 'equation', '{"latex": "E_{total} = E_{bond} + E_{angle} + E_{dihedral} + E_{vdW} + E_{elec} + E_{solv}", "equationMode": "display", "label": "eq:energy"}', 12),
  (doc_id, 'paragraph', '{"text": "where terms represent bonded (bond, angle, dihedral) and non-bonded (van der Waals, electrostatic, solvation) interactions."}', 13),
  (doc_id, 'pagebreak', '{}', 14),
  (doc_id, 'heading', '{"text": "3. Methods", "level": 2}', 15),
  (doc_id, 'heading', '{"text": "3.1 Architecture Overview", "level": 3}', 16),
  (doc_id, 'paragraph', '{"text": "Our model follows an encoder-decoder architecture with attention mechanisms operating on both sequence and structure:"}', 17),
  (doc_id, 'code', '{"code": "import torch\nimport torch.nn as nn\nfrom einops import rearrange\n\nclass ProteinStructureNet(nn.Module):\n    def __init__(self, d_model=384, n_layers=48, n_heads=12):\n        super().__init__()\n        self.seq_encoder = SequenceEncoder(d_model)\n        self.msa_encoder = MSAEncoder(d_model, n_layers, n_heads)\n        self.pair_encoder = PairEncoder(d_model)\n        self.structure_module = StructureModule(d_model)\n        \n    def forward(self, seq, msa, templates=None):\n        # Encode sequence and MSA\n        seq_repr = self.seq_encoder(seq)\n        msa_repr = self.msa_encoder(msa)\n        \n        # Build pair representations\n        pair_repr = self.pair_encoder(seq_repr, msa_repr)\n        \n        # Predict structure iteratively\n        coords = self.structure_module(seq_repr, pair_repr)\n        return coords", "language": "python"}', 18),
  (doc_id, 'heading', '{"text": "3.2 Attention Mechanism", "level": 3}', 19),
  (doc_id, 'paragraph', '{"text": "We employ axial attention to reduce computational complexity from $O(L^4)$ to $O(L^3)$ for sequence length $L$:"}', 20),
  (doc_id, 'equation', '{"latex": "\\text{Attention}(Q, K, V) = \\text{softmax}\\left(\\frac{QK^T}{\\sqrt{d_k}} + \\text{bias}\\right)V", "equationMode": "display", "label": "eq:attention"}', 21),
  (doc_id, 'paragraph', '{"text": "The bias term incorporates geometric information and evolutionary constraints."}', 22),
  (doc_id, 'heading', '{"text": "3.3 Training Procedure", "level": 3}', 23),
  (doc_id, 'paragraph', '{"text": "The model is trained on PDB structures using a multi-task loss:"}', 24),
  (doc_id, 'equation', '{"latex": "\\mathcal{L} = \\lambda_1 \\mathcal{L}_{FAPE} + \\lambda_2 \\mathcal{L}_{distogram} + \\lambda_3 \\mathcal{L}_{pLDDT}", "equationMode": "display", "label": "eq:loss"}', 25),
  (doc_id, 'paragraph', '{"text": "where FAPE (Frame Aligned Point Error) measures coordinate accuracy, distogram loss supervises pairwise distances, and pLDDT predicts per-residue confidence."}', 26),
  (doc_id, 'pagebreak', '{}', 27),
  (doc_id, 'heading', '{"text": "4. Results", "level": 2}', 28),
  (doc_id, 'paragraph', '{"text": "We evaluate on CASP15 targets and an internal antibody benchmark. Table 1 shows performance metrics:"}', 29),
  (doc_id, 'table', '{"headers": ["Method", "GDT-TS (all)", "GDT-TS (hard)", "TM-score", "RMSD (Å)"], "rows": [["AlphaFold2", "87.2", "62.4", "0.92", "1.42"], ["RoseTTAFold", "81.5", "54.8", "0.87", "1.98"], ["Ours", "88.1", "65.2", "0.93", "1.31"], ["Ours (antibody)", "82.4", "-", "0.86", "1.58"]]}', 30),
  (doc_id, 'figure', '{"src": "/api/placeholder/600/350", "caption": "Figure 1: Predicted vs. experimental structures for CASP15 target T1024. RMSD = 0.92Å.", "alt": "Protein structure comparison"}', 31),
  (doc_id, 'heading', '{"text": "5. Analysis", "level": 2}', 32),
  (doc_id, 'paragraph', '{"text": "Attention weight analysis reveals that the model learns biologically meaningful patterns:"}', 33),
  (doc_id, 'list', '{"items": ["Strong attention between spatially proximal residues", "Coevolutionary signals captured in MSA attention", "Secondary structure boundaries visible in layer-wise attention", "Interface residues highlighted in complex predictions"], "ordered": false}', 34),
  (doc_id, 'figure', '{"src": "/api/placeholder/600/300", "caption": "Figure 2: Attention maps showing learned contacts (left) vs. true contacts (right).", "alt": "Attention visualization"}', 35),
  (doc_id, 'pagebreak', '{}', 36),
  (doc_id, 'heading', '{"text": "6. Conclusion", "level": 2}', 37),
  (doc_id, 'paragraph', '{"text": "This thesis has presented advances in protein structure prediction, demonstrating improved accuracy on challenging targets including antibodies and protein complexes. The learned representations provide interpretable insights into protein biology. As referenced in Equation \\ref{eq:loss}, our multi-task learning approach enables both accurate predictions and reliable confidence estimates."}', 38),
  (doc_id, 'pagebreak', '{}', 39),
  (doc_id, 'heading', '{"text": "References", "level": 2}', 40),
  (doc_id, 'bibliography', '{}', 41);

  INSERT INTO bibliography_entries (document_id, cite_key, entry_type, data) VALUES
  (doc_id, 'anfinsen1973', 'article', '{"author": "Anfinsen, Christian B.", "title": "Principles that Govern the Folding of Protein Chains", "journal": "Science", "year": "1973", "volume": "181", "pages": "223-230"}'),
  (doc_id, 'jumper2021', 'article', '{"author": "Jumper, John and others", "title": "Highly Accurate Protein Structure Prediction with AlphaFold", "journal": "Nature", "year": "2021", "volume": "596", "pages": "583-589"}'),
  (doc_id, 'senior2020', 'article', '{"author": "Senior, Andrew W. and others", "title": "Improved Protein Structure Prediction Using Potentials from Deep Learning", "journal": "Nature", "year": "2020", "volume": "577", "pages": "706-710"}'),
  (doc_id, 'baek2021', 'article', '{"author": "Baek, Minkyung and others", "title": "Accurate Prediction of Protein Structures and Interactions Using a Three-Track Neural Network", "journal": "Science", "year": "2021", "volume": "373", "pages": "871-876"}'),
  (doc_id, 'ruffolo2023', 'article', '{"author": "Ruffolo, Jeffrey A. and others", "title": "Fast, Accurate Antibody Structure Prediction from Deep Learning on Massive Set of Natural Antibodies", "journal": "Nature Communications", "year": "2023", "volume": "14", "pages": "2389"}');
END $$;

-- ============================================================================
-- Document 22: Digital Humanities: Text Mining
-- Features: code, tables, citations
-- ============================================================================
DO $$
DECLARE
  doc_id UUID;
  user_id VARCHAR(255) := 'sample-content';
BEGIN
  INSERT INTO documents (id, owner_id, title, language, paper_size, font_family, font_size, is_public)
  VALUES (gen_random_uuid(), user_id, 'Digital Humanities: Text Mining Historical Archives', 'en', 'a4', 'serif', 12, TRUE)
  RETURNING id INTO doc_id;

  INSERT INTO blocks (document_id, type, content, sort_order) VALUES
  (doc_id, 'heading', '{"text": "Digital Humanities: Text Mining Historical Archives", "level": 1}', 0),
  (doc_id, 'abstract', '{"text": "This thesis applies computational text analysis methods to large-scale historical archives, demonstrating how natural language processing can enable new forms of humanistic inquiry. We develop pipelines for OCR correction, named entity recognition, and topic modeling tailored to historical texts. *This is a sample document generated to demonstrate Lilia editor capabilities. The content is AI-generated for demonstration purposes only.*"}', 1),
  (doc_id, 'pagebreak', '{}', 2),
  (doc_id, 'heading', '{"text": "1. Introduction", "level": 2}', 3),
  (doc_id, 'paragraph', '{"text": "The digitization of historical archives has created unprecedented opportunities for computational analysis \\cite{moretti2013}. Millions of pages of newspapers, letters, and official documents are now machine-readable, enabling scholars to detect patterns invisible to traditional close reading."}', 4),
  (doc_id, 'paragraph', '{"text": "This thesis develops and evaluates **text mining methods** adapted for historical materials, addressing challenges of OCR errors, language change, and genre variation \\cite{piotrowski2012}."}', 5),
  (doc_id, 'heading', '{"text": "2. Related Work", "level": 2}', 6),
  (doc_id, 'paragraph', '{"text": "Digital humanities has emerged as a field bridging computational methods and humanistic inquiry \\cite{jockers2013}. Key approaches include:"}', 7),
  (doc_id, 'list', '{"items": ["Distant reading: Statistical analysis of large corpora", "Topic modeling: Discovering thematic structure", "Network analysis: Mapping relationships", "Stylometry: Authorship attribution through style"], "ordered": false}', 8),
  (doc_id, 'pagebreak', '{}', 9),
  (doc_id, 'heading', '{"text": "3. Corpus and Methods", "level": 2}', 10),
  (doc_id, 'heading', '{"text": "3.1 Data Sources", "level": 3}', 11),
  (doc_id, 'paragraph', '{"text": "Our corpus comprises digitized materials from multiple archives:"}', 12),
  (doc_id, 'table', '{"headers": ["Source", "Type", "Date Range", "Pages", "Words"], "rows": [["Chronicling America", "Newspapers", "1836-1922", "16.5M", "4.2B"], ["Internet Archive", "Books", "1800-1924", "2.8M", "890B"], ["HathiTrust", "Mixed", "1700-1920", "17.6M", "6.1B"], ["British Newspapers", "Newspapers", "1800-1900", "8.2M", "2.1B"]]}', 13),
  (doc_id, 'heading', '{"text": "3.2 OCR Post-Correction", "level": 3}', 14),
  (doc_id, 'paragraph', '{"text": "Historical OCR is notoriously error-prone. We developed a neural correction model:"}', 15),
  (doc_id, 'code', '{"code": "from transformers import T5ForConditionalGeneration, T5Tokenizer\n\nclass OCRCorrector:\n    def __init__(self, model_path=\"historical-ocr-corrector\"):\n        self.tokenizer = T5Tokenizer.from_pretrained(model_path)\n        self.model = T5ForConditionalGeneration.from_pretrained(model_path)\n        \n    def correct(self, text, beam_size=5):\n        \"\"\"Correct OCR errors in historical text.\"\"\"\n        # Prefix signals the task\n        input_text = f\"correct: {text}\"\n        inputs = self.tokenizer(input_text, return_tensors=\"pt\", max_length=512)\n        \n        outputs = self.model.generate(\n            **inputs,\n            num_beams=beam_size,\n            max_length=512,\n            early_stopping=True\n        )\n        \n        return self.tokenizer.decode(outputs[0], skip_special_tokens=True)\n\n# Example usage\ncorrector = OCRCorrector()\nraw_ocr = \"Tlie Preiident fpoke to tlie affembled crowd\"\ncorrected = corrector.correct(raw_ocr)\n# Output: \"The President spoke to the assembled crowd\"", "language": "python"}', 16),
  (doc_id, 'heading', '{"text": "3.3 Named Entity Recognition", "level": 3}', 17),
  (doc_id, 'paragraph', '{"text": "We fine-tuned BERT for historical NER, recognizing persons, locations, organizations, and events:"}', 18),
  (doc_id, 'table', '{"headers": ["Entity Type", "Precision", "Recall", "F1"], "rows": [["PERSON", "0.91", "0.88", "0.89"], ["LOCATION", "0.93", "0.90", "0.91"], ["ORGANIZATION", "0.86", "0.82", "0.84"], ["DATE", "0.95", "0.93", "0.94"], ["EVENT", "0.78", "0.71", "0.74"]]}', 19),
  (doc_id, 'pagebreak', '{}', 20),
  (doc_id, 'heading', '{"text": "4. Topic Modeling", "level": 2}', 21),
  (doc_id, 'paragraph', '{"text": "We applied Latent Dirichlet Allocation (LDA) to discover thematic structure across the corpus:"}', 22),
  (doc_id, 'code', '{"code": "from gensim import corpora, models\nimport pyLDAvis.gensim_models\n\ndef train_lda_model(documents, num_topics=50):\n    \"\"\"Train LDA topic model on preprocessed documents.\"\"\"\n    # Create dictionary and corpus\n    dictionary = corpora.Dictionary(documents)\n    dictionary.filter_extremes(no_below=20, no_above=0.5)\n    corpus = [dictionary.doc2bow(doc) for doc in documents]\n    \n    # Train model\n    lda = models.LdaMulticore(\n        corpus=corpus,\n        id2word=dictionary,\n        num_topics=num_topics,\n        passes=15,\n        workers=4,\n        random_state=42\n    )\n    \n    return lda, corpus, dictionary", "language": "python"}', 23),
  (doc_id, 'paragraph', '{"text": "Table 3 shows selected topics from our newspaper corpus:"}', 24),
  (doc_id, 'table', '{"headers": ["Topic", "Top Terms", "Interpretation"], "rows": [["T12", "war, army, battle, general, troops", "Military conflict"], ["T23", "railway, train, station, passenger, line", "Transportation"], ["T31", "election, vote, candidate, party, ballot", "Politics"], ["T45", "price, market, wheat, cotton, trade", "Economic news"]]}', 25),
  (doc_id, 'heading', '{"text": "5. Case Study: Tracking Public Opinion", "level": 2}', 26),
  (doc_id, 'paragraph', '{"text": "We demonstrate the methods by tracking discourse around women''s suffrage from 1850-1920. Figure 1 shows topic prevalence over time."}', 27),
  (doc_id, 'figure', '{"src": "/api/placeholder/600/350", "caption": "Figure 1: Prevalence of suffrage-related topics in American newspapers, 1850-1920. Spikes correspond to major legislative events.", "alt": "Topic prevalence timeline"}', 28),
  (doc_id, 'pagebreak', '{}', 29),
  (doc_id, 'heading', '{"text": "6. Conclusion", "level": 2}', 30),
  (doc_id, 'paragraph', '{"text": "This thesis has demonstrated that computational methods can fruitfully complement traditional humanistic inquiry. Our tools enable scholars to identify patterns, track change over time, and discover unexpected connections across millions of documents. These methods do not replace close reading but rather inform and direct it."}', 31),
  (doc_id, 'pagebreak', '{}', 32),
  (doc_id, 'heading', '{"text": "References", "level": 2}', 33),
  (doc_id, 'bibliography', '{}', 34);

  INSERT INTO bibliography_entries (document_id, cite_key, entry_type, data) VALUES
  (doc_id, 'moretti2013', 'book', '{"author": "Moretti, Franco", "title": "Distant Reading", "year": "2013", "publisher": "Verso"}'),
  (doc_id, 'piotrowski2012', 'book', '{"author": "Piotrowski, Michael", "title": "Natural Language Processing for Historical Texts", "year": "2012", "publisher": "Morgan & Claypool"}'),
  (doc_id, 'jockers2013', 'book', '{"author": "Jockers, Matthew L.", "title": "Macroanalysis: Digital Methods and Literary History", "year": "2013", "publisher": "University of Illinois Press"}'),
  (doc_id, 'blei2003', 'article', '{"author": "Blei, David M. and Ng, Andrew Y. and Jordan, Michael I.", "title": "Latent Dirichlet Allocation", "journal": "Journal of Machine Learning Research", "year": "2003", "volume": "3", "pages": "993-1022"}'),
  (doc_id, 'underwood2019', 'book', '{"author": "Underwood, Ted", "title": "Distant Horizons: Digital Evidence and Literary Change", "year": "2019", "publisher": "University of Chicago Press"}');
END $$;

-- ============================================================================
-- Document 23: Environmental Economics
-- Features: equations, tables, figures
-- ============================================================================
DO $$
DECLARE
  doc_id UUID;
  user_id VARCHAR(255) := 'sample-content';
BEGIN
  INSERT INTO documents (id, owner_id, title, language, paper_size, font_family, font_size, is_public)
  VALUES (gen_random_uuid(), user_id, 'Environmental Economics: Carbon Pricing and Market Mechanisms', 'en', 'a4', 'serif', 12, TRUE)
  RETURNING id INTO doc_id;

  INSERT INTO blocks (document_id, type, content, sort_order) VALUES
  (doc_id, 'heading', '{"text": "Environmental Economics: Carbon Pricing and Market Mechanisms", "level": 1}', 0),
  (doc_id, 'abstract', '{"text": "This thesis examines economic instruments for addressing climate change, comparing carbon taxes and cap-and-trade systems. We develop theoretical models of optimal carbon pricing and empirically evaluate existing programs to assess their effectiveness and equity implications. *This is a sample document generated to demonstrate Lilia editor capabilities. The content is AI-generated for demonstration purposes only.*"}', 1),
  (doc_id, 'pagebreak', '{}', 2),
  (doc_id, 'heading', '{"text": "1. Introduction", "level": 2}', 3),
  (doc_id, 'paragraph', '{"text": "Climate change represents the largest market failure in human history \\cite{stern2007}. Carbon dioxide emissions impose costs on society that are not reflected in market prices, creating a classic negative externality. Economic theory suggests that **pricing carbon** can efficiently reduce emissions by internalizing these external costs \\cite{nordhaus2019}."}', 4),
  (doc_id, 'paragraph', '{"text": "This thesis develops frameworks for optimal carbon pricing and evaluates real-world implementations."}', 5),
  (doc_id, 'heading', '{"text": "2. Theoretical Framework", "level": 2}', 6),
  (doc_id, 'heading', '{"text": "2.1 The Externality Problem", "level": 3}', 7),
  (doc_id, 'paragraph', '{"text": "Let social cost of carbon (SCC) be the monetized damage from emitting one additional ton of CO₂:"}', 8),
  (doc_id, 'equation', '{"latex": "SCC = \\int_t^\\infty D''(M(s)) \\cdot \\frac{\\partial M(s)}{\\partial E(t)} \\cdot e^{-\\rho(s-t)} ds", "equationMode": "display", "label": "eq:scc"}', 9),
  (doc_id, 'paragraph', '{"text": "where $D(M)$ is the damage function, $M(s)$ is atmospheric concentration at time $s$, and $\\rho$ is the discount rate. Optimal emissions occur where marginal abatement cost equals SCC:"}', 10),
  (doc_id, 'equation', '{"latex": "MAC(E^*) = SCC", "equationMode": "display", "label": "eq:optimal"}', 11),
  (doc_id, 'heading', '{"text": "2.2 Policy Instruments", "level": 3}', 12),
  (doc_id, 'paragraph', '{"text": "Two main approaches achieve efficient pricing:"}', 13),
  (doc_id, 'list', '{"items": ["**Carbon tax**: Sets price, allows quantity to adjust", "**Cap-and-trade**: Sets quantity, allows price to adjust"], "ordered": false}', 14),
  (doc_id, 'paragraph', '{"text": "Under certainty about abatement costs and damages, these are equivalent. With uncertainty, Weitzman \\cite{weitzman1974} showed the choice depends on the relative slopes of marginal cost and benefit curves."}', 15),
  (doc_id, 'pagebreak', '{}', 16),
  (doc_id, 'heading', '{"text": "3. Optimal Carbon Price", "level": 2}', 17),
  (doc_id, 'paragraph', '{"text": "We calibrate an integrated assessment model to estimate optimal carbon prices:"}', 18),
  (doc_id, 'equation', '{"latex": "\\begin{align}\n\\max_{c_t, E_t} &\\sum_{t=0}^{\\infty} \\beta^t U(c_t) \\\\\n\\text{s.t. } & Y_t = F(K_t, L_t, E_t) \\cdot \\Omega(T_t) \\\\\n& T_{t+1} = \\phi_1 T_t + \\phi_2 M_t \\\\\n& M_{t+1} = (1-\\delta_M)M_t + E_t\n\\end{align}", "equationMode": "align", "label": "eq:iam"}', 19),
  (doc_id, 'paragraph', '{"text": "Table 1 shows optimal carbon prices under different assumptions:"}', 20),
  (doc_id, 'table', '{"headers": ["Scenario", "2025", "2030", "2040", "2050"], "rows": [["Baseline (ρ=3%)", "$45", "$62", "$98", "$148"], ["Low discount (ρ=1.5%)", "$85", "$115", "$175", "$265"], ["High damages", "$78", "$108", "$172", "$261"], ["2°C pathway", "$95", "$145", "$280", "$450"]]}', 21),
  (doc_id, 'heading', '{"text": "4. Empirical Analysis", "level": 2}', 22),
  (doc_id, 'heading', '{"text": "4.1 Existing Carbon Pricing Systems", "level": 3}', 23),
  (doc_id, 'paragraph', '{"text": "We evaluate performance of major carbon pricing programs:"}', 24),
  (doc_id, 'table', '{"headers": ["System", "Type", "Coverage", "2023 Price", "Emissions Impact"], "rows": [["EU ETS", "Cap-trade", "40%", "€85/t", "-35% since 2005"], ["California", "Cap-trade", "85%", "$30/t", "-14% since 2013"], ["BC Carbon Tax", "Tax", "70%", "C$65/t", "-15% since 2008"], ["Sweden", "Tax", "40%", "€120/t", "-27% since 1991"], ["China ETS", "Cap-trade", "40%", "¥60/t", "Too early"]]}', 25),
  (doc_id, 'figure', '{"src": "/api/placeholder/600/350", "caption": "Figure 1: Carbon price trajectories in major systems, 2005-2023.", "alt": "Carbon price comparison"}', 26),
  (doc_id, 'pagebreak', '{}', 27),
  (doc_id, 'heading', '{"text": "4.2 Distributional Effects", "level": 3}', 28),
  (doc_id, 'paragraph', '{"text": "Carbon pricing is often regressive, as lower-income households spend larger shares on energy. We estimate distributional impacts:"}', 29),
  (doc_id, 'table', '{"headers": ["Income Quintile", "Carbon Cost/Income", "With Dividend", "Net Impact"], "rows": [["Lowest 20%", "3.2%", "+1.8%", "-1.4%"], ["Q2", "2.4%", "+0.8%", "-1.6%"], ["Q3", "1.9%", "+0.2%", "-1.7%"], ["Q4", "1.6%", "-0.3%", "-1.9%"], ["Highest 20%", "1.1%", "-2.5%", "-3.6%"]]}', 30),
  (doc_id, 'paragraph', '{"text": "Revenue recycling through equal per-capita dividends makes the policy progressive (Figure 2)."}', 31),
  (doc_id, 'figure', '{"src": "/api/placeholder/550/300", "caption": "Figure 2: Distributional impact by income quintile with and without carbon dividend.", "alt": "Distributional impact chart"}', 32),
  (doc_id, 'heading', '{"text": "5. Conclusion", "level": 2}', 33),
  (doc_id, 'paragraph', '{"text": "This thesis has demonstrated that carbon pricing can be an effective and equitable climate policy when properly designed. Key recommendations include: setting prices consistent with the social cost of carbon, recycling revenue progressively, and coordinating internationally to prevent carbon leakage."}', 34),
  (doc_id, 'pagebreak', '{}', 35),
  (doc_id, 'heading', '{"text": "References", "level": 2}', 36),
  (doc_id, 'bibliography', '{}', 37);

  INSERT INTO bibliography_entries (document_id, cite_key, entry_type, data) VALUES
  (doc_id, 'stern2007', 'book', '{"author": "Stern, Nicholas", "title": "The Economics of Climate Change: The Stern Review", "year": "2007", "publisher": "Cambridge University Press"}'),
  (doc_id, 'nordhaus2019', 'article', '{"author": "Nordhaus, William D.", "title": "Climate Change: The Ultimate Challenge for Economics", "journal": "American Economic Review", "year": "2019", "volume": "109", "pages": "1991-2014"}'),
  (doc_id, 'weitzman1974', 'article', '{"author": "Weitzman, Martin L.", "title": "Prices vs. Quantities", "journal": "Review of Economic Studies", "year": "1974", "volume": "41", "pages": "477-491"}'),
  (doc_id, 'metcalf2019', 'article', '{"author": "Metcalf, Gilbert E.", "title": "On the Economics of a Carbon Tax for the United States", "journal": "Brookings Papers on Economic Activity", "year": "2019", "pages": "405-484"}'),
  (doc_id, 'goulder2013', 'article', '{"author": "Goulder, Lawrence H. and Schein, Andrew R.", "title": "Carbon Taxes versus Cap and Trade: A Critical Review", "journal": "Climate Change Economics", "year": "2013", "volume": "4"}');
END $$;

-- ============================================================================
-- Document 24: Medical Image Processing
-- Features: code, figures, equations
-- ============================================================================
DO $$
DECLARE
  doc_id UUID;
  user_id VARCHAR(255) := 'sample-content';
BEGIN
  INSERT INTO documents (id, owner_id, title, language, paper_size, font_family, font_size, is_public)
  VALUES (gen_random_uuid(), user_id, 'Medical Image Processing: Deep Learning for Radiology', 'en', 'a4', 'serif', 12, TRUE)
  RETURNING id INTO doc_id;

  INSERT INTO blocks (document_id, type, content, sort_order) VALUES
  (doc_id, 'heading', '{"text": "Medical Image Processing: Deep Learning for Radiology", "level": 1}', 0),
  (doc_id, 'abstract', '{"text": "This thesis develops deep learning methods for automated analysis of radiological images, focusing on segmentation, detection, and classification tasks. We present architectures optimized for 3D medical imaging and validate on multi-institutional datasets. *This is a sample document generated to demonstrate Lilia editor capabilities. The content is AI-generated for demonstration purposes only.*"}', 1),
  (doc_id, 'pagebreak', '{}', 2),
  (doc_id, 'heading', '{"text": "1. Introduction", "level": 2}', 3),
  (doc_id, 'paragraph', '{"text": "Medical imaging is central to modern clinical practice, with radiologists interpreting millions of scans annually \\cite{hosny2018}. Deep learning has demonstrated remarkable success in automated image analysis, achieving specialist-level performance on many diagnostic tasks \\cite{liu2019}."}', 4),
  (doc_id, 'paragraph', '{"text": "This thesis presents novel architectures for **3D medical image analysis**, addressing challenges of limited data, class imbalance, and interpretability."}', 5),
  (doc_id, 'heading', '{"text": "2. Methods", "level": 2}', 6),
  (doc_id, 'heading', '{"text": "2.1 U-Net Architecture", "level": 3}', 7),
  (doc_id, 'paragraph', '{"text": "We build on the U-Net architecture \\cite{ronneberger2015}, adapting it for 3D volumetric data:"}', 8),
  (doc_id, 'code', '{"code": "import torch\nimport torch.nn as nn\n\nclass UNet3D(nn.Module):\n    def __init__(self, in_channels=1, out_channels=4, init_features=32):\n        super().__init__()\n        features = init_features\n        \n        # Encoder\n        self.encoder1 = self._block(in_channels, features)\n        self.pool1 = nn.MaxPool3d(kernel_size=2, stride=2)\n        self.encoder2 = self._block(features, features * 2)\n        self.pool2 = nn.MaxPool3d(kernel_size=2, stride=2)\n        self.encoder3 = self._block(features * 2, features * 4)\n        self.pool3 = nn.MaxPool3d(kernel_size=2, stride=2)\n        \n        # Bottleneck\n        self.bottleneck = self._block(features * 4, features * 8)\n        \n        # Decoder with skip connections\n        self.upconv3 = nn.ConvTranspose3d(features * 8, features * 4, kernel_size=2, stride=2)\n        self.decoder3 = self._block(features * 8, features * 4)\n        self.upconv2 = nn.ConvTranspose3d(features * 4, features * 2, kernel_size=2, stride=2)\n        self.decoder2 = self._block(features * 4, features * 2)\n        self.upconv1 = nn.ConvTranspose3d(features * 2, features, kernel_size=2, stride=2)\n        self.decoder1 = self._block(features * 2, features)\n        \n        self.conv = nn.Conv3d(features, out_channels, kernel_size=1)\n        \n    def _block(self, in_channels, features):\n        return nn.Sequential(\n            nn.Conv3d(in_channels, features, kernel_size=3, padding=1, bias=False),\n            nn.BatchNorm3d(features),\n            nn.ReLU(inplace=True),\n            nn.Conv3d(features, features, kernel_size=3, padding=1, bias=False),\n            nn.BatchNorm3d(features),\n            nn.ReLU(inplace=True)\n        )", "language": "python"}', 9),
  (doc_id, 'pagebreak', '{}', 10),
  (doc_id, 'heading', '{"text": "2.2 Loss Functions", "level": 3}', 11),
  (doc_id, 'paragraph', '{"text": "Medical segmentation often involves severe class imbalance. We use a combination of Dice loss and focal loss:"}', 12),
  (doc_id, 'equation', '{"latex": "\\mathcal{L}_{Dice} = 1 - \\frac{2\\sum_i p_i g_i + \\epsilon}{\\sum_i p_i + \\sum_i g_i + \\epsilon}", "equationMode": "display", "label": "eq:dice"}', 13),
  (doc_id, 'equation', '{"latex": "\\mathcal{L}_{Focal} = -\\alpha(1-p_t)^\\gamma \\log(p_t)", "equationMode": "display", "label": "eq:focal"}', 14),
  (doc_id, 'paragraph', '{"text": "where $p_t$ is the predicted probability for the correct class, $\\alpha$ balances class importance, and $\\gamma$ focuses on hard examples."}', 15),
  (doc_id, 'heading', '{"text": "3. Experiments", "level": 2}', 16),
  (doc_id, 'heading', '{"text": "3.1 Brain Tumor Segmentation", "level": 3}', 17),
  (doc_id, 'paragraph', '{"text": "We evaluate on the BraTS 2023 challenge dataset. Table 1 shows performance metrics:"}', 18),
  (doc_id, 'table', '{"headers": ["Region", "Dice Score", "HD95 (mm)", "Sensitivity", "Specificity"], "rows": [["Whole Tumor", "0.912", "4.32", "0.924", "0.998"], ["Tumor Core", "0.867", "6.18", "0.881", "0.997"], ["Enhancing", "0.824", "8.45", "0.842", "0.999"], ["Average", "0.868", "6.32", "0.882", "0.998"]]}', 19),
  (doc_id, 'figure', '{"src": "/api/placeholder/600/300", "caption": "Figure 1: Example brain tumor segmentation. Left: MRI input. Middle: Ground truth. Right: Model prediction.", "alt": "Brain tumor segmentation example"}', 20),
  (doc_id, 'heading', '{"text": "3.2 Lung Nodule Detection", "level": 3}', 21),
  (doc_id, 'paragraph', '{"text": "For lung nodule detection, we use a two-stage approach: candidate generation followed by false positive reduction."}', 22),
  (doc_id, 'table', '{"headers": ["Metric", "Our Method", "Previous SOTA", "Improvement"], "rows": [["Sensitivity @1 FP/scan", "0.879", "0.842", "+3.7%"], ["Sensitivity @2 FP/scan", "0.923", "0.901", "+2.2%"], ["Sensitivity @4 FP/scan", "0.956", "0.942", "+1.4%"], ["FROC Score", "0.892", "0.871", "+2.1%"]]}', 23),
  (doc_id, 'figure', '{"src": "/api/placeholder/600/300", "caption": "Figure 2: FROC curves comparing our method to baselines on LUNA16 dataset.", "alt": "FROC comparison curves"}', 24),
  (doc_id, 'pagebreak', '{}', 25),
  (doc_id, 'heading', '{"text": "4. Discussion", "level": 2}', 26),
  (doc_id, 'paragraph', '{"text": "Our methods achieve state-of-the-art performance on multiple benchmarks. Key insights include:"}', 27),
  (doc_id, 'list', '{"items": ["3D context is critical for volumetric medical images", "Combined loss functions improve boundary delineation", "Data augmentation dramatically improves generalization", "Attention mechanisms help with variable tumor appearance"], "ordered": false}', 28),
  (doc_id, 'heading', '{"text": "5. Conclusion", "level": 2}', 29),
  (doc_id, 'paragraph', '{"text": "This thesis has demonstrated that deep learning can achieve radiologist-level performance on key imaging tasks. Our methods provide interpretable outputs suitable for clinical integration. Future work will focus on uncertainty quantification and multi-task learning across imaging modalities."}', 30),
  (doc_id, 'pagebreak', '{}', 31),
  (doc_id, 'heading', '{"text": "References", "level": 2}', 32),
  (doc_id, 'bibliography', '{}', 33);

  INSERT INTO bibliography_entries (document_id, cite_key, entry_type, data) VALUES
  (doc_id, 'hosny2018', 'article', '{"author": "Hosny, Ahmed and others", "title": "Artificial Intelligence in Radiology", "journal": "Nature Reviews Cancer", "year": "2018", "volume": "18", "pages": "500-510"}'),
  (doc_id, 'liu2019', 'article', '{"author": "Liu, Xiaoxuan and others", "title": "A Comparison of Deep Learning Performance Against Health-Care Professionals in Detecting Diseases from Medical Imaging", "journal": "The Lancet Digital Health", "year": "2019", "volume": "1", "pages": "e271-e297"}'),
  (doc_id, 'ronneberger2015', 'inproceedings', '{"author": "Ronneberger, Olaf and Fischer, Philipp and Brox, Thomas", "title": "U-Net: Convolutional Networks for Biomedical Image Segmentation", "booktitle": "MICCAI", "year": "2015"}'),
  (doc_id, 'isensee2021', 'article', '{"author": "Isensee, Fabian and others", "title": "nnU-Net: A Self-Configuring Method for Deep Learning-Based Biomedical Image Segmentation", "journal": "Nature Methods", "year": "2021", "volume": "18", "pages": "203-211"}'),
  (doc_id, 'litjens2017', 'article', '{"author": "Litjens, Geert and others", "title": "A Survey on Deep Learning in Medical Image Analysis", "journal": "Medical Image Analysis", "year": "2017", "volume": "42", "pages": "60-88"}');
END $$;

-- ============================================================================
-- Document 25: Smart City Infrastructure
-- Features: diagrams, tables, systems analysis
-- ============================================================================
DO $$
DECLARE
  doc_id UUID;
  user_id VARCHAR(255) := 'sample-content';
BEGIN
  INSERT INTO documents (id, owner_id, title, language, paper_size, font_family, font_size, is_public)
  VALUES (gen_random_uuid(), user_id, 'Smart City Infrastructure: IoT Systems and Urban Management', 'en', 'a4', 'serif', 12, TRUE)
  RETURNING id INTO doc_id;

  INSERT INTO blocks (document_id, type, content, sort_order) VALUES
  (doc_id, 'heading', '{"text": "Smart City Infrastructure: IoT Systems and Urban Management", "level": 1}', 0),
  (doc_id, 'abstract', '{"text": "This thesis examines the technological infrastructure underlying smart city initiatives, analyzing IoT sensor networks, data platforms, and governance frameworks. Through case studies of leading smart cities, we identify success factors and challenges in urban digital transformation. *This is a sample document generated to demonstrate Lilia editor capabilities. The content is AI-generated for demonstration purposes only.*"}', 1),
  (doc_id, 'pagebreak', '{}', 2),
  (doc_id, 'heading', '{"text": "1. Introduction", "level": 2}', 3),
  (doc_id, 'paragraph', '{"text": "Smart cities leverage digital technology to improve urban services, enhance sustainability, and increase citizen quality of life \\cite{kitchin2014}. The global smart city market is projected to exceed $2 trillion by 2030, yet implementations vary dramatically in scope and success."}', 4),
  (doc_id, 'paragraph', '{"text": "This thesis develops a comprehensive framework for smart city infrastructure, examining technical architecture, data governance, and citizen engagement \\cite{batty2012}."}', 5),
  (doc_id, 'heading', '{"text": "2. Architecture Framework", "level": 2}', 6),
  (doc_id, 'heading', '{"text": "2.1 Infrastructure Layers", "level": 3}', 7),
  (doc_id, 'paragraph', '{"text": "Smart city infrastructure comprises multiple interconnected layers:"}', 8),
  (doc_id, 'table', '{"headers": ["Layer", "Components", "Function"], "rows": [["Sensing", "IoT sensors, cameras, meters", "Data collection"], ["Network", "5G, LoRaWAN, fiber, Wi-Fi", "Connectivity"], ["Platform", "Cloud, edge computing, APIs", "Data processing"], ["Analytics", "ML models, dashboards", "Intelligence"], ["Application", "Apps, portals, services", "User interface"], ["Governance", "Policies, standards, privacy", "Oversight"]]}', 9),
  (doc_id, 'heading', '{"text": "2.2 Sensor Networks", "level": 3}', 10),
  (doc_id, 'paragraph', '{"text": "A typical smart city deploys diverse sensor types:"}', 11),
  (doc_id, 'table', '{"headers": ["Sensor Type", "Application", "Data Rate", "Deployment Scale"], "rows": [["Traffic sensors", "Flow monitoring", "Real-time", "1000s per city"], ["Air quality", "Pollution monitoring", "Minutes", "100s per city"], ["Smart meters", "Energy/water usage", "Hourly", "Per household"], ["Waste bins", "Fill levels", "Daily", "1000s per city"], ["Environmental", "Noise, weather", "Real-time", "100s per city"], ["Video cameras", "Security, traffic", "Continuous", "1000s per city"]]}', 12),
  (doc_id, 'figure', '{"src": "/api/placeholder/600/350", "caption": "Figure 1: Smart city architecture showing data flow from sensors through platform to applications.", "alt": "Smart city architecture diagram"}', 13),
  (doc_id, 'pagebreak', '{}', 14),
  (doc_id, 'heading', '{"text": "3. Case Studies", "level": 2}', 15),
  (doc_id, 'heading', '{"text": "3.1 Barcelona", "level": 3}', 16),
  (doc_id, 'paragraph', '{"text": "Barcelona pioneered the \"City OS\" concept, integrating multiple systems:"}', 17),
  (doc_id, 'list', '{"items": ["Smart streetlights with environmental sensors", "IoT-enabled irrigation reducing water use 25%", "Connected waste management cutting collection costs 30%", "Open data platform with 500+ datasets", "Citizen participation portal (Decidim)"], "ordered": false}', 18),
  (doc_id, 'heading', '{"text": "3.2 Singapore", "level": 3}', 19),
  (doc_id, 'paragraph', '{"text": "Singapore''s Smart Nation initiative represents comprehensive urban digitalization:"}', 20),
  (doc_id, 'table', '{"headers": ["Initiative", "Technology", "Impact"], "rows": [["Virtual Singapore", "3D city model, digital twin", "Planning simulation"], ["Smart Mobility", "Autonomous vehicles, dynamic routing", "-15% congestion"], ["HDB Smart", "Estate sensors, energy management", "-10% energy use"], ["TraceTogether", "Contact tracing", "Pandemic response"], ["GovTech", "Digital services", "99% services online"]]}', 21),
  (doc_id, 'heading', '{"text": "3.3 Amsterdam", "level": 3}', 22),
  (doc_id, 'paragraph', '{"text": "Amsterdam emphasizes citizen-centric and collaborative approaches, with over 150 smart city projects coordinated through public-private partnerships."}', 23),
  (doc_id, 'heading', '{"text": "4. Challenges", "level": 2}', 24),
  (doc_id, 'paragraph', '{"text": "Smart city implementation faces significant obstacles:"}', 25),
  (doc_id, 'list', '{"items": ["**Privacy**: Pervasive sensing raises surveillance concerns", "**Interoperability**: Proprietary systems limit integration", "**Digital divide**: Benefits may not reach all citizens", "**Cybersecurity**: Critical infrastructure vulnerabilities", "**Vendor lock-in**: Dependence on specific technology providers", "**Governance**: Unclear accountability and decision rights"], "ordered": false}', 26),
  (doc_id, 'pagebreak', '{}', 27),
  (doc_id, 'heading', '{"text": "5. Recommendations", "level": 2}', 28),
  (doc_id, 'paragraph', '{"text": "Based on our analysis, we recommend the following principles for smart city development:"}', 29),
  (doc_id, 'list', '{"items": ["Adopt open standards and APIs for interoperability", "Implement privacy-by-design principles", "Ensure equitable access across demographics and neighborhoods", "Develop robust cybersecurity frameworks", "Establish clear data governance and citizen rights", "Prioritize citizen engagement in planning and evaluation"], "ordered": true}', 30),
  (doc_id, 'heading', '{"text": "6. Conclusion", "level": 2}', 31),
  (doc_id, 'paragraph', '{"text": "Smart city infrastructure offers significant potential for improving urban life, but success requires careful attention to technical architecture, governance, and equity. The cities that thrive will be those that treat technology as a tool for serving citizens rather than an end in itself."}', 32),
  (doc_id, 'pagebreak', '{}', 33),
  (doc_id, 'heading', '{"text": "References", "level": 2}', 34),
  (doc_id, 'bibliography', '{}', 35);

  INSERT INTO bibliography_entries (document_id, cite_key, entry_type, data) VALUES
  (doc_id, 'kitchin2014', 'article', '{"author": "Kitchin, Rob", "title": "The Real-Time City? Big Data and Smart Urbanism", "journal": "GeoJournal", "year": "2014", "volume": "79", "pages": "1-14"}'),
  (doc_id, 'batty2012', 'article', '{"author": "Batty, Michael and others", "title": "Smart Cities of the Future", "journal": "European Physical Journal Special Topics", "year": "2012", "volume": "214", "pages": "481-518"}'),
  (doc_id, 'townsend2013', 'book', '{"author": "Townsend, Anthony M.", "title": "Smart Cities: Big Data, Civic Hackers, and the Quest for a New Utopia", "year": "2013", "publisher": "W.W. Norton"}'),
  (doc_id, 'greenfield2013', 'book', '{"author": "Greenfield, Adam", "title": "Against the Smart City", "year": "2013", "publisher": "Do Projects"}'),
  (doc_id, 'hollands2008', 'article', '{"author": "Hollands, Robert G.", "title": "Will the Real Smart City Please Stand Up?", "journal": "City", "year": "2008", "volume": "12", "pages": "303-320"}');
END $$;

-- ============================================================================
-- Document 26: Cognitive Science: Memory Models
-- Features: equations, figures, theoretical modeling
-- ============================================================================
DO $$
DECLARE
  doc_id UUID;
  user_id VARCHAR(255) := 'sample-content';
BEGIN
  INSERT INTO documents (id, owner_id, title, language, paper_size, font_family, font_size, is_public)
  VALUES (gen_random_uuid(), user_id, 'Cognitive Science: Computational Models of Human Memory', 'en', 'a4', 'serif', 12, TRUE)
  RETURNING id INTO doc_id;

  INSERT INTO blocks (document_id, type, content, sort_order) VALUES
  (doc_id, 'heading', '{"text": "Cognitive Science: Computational Models of Human Memory", "level": 1}', 0),
  (doc_id, 'abstract', '{"text": "This thesis develops computational models of human memory encoding, consolidation, and retrieval. We present a neural network architecture that captures key empirical phenomena and generates testable predictions about memory dynamics. *This is a sample document generated to demonstrate Lilia editor capabilities. The content is AI-generated for demonstration purposes only.*"}', 1),
  (doc_id, 'pagebreak', '{}', 2),
  (doc_id, 'heading', '{"text": "1. Introduction", "level": 2}', 3),
  (doc_id, 'paragraph', '{"text": "Human memory is not a simple recording but an active reconstructive process \\cite{schacter2012}. Computational models provide a framework for formalizing theories and generating predictions about memory function \\cite{mcclelland1995}."}', 4),
  (doc_id, 'paragraph', '{"text": "This thesis develops models that bridge cognitive psychology and neuroscience, capturing both behavioral phenomena and neural mechanisms of memory."}', 5),
  (doc_id, 'heading', '{"text": "2. Theoretical Background", "level": 2}', 6),
  (doc_id, 'heading', '{"text": "2.1 Memory Systems", "level": 3}', 7),
  (doc_id, 'paragraph', '{"text": "Multiple memory systems serve different functions \\cite{tulving1985}:"}', 8),
  (doc_id, 'list', '{"items": ["**Episodic**: Autobiographical events and contexts", "**Semantic**: General knowledge and facts", "**Procedural**: Skills and habits", "**Working**: Temporary maintenance and manipulation"], "ordered": false}', 9),
  (doc_id, 'heading', '{"text": "2.2 Encoding and Retrieval", "level": 3}', 10),
  (doc_id, 'paragraph', '{"text": "The encoding specificity principle states that retrieval depends on the match between encoding and retrieval contexts \\cite{tulving1973}. This can be formalized as:"}', 11),
  (doc_id, 'equation', '{"latex": "P(recall) = f(sim(c_{encode}, c_{retrieve}), s_{item})", "equationMode": "display", "label": "eq:encoding-specificity"}', 12),
  (doc_id, 'paragraph', '{"text": "where $c$ represents context and $s_{item}$ is item strength."}', 13),
  (doc_id, 'pagebreak', '{}', 14),
  (doc_id, 'heading', '{"text": "3. Model Architecture", "level": 2}', 15),
  (doc_id, 'heading', '{"text": "3.1 Complementary Learning Systems", "level": 3}', 16),
  (doc_id, 'paragraph', '{"text": "Following McClelland et al. \\cite{mcclelland1995}, we implement a dual-system architecture with fast hippocampal learning and slow cortical consolidation:"}', 17),
  (doc_id, 'equation', '{"latex": "\\frac{dw_{hipp}}{dt} = \\eta_{hipp} \\cdot x_i \\cdot x_j \\quad (\\eta_{hipp} \\text{ large})", "equationMode": "display", "label": "eq:hipp-learning"}', 18),
  (doc_id, 'equation', '{"latex": "\\frac{dw_{ctx}}{dt} = \\eta_{ctx} \\cdot replay(w_{hipp}) \\quad (\\eta_{ctx} \\text{ small})", "equationMode": "display", "label": "eq:ctx-learning"}', 19),
  (doc_id, 'heading', '{"text": "3.2 Context Drift Model", "level": 3}', 20),
  (doc_id, 'paragraph', '{"text": "We model temporal context as a slowly drifting representation:"}', 21),
  (doc_id, 'equation', '{"latex": "c_{t+1} = \\rho c_t + \\beta f_{in}(item_t)", "equationMode": "display", "label": "eq:context-drift"}', 22),
  (doc_id, 'paragraph', '{"text": "where $\\rho$ controls context persistence and $\\beta$ governs the influence of new items."}', 23),
  (doc_id, 'heading', '{"text": "4. Simulation Results", "level": 2}', 24),
  (doc_id, 'heading', '{"text": "4.1 Serial Position Effects", "level": 3}', 25),
  (doc_id, 'paragraph', '{"text": "The model captures classic serial position effects in free recall:"}', 26),
  (doc_id, 'figure', '{"src": "/api/placeholder/600/350", "caption": "Figure 1: Serial position curve showing primacy and recency effects. Blue: Human data. Orange: Model predictions.", "alt": "Serial position curve"}', 27),
  (doc_id, 'table', '{"headers": ["Effect", "Human Data", "Model", "R²"], "rows": [["Primacy", "0.65", "0.62", "0.94"], ["Recency", "0.78", "0.81", "0.97"], ["Asymptote", "0.42", "0.40", "0.91"], ["Overall", "-", "-", "0.95"]]}', 28),
  (doc_id, 'pagebreak', '{}', 29),
  (doc_id, 'heading', '{"text": "4.2 Spacing Effect", "level": 3}', 30),
  (doc_id, 'paragraph', '{"text": "Spaced repetitions improve long-term retention. Our model captures this through context differentiation:"}', 31),
  (doc_id, 'figure', '{"src": "/api/placeholder/550/300", "caption": "Figure 2: Retention as a function of inter-study interval. Spaced practice (solid) vs. massed practice (dashed).", "alt": "Spacing effect graph"}', 32),
  (doc_id, 'heading', '{"text": "5. Discussion", "level": 2}', 33),
  (doc_id, 'paragraph', '{"text": "Our model provides a unified account of multiple memory phenomena within a neurally plausible architecture. The complementary learning systems framework explains why the brain maintains separate fast and slow learning systems—a design constraint that modern AI systems are also beginning to adopt."}', 34),
  (doc_id, 'heading', '{"text": "6. Conclusion", "level": 2}', 35),
  (doc_id, 'paragraph', '{"text": "This thesis has demonstrated that computational modeling can bridge levels of analysis in memory research. The model captures behavioral phenomena while making contact with neural mechanisms, generating predictions for future experimental testing."}', 36),
  (doc_id, 'pagebreak', '{}', 37),
  (doc_id, 'heading', '{"text": "References", "level": 2}', 38),
  (doc_id, 'bibliography', '{}', 39);

  INSERT INTO bibliography_entries (document_id, cite_key, entry_type, data) VALUES
  (doc_id, 'schacter2012', 'article', '{"author": "Schacter, Daniel L. and others", "title": "The Future of Memory: Remembering, Imagining, and the Brain", "journal": "Neuron", "year": "2012", "volume": "76", "pages": "677-694"}'),
  (doc_id, 'mcclelland1995', 'article', '{"author": "McClelland, James L. and McNaughton, Bruce L. and O''Reilly, Randall C.", "title": "Why There Are Complementary Learning Systems in the Hippocampus and Neocortex", "journal": "Psychological Review", "year": "1995", "volume": "102", "pages": "419-457"}'),
  (doc_id, 'tulving1985', 'article', '{"author": "Tulving, Endel", "title": "How Many Memory Systems Are There?", "journal": "American Psychologist", "year": "1985", "volume": "40", "pages": "385-398"}'),
  (doc_id, 'tulving1973', 'article', '{"author": "Tulving, Endel and Thomson, Donald M.", "title": "Encoding Specificity and Retrieval Processes in Episodic Memory", "journal": "Psychological Review", "year": "1973", "volume": "80", "pages": "352-373"}'),
  (doc_id, 'howard2002', 'article', '{"author": "Howard, Marc W. and Kahana, Michael J.", "title": "A Distributed Representation of Temporal Context", "journal": "Journal of Mathematical Psychology", "year": "2002", "volume": "46", "pages": "269-299"}');
END $$;

-- ============================================================================
-- Document 27: Materials Science: Nanomaterials
-- Features: equations, figures, tables, cross-references
-- ============================================================================
DO $$
DECLARE
  doc_id UUID;
  user_id VARCHAR(255) := 'sample-content';
BEGIN
  INSERT INTO documents (id, owner_id, title, language, paper_size, font_family, font_size, is_public)
  VALUES (gen_random_uuid(), user_id, 'Nanomaterials: Synthesis, Characterization, and Applications', 'en', 'a4', 'serif', 12, TRUE)
  RETURNING id INTO doc_id;

  INSERT INTO blocks (document_id, type, content, sort_order) VALUES
  (doc_id, 'heading', '{"text": "Nanomaterials: Synthesis, Characterization, and Applications", "level": 1}', 0),
  (doc_id, 'abstract', '{"text": "This thesis investigates the synthesis and properties of novel nanomaterials, including carbon nanotubes, graphene, and metal nanoparticles. We develop new fabrication methods enabling precise control of size and morphology, and demonstrate applications in energy storage and catalysis. *This is a sample document generated to demonstrate Lilia editor capabilities. The content is AI-generated for demonstration purposes only.*"}', 1),
  (doc_id, 'pagebreak', '{}', 2),
  (doc_id, 'heading', '{"text": "1. Introduction", "level": 2}', 3),
  (doc_id, 'paragraph', '{"text": "Nanomaterials exhibit unique properties arising from their high surface-to-volume ratios and quantum confinement effects \\cite{alivisatos1996}. These properties have enabled revolutionary applications in electronics, medicine, and energy \\cite{whitesides2005}."}', 4),
  (doc_id, 'paragraph', '{"text": "This thesis develops synthesis methods for **controlled nanostructure fabrication** and investigates structure-property relationships."}', 5),
  (doc_id, 'heading', '{"text": "2. Theoretical Background", "level": 2}', 6),
  (doc_id, 'heading', '{"text": "2.1 Quantum Confinement", "level": 3}', 7),
  (doc_id, 'paragraph', '{"text": "When material dimensions approach the electron de Broglie wavelength, quantum effects become significant. For a particle in a box:"}', 8),
  (doc_id, 'equation', '{"latex": "E_n = \\frac{n^2 \\pi^2 \\hbar^2}{2mL^2}", "equationMode": "display", "label": "eq:quantum-box"}', 9),
  (doc_id, 'paragraph', '{"text": "As size $L$ decreases, energy levels increase and discrete, leading to size-tunable optical and electronic properties."}', 10),
  (doc_id, 'heading', '{"text": "2.2 Surface Energy", "level": 3}', 11),
  (doc_id, 'paragraph', '{"text": "Surface atoms have unsatisfied bonds, contributing excess energy:"}', 12),
  (doc_id, 'equation', '{"latex": "\\gamma = \\frac{1}{2}N_s \\varepsilon_{bond}", "equationMode": "display", "label": "eq:surface-energy"}', 13),
  (doc_id, 'paragraph', '{"text": "For nanoparticles, the surface energy contribution becomes substantial. The fraction of surface atoms scales as:"}', 14),
  (doc_id, 'equation', '{"latex": "f_{surface} \\approx \\frac{4r_0}{r}", "equationMode": "display", "label": "eq:surface-fraction"}', 15),
  (doc_id, 'paragraph', '{"text": "where $r_0$ is atomic radius and $r$ is particle radius."}', 16),
  (doc_id, 'pagebreak', '{}', 17),
  (doc_id, 'heading', '{"text": "3. Synthesis Methods", "level": 2}', 18),
  (doc_id, 'heading', '{"text": "3.1 Gold Nanoparticles", "level": 3}', 19),
  (doc_id, 'paragraph', '{"text": "We synthesized gold nanoparticles via the Turkevich method with modifications for size control:"}', 20),
  (doc_id, 'table', '{"headers": ["Citrate:Au Ratio", "Temperature (°C)", "Size (nm)", "PDI"], "rows": [["3:1", "100", "42 ± 5", "0.12"], ["5:1", "100", "28 ± 3", "0.08"], ["10:1", "100", "15 ± 2", "0.05"], ["10:1", "70", "20 ± 2", "0.06"]]}', 21),
  (doc_id, 'figure', '{"src": "/api/placeholder/600/300", "caption": "Figure 1: TEM images of gold nanoparticles synthesized at different citrate:Au ratios. Scale bar: 50 nm.", "alt": "Gold nanoparticle TEM images"}', 22),
  (doc_id, 'heading', '{"text": "3.2 Carbon Nanotubes", "level": 3}', 23),
  (doc_id, 'paragraph', '{"text": "Single-walled carbon nanotubes were grown by chemical vapor deposition. Chirality determines electronic properties—metallic vs. semiconducting behavior."}', 24),
  (doc_id, 'equation', '{"latex": "(n, m): \\begin{cases} \\text{metallic} & \\text{if } (n-m) \\mod 3 = 0 \\\\ \\text{semiconducting} & \\text{otherwise} \\end{cases}", "equationMode": "display", "label": "eq:cnt-chirality"}', 25),
  (doc_id, 'pagebreak', '{}', 26),
  (doc_id, 'heading', '{"text": "4. Characterization", "level": 2}', 27),
  (doc_id, 'paragraph', '{"text": "We employed multiple characterization techniques, summarized in Table 2:"}', 28),
  (doc_id, 'table', '{"headers": ["Technique", "Information Obtained", "Resolution"], "rows": [["TEM", "Morphology, size distribution", "< 1 nm"], ["XRD", "Crystal structure, grain size", "~ 1 nm"], ["UV-Vis", "Optical properties, plasmon resonance", "-"], ["Raman", "Vibrational modes, defects", "-"], ["XPS", "Surface composition, oxidation state", "~ 1 nm depth"], ["BET", "Surface area, porosity", "-"]]}', 29),
  (doc_id, 'figure', '{"src": "/api/placeholder/600/350", "caption": "Figure 2: UV-Vis spectra showing size-dependent plasmon resonance of gold nanoparticles. Peak shifts from 520 to 538 nm as size increases from 15 to 42 nm.", "alt": "UV-Vis spectra of gold nanoparticles"}', 30),
  (doc_id, 'heading', '{"text": "5. Applications", "level": 2}', 31),
  (doc_id, 'heading', '{"text": "5.1 Catalysis", "level": 3}', 32),
  (doc_id, 'paragraph', '{"text": "Gold nanoparticles show surprising catalytic activity despite bulk gold''s inertness. As referenced in Equation \\ref{eq:surface-fraction}, the high surface fraction enables efficient catalysis."}', 33),
  (doc_id, 'heading', '{"text": "6. Conclusion", "level": 2}', 34),
  (doc_id, 'paragraph', '{"text": "This thesis has demonstrated synthesis methods for size-controlled nanomaterials and established structure-property relationships relevant to practical applications. The techniques developed enable reproducible fabrication of nanomaterials with tailored properties."}', 35),
  (doc_id, 'pagebreak', '{}', 36),
  (doc_id, 'heading', '{"text": "References", "level": 2}', 37),
  (doc_id, 'bibliography', '{}', 38);

  INSERT INTO bibliography_entries (document_id, cite_key, entry_type, data) VALUES
  (doc_id, 'alivisatos1996', 'article', '{"author": "Alivisatos, A. Paul", "title": "Semiconductor Clusters, Nanocrystals, and Quantum Dots", "journal": "Science", "year": "1996", "volume": "271", "pages": "933-937"}'),
  (doc_id, 'whitesides2005', 'article', '{"author": "Whitesides, George M.", "title": "Nanoscience, Nanotechnology, and Chemistry", "journal": "Small", "year": "2005", "volume": "1", "pages": "172-179"}'),
  (doc_id, 'turkevich1951', 'article', '{"author": "Turkevich, John and others", "title": "A Study of the Nucleation and Growth Processes in the Synthesis of Colloidal Gold", "journal": "Discussions of the Faraday Society", "year": "1951", "volume": "11", "pages": "55-75"}'),
  (doc_id, 'iijima1991', 'article', '{"author": "Iijima, Sumio", "title": "Helical Microtubules of Graphitic Carbon", "journal": "Nature", "year": "1991", "volume": "354", "pages": "56-58"}'),
  (doc_id, 'novoselov2004', 'article', '{"author": "Novoselov, Konstantin S. and others", "title": "Electric Field Effect in Atomically Thin Carbon Films", "journal": "Science", "year": "2004", "volume": "306", "pages": "666-669"}');
END $$;

-- ============================================================================
-- Document 28: Computational Linguistics: NLP
-- Features: code, tables, equations
-- ============================================================================
DO $$
DECLARE
  doc_id UUID;
  user_id VARCHAR(255) := 'sample-content';
BEGIN
  INSERT INTO documents (id, owner_id, title, language, paper_size, font_family, font_size, is_public)
  VALUES (gen_random_uuid(), user_id, 'Computational Linguistics: Neural Language Models', 'en', 'a4', 'serif', 12, TRUE)
  RETURNING id INTO doc_id;

  INSERT INTO blocks (document_id, type, content, sort_order) VALUES
  (doc_id, 'heading', '{"text": "Computational Linguistics: Neural Language Models", "level": 1}', 0),
  (doc_id, 'abstract', '{"text": "This thesis investigates transformer-based language models, analyzing their linguistic capabilities and limitations. We develop interpretability methods to understand what these models learn about language structure and present novel architectures for improved syntactic generalization. *This is a sample document generated to demonstrate Lilia editor capabilities. The content is AI-generated for demonstration purposes only.*"}', 1),
  (doc_id, 'pagebreak', '{}', 2),
  (doc_id, 'heading', '{"text": "1. Introduction", "level": 2}', 3),
  (doc_id, 'paragraph', '{"text": "Large language models have achieved remarkable performance on NLP benchmarks, raising questions about the nature of their linguistic knowledge \\cite{vaswani2017}. Do these models learn genuine grammatical structure, or do they rely on surface statistical patterns?"}', 4),
  (doc_id, 'paragraph', '{"text": "This thesis develops methods for **probing linguistic representations** and identifies systematic gaps in current models'' syntactic capabilities \\cite{linzen2016}."}', 5),
  (doc_id, 'heading', '{"text": "2. Background", "level": 2}', 6),
  (doc_id, 'heading', '{"text": "2.1 Transformer Architecture", "level": 3}', 7),
  (doc_id, 'paragraph', '{"text": "The transformer model processes sequences through self-attention:"}', 8),
  (doc_id, 'equation', '{"latex": "\\text{Attention}(Q, K, V) = \\text{softmax}\\left(\\frac{QK^T}{\\sqrt{d_k}}\\right)V", "equationMode": "display", "label": "eq:attention"}', 9),
  (doc_id, 'paragraph', '{"text": "Multi-head attention allows the model to attend to information from different representation subspaces:"}', 10),
  (doc_id, 'equation', '{"latex": "\\text{MultiHead}(Q, K, V) = \\text{Concat}(\\text{head}_1, ..., \\text{head}_h)W^O", "equationMode": "display", "label": "eq:multihead"}', 11),
  (doc_id, 'heading', '{"text": "2.2 Language Modeling Objective", "level": 3}', 12),
  (doc_id, 'paragraph', '{"text": "Language models are trained to predict the next token:"}', 13),
  (doc_id, 'equation', '{"latex": "\\mathcal{L} = -\\sum_{t=1}^{T} \\log P(w_t | w_1, ..., w_{t-1}; \\theta)", "equationMode": "display", "label": "eq:lm-loss"}', 14),
  (doc_id, 'pagebreak', '{}', 15),
  (doc_id, 'heading', '{"text": "3. Probing Methods", "level": 2}', 16),
  (doc_id, 'heading', '{"text": "3.1 Structural Probes", "level": 3}', 17),
  (doc_id, 'paragraph', '{"text": "We design probes to test whether models encode syntactic structure:"}', 18),
  (doc_id, 'code', '{"code": "import torch\nimport torch.nn as nn\nfrom transformers import AutoModel, AutoTokenizer\n\nclass SyntacticProbe(nn.Module):\n    \"\"\"Probe for syntactic tree distance.\"\"\"\n    def __init__(self, hidden_size, probe_rank=128):\n        super().__init__()\n        self.proj = nn.Linear(hidden_size, probe_rank)\n        \n    def forward(self, hidden_states):\n        \"\"\"Compute pairwise distances in probe space.\"\"\"\n        projected = self.proj(hidden_states)  # (batch, seq, rank)\n        \n        # Compute squared distances\n        diff = projected.unsqueeze(2) - projected.unsqueeze(1)\n        distances = (diff ** 2).sum(dim=-1)\n        \n        return distances\n    \n    def loss(self, predicted_dist, gold_tree_dist):\n        \"\"\"L1 loss between predicted and gold tree distances.\"\"\"\n        return (predicted_dist - gold_tree_dist).abs().mean()", "language": "python"}', 19),
  (doc_id, 'heading', '{"text": "3.2 Behavioral Tests", "level": 3}', 20),
  (doc_id, 'paragraph', '{"text": "We construct minimal pairs testing specific grammatical phenomena:"}', 21),
  (doc_id, 'table', '{"headers": ["Phenomenon", "Grammatical", "Ungrammatical", "Model Accuracy"], "rows": [["Subject-verb agreement", "The keys are on the table", "The keys is on the table", "94.2%"], ["Agreement across RC", "The key that the men have is...", "The key that the men have are...", "78.4%"], ["Reflexive binding", "The queen saw herself", "The queen saw himself", "91.8%"], ["NPI licensing", "Nobody has ever seen it", "Somebody has ever seen it", "72.1%"]]}', 22),
  (doc_id, 'pagebreak', '{}', 23),
  (doc_id, 'heading', '{"text": "4. Results", "level": 2}', 24),
  (doc_id, 'heading', '{"text": "4.1 Structural Encoding", "level": 3}', 25),
  (doc_id, 'paragraph', '{"text": "Probing reveals that transformers encode tree structure implicitly. Correlation between probe distances and gold tree distances:"}', 26),
  (doc_id, 'table', '{"headers": ["Model", "Layer", "Distance Corr.", "Depth Corr."], "rows": [["BERT-base", "7", "0.82", "0.91"], ["BERT-large", "17", "0.85", "0.93"], ["GPT-2", "8", "0.78", "0.86"], ["GPT-2 Large", "24", "0.81", "0.89"]]}', 27),
  (doc_id, 'figure', '{"src": "/api/placeholder/600/350", "caption": "Figure 1: Attention patterns in BERT corresponding to syntactic dependencies. Head 8-11 shows subject-verb relations.", "alt": "Attention pattern visualization"}', 28),
  (doc_id, 'heading', '{"text": "5. Improved Architectures", "level": 2}', 29),
  (doc_id, 'paragraph', '{"text": "Based on our analysis, we propose architectural modifications to improve syntactic generalization:"}', 30),
  (doc_id, 'list', '{"items": ["Explicit tree-structured attention bias", "Syntactic pretraining objectives", "Hierarchical position encodings", "Recursive self-attention layers"], "ordered": false}', 31),
  (doc_id, 'heading', '{"text": "6. Conclusion", "level": 2}', 32),
  (doc_id, 'paragraph', '{"text": "This thesis has shown that transformer language models acquire substantial syntactic knowledge but exhibit systematic gaps in complex constructions. Architectural innovations informed by linguistic theory can improve generalization to challenging grammatical patterns."}', 33),
  (doc_id, 'pagebreak', '{}', 34),
  (doc_id, 'heading', '{"text": "References", "level": 2}', 35),
  (doc_id, 'bibliography', '{}', 36);

  INSERT INTO bibliography_entries (document_id, cite_key, entry_type, data) VALUES
  (doc_id, 'vaswani2017', 'inproceedings', '{"author": "Vaswani, Ashish and others", "title": "Attention Is All You Need", "booktitle": "NeurIPS", "year": "2017"}'),
  (doc_id, 'linzen2016', 'article', '{"author": "Linzen, Tal and others", "title": "Assessing the Ability of LSTMs to Learn Syntax-Sensitive Dependencies", "journal": "TACL", "year": "2016", "volume": "4", "pages": "521-535"}'),
  (doc_id, 'hewitt2019', 'inproceedings', '{"author": "Hewitt, John and Manning, Christopher D.", "title": "A Structural Probe for Finding Syntax in Word Representations", "booktitle": "NAACL", "year": "2019"}'),
  (doc_id, 'clark2019', 'inproceedings', '{"author": "Clark, Kevin and others", "title": "What Does BERT Look At? An Analysis of BERT''s Attention", "booktitle": "BlackboxNLP Workshop", "year": "2019"}'),
  (doc_id, 'warstadt2020', 'article', '{"author": "Warstadt, Alex and others", "title": "BLiMP: The Benchmark of Linguistic Minimal Pairs for English", "journal": "TACL", "year": "2020", "volume": "8", "pages": "377-392"}');
END $$;

-- ============================================================================
-- Document 29: Sports Analytics
-- Features: tables, statistics, figures
-- ============================================================================
DO $$
DECLARE
  doc_id UUID;
  user_id VARCHAR(255) := 'sample-content';
BEGIN
  INSERT INTO documents (id, owner_id, title, language, paper_size, font_family, font_size, is_public)
  VALUES (gen_random_uuid(), user_id, 'Sports Analytics: Statistical Modeling in Professional Basketball', 'en', 'a4', 'serif', 12, TRUE)
  RETURNING id INTO doc_id;

  INSERT INTO blocks (document_id, type, content, sort_order) VALUES
  (doc_id, 'heading', '{"text": "Sports Analytics: Statistical Modeling in Professional Basketball", "level": 1}', 0),
  (doc_id, 'abstract', '{"text": "This thesis applies advanced statistical methods to professional basketball, developing models for player evaluation, team strategy optimization, and game outcome prediction. We introduce novel metrics that better capture player impact and demonstrate applications using tracking data. *This is a sample document generated to demonstrate Lilia editor capabilities. The content is AI-generated for demonstration purposes only.*"}', 1),
  (doc_id, 'pagebreak', '{}', 2),
  (doc_id, 'heading', '{"text": "1. Introduction", "level": 2}', 3),
  (doc_id, 'paragraph', '{"text": "The sports analytics revolution has transformed how teams evaluate talent and make strategic decisions \\cite{lewis2003}. Basketball, with its continuous play and rich data streams, offers particularly fertile ground for statistical analysis \\cite{kubatko2007}."}', 4),
  (doc_id, 'paragraph', '{"text": "This thesis develops **player impact metrics** and **spatial analysis methods** using player tracking data from NBA games."}', 5),
  (doc_id, 'heading', '{"text": "2. Background", "level": 2}', 6),
  (doc_id, 'heading', '{"text": "2.1 Traditional Statistics", "level": 3}', 7),
  (doc_id, 'paragraph', '{"text": "Traditional box score statistics provide limited insight into player impact:"}', 8),
  (doc_id, 'table', '{"headers": ["Statistic", "Limitation"], "rows": [["Points", "Ignores efficiency and shot selection"], ["Rebounds", "Doesn''t distinguish contested vs. uncontested"], ["Assists", "Credit assignment is subjective"], ["Plus/Minus", "Confounded by teammate quality"]]}', 9),
  (doc_id, 'heading', '{"text": "2.2 Advanced Metrics", "level": 3}', 10),
  (doc_id, 'paragraph', '{"text": "Modern analytics has developed more sophisticated measures \\cite{oliver2004}:"}', 10),
  (doc_id, 'list', '{"items": ["**PER (Player Efficiency Rating)**: Aggregate per-minute production", "**Win Shares**: Wins attributed to player contributions", "**BPM (Box Plus/Minus)**: Box score-based estimate of plus/minus", "**RAPTOR**: Regularized adjusted plus/minus with priors"], "ordered": false}', 11),
  (doc_id, 'pagebreak', '{}', 12),
  (doc_id, 'heading', '{"text": "3. Methodology", "level": 2}', 13),
  (doc_id, 'heading', '{"text": "3.1 Regularized Plus/Minus", "level": 3}', 14),
  (doc_id, 'paragraph', '{"text": "We model point differential as a function of players on court:"}', 15),
  (doc_id, 'equation', '{"latex": "y_i = \\beta_0 + \\sum_{p=1}^{P} \\beta_p x_{ip} + \\varepsilon_i", "equationMode": "display", "label": "eq:rapm"}', 16),
  (doc_id, 'paragraph', '{"text": "where $x_{ip} \\in \\{-1, 0, +1\\}$ indicates if player $p$ is on court for home/away/neither team. Ridge regularization addresses multicollinearity:"}', 17),
  (doc_id, 'equation', '{"latex": "\\hat{\\beta} = \\arg\\min_\\beta \\sum_i (y_i - X_i\\beta)^2 + \\lambda ||\\beta||_2^2", "equationMode": "display", "label": "eq:ridge"}', 18),
  (doc_id, 'heading', '{"text": "3.2 Spatial Analysis", "level": 3}', 19),
  (doc_id, 'paragraph', '{"text": "Player tracking data enables shot location analysis. We model shooting percentage as a function of location using Gaussian process regression:"}', 20),
  (doc_id, 'equation', '{"latex": "f(x) \\sim \\mathcal{GP}(m(x), k(x, x''))", "equationMode": "display", "label": "eq:gp"}', 21),
  (doc_id, 'figure', '{"src": "/api/placeholder/600/350", "caption": "Figure 1: Shot chart showing expected vs. actual shooting percentage for a sample player. Red indicates above-average efficiency.", "alt": "Player shot chart"}', 22),
  (doc_id, 'pagebreak', '{}', 23),
  (doc_id, 'heading', '{"text": "4. Results", "level": 2}', 24),
  (doc_id, 'heading', '{"text": "4.1 Player Rankings", "level": 3}', 25),
  (doc_id, 'paragraph', '{"text": "Table 2 shows top players by our regularized plus/minus metric (2023 season):"}', 26),
  (doc_id, 'table', '{"headers": ["Rank", "Player", "Team", "RAPM", "Traditional +/-"], "rows": [["1", "Player A", "DEN", "+7.2", "+12.4"], ["2", "Player B", "MIL", "+6.8", "+8.9"], ["3", "Player C", "BOS", "+5.9", "+11.2"], ["4", "Player D", "PHI", "+5.4", "+6.8"], ["5", "Player E", "PHX", "+5.1", "+4.2"]]}', 27),
  (doc_id, 'heading', '{"text": "4.2 Team Strategy", "level": 3}', 28),
  (doc_id, 'paragraph', '{"text": "Spatial analysis reveals optimal shot selection strategies:"}', 29),
  (doc_id, 'table', '{"headers": ["Shot Zone", "League Avg eFG%", "Points per Shot", "Optimal %"], "rows": [["Restricted Area", "63.2%", "1.26", "35%"], ["Paint (non-RA)", "40.1%", "0.80", "5%"], ["Mid-Range", "41.8%", "0.84", "10%"], ["Corner 3", "39.2%", "1.18", "15%"], ["Above Break 3", "36.4%", "1.09", "35%"]]}', 30),
  (doc_id, 'figure', '{"src": "/api/placeholder/550/300", "caption": "Figure 2: Evolution of shot distribution in the NBA, 2000-2023. Three-point attempts have nearly tripled.", "alt": "Shot distribution trends"}', 31),
  (doc_id, 'heading', '{"text": "5. Discussion", "level": 2}', 32),
  (doc_id, 'paragraph', '{"text": "Our analysis reveals several insights for team strategy:"}', 32),
  (doc_id, 'list', '{"items": ["Mid-range shots are generally inefficient unless shooter-specific data indicates otherwise", "Lineup optimization can add 2-4 wins per season", "Player tracking enables more accurate credit assignment than box scores", "Defensive impact remains harder to quantify than offensive contribution"], "ordered": false}', 33),
  (doc_id, 'heading', '{"text": "6. Conclusion", "level": 2}', 34),
  (doc_id, 'paragraph', '{"text": "This thesis has demonstrated how advanced statistical methods can improve player evaluation and strategic decision-making in professional basketball. As tracking data becomes more detailed, opportunities for analytical insight continue to expand."}', 35),
  (doc_id, 'pagebreak', '{}', 36),
  (doc_id, 'heading', '{"text": "References", "level": 2}', 37),
  (doc_id, 'bibliography', '{}', 38);

  INSERT INTO bibliography_entries (document_id, cite_key, entry_type, data) VALUES
  (doc_id, 'lewis2003', 'book', '{"author": "Lewis, Michael", "title": "Moneyball: The Art of Winning an Unfair Game", "year": "2003", "publisher": "W.W. Norton"}'),
  (doc_id, 'kubatko2007', 'article', '{"author": "Kubatko, Justin and others", "title": "A Starting Point for Analyzing Basketball Statistics", "journal": "Journal of Quantitative Analysis in Sports", "year": "2007", "volume": "3"}'),
  (doc_id, 'oliver2004', 'book', '{"author": "Oliver, Dean", "title": "Basketball on Paper: Rules and Tools for Performance Analysis", "year": "2004", "publisher": "Potomac Books"}'),
  (doc_id, 'engelmann2017', 'article', '{"author": "Engelmann, Jacob", "title": "Possession-Based Player Performance Analysis in Basketball", "journal": "MIT Sloan Sports Analytics Conference", "year": "2017"}'),
  (doc_id, 'cervone2016', 'article', '{"author": "Cervone, Dan and others", "title": "A Multiresolution Stochastic Process Model for Predicting Basketball Possession Outcomes", "journal": "Journal of the American Statistical Association", "year": "2016", "volume": "111", "pages": "585-599"}');
END $$;

-- ============================================================================
-- Document 30: Music Information Retrieval
-- Features: equations, code, figures
-- ============================================================================
DO $$
DECLARE
  doc_id UUID;
  user_id VARCHAR(255) := 'sample-content';
BEGIN
  INSERT INTO documents (id, owner_id, title, language, paper_size, font_family, font_size, is_public)
  VALUES (gen_random_uuid(), user_id, 'Music Information Retrieval: Deep Learning for Audio Analysis', 'en', 'a4', 'serif', 12, TRUE)
  RETURNING id INTO doc_id;

  INSERT INTO blocks (document_id, type, content, sort_order) VALUES
  (doc_id, 'heading', '{"text": "Music Information Retrieval: Deep Learning for Audio Analysis", "level": 1}', 0),
  (doc_id, 'abstract', '{"text": "This thesis develops deep learning methods for music information retrieval, addressing tasks including genre classification, source separation, and music generation. We present architectures optimized for audio spectrograms and evaluate on standard benchmarks. *This is a sample document generated to demonstrate Lilia editor capabilities. The content is AI-generated for demonstration purposes only.*"}', 1),
  (doc_id, 'pagebreak', '{}', 2),
  (doc_id, 'heading', '{"text": "1. Introduction", "level": 2}', 3),
  (doc_id, 'paragraph', '{"text": "Music information retrieval (MIR) aims to automatically analyze and organize music \\cite{downie2003}. Deep learning has dramatically improved performance on MIR tasks, enabling applications from recommendation systems to automatic transcription \\cite{humphrey2012}."}', 4),
  (doc_id, 'paragraph', '{"text": "This thesis develops neural architectures for **audio representation learning** and demonstrates applications across multiple MIR tasks."}', 5),
  (doc_id, 'heading', '{"text": "2. Audio Representations", "level": 2}', 6),
  (doc_id, 'heading', '{"text": "2.1 Spectrogram Computation", "level": 3}', 7),
  (doc_id, 'paragraph', '{"text": "Audio is typically converted to time-frequency representations. The Short-Time Fourier Transform (STFT) is:"}', 8),
  (doc_id, 'equation', '{"latex": "X(m, k) = \\sum_{n=0}^{N-1} x(n + mH) w(n) e^{-j2\\pi kn/N}", "equationMode": "display", "label": "eq:stft"}', 9),
  (doc_id, 'paragraph', '{"text": "where $m$ is frame index, $k$ is frequency bin, $H$ is hop size, and $w(n)$ is the window function."}', 10),
  (doc_id, 'heading', '{"text": "2.2 Mel Spectrogram", "level": 3}', 11),
  (doc_id, 'paragraph', '{"text": "Mel-scale filtering models human auditory perception:"}', 12),
  (doc_id, 'equation', '{"latex": "\\text{mel}(f) = 2595 \\log_{10}\\left(1 + \\frac{f}{700}\\right)", "equationMode": "display", "label": "eq:mel"}', 13),
  (doc_id, 'paragraph', '{"text": "The resulting mel spectrogram is commonly used as CNN input."}', 14),
  (doc_id, 'pagebreak', '{}', 15),
  (doc_id, 'heading', '{"text": "3. Architecture", "level": 2}', 16),
  (doc_id, 'heading', '{"text": "3.1 Audio Encoder", "level": 3}', 17),
  (doc_id, 'paragraph', '{"text": "We implement a convolutional encoder for mel spectrograms:"}', 18),
  (doc_id, 'code', '{"code": "import torch\nimport torch.nn as nn\nimport torchaudio.transforms as T\n\nclass AudioEncoder(nn.Module):\n    def __init__(self, n_mels=128, sample_rate=22050, embedding_dim=512):\n        super().__init__()\n        \n        # Mel spectrogram transform\n        self.mel_spec = T.MelSpectrogram(\n            sample_rate=sample_rate,\n            n_fft=2048,\n            hop_length=512,\n            n_mels=n_mels\n        )\n        self.amplitude_to_db = T.AmplitudeToDB()\n        \n        # Convolutional backbone\n        self.conv_layers = nn.Sequential(\n            nn.Conv2d(1, 64, kernel_size=3, padding=1),\n            nn.BatchNorm2d(64),\n            nn.ReLU(),\n            nn.MaxPool2d(2),\n            nn.Conv2d(64, 128, kernel_size=3, padding=1),\n            nn.BatchNorm2d(128),\n            nn.ReLU(),\n            nn.MaxPool2d(2),\n            nn.Conv2d(128, 256, kernel_size=3, padding=1),\n            nn.BatchNorm2d(256),\n            nn.ReLU(),\n            nn.AdaptiveAvgPool2d((1, 1))\n        )\n        \n        self.fc = nn.Linear(256, embedding_dim)\n    \n    def forward(self, waveform):\n        # Compute mel spectrogram\n        mel = self.mel_spec(waveform)\n        mel_db = self.amplitude_to_db(mel)\n        \n        # Add channel dimension\n        mel_db = mel_db.unsqueeze(1)\n        \n        # Extract features\n        features = self.conv_layers(mel_db)\n        features = features.view(features.size(0), -1)\n        \n        return self.fc(features)", "language": "python"}', 19),
  (doc_id, 'pagebreak', '{}', 20),
  (doc_id, 'heading', '{"text": "4. Experiments", "level": 2}', 21),
  (doc_id, 'heading', '{"text": "4.1 Genre Classification", "level": 3}', 22),
  (doc_id, 'paragraph', '{"text": "We evaluate on the GTZAN dataset (10 genres, 100 tracks each):"}', 23),
  (doc_id, 'table', '{"headers": ["Model", "Accuracy", "Precision", "Recall", "F1"], "rows": [["SVM + MFCC", "61.2%", "0.60", "0.61", "0.60"], ["CNN (mel)", "78.4%", "0.78", "0.78", "0.78"], ["ResNet-18", "82.1%", "0.82", "0.82", "0.82"], ["Ours (attention)", "85.7%", "0.86", "0.86", "0.86"]]}', 24),
  (doc_id, 'figure', '{"src": "/api/placeholder/600/350", "caption": "Figure 1: Confusion matrix for 10-genre classification. Classical and jazz show highest confusion.", "alt": "Genre classification confusion matrix"}', 25),
  (doc_id, 'heading', '{"text": "4.2 Source Separation", "level": 3}', 26),
  (doc_id, 'paragraph', '{"text": "We evaluate music source separation on the MUSDB18 dataset:"}', 27),
  (doc_id, 'table', '{"headers": ["Source", "SDR (dB)", "SIR (dB)", "SAR (dB)"], "rows": [["Vocals", "6.42", "14.21", "6.98"], ["Drums", "5.89", "11.84", "6.45"], ["Bass", "5.12", "9.76", "5.78"], ["Other", "4.23", "8.12", "5.02"], ["Average", "5.42", "10.98", "6.06"]]}', 28),
  (doc_id, 'heading', '{"text": "4.3 Music Generation", "level": 3}', 29),
  (doc_id, 'paragraph', '{"text": "We demonstrate unconditional music generation using a transformer decoder trained on MIDI sequences. Human evaluators rated generated samples:"}', 30),
  (doc_id, 'figure', '{"src": "/api/placeholder/550/300", "caption": "Figure 2: Human evaluation of generated music on musicality, coherence, and novelty (1-5 scale).", "alt": "Music generation evaluation"}', 31),
  (doc_id, 'heading', '{"text": "5. Conclusion", "level": 2}', 32),
  (doc_id, 'paragraph', '{"text": "This thesis has presented deep learning approaches for multiple music information retrieval tasks. Our audio encoder provides effective representations for classification, and our generation models produce musically coherent outputs. These methods enable practical applications in music recommendation, production assistance, and creative tools."}', 33),
  (doc_id, 'pagebreak', '{}', 34),
  (doc_id, 'heading', '{"text": "References", "level": 2}', 35),
  (doc_id, 'bibliography', '{}', 36);

  INSERT INTO bibliography_entries (document_id, cite_key, entry_type, data) VALUES
  (doc_id, 'downie2003', 'article', '{"author": "Downie, J. Stephen", "title": "Music Information Retrieval", "journal": "Annual Review of Information Science and Technology", "year": "2003", "volume": "37", "pages": "295-340"}'),
  (doc_id, 'humphrey2012', 'article', '{"author": "Humphrey, Eric J. and others", "title": "Feature Learning and Deep Architectures: New Directions for Music Informatics", "journal": "Journal of Intelligent Information Systems", "year": "2012", "volume": "41", "pages": "461-481"}'),
  (doc_id, 'mcfee2015', 'inproceedings', '{"author": "McFee, Brian and others", "title": "librosa: Audio and Music Signal Analysis in Python", "booktitle": "Proceedings of the 14th Python in Science Conference", "year": "2015"}'),
  (doc_id, 'stoller2018', 'inproceedings', '{"author": "Stöller, Daniel and others", "title": "Wave-U-Net: A Multi-Scale Neural Network for End-to-End Audio Source Separation", "booktitle": "ISMIR", "year": "2018"}'),
  (doc_id, 'dhariwal2020', 'article', '{"author": "Dhariwal, Prafulla and others", "title": "Jukebox: A Generative Model for Music", "journal": "arXiv preprint", "year": "2020"}');
END $$;

-- ============================================================================
-- Final verification query for all batches
-- ============================================================================
-- Run this to verify all 30 documents were created:
-- SELECT d.title, COUNT(DISTINCT b.id) as block_count, COUNT(DISTINCT be.id) as citation_count
-- FROM documents d
-- LEFT JOIN blocks b ON d.id = b.document_id
-- LEFT JOIN bibliography_entries be ON d.id = be.document_id
-- WHERE d.owner_id = 'sample-content'
-- GROUP BY d.id, d.title
-- ORDER BY d.created_at;

-- Summary statistics:
-- SELECT
--   COUNT(*) as total_documents,
--   SUM(block_count) as total_blocks,
--   SUM(citation_count) as total_citations
-- FROM (
--   SELECT d.id, COUNT(DISTINCT b.id) as block_count, COUNT(DISTINCT be.id) as citation_count
--   FROM documents d
--   LEFT JOIN blocks b ON d.id = b.document_id
--   LEFT JOIN bibliography_entries be ON d.id = be.document_id
--   WHERE d.owner_id = 'sample-content'
--   GROUP BY d.id
-- ) sub;

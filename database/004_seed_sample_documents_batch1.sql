-- Batch 1: STEM Sample Thesis Documents (1-10)
-- 10 AI-generated sample documents showcasing Lilia editor features

-- First, ensure the sample-content user exists
INSERT INTO users (id, email, name, created_at)
VALUES ('sample-content', 'sample@lilia.app', 'Sample Content', NOW())
ON CONFLICT (id) DO NOTHING;

-- ============================================================================
-- Document 1: Quantum Computing Fundamentals
-- Features: equations, theorems, definitions, proofs, cross-references
-- ============================================================================
DO $$
DECLARE
  doc_id UUID;
  user_id VARCHAR(255) := 'sample-content';
BEGIN
  INSERT INTO documents (id, owner_id, title, language, paper_size, font_family, font_size, is_public)
  VALUES (gen_random_uuid(), user_id, 'Quantum Computing Fundamentals', 'en', 'a4', 'serif', 12, TRUE)
  RETURNING id INTO doc_id;

  INSERT INTO blocks (document_id, type, content, sort_order) VALUES
  (doc_id, 'heading', '{"text": "Quantum Computing Fundamentals", "level": 1}', 0),
  (doc_id, 'abstract', '{"text": "This thesis provides a comprehensive introduction to the fundamental principles of quantum computing, exploring the mathematical foundations, key algorithms, and potential applications of this emerging paradigm. We examine quantum bits (qubits), quantum gates, and quantum circuits, establishing the theoretical framework necessary for understanding quantum computational advantage. *This is a sample document generated to demonstrate Lilia editor capabilities. The content is AI-generated for demonstration purposes only.*"}', 1),
  (doc_id, 'pagebreak', '{}', 2),
  (doc_id, 'heading', '{"text": "1. Introduction", "level": 2}', 3),
  (doc_id, 'paragraph', '{"text": "Quantum computing represents a paradigm shift in computational theory, leveraging the principles of quantum mechanics to process information in fundamentally new ways \\cite{nielsen2000}. Unlike classical computers that operate on binary bits, quantum computers utilize **quantum bits** or *qubits*, which can exist in superposition states, enabling massive parallelism in computation."}', 4),
  (doc_id, 'paragraph', '{"text": "The field has gained significant momentum since Feynman''s 1982 proposal for quantum simulation \\cite{feynman1982} and Shor''s groundbreaking factoring algorithm \\cite{shor1994}. Today, quantum computing stands at the threshold of practical applications in cryptography, optimization, and scientific simulation."}', 5),
  (doc_id, 'heading', '{"text": "2. Literature Review", "level": 2}', 6),
  (doc_id, 'paragraph', '{"text": "The theoretical foundations of quantum computing were established through seminal works by Deutsch \\cite{deutsch1985}, who formalized the concept of a quantum Turing machine, and later by Bernstein and Vazirani \\cite{bernstein1997}, who developed the complexity-theoretic framework for quantum computation."}', 7),
  (doc_id, 'paragraph', '{"text": "Recent advances have demonstrated quantum supremacy \\cite{arute2019}, where quantum processors performed calculations intractable for classical supercomputers. This milestone validates decades of theoretical work and opens new avenues for practical quantum applications."}', 8),
  (doc_id, 'pagebreak', '{}', 9),
  (doc_id, 'heading', '{"text": "3. Mathematical Foundations", "level": 2}', 10),
  (doc_id, 'heading', '{"text": "3.1 Quantum State Representation", "level": 3}', 11),
  (doc_id, 'paragraph', '{"text": "A qubit exists in a two-dimensional complex Hilbert space $\\mathcal{H}^2$. The computational basis states are denoted $|0\\rangle$ and $|1\\rangle$, and a general qubit state can be written as:"}', 12),
  (doc_id, 'equation', '{"latex": "|\\psi\\rangle = \\alpha|0\\rangle + \\beta|1\\rangle", "equationMode": "display", "label": "eq:qubit"}', 13),
  (doc_id, 'paragraph', '{"text": "where $\\alpha, \\beta \\in \\mathbb{C}$ and $|\\alpha|^2 + |\\beta|^2 = 1$ (normalization condition). This can be geometrically represented on the Bloch sphere as shown in Equation \\ref{eq:qubit}."}', 14),
  (doc_id, 'theorem', '{"theoremType": "definition", "title": "Qubit", "text": "A **qubit** is a quantum mechanical two-level system that serves as the basic unit of quantum information. Unlike classical bits, qubits can exist in coherent superpositions of the basis states $|0\\rangle$ and $|1\\rangle$.", "label": "def:qubit"}', 15),
  (doc_id, 'heading', '{"text": "3.2 Quantum Gates", "level": 3}', 16),
  (doc_id, 'paragraph', '{"text": "Quantum gates are unitary operators that transform qubit states. The most fundamental single-qubit gates include the Pauli matrices:"}', 17),
  (doc_id, 'equation', '{"latex": "X = \\begin{pmatrix} 0 & 1 \\\\ 1 & 0 \\end{pmatrix}, \\quad Y = \\begin{pmatrix} 0 & -i \\\\ i & 0 \\end{pmatrix}, \\quad Z = \\begin{pmatrix} 1 & 0 \\\\ 0 & -1 \\end{pmatrix}", "equationMode": "display", "label": "eq:pauli"}', 18),
  (doc_id, 'paragraph', '{"text": "The Hadamard gate, essential for creating superposition states, is defined as:"}', 19),
  (doc_id, 'equation', '{"latex": "H = \\frac{1}{\\sqrt{2}}\\begin{pmatrix} 1 & 1 \\\\ 1 & -1 \\end{pmatrix}", "equationMode": "display", "label": "eq:hadamard"}', 20),
  (doc_id, 'theorem', '{"theoremType": "theorem", "title": "Universal Gate Set", "text": "The set $\\{H, T, \\text{CNOT}\\}$ forms a universal gate set for quantum computation. Any unitary operation can be approximated to arbitrary precision using a finite sequence of these gates.", "label": "thm:universal"}', 21),
  (doc_id, 'theorem', '{"theoremType": "proof", "title": "", "text": "The proof follows from the Solovay-Kitaev theorem, which establishes that any single-qubit unitary can be efficiently approximated by products of $H$ and $T$ gates. Combined with CNOT for two-qubit entanglement, this enables universal computation. $\\square$", "label": ""}', 22),
  (doc_id, 'pagebreak', '{}', 23),
  (doc_id, 'heading', '{"text": "4. Quantum Algorithms", "level": 2}', 24),
  (doc_id, 'heading', '{"text": "4.1 Grover''s Search Algorithm", "level": 3}', 25),
  (doc_id, 'paragraph', '{"text": "Grover''s algorithm provides a quadratic speedup for unstructured search problems. Given a function $f: \\{0,1\\}^n \\rightarrow \\{0,1\\}$ with a unique solution $x^*$ such that $f(x^*) = 1$, the algorithm finds $x^*$ in $O(\\sqrt{N})$ queries, where $N = 2^n$."}', 26),
  (doc_id, 'theorem', '{"theoremType": "lemma", "title": "Grover Iteration", "text": "A single Grover iteration rotates the state vector by angle $2\\theta$ toward the target state, where $\\sin\\theta = 1/\\sqrt{N}$.", "label": "lem:grover"}', 27),
  (doc_id, 'paragraph', '{"text": "The optimal number of iterations is approximately:"}', 28),
  (doc_id, 'equation', '{"latex": "k_{\\text{opt}} = \\left\\lfloor \\frac{\\pi}{4}\\sqrt{N} \\right\\rfloor", "equationMode": "display", "label": "eq:grover-iter"}', 29),
  (doc_id, 'heading', '{"text": "4.2 Quantum Fourier Transform", "level": 3}', 30),
  (doc_id, 'paragraph', '{"text": "The Quantum Fourier Transform (QFT) is central to many quantum algorithms, including Shor''s factoring algorithm. The QFT maps computational basis states according to:"}', 31),
  (doc_id, 'equation', '{"latex": "\\text{QFT}|j\\rangle = \\frac{1}{\\sqrt{N}}\\sum_{k=0}^{N-1} e^{2\\pi ijk/N}|k\\rangle", "equationMode": "display", "label": "eq:qft"}', 32),
  (doc_id, 'list', '{"items": ["QFT requires only $O(n^2)$ gates for $n$ qubits", "Classical FFT requires $O(n \\cdot 2^n)$ operations", "Exponential speedup enables efficient period finding"], "ordered": false}', 33),
  (doc_id, 'heading', '{"text": "5. Results and Discussion", "level": 2}', 34),
  (doc_id, 'paragraph', '{"text": "Table 1 summarizes the computational complexity of key quantum algorithms compared to their classical counterparts."}', 35),
  (doc_id, 'table', '{"headers": ["Algorithm", "Classical Complexity", "Quantum Complexity", "Speedup"], "rows": [["Unstructured Search", "$O(N)$", "$O(\\sqrt{N})$", "Quadratic"], ["Integer Factoring", "$O(e^{n^{1/3}})$", "$O(n^3)$", "Exponential"], ["Simulation", "$O(2^n)$", "$O(n^4)$", "Exponential"], ["Linear Systems", "$O(N^3)$", "$O(\\log N)$", "Exponential"]]}', 36),
  (doc_id, 'paragraph', '{"text": "These results demonstrate that quantum computing offers significant advantages for specific problem classes. As referenced in Theorem \\ref{thm:universal}, the universality of quantum gates ensures these algorithms can be implemented on any sufficiently capable quantum processor."}', 37),
  (doc_id, 'pagebreak', '{}', 38),
  (doc_id, 'heading', '{"text": "6. Conclusion", "level": 2}', 39),
  (doc_id, 'paragraph', '{"text": "This thesis has established the fundamental principles of quantum computing, from the mathematical representation of qubits (Definition \\ref{def:qubit}) through quantum gates and algorithms. The exponential speedups demonstrated in Table 1 highlight the transformative potential of quantum computation."}', 40),
  (doc_id, 'paragraph', '{"text": "Future work will focus on error correction techniques and the implementation of fault-tolerant quantum circuits, essential steps toward practical quantum computers capable of solving real-world problems beyond the reach of classical computation."}', 41),
  (doc_id, 'pagebreak', '{}', 42),
  (doc_id, 'heading', '{"text": "References", "level": 2}', 43),
  (doc_id, 'bibliography', '{}', 44);

  INSERT INTO bibliography_entries (document_id, cite_key, entry_type, data) VALUES
  (doc_id, 'nielsen2000', 'book', '{"author": "Nielsen, Michael A. and Chuang, Isaac L.", "title": "Quantum Computation and Quantum Information", "year": "2000", "publisher": "Cambridge University Press"}'),
  (doc_id, 'feynman1982', 'article', '{"author": "Feynman, Richard P.", "title": "Simulating Physics with Computers", "journal": "International Journal of Theoretical Physics", "year": "1982", "volume": "21", "pages": "467-488"}'),
  (doc_id, 'shor1994', 'inproceedings', '{"author": "Shor, Peter W.", "title": "Algorithms for Quantum Computation: Discrete Logarithms and Factoring", "booktitle": "Proceedings 35th Annual Symposium on Foundations of Computer Science", "year": "1994", "pages": "124-134"}'),
  (doc_id, 'deutsch1985', 'article', '{"author": "Deutsch, David", "title": "Quantum Theory, the Church-Turing Principle and the Universal Quantum Computer", "journal": "Proceedings of the Royal Society A", "year": "1985", "volume": "400", "pages": "97-117"}'),
  (doc_id, 'bernstein1997', 'article', '{"author": "Bernstein, Ethan and Vazirani, Umesh", "title": "Quantum Complexity Theory", "journal": "SIAM Journal on Computing", "year": "1997", "volume": "26", "pages": "1411-1473"}'),
  (doc_id, 'arute2019', 'article', '{"author": "Arute, Frank and others", "title": "Quantum Supremacy Using a Programmable Superconducting Processor", "journal": "Nature", "year": "2019", "volume": "574", "pages": "505-510"}');
END $$;

-- ============================================================================
-- Document 2: Machine Learning in Medical Diagnosis
-- Features: tables, figures, code blocks, lists, citations
-- ============================================================================
DO $$
DECLARE
  doc_id UUID;
  user_id VARCHAR(255) := 'sample-content';
BEGIN
  INSERT INTO documents (id, owner_id, title, language, paper_size, font_family, font_size, is_public)
  VALUES (gen_random_uuid(), user_id, 'Machine Learning in Medical Diagnosis', 'en', 'a4', 'serif', 12, TRUE)
  RETURNING id INTO doc_id;

  INSERT INTO blocks (document_id, type, content, sort_order) VALUES
  (doc_id, 'heading', '{"text": "Machine Learning in Medical Diagnosis", "level": 1}', 0),
  (doc_id, 'abstract', '{"text": "This thesis investigates the application of machine learning algorithms for automated medical diagnosis, focusing on deep learning approaches for medical image analysis. We present a comprehensive framework for disease classification using convolutional neural networks and evaluate performance across multiple diagnostic tasks. *This is a sample document generated to demonstrate Lilia editor capabilities. The content is AI-generated for demonstration purposes only.*"}', 1),
  (doc_id, 'pagebreak', '{}', 2),
  (doc_id, 'heading', '{"text": "1. Introduction", "level": 2}', 3),
  (doc_id, 'paragraph', '{"text": "The integration of machine learning into healthcare represents one of the most promising applications of artificial intelligence \\cite{topol2019}. Medical diagnosis, traditionally dependent on human expertise, can be enhanced through algorithms capable of processing vast amounts of clinical data with consistent accuracy."}', 4),
  (doc_id, 'paragraph', '{"text": "Deep learning, particularly **convolutional neural networks** (CNNs), has demonstrated remarkable success in medical imaging tasks \\cite{litjens2017}. These models can identify subtle patterns in radiological images that may escape human observation, potentially improving early detection rates for various conditions."}', 5),
  (doc_id, 'heading', '{"text": "2. Literature Review", "level": 2}', 6),
  (doc_id, 'paragraph', '{"text": "The application of neural networks to medical diagnosis dates back to the 1990s \\cite{baxt1995}, but recent advances in computational power and data availability have catalyzed rapid progress. Esteva et al. \\cite{esteva2017} demonstrated dermatologist-level classification of skin cancer using deep learning, while Rajpurkar et al. \\cite{rajpurkar2017} achieved radiologist-level pneumonia detection from chest X-rays."}', 7),
  (doc_id, 'list', '{"items": ["Image classification for disease detection", "Segmentation for tumor localization", "Object detection for anatomical structures", "Time-series analysis for patient monitoring"], "ordered": false}', 8),
  (doc_id, 'pagebreak', '{}', 9),
  (doc_id, 'heading', '{"text": "3. Methodology", "level": 2}', 10),
  (doc_id, 'heading', '{"text": "3.1 Dataset Description", "level": 3}', 11),
  (doc_id, 'paragraph', '{"text": "Our study utilizes multiple publicly available medical imaging datasets. Table 1 summarizes the datasets employed in this research."}', 12),
  (doc_id, 'table', '{"headers": ["Dataset", "Modality", "Classes", "Images", "Resolution"], "rows": [["ChestX-ray14", "X-ray", "14", "112,120", "1024×1024"], ["ISIC 2019", "Dermoscopy", "8", "25,331", "Variable"], ["BraTS 2020", "MRI", "4", "369", "240×240×155"], ["Diabetic Retinopathy", "Fundus", "5", "88,702", "Variable"]]}', 13),
  (doc_id, 'heading', '{"text": "3.2 Model Architecture", "level": 3}', 14),
  (doc_id, 'paragraph', '{"text": "We employ a modified ResNet-50 architecture with attention mechanisms. The following code demonstrates our model definition in PyTorch:"}', 15),
  (doc_id, 'code', '{"code": "import torch\nimport torch.nn as nn\nfrom torchvision.models import resnet50\n\nclass MedicalDiagnosisModel(nn.Module):\n    def __init__(self, num_classes, pretrained=True):\n        super().__init__()\n        self.backbone = resnet50(pretrained=pretrained)\n        self.backbone.fc = nn.Identity()\n        \n        # Attention module\n        self.attention = nn.Sequential(\n            nn.Linear(2048, 512),\n            nn.ReLU(),\n            nn.Linear(512, 2048),\n            nn.Sigmoid()\n        )\n        \n        # Classification head\n        self.classifier = nn.Sequential(\n            nn.Dropout(0.5),\n            nn.Linear(2048, num_classes)\n        )\n    \n    def forward(self, x):\n        features = self.backbone(x)\n        attention_weights = self.attention(features)\n        weighted_features = features * attention_weights\n        return self.classifier(weighted_features)", "language": "python"}', 16),
  (doc_id, 'heading', '{"text": "3.3 Training Procedure", "level": 3}', 17),
  (doc_id, 'paragraph', '{"text": "The model was trained using the following hyperparameters and data augmentation pipeline:"}', 18),
  (doc_id, 'code', '{"code": "# Training configuration\nconfig = {\n    \"learning_rate\": 1e-4,\n    \"batch_size\": 32,\n    \"epochs\": 100,\n    \"optimizer\": \"AdamW\",\n    \"weight_decay\": 1e-5,\n    \"scheduler\": \"CosineAnnealingLR\"\n}\n\n# Data augmentation\ntransform = transforms.Compose([\n    transforms.RandomResizedCrop(224),\n    transforms.RandomHorizontalFlip(),\n    transforms.RandomRotation(15),\n    transforms.ColorJitter(brightness=0.1),\n    transforms.ToTensor(),\n    transforms.Normalize(mean=[0.485], std=[0.229])\n])", "language": "python"}', 19),
  (doc_id, 'pagebreak', '{}', 20),
  (doc_id, 'heading', '{"text": "4. Results", "level": 2}', 21),
  (doc_id, 'paragraph', '{"text": "Our model achieved state-of-the-art performance across multiple diagnostic tasks. Table 2 presents the classification metrics on each dataset."}', 22),
  (doc_id, 'table', '{"headers": ["Dataset", "Accuracy", "Sensitivity", "Specificity", "AUC-ROC"], "rows": [["ChestX-ray14", "0.892", "0.874", "0.908", "0.941"], ["ISIC 2019", "0.867", "0.851", "0.883", "0.923"], ["BraTS 2020", "0.934", "0.921", "0.947", "0.968"], ["Diabetic Retinopathy", "0.856", "0.839", "0.871", "0.912"]]}', 23),
  (doc_id, 'paragraph', '{"text": "Figure 1 illustrates the receiver operating characteristic curves for each classification task, demonstrating consistent performance across disease categories."}', 24),
  (doc_id, 'figure', '{"src": "/api/placeholder/600/400", "caption": "Figure 1: ROC curves for multi-class disease classification. The model demonstrates high discriminative ability across all diagnostic tasks.", "alt": "ROC curves showing model performance"}', 25),
  (doc_id, 'heading', '{"text": "5. Discussion", "level": 2}', 26),
  (doc_id, 'paragraph', '{"text": "The results demonstrate that deep learning models can achieve diagnostic accuracy comparable to or exceeding that of trained specialists in several imaging domains. Key findings include:"}', 27),
  (doc_id, 'list', '{"items": ["Attention mechanisms improve model interpretability", "Transfer learning reduces data requirements", "Multi-task learning enhances generalization", "Ensemble methods further improve accuracy"], "ordered": true}', 28),
  (doc_id, 'paragraph', '{"text": "However, several challenges remain for clinical deployment, including the need for prospective validation, regulatory approval, and integration with existing clinical workflows \\cite{kelly2019}."}', 29),
  (doc_id, 'pagebreak', '{}', 30),
  (doc_id, 'heading', '{"text": "6. Conclusion", "level": 2}', 31),
  (doc_id, 'paragraph', '{"text": "This thesis has demonstrated the feasibility and effectiveness of deep learning approaches for automated medical diagnosis. Our attention-based CNN architecture achieved strong performance across diverse imaging modalities, suggesting broad applicability in clinical settings."}', 32),
  (doc_id, 'paragraph', '{"text": "Future work will focus on explainability techniques to build clinician trust, federated learning for privacy-preserving model training across institutions, and prospective clinical trials to validate real-world effectiveness."}', 33),
  (doc_id, 'pagebreak', '{}', 34),
  (doc_id, 'heading', '{"text": "References", "level": 2}', 35),
  (doc_id, 'bibliography', '{}', 36);

  INSERT INTO bibliography_entries (document_id, cite_key, entry_type, data) VALUES
  (doc_id, 'topol2019', 'article', '{"author": "Topol, Eric J.", "title": "High-Performance Medicine: The Convergence of Human and Artificial Intelligence", "journal": "Nature Medicine", "year": "2019", "volume": "25", "pages": "44-56"}'),
  (doc_id, 'litjens2017', 'article', '{"author": "Litjens, Geert and others", "title": "A Survey on Deep Learning in Medical Image Analysis", "journal": "Medical Image Analysis", "year": "2017", "volume": "42", "pages": "60-88"}'),
  (doc_id, 'baxt1995', 'article', '{"author": "Baxt, William G.", "title": "Application of Artificial Neural Networks to Clinical Medicine", "journal": "The Lancet", "year": "1995", "volume": "346", "pages": "1135-1138"}'),
  (doc_id, 'esteva2017', 'article', '{"author": "Esteva, Andre and others", "title": "Dermatologist-Level Classification of Skin Cancer with Deep Neural Networks", "journal": "Nature", "year": "2017", "volume": "542", "pages": "115-118"}'),
  (doc_id, 'rajpurkar2017', 'article', '{"author": "Rajpurkar, Pranav and others", "title": "CheXNet: Radiologist-Level Pneumonia Detection on Chest X-Rays with Deep Learning", "journal": "arXiv preprint", "year": "2017"}'),
  (doc_id, 'kelly2019', 'article', '{"author": "Kelly, Christopher J. and others", "title": "Key Challenges for Delivering Clinical Impact with Artificial Intelligence", "journal": "BMC Medicine", "year": "2019", "volume": "17", "pages": "195"}');
END $$;

-- ============================================================================
-- Document 3: Climate Change Modeling
-- Features: equations, figures, data tables, align mode equations
-- ============================================================================
DO $$
DECLARE
  doc_id UUID;
  user_id VARCHAR(255) := 'sample-content';
BEGIN
  INSERT INTO documents (id, owner_id, title, language, paper_size, font_family, font_size, is_public)
  VALUES (gen_random_uuid(), user_id, 'Climate Change Modeling and Prediction', 'en', 'a4', 'serif', 12, TRUE)
  RETURNING id INTO doc_id;

  INSERT INTO blocks (document_id, type, content, sort_order) VALUES
  (doc_id, 'heading', '{"text": "Climate Change Modeling and Prediction", "level": 1}', 0),
  (doc_id, 'abstract', '{"text": "This thesis develops computational models for predicting climate change impacts using coupled atmosphere-ocean general circulation models. We present novel approaches for incorporating feedback mechanisms and analyze long-term temperature projections under various emission scenarios. *This is a sample document generated to demonstrate Lilia editor capabilities. The content is AI-generated for demonstration purposes only.*"}', 1),
  (doc_id, 'pagebreak', '{}', 2),
  (doc_id, 'heading', '{"text": "1. Introduction", "level": 2}', 3),
  (doc_id, 'paragraph', '{"text": "Climate change represents one of the most pressing challenges facing humanity \\cite{ipcc2021}. Accurate climate models are essential for understanding future climate trajectories and informing policy decisions. This thesis presents advances in **coupled climate modeling** that improve prediction accuracy for temperature and precipitation patterns."}', 4),
  (doc_id, 'paragraph', '{"text": "The fundamental challenge in climate modeling lies in representing the complex interactions between the atmosphere, oceans, ice sheets, and biosphere \\cite{trenberth2007}. Our approach integrates multiple feedback mechanisms to provide more realistic projections."}', 5),
  (doc_id, 'heading', '{"text": "2. Literature Review", "level": 2}', 6),
  (doc_id, 'paragraph', '{"text": "Global climate models have evolved significantly since the pioneering work of Manabe and Wetherald \\cite{manabe1967}. Modern Earth System Models incorporate biogeochemical cycles, dynamic vegetation, and interactive ice sheets \\cite{flato2013}."}', 7),
  (doc_id, 'pagebreak', '{}', 8),
  (doc_id, 'heading', '{"text": "3. Mathematical Framework", "level": 2}', 9),
  (doc_id, 'heading', '{"text": "3.1 Radiative Forcing", "level": 3}', 10),
  (doc_id, 'paragraph', '{"text": "The energy balance of Earth can be expressed through the radiative forcing equation. The change in global mean temperature $\\Delta T$ in response to radiative forcing $\\Delta F$ is:"}', 11),
  (doc_id, 'equation', '{"latex": "\\Delta T = \\lambda \\cdot \\Delta F", "equationMode": "display", "label": "eq:forcing"}', 12),
  (doc_id, 'paragraph', '{"text": "where $\\lambda$ is the climate sensitivity parameter (K·W⁻¹·m²). The total radiative forcing includes contributions from greenhouse gases, aerosols, and solar variability:"}', 13),
  (doc_id, 'equation', '{"latex": "\\begin{align}\n\\Delta F_{\\text{total}} &= \\Delta F_{\\text{CO}_2} + \\Delta F_{\\text{CH}_4} + \\Delta F_{\\text{N}_2\\text{O}} + \\Delta F_{\\text{aerosols}} + \\Delta F_{\\text{solar}} \\\\\n&= 5.35 \\ln\\left(\\frac{C}{C_0}\\right) + 0.036(\\sqrt{M} - \\sqrt{M_0}) + \\cdots\n\\end{align}", "equationMode": "align", "label": "eq:total-forcing"}', 14),
  (doc_id, 'heading', '{"text": "3.2 Atmosphere-Ocean Coupling", "level": 3}', 15),
  (doc_id, 'paragraph', '{"text": "The coupled system is governed by conservation equations for momentum, energy, and mass. The Navier-Stokes equations for atmospheric flow are:"}', 16),
  (doc_id, 'equation', '{"latex": "\\frac{\\partial \\mathbf{v}}{\\partial t} + (\\mathbf{v} \\cdot \\nabla)\\mathbf{v} = -\\frac{1}{\\rho}\\nabla p - 2\\mathbf{\\Omega} \\times \\mathbf{v} + \\mathbf{g} + \\mathbf{F}", "equationMode": "display", "label": "eq:navier-stokes"}', 17),
  (doc_id, 'paragraph', '{"text": "Ocean heat uptake follows the advection-diffusion equation:"}', 18),
  (doc_id, 'equation', '{"latex": "\\frac{\\partial T}{\\partial t} + \\mathbf{u} \\cdot \\nabla T = \\kappa \\nabla^2 T + Q", "equationMode": "display", "label": "eq:ocean-heat"}', 19),
  (doc_id, 'heading', '{"text": "3.3 Feedback Mechanisms", "level": 3}', 20),
  (doc_id, 'paragraph', '{"text": "Climate feedbacks amplify or dampen the initial temperature response. The key feedbacks incorporated in our model are:"}', 21),
  (doc_id, 'table', '{"headers": ["Feedback", "Mechanism", "Effect (W/m²/K)"], "rows": [["Water Vapor", "Increased evaporation with warming", "+1.8 ± 0.2"], ["Ice-Albedo", "Reduced reflectivity from ice loss", "+0.3 ± 0.1"], ["Cloud (SW)", "Changes in cloud cover", "-0.2 to +0.8"], ["Cloud (LW)", "Changes in cloud height", "+0.5 ± 0.2"], ["Lapse Rate", "Atmospheric temperature profile", "-0.6 ± 0.2"]]}', 22),
  (doc_id, 'pagebreak', '{}', 23),
  (doc_id, 'heading', '{"text": "4. Model Implementation", "level": 2}', 24),
  (doc_id, 'paragraph', '{"text": "Our model operates on a global grid with the following specifications:"}', 25),
  (doc_id, 'list', '{"items": ["Horizontal resolution: 1° × 1° (atmosphere), 0.25° × 0.25° (ocean)", "Vertical levels: 47 atmospheric layers, 60 ocean layers", "Time step: 30 minutes (atmosphere), 1 hour (ocean)", "Spin-up period: 500 years to reach equilibrium"], "ordered": false}', 26),
  (doc_id, 'heading', '{"text": "5. Results", "level": 2}', 27),
  (doc_id, 'paragraph', '{"text": "We present projections for three Representative Concentration Pathways (RCPs). Table 2 shows projected global mean temperature anomalies for 2100 relative to pre-industrial levels."}', 28),
  (doc_id, 'table', '{"headers": ["Scenario", "2050 Anomaly (°C)", "2100 Anomaly (°C)", "Sea Level Rise (m)", "Arctic Ice Loss (%)"], "rows": [["RCP 2.6", "1.2 ± 0.3", "1.6 ± 0.4", "0.28 ± 0.08", "45 ± 12"], ["RCP 4.5", "1.6 ± 0.4", "2.8 ± 0.6", "0.47 ± 0.12", "68 ± 15"], ["RCP 8.5", "2.1 ± 0.5", "4.8 ± 0.9", "0.84 ± 0.22", "94 ± 6"]]}', 29),
  (doc_id, 'figure', '{"src": "/api/placeholder/600/400", "caption": "Figure 1: Projected global mean temperature anomaly (°C) under different emission scenarios from 2020 to 2100.", "alt": "Temperature projection graph"}', 30),
  (doc_id, 'paragraph', '{"text": "Regional variations are significant, with Arctic warming projected at 2-3 times the global average (Figure 2). Precipitation patterns show increased variability in tropical regions."}', 31),
  (doc_id, 'figure', '{"src": "/api/placeholder/600/350", "caption": "Figure 2: Spatial distribution of projected temperature change (°C) for 2100 under RCP 4.5.", "alt": "Global temperature map"}', 32),
  (doc_id, 'pagebreak', '{}', 33),
  (doc_id, 'heading', '{"text": "6. Discussion", "level": 2}', 34),
  (doc_id, 'paragraph', '{"text": "Our results align with the IPCC ensemble range while providing improved regional detail. The incorporation of updated feedback parameterizations narrows uncertainty bounds, particularly for cloud feedbacks."}', 35),
  (doc_id, 'paragraph', '{"text": "Key limitations include incomplete representation of tipping points and ice sheet dynamics. Future model development should address these critical uncertainties \\cite{lenton2008}."}', 36),
  (doc_id, 'heading', '{"text": "7. Conclusion", "level": 2}', 37),
  (doc_id, 'paragraph', '{"text": "This thesis has presented a comprehensive climate modeling framework with improved representation of physical processes and feedbacks. Our projections underscore the urgency of emissions reductions to limit warming to the 1.5-2°C targets of the Paris Agreement."}', 38),
  (doc_id, 'pagebreak', '{}', 39),
  (doc_id, 'heading', '{"text": "References", "level": 2}', 40),
  (doc_id, 'bibliography', '{}', 41);

  INSERT INTO bibliography_entries (document_id, cite_key, entry_type, data) VALUES
  (doc_id, 'ipcc2021', 'book', '{"author": "IPCC", "title": "Climate Change 2021: The Physical Science Basis", "year": "2021", "publisher": "Cambridge University Press"}'),
  (doc_id, 'trenberth2007', 'article', '{"author": "Trenberth, Kevin E. and others", "title": "Observations: Surface and Atmospheric Climate Change", "journal": "Climate Change 2007: The Physical Science Basis", "year": "2007"}'),
  (doc_id, 'manabe1967', 'article', '{"author": "Manabe, Syukuro and Wetherald, Richard T.", "title": "Thermal Equilibrium of the Atmosphere with a Given Distribution of Relative Humidity", "journal": "Journal of the Atmospheric Sciences", "year": "1967", "volume": "24", "pages": "241-259"}'),
  (doc_id, 'flato2013', 'article', '{"author": "Flato, Gregory and others", "title": "Evaluation of Climate Models", "journal": "Climate Change 2013: The Physical Science Basis", "year": "2013", "pages": "741-866"}'),
  (doc_id, 'lenton2008', 'article', '{"author": "Lenton, Timothy M. and others", "title": "Tipping Elements in the Earth''s Climate System", "journal": "Proceedings of the National Academy of Sciences", "year": "2008", "volume": "105", "pages": "1786-1793"}');
END $$;

-- ============================================================================
-- Document 4: Blockchain Technology Analysis
-- Features: code blocks, diagrams, technical tables
-- ============================================================================
DO $$
DECLARE
  doc_id UUID;
  user_id VARCHAR(255) := 'sample-content';
BEGIN
  INSERT INTO documents (id, owner_id, title, language, paper_size, font_family, font_size, is_public)
  VALUES (gen_random_uuid(), user_id, 'Blockchain Technology: Security and Scalability Analysis', 'en', 'a4', 'serif', 12, TRUE)
  RETURNING id INTO doc_id;

  INSERT INTO blocks (document_id, type, content, sort_order) VALUES
  (doc_id, 'heading', '{"text": "Blockchain Technology: Security and Scalability Analysis", "level": 1}', 0),
  (doc_id, 'abstract', '{"text": "This thesis provides a comprehensive analysis of blockchain technology, examining security mechanisms, consensus algorithms, and scalability solutions. We evaluate leading platforms and propose optimizations for enterprise blockchain applications. *This is a sample document generated to demonstrate Lilia editor capabilities. The content is AI-generated for demonstration purposes only.*"}', 1),
  (doc_id, 'pagebreak', '{}', 2),
  (doc_id, 'heading', '{"text": "1. Introduction", "level": 2}', 3),
  (doc_id, 'paragraph', '{"text": "Blockchain technology has evolved from its origins in cryptocurrency to become a foundational technology for decentralized applications \\cite{nakamoto2008}. The core innovation lies in achieving **distributed consensus** without a central authority, enabling trustless transactions between parties."}', 4),
  (doc_id, 'paragraph', '{"text": "This thesis examines the technical architecture of blockchain systems, with particular focus on security guarantees and performance characteristics. We analyze both public and permissioned blockchain platforms \\cite{androulaki2018}."}', 5),
  (doc_id, 'heading', '{"text": "2. Background", "level": 2}', 6),
  (doc_id, 'heading', '{"text": "2.1 Distributed Ledger Architecture", "level": 3}', 7),
  (doc_id, 'paragraph', '{"text": "A blockchain is a linked list of blocks, where each block contains a cryptographic hash of the previous block, a timestamp, and transaction data:"}', 8),
  (doc_id, 'code', '{"code": "interface Block {\n  index: number;\n  timestamp: number;\n  transactions: Transaction[];\n  previousHash: string;\n  hash: string;\n  nonce: number;\n}\n\ninterface Transaction {\n  sender: string;\n  recipient: string;\n  amount: number;\n  signature: string;\n}", "language": "typescript"}', 9),
  (doc_id, 'heading', '{"text": "2.2 Consensus Mechanisms", "level": 3}', 10),
  (doc_id, 'paragraph', '{"text": "Various consensus algorithms have been developed to achieve agreement in distributed systems. Table 1 compares the major approaches:"}', 11),
  (doc_id, 'table', '{"headers": ["Algorithm", "Type", "Throughput (TPS)", "Finality", "Energy"], "rows": [["Proof of Work", "Probabilistic", "7", "~60 min", "Very High"], ["Proof of Stake", "Probabilistic", "100-1000", "~10 min", "Low"], ["PBFT", "Deterministic", "1000-10000", "Immediate", "Low"], ["Raft", "Deterministic", "10000+", "Immediate", "Very Low"]]}', 12),
  (doc_id, 'pagebreak', '{}', 13),
  (doc_id, 'heading', '{"text": "3. Security Analysis", "level": 2}', 14),
  (doc_id, 'heading', '{"text": "3.1 Cryptographic Foundations", "level": 3}', 15),
  (doc_id, 'paragraph', '{"text": "Blockchain security relies on cryptographic primitives including hash functions and digital signatures. The following implementation demonstrates ECDSA signature verification:"}', 16),
  (doc_id, 'code', '{"code": "from cryptography.hazmat.primitives import hashes\nfrom cryptography.hazmat.primitives.asymmetric import ec\nfrom cryptography.exceptions import InvalidSignature\n\ndef verify_transaction(transaction, public_key):\n    \"\"\"Verify a transaction signature using ECDSA.\"\"\"\n    message = serialize_transaction(transaction)\n    \n    try:\n        public_key.verify(\n            transaction.signature,\n            message,\n            ec.ECDSA(hashes.SHA256())\n        )\n        return True\n    except InvalidSignature:\n        return False\n\ndef serialize_transaction(tx):\n    \"\"\"Create canonical representation for signing.\"\"\"\n    return f\"{tx.sender}:{tx.recipient}:{tx.amount}:{tx.nonce}\".encode()", "language": "python"}', 17),
  (doc_id, 'heading', '{"text": "3.2 Attack Vectors", "level": 3}', 18),
  (doc_id, 'paragraph', '{"text": "We identify and analyze the following attack categories:"}', 19),
  (doc_id, 'list', '{"items": ["**51% Attack**: Malicious control of majority hash rate", "**Double Spending**: Exploiting confirmation delays", "**Sybil Attack**: Creating multiple fake identities", "**Eclipse Attack**: Isolating nodes from the network", "**Smart Contract Vulnerabilities**: Reentrancy, overflow, etc."], "ordered": true}', 20),
  (doc_id, 'heading', '{"text": "3.3 Smart Contract Security", "level": 3}', 21),
  (doc_id, 'paragraph', '{"text": "Smart contract vulnerabilities represent a significant risk. The following Solidity code demonstrates a secure pattern to prevent reentrancy attacks:"}', 22),
  (doc_id, 'code', '{"code": "// SPDX-License-Identifier: MIT\npragma solidity ^0.8.0;\n\nimport \"@openzeppelin/contracts/security/ReentrancyGuard.sol\";\n\ncontract SecureVault is ReentrancyGuard {\n    mapping(address => uint256) private balances;\n    \n    function deposit() external payable {\n        balances[msg.sender] += msg.value;\n    }\n    \n    function withdraw(uint256 amount) external nonReentrant {\n        require(balances[msg.sender] >= amount, \"Insufficient balance\");\n        \n        // Update state before external call (checks-effects-interactions)\n        balances[msg.sender] -= amount;\n        \n        // External call last\n        (bool success, ) = msg.sender.call{value: amount}(\"\");\n        require(success, \"Transfer failed\");\n    }\n}", "language": "solidity"}', 23),
  (doc_id, 'pagebreak', '{}', 24),
  (doc_id, 'heading', '{"text": "4. Scalability Solutions", "level": 2}', 25),
  (doc_id, 'paragraph', '{"text": "Blockchain scalability remains a critical challenge. We evaluate Layer 2 solutions that process transactions off-chain while inheriting the security of the base layer:"}', 26),
  (doc_id, 'table', '{"headers": ["Solution", "Approach", "TPS", "Security Model"], "rows": [["Lightning Network", "Payment Channels", "1,000,000+", "Game-theoretic"], ["Optimistic Rollups", "Fraud Proofs", "2,000-4,000", "Honest minority"], ["ZK-Rollups", "Validity Proofs", "2,000-3,000", "Cryptographic"], ["Plasma", "Child Chains", "~1,000", "Data availability"]]}', 27),
  (doc_id, 'heading', '{"text": "5. Experimental Results", "level": 2}', 28),
  (doc_id, 'paragraph', '{"text": "We benchmarked several blockchain platforms under varying load conditions. Figure 1 shows throughput and latency measurements."}', 29),
  (doc_id, 'figure', '{"src": "/api/placeholder/600/350", "caption": "Figure 1: Transaction throughput comparison across blockchain platforms under increasing load.", "alt": "Blockchain throughput benchmark"}', 30),
  (doc_id, 'pagebreak', '{}', 31),
  (doc_id, 'heading', '{"text": "6. Conclusion", "level": 2}', 32),
  (doc_id, 'paragraph', '{"text": "This thesis has provided a comprehensive analysis of blockchain technology, demonstrating that security and scalability require careful architectural decisions. Our findings indicate that permissioned blockchains with BFT consensus offer the best performance for enterprise applications, while Layer 2 solutions show promise for scaling public networks."}', 33),
  (doc_id, 'pagebreak', '{}', 34),
  (doc_id, 'heading', '{"text": "References", "level": 2}', 35),
  (doc_id, 'bibliography', '{}', 36);

  INSERT INTO bibliography_entries (document_id, cite_key, entry_type, data) VALUES
  (doc_id, 'nakamoto2008', 'misc', '{"author": "Nakamoto, Satoshi", "title": "Bitcoin: A Peer-to-Peer Electronic Cash System", "year": "2008", "howpublished": "https://bitcoin.org/bitcoin.pdf"}'),
  (doc_id, 'androulaki2018', 'inproceedings', '{"author": "Androulaki, Elli and others", "title": "Hyperledger Fabric: A Distributed Operating System for Permissioned Blockchains", "booktitle": "Proceedings of the Thirteenth EuroSys Conference", "year": "2018"}'),
  (doc_id, 'buterin2014', 'misc', '{"author": "Buterin, Vitalik", "title": "Ethereum: A Next-Generation Smart Contract and Decentralized Application Platform", "year": "2014"}'),
  (doc_id, 'castro1999', 'inproceedings', '{"author": "Castro, Miguel and Liskov, Barbara", "title": "Practical Byzantine Fault Tolerance", "booktitle": "OSDI", "year": "1999", "pages": "173-186"}');
END $$;

-- ============================================================================
-- Document 5: Renewable Energy Systems
-- Features: equations, tables, technical analysis
-- ============================================================================
DO $$
DECLARE
  doc_id UUID;
  user_id VARCHAR(255) := 'sample-content';
BEGIN
  INSERT INTO documents (id, owner_id, title, language, paper_size, font_family, font_size, is_public)
  VALUES (gen_random_uuid(), user_id, 'Renewable Energy Systems: Optimization and Grid Integration', 'en', 'a4', 'serif', 12, TRUE)
  RETURNING id INTO doc_id;

  INSERT INTO blocks (document_id, type, content, sort_order) VALUES
  (doc_id, 'heading', '{"text": "Renewable Energy Systems: Optimization and Grid Integration", "level": 1}', 0),
  (doc_id, 'abstract', '{"text": "This thesis investigates optimal integration strategies for renewable energy sources into electrical grids. We develop mathematical models for solar and wind power generation, analyze storage requirements, and propose control strategies for maintaining grid stability with high renewable penetration. *This is a sample document generated to demonstrate Lilia editor capabilities. The content is AI-generated for demonstration purposes only.*"}', 1),
  (doc_id, 'pagebreak', '{}', 2),
  (doc_id, 'heading', '{"text": "1. Introduction", "level": 2}', 3),
  (doc_id, 'paragraph', '{"text": "The transition to renewable energy is essential for addressing climate change and achieving sustainable development goals \\cite{ipcc2018}. However, the variable nature of solar and wind resources presents significant challenges for grid operators accustomed to dispatchable generation."}', 4),
  (doc_id, 'paragraph', '{"text": "This thesis develops optimization frameworks for renewable energy integration, addressing the **intermittency challenge** through storage optimization and demand-side management strategies \\cite{denholm2010}."}', 5),
  (doc_id, 'heading', '{"text": "2. Literature Review", "level": 2}', 6),
  (doc_id, 'paragraph', '{"text": "Grid integration of renewables has been extensively studied, with key contributions from Lund et al. \\cite{lund2015} on 100% renewable systems and Jacobson et al. \\cite{jacobson2017} on transition pathways. Energy storage technologies have been reviewed by Dunn et al. \\cite{dunn2011}."}', 7),
  (doc_id, 'pagebreak', '{}', 8),
  (doc_id, 'heading', '{"text": "3. Mathematical Framework", "level": 2}', 9),
  (doc_id, 'heading', '{"text": "3.1 Solar Power Generation Model", "level": 3}', 10),
  (doc_id, 'paragraph', '{"text": "The power output of a photovoltaic system depends on irradiance, temperature, and system characteristics:"}', 11),
  (doc_id, 'equation', '{"latex": "P_{PV} = \\eta_{inv} \\cdot A \\cdot G \\cdot \\eta_{ref} \\left[1 - \\beta(T_c - T_{ref})\\right]", "equationMode": "display", "label": "eq:pv-power"}', 12),
  (doc_id, 'paragraph', '{"text": "where $G$ is global horizontal irradiance (W/m²), $A$ is panel area, $\\eta_{ref}$ is reference efficiency, $\\beta$ is temperature coefficient, and $T_c$ is cell temperature given by:"}', 13),
  (doc_id, 'equation', '{"latex": "T_c = T_{amb} + \\frac{NOCT - 20}{800} \\cdot G", "equationMode": "display", "label": "eq:cell-temp"}', 14),
  (doc_id, 'heading', '{"text": "3.2 Wind Power Generation Model", "level": 3}', 15),
  (doc_id, 'paragraph', '{"text": "Wind turbine power output follows the well-known cubic relationship with wind speed:"}', 16),
  (doc_id, 'equation', '{"latex": "P_w = \\begin{cases}\n0 & v < v_{cut-in} \\\\\n\\frac{1}{2}\\rho A C_p(\\lambda, \\beta) v^3 & v_{cut-in} \\leq v < v_{rated} \\\\\nP_{rated} & v_{rated} \\leq v < v_{cut-out} \\\\\n0 & v \\geq v_{cut-out}\n\\end{cases}", "equationMode": "display", "label": "eq:wind-power"}', 17),
  (doc_id, 'paragraph', '{"text": "The power coefficient $C_p$ is bounded by the Betz limit of $16/27 \\approx 0.593$."}', 18),
  (doc_id, 'heading', '{"text": "3.3 Storage Optimization", "level": 3}', 19),
  (doc_id, 'paragraph', '{"text": "The optimal storage capacity minimizes total system cost while meeting reliability constraints. The optimization problem is formulated as:"}', 20),
  (doc_id, 'equation', '{"latex": "\\min_{E_s, P_s} \\left( C_{cap} \\cdot E_s + C_{power} \\cdot P_s + \\sum_{t=1}^{T} C_{op}(t) \\right)", "equationMode": "display", "label": "eq:storage-opt"}', 21),
  (doc_id, 'paragraph', '{"text": "subject to energy balance constraints:"}', 22),
  (doc_id, 'equation', '{"latex": "P_{gen}(t) + P_{discharge}(t) = P_{load}(t) + P_{charge}(t) + P_{curtail}(t)", "equationMode": "display", "label": "eq:balance"}', 23),
  (doc_id, 'pagebreak', '{}', 24),
  (doc_id, 'heading', '{"text": "4. Results", "level": 2}', 25),
  (doc_id, 'paragraph', '{"text": "We analyze scenarios with varying renewable penetration levels. Table 1 summarizes the key system metrics."}', 26),
  (doc_id, 'table', '{"headers": ["Renewable %", "Storage (GWh)", "Curtailment %", "LCOE ($/MWh)", "CO₂ (Mt/yr)"], "rows": [["30%", "2.4", "1.2%", "48", "120"], ["50%", "8.6", "3.8%", "52", "85"], ["70%", "24.3", "8.2%", "61", "45"], ["90%", "68.7", "15.4%", "78", "15"], ["100%", "142.5", "22.1%", "98", "0"]]}', 27),
  (doc_id, 'figure', '{"src": "/api/placeholder/600/350", "caption": "Figure 1: Daily generation profile showing solar, wind, storage dispatch, and load for a 70% renewable scenario.", "alt": "Renewable energy generation profile"}', 28),
  (doc_id, 'heading', '{"text": "5. Discussion", "level": 2}', 29),
  (doc_id, 'paragraph', '{"text": "Key findings from our analysis include:"}', 30),
  (doc_id, 'list', '{"items": ["Storage requirements scale non-linearly with renewable penetration", "Geographic diversity reduces overall storage needs by 20-30%", "Demand flexibility can substitute for 15-25% of storage capacity", "Hydrogen electrolysis enables seasonal storage at high penetration levels"], "ordered": false}', 31),
  (doc_id, 'pagebreak', '{}', 32),
  (doc_id, 'heading', '{"text": "6. Conclusion", "level": 2}', 33),
  (doc_id, 'paragraph', '{"text": "This thesis demonstrates that high renewable penetration is technically feasible with appropriate storage and grid management strategies. The marginal cost of storage increases significantly above 80% renewable share, highlighting the importance of demand flexibility and sector coupling for fully decarbonized systems."}', 34),
  (doc_id, 'pagebreak', '{}', 35),
  (doc_id, 'heading', '{"text": "References", "level": 2}', 36),
  (doc_id, 'bibliography', '{}', 37);

  INSERT INTO bibliography_entries (document_id, cite_key, entry_type, data) VALUES
  (doc_id, 'ipcc2018', 'book', '{"author": "IPCC", "title": "Global Warming of 1.5°C", "year": "2018", "publisher": "World Meteorological Organization"}'),
  (doc_id, 'denholm2010', 'article', '{"author": "Denholm, Paul and Hand, Maureen", "title": "Grid Flexibility and Storage Required to Achieve Very High Penetration of Variable Renewable Electricity", "journal": "Energy Policy", "year": "2011", "volume": "39", "pages": "1817-1830"}'),
  (doc_id, 'lund2015', 'article', '{"author": "Lund, Henrik and Mathiesen, Brian Vad", "title": "Energy System Analysis of 100% Renewable Energy Systems", "journal": "Energy", "year": "2015", "volume": "34", "pages": "524-531"}'),
  (doc_id, 'jacobson2017', 'article', '{"author": "Jacobson, Mark Z. and others", "title": "100% Clean and Renewable Wind, Water, and Sunlight All-Sector Energy Roadmaps", "journal": "Joule", "year": "2017", "volume": "1", "pages": "108-121"}'),
  (doc_id, 'dunn2011', 'article', '{"author": "Dunn, Bruce and Kamath, Haresh and Tarascon, Jean-Marie", "title": "Electrical Energy Storage for the Grid: A Battery of Choices", "journal": "Science", "year": "2011", "volume": "334", "pages": "928-935"}');
END $$;

-- ============================================================================
-- Document 6: Neural Network Architectures
-- Features: code, equations, figures
-- ============================================================================
DO $$
DECLARE
  doc_id UUID;
  user_id VARCHAR(255) := 'sample-content';
BEGIN
  INSERT INTO documents (id, owner_id, title, language, paper_size, font_family, font_size, is_public)
  VALUES (gen_random_uuid(), user_id, 'Deep Neural Network Architectures for Computer Vision', 'en', 'a4', 'serif', 12, TRUE)
  RETURNING id INTO doc_id;

  INSERT INTO blocks (document_id, type, content, sort_order) VALUES
  (doc_id, 'heading', '{"text": "Deep Neural Network Architectures for Computer Vision", "level": 1}', 0),
  (doc_id, 'abstract', '{"text": "This thesis explores advanced neural network architectures for computer vision tasks, including image classification, object detection, and semantic segmentation. We analyze the evolution from convolutional networks to transformer-based vision models and present novel architectural innovations. *This is a sample document generated to demonstrate Lilia editor capabilities. The content is AI-generated for demonstration purposes only.*"}', 1),
  (doc_id, 'pagebreak', '{}', 2),
  (doc_id, 'heading', '{"text": "1. Introduction", "level": 2}', 3),
  (doc_id, 'paragraph', '{"text": "Deep learning has revolutionized computer vision, achieving human-level performance on many visual recognition tasks \\cite{krizhevsky2012}. The development of increasingly sophisticated architectures has driven rapid progress, from AlexNet to modern vision transformers \\cite{dosovitskiy2021}."}', 4),
  (doc_id, 'paragraph', '{"text": "This thesis provides a comprehensive analysis of neural network design principles and introduces architectural innovations that improve both accuracy and computational efficiency."}', 5),
  (doc_id, 'heading', '{"text": "2. Background", "level": 2}', 6),
  (doc_id, 'heading', '{"text": "2.1 Convolutional Neural Networks", "level": 3}', 7),
  (doc_id, 'paragraph', '{"text": "The convolution operation extracts local features through learnable filters. For input $\\mathbf{x}$ and kernel $\\mathbf{w}$, the 2D convolution is:"}', 8),
  (doc_id, 'equation', '{"latex": "(\\mathbf{x} * \\mathbf{w})_{i,j} = \\sum_{m}\\sum_{n} \\mathbf{x}_{i+m, j+n} \\cdot \\mathbf{w}_{m,n}", "equationMode": "display", "label": "eq:conv"}', 9),
  (doc_id, 'heading', '{"text": "2.2 Attention Mechanisms", "level": 3}', 10),
  (doc_id, 'paragraph', '{"text": "Self-attention allows modeling long-range dependencies. The scaled dot-product attention is computed as:"}', 11),
  (doc_id, 'equation', '{"latex": "\\text{Attention}(Q, K, V) = \\text{softmax}\\left(\\frac{QK^T}{\\sqrt{d_k}}\\right)V", "equationMode": "display", "label": "eq:attention"}', 12),
  (doc_id, 'pagebreak', '{}', 13),
  (doc_id, 'heading', '{"text": "3. Architecture Design", "level": 2}', 14),
  (doc_id, 'heading', '{"text": "3.1 Residual Connections", "level": 3}', 15),
  (doc_id, 'paragraph', '{"text": "Residual connections enable training of very deep networks by providing gradient highways:"}', 16),
  (doc_id, 'equation', '{"latex": "\\mathbf{y} = \\mathcal{F}(\\mathbf{x}, \\{W_i\\}) + \\mathbf{x}", "equationMode": "display", "label": "eq:residual"}', 17),
  (doc_id, 'paragraph', '{"text": "Our implementation of a residual block in PyTorch:"}', 18),
  (doc_id, 'code', '{"code": "import torch.nn as nn\nimport torch.nn.functional as F\n\nclass ResidualBlock(nn.Module):\n    def __init__(self, channels, stride=1, downsample=None):\n        super().__init__()\n        self.conv1 = nn.Conv2d(channels, channels, 3, stride, 1, bias=False)\n        self.bn1 = nn.BatchNorm2d(channels)\n        self.conv2 = nn.Conv2d(channels, channels, 3, 1, 1, bias=False)\n        self.bn2 = nn.BatchNorm2d(channels)\n        self.downsample = downsample\n        \n    def forward(self, x):\n        identity = x\n        out = F.relu(self.bn1(self.conv1(x)))\n        out = self.bn2(self.conv2(out))\n        if self.downsample:\n            identity = self.downsample(x)\n        return F.relu(out + identity)", "language": "python"}', 19),
  (doc_id, 'heading', '{"text": "3.2 Vision Transformer", "level": 3}', 20),
  (doc_id, 'paragraph', '{"text": "Vision Transformers (ViT) divide images into patches and process them as sequences:"}', 21),
  (doc_id, 'code', '{"code": "class PatchEmbedding(nn.Module):\n    def __init__(self, img_size=224, patch_size=16, in_channels=3, embed_dim=768):\n        super().__init__()\n        self.num_patches = (img_size // patch_size) ** 2\n        self.proj = nn.Conv2d(in_channels, embed_dim, patch_size, patch_size)\n        self.cls_token = nn.Parameter(torch.zeros(1, 1, embed_dim))\n        self.pos_embed = nn.Parameter(torch.zeros(1, self.num_patches + 1, embed_dim))\n        \n    def forward(self, x):\n        B = x.shape[0]\n        x = self.proj(x).flatten(2).transpose(1, 2)  # (B, N, D)\n        cls_tokens = self.cls_token.expand(B, -1, -1)\n        x = torch.cat([cls_tokens, x], dim=1)\n        return x + self.pos_embed", "language": "python"}', 22),
  (doc_id, 'pagebreak', '{}', 23),
  (doc_id, 'heading', '{"text": "4. Experimental Results", "level": 2}', 24),
  (doc_id, 'paragraph', '{"text": "We evaluate architectures on ImageNet-1K classification. Table 1 compares accuracy and computational cost."}', 25),
  (doc_id, 'table', '{"headers": ["Model", "Params (M)", "FLOPs (G)", "Top-1 Acc", "Top-5 Acc"], "rows": [["ResNet-50", "25.6", "4.1", "76.1%", "92.9%"], ["ResNet-152", "60.2", "11.6", "78.3%", "94.2%"], ["EfficientNet-B4", "19.3", "4.2", "82.9%", "96.4%"], ["ViT-B/16", "86.6", "17.6", "84.5%", "97.2%"], ["Swin-B", "88.0", "15.4", "85.2%", "97.5%"]]}', 26),
  (doc_id, 'figure', '{"src": "/api/placeholder/600/400", "caption": "Figure 1: Accuracy vs. computational cost (FLOPs) for various architectures. Pareto-optimal models are highlighted.", "alt": "Accuracy vs FLOPs comparison"}', 27),
  (doc_id, 'heading', '{"text": "5. Discussion", "level": 2}', 28),
  (doc_id, 'paragraph', '{"text": "Key architectural insights from our analysis:"}', 29),
  (doc_id, 'list', '{"items": ["Transformer-based models achieve superior accuracy but require more data", "Efficient architectures like EfficientNet provide the best accuracy/compute trade-off", "Hybrid CNN-Transformer models combine strengths of both approaches", "Pre-training on large datasets significantly improves downstream performance"], "ordered": false}', 30),
  (doc_id, 'pagebreak', '{}', 31),
  (doc_id, 'heading', '{"text": "6. Conclusion", "level": 2}', 32),
  (doc_id, 'paragraph', '{"text": "This thesis has surveyed modern neural network architectures for computer vision, demonstrating the evolution from pure convolutional networks to attention-based designs. Our analysis provides guidelines for architecture selection based on computational constraints and accuracy requirements."}', 33),
  (doc_id, 'pagebreak', '{}', 34),
  (doc_id, 'heading', '{"text": "References", "level": 2}', 35),
  (doc_id, 'bibliography', '{}', 36);

  INSERT INTO bibliography_entries (document_id, cite_key, entry_type, data) VALUES
  (doc_id, 'krizhevsky2012', 'inproceedings', '{"author": "Krizhevsky, Alex and Sutskever, Ilya and Hinton, Geoffrey E.", "title": "ImageNet Classification with Deep Convolutional Neural Networks", "booktitle": "NeurIPS", "year": "2012"}'),
  (doc_id, 'dosovitskiy2021', 'inproceedings', '{"author": "Dosovitskiy, Alexey and others", "title": "An Image is Worth 16x16 Words: Transformers for Image Recognition at Scale", "booktitle": "ICLR", "year": "2021"}'),
  (doc_id, 'he2016', 'inproceedings', '{"author": "He, Kaiming and others", "title": "Deep Residual Learning for Image Recognition", "booktitle": "CVPR", "year": "2016"}'),
  (doc_id, 'tan2019', 'inproceedings', '{"author": "Tan, Mingxing and Le, Quoc V.", "title": "EfficientNet: Rethinking Model Scaling for Convolutional Neural Networks", "booktitle": "ICML", "year": "2019"}'),
  (doc_id, 'liu2021', 'inproceedings', '{"author": "Liu, Ze and others", "title": "Swin Transformer: Hierarchical Vision Transformer using Shifted Windows", "booktitle": "ICCV", "year": "2021"}');
END $$;

-- ============================================================================
-- Document 7: Cryptographic Protocols
-- Features: theorems, proofs, definitions, equations
-- ============================================================================
DO $$
DECLARE
  doc_id UUID;
  user_id VARCHAR(255) := 'sample-content';
BEGIN
  INSERT INTO documents (id, owner_id, title, language, paper_size, font_family, font_size, is_public)
  VALUES (gen_random_uuid(), user_id, 'Modern Cryptographic Protocols: Security Analysis', 'en', 'a4', 'serif', 12, TRUE)
  RETURNING id INTO doc_id;

  INSERT INTO blocks (document_id, type, content, sort_order) VALUES
  (doc_id, 'heading', '{"text": "Modern Cryptographic Protocols: Security Analysis", "level": 1}', 0),
  (doc_id, 'abstract', '{"text": "This thesis provides rigorous security analysis of modern cryptographic protocols, including key exchange, digital signatures, and zero-knowledge proofs. We develop formal models for protocol verification and prove security properties under standard cryptographic assumptions. *This is a sample document generated to demonstrate Lilia editor capabilities. The content is AI-generated for demonstration purposes only.*"}', 1),
  (doc_id, 'pagebreak', '{}', 2),
  (doc_id, 'heading', '{"text": "1. Introduction", "level": 2}', 3),
  (doc_id, 'paragraph', '{"text": "Cryptographic protocols form the foundation of secure communication in the digital age \\cite{goldreich2004}. From TLS securing web traffic to end-to-end encryption in messaging applications, these protocols protect billions of daily transactions."}', 4),
  (doc_id, 'paragraph', '{"text": "This thesis develops formal methods for analyzing protocol security, providing mathematical guarantees rather than relying on ad-hoc testing. We focus on **provable security** in the computational model \\cite{bellare1993}."}', 5),
  (doc_id, 'heading', '{"text": "2. Cryptographic Foundations", "level": 2}', 6),
  (doc_id, 'heading', '{"text": "2.1 Computational Assumptions", "level": 3}', 7),
  (doc_id, 'theorem', '{"theoremType": "definition", "title": "Discrete Logarithm Problem", "text": "Given a cyclic group $G$ of prime order $p$ with generator $g$, and an element $h \\in G$, the **discrete logarithm problem** (DLP) is to find $x \\in \\mathbb{Z}_p$ such that $g^x = h$.", "label": "def:dlp"}', 8),
  (doc_id, 'theorem', '{"theoremType": "definition", "title": "Decisional Diffie-Hellman", "text": "The **DDH assumption** states that for random $a, b, c \\in \\mathbb{Z}_p$, the distributions $(g^a, g^b, g^{ab})$ and $(g^a, g^b, g^c)$ are computationally indistinguishable.", "label": "def:ddh"}', 9),
  (doc_id, 'pagebreak', '{}', 10),
  (doc_id, 'heading', '{"text": "3. Key Exchange Protocols", "level": 2}', 11),
  (doc_id, 'heading', '{"text": "3.1 Diffie-Hellman Key Exchange", "level": 3}', 12),
  (doc_id, 'paragraph', '{"text": "The classic Diffie-Hellman protocol allows two parties to establish a shared secret over an insecure channel:"}', 13),
  (doc_id, 'list', '{"items": ["Alice selects random $a \\leftarrow \\mathbb{Z}_p$ and sends $A = g^a$", "Bob selects random $b \\leftarrow \\mathbb{Z}_p$ and sends $B = g^b$", "Shared secret: $K = B^a = A^b = g^{ab}$"], "ordered": true}', 14),
  (doc_id, 'theorem', '{"theoremType": "theorem", "title": "DH Security", "text": "The Diffie-Hellman key exchange is secure against passive eavesdroppers under the DDH assumption in group $G$.", "label": "thm:dh-security"}', 15),
  (doc_id, 'theorem', '{"theoremType": "proof", "title": "", "text": "Suppose an adversary $\\mathcal{A}$ can distinguish the shared key $g^{ab}$ from random with advantage $\\epsilon$. We construct a DDH distinguisher $\\mathcal{D}$ as follows: Given $(g^a, g^b, Z)$, output $Z$ as the key and run $\\mathcal{A}$. If $Z = g^{ab}$, $\\mathcal{A}$''s view is identical to the real protocol. If $Z = g^c$ for random $c$, the key is uniformly random. Thus $\\mathcal{D}$ has the same advantage $\\epsilon$, contradicting DDH. $\\square$", "label": ""}', 16),
  (doc_id, 'heading', '{"text": "3.2 Authenticated Key Exchange", "level": 3}', 17),
  (doc_id, 'paragraph', '{"text": "Plain Diffie-Hellman is vulnerable to man-in-the-middle attacks. Authenticated variants add identity verification:"}', 18),
  (doc_id, 'equation', '{"latex": "\\sigma_A = \\text{Sign}_{sk_A}(g^a \\| g^b \\| B)", "equationMode": "display", "label": "eq:sig-auth"}', 19),
  (doc_id, 'pagebreak', '{}', 20),
  (doc_id, 'heading', '{"text": "4. Zero-Knowledge Proofs", "level": 2}', 21),
  (doc_id, 'theorem', '{"theoremType": "definition", "title": "Zero-Knowledge Proof", "text": "A protocol $(P, V)$ is a **zero-knowledge proof** for language $L$ if it satisfies: (1) *Completeness*: $\\Pr[V \\text{ accepts } | x \\in L] \\geq 1 - \\text{negl}(n)$; (2) *Soundness*: $\\Pr[V \\text{ accepts } | x \\notin L] \\leq \\text{negl}(n)$; (3) *Zero-Knowledge*: $\\exists$ PPT simulator $S$ such that $\\text{View}_V[P(x,w) \\leftrightarrow V(x)] \\approx_c S(x)$.", "label": "def:zkp"}', 22),
  (doc_id, 'heading', '{"text": "4.1 Schnorr Identification", "level": 3}', 23),
  (doc_id, 'paragraph', '{"text": "The Schnorr protocol proves knowledge of discrete logarithm in zero-knowledge:"}', 24),
  (doc_id, 'list', '{"items": ["Prover: $r \\leftarrow \\mathbb{Z}_p$, send $R = g^r$", "Verifier: send challenge $c \\leftarrow \\mathbb{Z}_p$", "Prover: send $s = r + cx \\mod p$", "Verifier: accept iff $g^s = R \\cdot h^c$"], "ordered": true}', 25),
  (doc_id, 'theorem', '{"theoremType": "lemma", "title": "Schnorr Soundness", "text": "If an adversary can make the verifier accept for statement $h$ with two different challenges $c \\neq c''$, then the discrete logarithm of $h$ can be extracted.", "label": "lem:schnorr"}', 26),
  (doc_id, 'theorem', '{"theoremType": "proof", "title": "", "text": "Given accepting transcripts $(R, c, s)$ and $(R, c'', s'')$ with $c \\neq c''$, we have $g^s = Rh^c$ and $g^{s''} = Rh^{c''}$. Dividing: $g^{s-s''} = h^{c-c''}$, so $x = (s - s'')(c - c'')^{-1} \\mod p$. $\\square$", "label": ""}', 27),
  (doc_id, 'pagebreak', '{}', 28),
  (doc_id, 'heading', '{"text": "5. Digital Signatures", "level": 2}', 29),
  (doc_id, 'paragraph', '{"text": "Digital signatures provide authentication, integrity, and non-repudiation. We analyze the security of Schnorr signatures in the random oracle model."}', 30),
  (doc_id, 'equation', '{"latex": "\\begin{align}\nc &= H(R \\| m) \\\\\ns &= r + c \\cdot x \\mod p \\\\\n\\sigma &= (R, s)\n\\end{align}", "equationMode": "align", "label": "eq:schnorr-sig"}', 31),
  (doc_id, 'theorem', '{"theoremType": "theorem", "title": "Schnorr Signature Security", "text": "Schnorr signatures are existentially unforgeable under chosen-message attack (EUF-CMA) in the random oracle model, assuming the hardness of the discrete logarithm problem.", "label": "thm:schnorr-euf"}', 32),
  (doc_id, 'heading', '{"text": "6. Conclusion", "level": 2}', 33),
  (doc_id, 'paragraph', '{"text": "This thesis has presented rigorous security analyses of fundamental cryptographic protocols. The formal proofs demonstrate that security can be reduced to well-studied computational assumptions, providing confidence in protocol deployment."}', 34),
  (doc_id, 'pagebreak', '{}', 35),
  (doc_id, 'heading', '{"text": "References", "level": 2}', 36),
  (doc_id, 'bibliography', '{}', 37);

  INSERT INTO bibliography_entries (document_id, cite_key, entry_type, data) VALUES
  (doc_id, 'goldreich2004', 'book', '{"author": "Goldreich, Oded", "title": "Foundations of Cryptography", "year": "2004", "publisher": "Cambridge University Press"}'),
  (doc_id, 'bellare1993', 'inproceedings', '{"author": "Bellare, Mihir and Rogaway, Phillip", "title": "Random Oracles are Practical: A Paradigm for Designing Efficient Protocols", "booktitle": "CCS", "year": "1993"}'),
  (doc_id, 'diffie1976', 'article', '{"author": "Diffie, Whitfield and Hellman, Martin", "title": "New Directions in Cryptography", "journal": "IEEE Transactions on Information Theory", "year": "1976", "volume": "22", "pages": "644-654"}'),
  (doc_id, 'schnorr1991', 'article', '{"author": "Schnorr, Claus-Peter", "title": "Efficient Signature Generation by Smart Cards", "journal": "Journal of Cryptology", "year": "1991", "volume": "4", "pages": "161-174"}'),
  (doc_id, 'boneh2006', 'inproceedings', '{"author": "Boneh, Dan and Boyen, Xavier", "title": "Short Signatures Without Random Oracles", "booktitle": "EUROCRYPT", "year": "2004"}');
END $$;

-- ============================================================================
-- Document 8: Astrophysics: Black Hole Dynamics
-- Features: complex equations, theoretical physics
-- ============================================================================
DO $$
DECLARE
  doc_id UUID;
  user_id VARCHAR(255) := 'sample-content';
BEGIN
  INSERT INTO documents (id, owner_id, title, language, paper_size, font_family, font_size, is_public)
  VALUES (gen_random_uuid(), user_id, 'Black Hole Dynamics in General Relativity', 'en', 'a4', 'serif', 12, TRUE)
  RETURNING id INTO doc_id;

  INSERT INTO blocks (document_id, type, content, sort_order) VALUES
  (doc_id, 'heading', '{"text": "Black Hole Dynamics in General Relativity", "level": 1}', 0),
  (doc_id, 'abstract', '{"text": "This thesis investigates the dynamics of black holes within the framework of general relativity, focusing on the Kerr solution for rotating black holes and their thermodynamic properties. We derive the equations governing black hole mergers and analyze gravitational wave signatures. *This is a sample document generated to demonstrate Lilia editor capabilities. The content is AI-generated for demonstration purposes only.*"}', 1),
  (doc_id, 'pagebreak', '{}', 2),
  (doc_id, 'heading', '{"text": "1. Introduction", "level": 2}', 3),
  (doc_id, 'paragraph', '{"text": "Black holes represent one of the most fascinating predictions of general relativity, regions of spacetime where gravity is so intense that nothing, not even light, can escape \\cite{hawking1973}. The recent detection of gravitational waves from binary black hole mergers \\cite{abbott2016} has opened a new era of observational black hole physics."}', 4),
  (doc_id, 'paragraph', '{"text": "This thesis develops the mathematical framework for understanding black hole dynamics, from the geometry of isolated black holes to the complex dynamics of binary systems."}', 5),
  (doc_id, 'heading', '{"text": "2. Mathematical Framework", "level": 2}', 6),
  (doc_id, 'heading', '{"text": "2.1 Einstein Field Equations", "level": 3}', 7),
  (doc_id, 'paragraph', '{"text": "The foundation of general relativity is the Einstein field equation relating spacetime curvature to energy-momentum content:"}', 8),
  (doc_id, 'equation', '{"latex": "G_{\\mu\\nu} + \\Lambda g_{\\mu\\nu} = \\frac{8\\pi G}{c^4} T_{\\mu\\nu}", "equationMode": "display", "label": "eq:einstein"}', 9),
  (doc_id, 'paragraph', '{"text": "where $G_{\\mu\\nu} = R_{\\mu\\nu} - \\frac{1}{2}Rg_{\\mu\\nu}$ is the Einstein tensor, $\\Lambda$ is the cosmological constant, and $T_{\\mu\\nu}$ is the stress-energy tensor."}', 10),
  (doc_id, 'heading', '{"text": "2.2 Schwarzschild Solution", "level": 3}', 11),
  (doc_id, 'paragraph', '{"text": "The Schwarzschild metric describes a non-rotating, uncharged black hole:"}', 12),
  (doc_id, 'equation', '{"latex": "ds^2 = -\\left(1 - \\frac{r_s}{r}\\right)c^2dt^2 + \\left(1 - \\frac{r_s}{r}\\right)^{-1}dr^2 + r^2d\\Omega^2", "equationMode": "display", "label": "eq:schwarzschild"}', 13),
  (doc_id, 'paragraph', '{"text": "where $r_s = 2GM/c^2$ is the Schwarzschild radius and $d\\Omega^2 = d\\theta^2 + \\sin^2\\theta \\, d\\phi^2$."}', 14),
  (doc_id, 'pagebreak', '{}', 15),
  (doc_id, 'heading', '{"text": "3. Rotating Black Holes", "level": 2}', 16),
  (doc_id, 'heading', '{"text": "3.1 The Kerr Metric", "level": 3}', 17),
  (doc_id, 'paragraph', '{"text": "Astrophysical black holes typically rotate. The Kerr solution in Boyer-Lindquist coordinates is:"}', 18),
  (doc_id, 'equation', '{"latex": "\\begin{align}\nds^2 &= -\\left(1 - \\frac{r_s r}{\\Sigma}\\right)c^2dt^2 - \\frac{2r_s ra\\sin^2\\theta}{\\Sigma}c\\,dt\\,d\\phi + \\frac{\\Sigma}{\\Delta}dr^2 \\\\\n&\\quad + \\Sigma \\, d\\theta^2 + \\left(r^2 + a^2 + \\frac{r_s ra^2\\sin^2\\theta}{\\Sigma}\\right)\\sin^2\\theta \\, d\\phi^2\n\\end{align}", "equationMode": "align", "label": "eq:kerr"}', 19),
  (doc_id, 'paragraph', '{"text": "where $a = J/Mc$ is the spin parameter, $\\Sigma = r^2 + a^2\\cos^2\\theta$, and $\\Delta = r^2 - r_s r + a^2$."}', 20),
  (doc_id, 'theorem', '{"theoremType": "theorem", "title": "Kerr Bound", "text": "For a Kerr black hole, the spin parameter is bounded by $|a| \\leq GM/c^2$. When $|a| = GM/c^2$, the black hole is said to be *extremal*.", "label": "thm:kerr-bound"}', 21),
  (doc_id, 'heading', '{"text": "3.2 Ergosphere and Frame Dragging", "level": 3}', 22),
  (doc_id, 'paragraph', '{"text": "The ergosphere is the region where $g_{tt} > 0$, bounded by the ergosurface:"}', 23),
  (doc_id, 'equation', '{"latex": "r_e(\\theta) = \\frac{r_s + \\sqrt{r_s^2 - 4a^2\\cos^2\\theta}}{2}", "equationMode": "display", "label": "eq:ergosphere"}', 24),
  (doc_id, 'paragraph', '{"text": "Within the ergosphere, all timelike observers must co-rotate with the black hole—a phenomenon known as *frame dragging*."}', 25),
  (doc_id, 'pagebreak', '{}', 26),
  (doc_id, 'heading', '{"text": "4. Black Hole Thermodynamics", "level": 2}', 27),
  (doc_id, 'paragraph', '{"text": "Black holes obey laws analogous to thermodynamics \\cite{bekenstein1973}. The Hawking temperature for a Schwarzschild black hole is:"}', 28),
  (doc_id, 'equation', '{"latex": "T_H = \\frac{\\hbar c^3}{8\\pi G M k_B} \\approx 6.17 \\times 10^{-8} \\left(\\frac{M_\\odot}{M}\\right) \\text{ K}", "equationMode": "display", "label": "eq:hawking-temp"}', 29),
  (doc_id, 'paragraph', '{"text": "The Bekenstein-Hawking entropy is proportional to the horizon area:"}', 30),
  (doc_id, 'equation', '{"latex": "S_{BH} = \\frac{k_B c^3 A}{4G\\hbar} = \\frac{k_B}{4}\\frac{A}{\\ell_P^2}", "equationMode": "display", "label": "eq:bh-entropy"}', 31),
  (doc_id, 'table', '{"headers": ["Property", "First Law", "Thermodynamic Analog"], "rows": [["Mass $M$", "$dM = \\frac{\\kappa}{8\\pi G}dA + \\Omega dJ$", "Energy $E$"], ["Surface Gravity $\\kappa$", "Constant on horizon", "Temperature $T$"], ["Area $A$", "$dA \\geq 0$", "Entropy $S$"], ["Angular Velocity $\\Omega$", "Conjugate to $J$", "Chemical potential"]]}', 32),
  (doc_id, 'heading', '{"text": "5. Gravitational Waves from Black Hole Mergers", "level": 2}', 33),
  (doc_id, 'paragraph', '{"text": "Binary black hole systems emit gravitational waves, losing energy and angular momentum until merger. The gravitational wave strain in the quadrupole approximation:"}', 34),
  (doc_id, 'equation', '{"latex": "h_{ij} = \\frac{2G}{c^4 r}\\ddot{Q}_{ij}^{TT}", "equationMode": "display", "label": "eq:gw-strain"}', 35),
  (doc_id, 'paragraph', '{"text": "where $Q_{ij}^{TT}$ is the transverse-traceless part of the quadrupole moment tensor."}', 36),
  (doc_id, 'pagebreak', '{}', 37),
  (doc_id, 'heading', '{"text": "6. Conclusion", "level": 2}', 38),
  (doc_id, 'paragraph', '{"text": "This thesis has developed the theoretical framework for understanding black hole dynamics in general relativity. From the elegant mathematics of the Kerr solution to the remarkable thermodynamic properties, black holes continue to reveal deep connections between gravity, quantum mechanics, and information theory."}', 39),
  (doc_id, 'pagebreak', '{}', 40),
  (doc_id, 'heading', '{"text": "References", "level": 2}', 41),
  (doc_id, 'bibliography', '{}', 42);

  INSERT INTO bibliography_entries (document_id, cite_key, entry_type, data) VALUES
  (doc_id, 'hawking1973', 'book', '{"author": "Hawking, Stephen W. and Ellis, George F. R.", "title": "The Large Scale Structure of Space-Time", "year": "1973", "publisher": "Cambridge University Press"}'),
  (doc_id, 'abbott2016', 'article', '{"author": "Abbott, B. P. and others", "title": "Observation of Gravitational Waves from a Binary Black Hole Merger", "journal": "Physical Review Letters", "year": "2016", "volume": "116", "pages": "061102"}'),
  (doc_id, 'bekenstein1973', 'article', '{"author": "Bekenstein, Jacob D.", "title": "Black Holes and Entropy", "journal": "Physical Review D", "year": "1973", "volume": "7", "pages": "2333-2346"}'),
  (doc_id, 'kerr1963', 'article', '{"author": "Kerr, Roy P.", "title": "Gravitational Field of a Spinning Mass as an Example of Algebraically Special Metrics", "journal": "Physical Review Letters", "year": "1963", "volume": "11", "pages": "237-238"}'),
  (doc_id, 'penrose1969', 'article', '{"author": "Penrose, Roger", "title": "Gravitational Collapse: The Role of General Relativity", "journal": "Nuovo Cimento", "year": "1969", "volume": "1", "pages": "252-276"}');
END $$;

-- ============================================================================
-- Document 9: Genetic Algorithm Optimization
-- Features: code, tables, algorithmic analysis
-- ============================================================================
DO $$
DECLARE
  doc_id UUID;
  user_id VARCHAR(255) := 'sample-content';
BEGIN
  INSERT INTO documents (id, owner_id, title, language, paper_size, font_family, font_size, is_public)
  VALUES (gen_random_uuid(), user_id, 'Genetic Algorithm Optimization: Theory and Applications', 'en', 'a4', 'serif', 12, TRUE)
  RETURNING id INTO doc_id;

  INSERT INTO blocks (document_id, type, content, sort_order) VALUES
  (doc_id, 'heading', '{"text": "Genetic Algorithm Optimization: Theory and Applications", "level": 1}', 0),
  (doc_id, 'abstract', '{"text": "This thesis presents a comprehensive study of genetic algorithms for combinatorial optimization problems. We analyze convergence properties, develop novel crossover and mutation operators, and demonstrate applications in scheduling, routing, and machine learning hyperparameter optimization. *This is a sample document generated to demonstrate Lilia editor capabilities. The content is AI-generated for demonstration purposes only.*"}', 1),
  (doc_id, 'pagebreak', '{}', 2),
  (doc_id, 'heading', '{"text": "1. Introduction", "level": 2}', 3),
  (doc_id, 'paragraph', '{"text": "Genetic algorithms (GAs) are metaheuristic optimization techniques inspired by natural selection \\cite{holland1975}. They have proven effective for problems where traditional optimization methods fail, particularly NP-hard combinatorial problems \\cite{goldberg1989}."}', 4),
  (doc_id, 'paragraph', '{"text": "This thesis develops improved genetic operators and analyzes their effectiveness across a range of benchmark problems."}', 5),
  (doc_id, 'heading', '{"text": "2. Algorithmic Framework", "level": 2}', 6),
  (doc_id, 'heading', '{"text": "2.1 Basic Genetic Algorithm", "level": 3}', 7),
  (doc_id, 'paragraph', '{"text": "The standard genetic algorithm iterates through selection, crossover, and mutation operations:"}', 8),
  (doc_id, 'code', '{"code": "import numpy as np\nfrom typing import Callable, List, Tuple\n\nclass GeneticAlgorithm:\n    def __init__(self, pop_size: int, gene_length: int, \n                 fitness_fn: Callable, mutation_rate: float = 0.01):\n        self.pop_size = pop_size\n        self.gene_length = gene_length\n        self.fitness_fn = fitness_fn\n        self.mutation_rate = mutation_rate\n        self.population = self._initialize_population()\n        \n    def _initialize_population(self) -> np.ndarray:\n        return np.random.randint(0, 2, (self.pop_size, self.gene_length))\n    \n    def evolve(self, generations: int) -> Tuple[np.ndarray, float]:\n        for gen in range(generations):\n            fitness = np.array([self.fitness_fn(ind) for ind in self.population])\n            \n            # Selection (tournament)\n            parents = self._tournament_selection(fitness)\n            \n            # Crossover\n            offspring = self._crossover(parents)\n            \n            # Mutation\n            offspring = self._mutate(offspring)\n            \n            self.population = offspring\n            \n        best_idx = np.argmax(fitness)\n        return self.population[best_idx], fitness[best_idx]", "language": "python"}', 9),
  (doc_id, 'heading', '{"text": "2.2 Selection Mechanisms", "level": 3}', 10),
  (doc_id, 'paragraph', '{"text": "We implement and compare several selection strategies:"}', 11),
  (doc_id, 'table', '{"headers": ["Method", "Selection Pressure", "Diversity", "Complexity"], "rows": [["Roulette Wheel", "Low", "High", "$O(n)$"], ["Tournament", "Adjustable", "Medium", "$O(k \\cdot n)$"], ["Rank-Based", "Medium", "Medium", "$O(n \\log n)$"], ["Truncation", "High", "Low", "$O(n \\log n)$"]]}', 12),
  (doc_id, 'code', '{"code": "def _tournament_selection(self, fitness: np.ndarray, k: int = 3) -> np.ndarray:\n    \"\"\"Tournament selection with tournament size k.\"\"\"\n    selected = []\n    for _ in range(self.pop_size):\n        # Random tournament\n        contestants = np.random.choice(self.pop_size, k, replace=False)\n        winner = contestants[np.argmax(fitness[contestants])]\n        selected.append(self.population[winner].copy())\n    return np.array(selected)", "language": "python"}', 13),
  (doc_id, 'pagebreak', '{}', 14),
  (doc_id, 'heading', '{"text": "3. Crossover Operators", "level": 2}', 15),
  (doc_id, 'heading', '{"text": "3.1 Order Crossover (OX)", "level": 3}', 16),
  (doc_id, 'paragraph', '{"text": "For permutation problems like TSP, order crossover preserves relative ordering:"}', 17),
  (doc_id, 'code', '{"code": "def order_crossover(parent1: List[int], parent2: List[int]) -> List[int]:\n    \"\"\"Order crossover for permutation representation.\"\"\"\n    n = len(parent1)\n    # Select crossover points\n    start, end = sorted(np.random.choice(n, 2, replace=False))\n    \n    # Copy segment from parent1\n    child = [None] * n\n    child[start:end] = parent1[start:end]\n    \n    # Fill remaining from parent2 in order\n    p2_values = [v for v in parent2 if v not in child[start:end]]\n    j = 0\n    for i in range(n):\n        if child[i] is None:\n            child[i] = p2_values[j]\n            j += 1\n    \n    return child", "language": "python"}', 18),
  (doc_id, 'heading', '{"text": "4. Applications", "level": 2}', 19),
  (doc_id, 'heading', '{"text": "4.1 Traveling Salesman Problem", "level": 3}', 20),
  (doc_id, 'paragraph', '{"text": "We benchmark our GA on standard TSP instances from TSPLIB. Table 2 shows results."}', 21),
  (doc_id, 'table', '{"headers": ["Instance", "Optimal", "GA Best", "Gap %", "Time (s)"], "rows": [["berlin52", "7542", "7544", "0.03%", "2.3"], ["kroA100", "21282", "21356", "0.35%", "8.7"], ["ch150", "6528", "6612", "1.29%", "18.4"], ["a280", "2579", "2648", "2.68%", "42.1"], ["pcb442", "50778", "52341", "3.08%", "124.6"]]}', 22),
  (doc_id, 'figure', '{"src": "/api/placeholder/600/350", "caption": "Figure 1: Convergence curves for the berlin52 TSP instance showing fitness improvement over generations.", "alt": "GA convergence plot"}', 23),
  (doc_id, 'heading', '{"text": "4.2 Neural Network Architecture Search", "level": 3}', 24),
  (doc_id, 'paragraph', '{"text": "GAs can optimize neural network architectures. Our encoding represents:"}', 25),
  (doc_id, 'list', '{"items": ["Number and size of hidden layers", "Activation functions (ReLU, tanh, sigmoid)", "Dropout rates and regularization", "Learning rate and optimizer choice"], "ordered": false}', 26),
  (doc_id, 'pagebreak', '{}', 27),
  (doc_id, 'heading', '{"text": "5. Convergence Analysis", "level": 2}', 28),
  (doc_id, 'paragraph', '{"text": "The Schema Theorem provides theoretical foundation for GA convergence:"}', 29),
  (doc_id, 'equation', '{"latex": "E[m(H, t+1)] \\geq m(H,t) \\cdot \\frac{f(H)}{\\bar{f}} \\cdot \\left(1 - p_c \\cdot \\frac{\\delta(H)}{\\ell - 1}\\right) \\cdot (1 - p_m)^{o(H)}", "equationMode": "display", "label": "eq:schema"}', 30),
  (doc_id, 'paragraph', '{"text": "where $m(H,t)$ is the expected number of instances of schema $H$, $\\delta(H)$ is defining length, $o(H)$ is order, and $\\bar{f}$ is average fitness."}', 31),
  (doc_id, 'heading', '{"text": "6. Conclusion", "level": 2}', 32),
  (doc_id, 'paragraph', '{"text": "This thesis has developed improved genetic algorithm techniques and demonstrated their effectiveness on benchmark optimization problems. Our adaptive operators show significant improvements over standard approaches, particularly for large-scale instances."}', 33),
  (doc_id, 'pagebreak', '{}', 34),
  (doc_id, 'heading', '{"text": "References", "level": 2}', 35),
  (doc_id, 'bibliography', '{}', 36);

  INSERT INTO bibliography_entries (document_id, cite_key, entry_type, data) VALUES
  (doc_id, 'holland1975', 'book', '{"author": "Holland, John H.", "title": "Adaptation in Natural and Artificial Systems", "year": "1975", "publisher": "University of Michigan Press"}'),
  (doc_id, 'goldberg1989', 'book', '{"author": "Goldberg, David E.", "title": "Genetic Algorithms in Search, Optimization, and Machine Learning", "year": "1989", "publisher": "Addison-Wesley"}'),
  (doc_id, 'mitchell1998', 'book', '{"author": "Mitchell, Melanie", "title": "An Introduction to Genetic Algorithms", "year": "1998", "publisher": "MIT Press"}'),
  (doc_id, 'whitley1994', 'article', '{"author": "Whitley, Darrell", "title": "A Genetic Algorithm Tutorial", "journal": "Statistics and Computing", "year": "1994", "volume": "4", "pages": "65-85"}'),
  (doc_id, 'eiben2003', 'book', '{"author": "Eiben, A. E. and Smith, J. E.", "title": "Introduction to Evolutionary Computing", "year": "2003", "publisher": "Springer"}');
END $$;

-- ============================================================================
-- Document 10: Fluid Dynamics Simulation
-- Features: equations, figures, computational methods
-- ============================================================================
DO $$
DECLARE
  doc_id UUID;
  user_id VARCHAR(255) := 'sample-content';
BEGIN
  INSERT INTO documents (id, owner_id, title, language, paper_size, font_family, font_size, is_public)
  VALUES (gen_random_uuid(), user_id, 'Computational Fluid Dynamics: Methods and Applications', 'en', 'a4', 'serif', 12, TRUE)
  RETURNING id INTO doc_id;

  INSERT INTO blocks (document_id, type, content, sort_order) VALUES
  (doc_id, 'heading', '{"text": "Computational Fluid Dynamics: Methods and Applications", "level": 1}', 0),
  (doc_id, 'abstract', '{"text": "This thesis develops numerical methods for solving the Navier-Stokes equations with applications to aerodynamic analysis and turbulence modeling. We implement finite volume methods on unstructured meshes and validate against experimental data for benchmark flows. *This is a sample document generated to demonstrate Lilia editor capabilities. The content is AI-generated for demonstration purposes only.*"}', 1),
  (doc_id, 'pagebreak', '{}', 2),
  (doc_id, 'heading', '{"text": "1. Introduction", "level": 2}', 3),
  (doc_id, 'paragraph', '{"text": "Computational Fluid Dynamics (CFD) has become an essential tool in engineering design, enabling simulation of complex flow phenomena that are difficult or impossible to study experimentally \\cite{anderson1995}. This thesis presents advances in numerical methods for turbulent flows."}', 4),
  (doc_id, 'paragraph', '{"text": "Our contributions include improved turbulence models and efficient parallel implementations suitable for high-fidelity industrial simulations \\cite{versteeg2007}."}', 5),
  (doc_id, 'heading', '{"text": "2. Governing Equations", "level": 2}', 6),
  (doc_id, 'heading', '{"text": "2.1 Navier-Stokes Equations", "level": 3}', 7),
  (doc_id, 'paragraph', '{"text": "The motion of viscous, incompressible fluids is governed by the Navier-Stokes equations:"}', 8),
  (doc_id, 'equation', '{"latex": "\\rho\\left(\\frac{\\partial \\mathbf{u}}{\\partial t} + (\\mathbf{u} \\cdot \\nabla)\\mathbf{u}\\right) = -\\nabla p + \\mu \\nabla^2 \\mathbf{u} + \\mathbf{f}", "equationMode": "display", "label": "eq:momentum"}', 9),
  (doc_id, 'paragraph', '{"text": "with the continuity equation for mass conservation:"}', 10),
  (doc_id, 'equation', '{"latex": "\\nabla \\cdot \\mathbf{u} = 0", "equationMode": "display", "label": "eq:continuity"}', 11),
  (doc_id, 'paragraph', '{"text": "where $\\mathbf{u}$ is velocity, $p$ is pressure, $\\rho$ is density, $\\mu$ is dynamic viscosity, and $\\mathbf{f}$ represents body forces."}', 12),
  (doc_id, 'heading', '{"text": "2.2 Reynolds-Averaged Navier-Stokes", "level": 3}', 13),
  (doc_id, 'paragraph', '{"text": "For turbulent flows, we decompose variables into mean and fluctuating components: $\\mathbf{u} = \\bar{\\mathbf{u}} + \\mathbf{u}''$. The RANS equations introduce the Reynolds stress tensor:"}', 14),
  (doc_id, 'equation', '{"latex": "\\bar{\\mathbf{u}} \\cdot \\nabla \\bar{\\mathbf{u}} = -\\frac{1}{\\rho}\\nabla \\bar{p} + \\nu \\nabla^2 \\bar{\\mathbf{u}} - \\nabla \\cdot \\overline{\\mathbf{u}'' \\otimes \\mathbf{u}''}", "equationMode": "display", "label": "eq:rans"}', 15),
  (doc_id, 'pagebreak', '{}', 16),
  (doc_id, 'heading', '{"text": "3. Turbulence Modeling", "level": 2}', 17),
  (doc_id, 'heading', '{"text": "3.1 k-ε Model", "level": 3}', 18),
  (doc_id, 'paragraph', '{"text": "The standard $k$-$\\varepsilon$ model solves transport equations for turbulent kinetic energy and dissipation rate:"}', 19),
  (doc_id, 'equation', '{"latex": "\\begin{align}\n\\frac{\\partial k}{\\partial t} + \\bar{u}_j \\frac{\\partial k}{\\partial x_j} &= P_k - \\varepsilon + \\frac{\\partial}{\\partial x_j}\\left[\\left(\\nu + \\frac{\\nu_t}{\\sigma_k}\\right)\\frac{\\partial k}{\\partial x_j}\\right] \\\\\n\\frac{\\partial \\varepsilon}{\\partial t} + \\bar{u}_j \\frac{\\partial \\varepsilon}{\\partial x_j} &= C_{\\varepsilon 1}\\frac{\\varepsilon}{k}P_k - C_{\\varepsilon 2}\\frac{\\varepsilon^2}{k} + \\frac{\\partial}{\\partial x_j}\\left[\\left(\\nu + \\frac{\\nu_t}{\\sigma_\\varepsilon}\\right)\\frac{\\partial \\varepsilon}{\\partial x_j}\\right]\n\\end{align}", "equationMode": "align", "label": "eq:k-epsilon"}', 20),
  (doc_id, 'paragraph', '{"text": "Model constants are given in Table 1:"}', 21),
  (doc_id, 'table', '{"headers": ["Constant", "Value", "Physical Meaning"], "rows": [["$C_\\mu$", "0.09", "Eddy viscosity coefficient"], ["$C_{\\varepsilon 1}$", "1.44", "Production coefficient"], ["$C_{\\varepsilon 2}$", "1.92", "Destruction coefficient"], ["$\\sigma_k$", "1.0", "Turbulent Prandtl number for $k$"], ["$\\sigma_\\varepsilon$", "1.3", "Turbulent Prandtl number for $\\varepsilon$"]]}', 22),
  (doc_id, 'heading', '{"text": "4. Numerical Methods", "level": 2}', 23),
  (doc_id, 'heading', '{"text": "4.1 Finite Volume Discretization", "level": 3}', 24),
  (doc_id, 'paragraph', '{"text": "We employ a cell-centered finite volume method. The integral form of the conservation law over control volume $\\Omega$ with boundary $\\partial\\Omega$:"}', 25),
  (doc_id, 'equation', '{"latex": "\\frac{\\partial}{\\partial t}\\int_\\Omega \\phi \\, dV + \\oint_{\\partial\\Omega} \\phi \\mathbf{u} \\cdot \\mathbf{n} \\, dA = \\oint_{\\partial\\Omega} \\Gamma \\nabla\\phi \\cdot \\mathbf{n} \\, dA + \\int_\\Omega S_\\phi \\, dV", "equationMode": "display", "label": "eq:fvm"}', 26),
  (doc_id, 'paragraph', '{"text": "Second-order upwind schemes are used for convective terms, with central differencing for diffusive terms."}', 27),
  (doc_id, 'pagebreak', '{}', 28),
  (doc_id, 'heading', '{"text": "5. Results", "level": 2}', 29),
  (doc_id, 'paragraph', '{"text": "We validate our implementation against the backward-facing step benchmark \\cite{driver1985}. Table 2 compares predicted reattachment lengths."}', 30),
  (doc_id, 'table', '{"headers": ["Model", "Reattachment $x/H$", "Experiment", "Error"], "rows": [["$k$-$\\varepsilon$ Standard", "5.8", "6.1", "-4.9%"], ["$k$-$\\varepsilon$ Realizable", "6.0", "6.1", "-1.6%"], ["$k$-$\\omega$ SST", "6.2", "6.1", "+1.6%"], ["RSM", "6.0", "6.1", "-1.6%"]]}', 31),
  (doc_id, 'figure', '{"src": "/api/placeholder/600/350", "caption": "Figure 1: Velocity streamlines for backward-facing step flow at Re = 5000. Recirculation zone clearly visible downstream of step.", "alt": "Streamlines of backward-facing step flow"}', 32),
  (doc_id, 'figure', '{"src": "/api/placeholder/600/300", "caption": "Figure 2: Wall shear stress distribution comparing CFD predictions with experimental measurements.", "alt": "Wall shear stress comparison"}', 33),
  (doc_id, 'heading', '{"text": "6. Conclusion", "level": 2}', 34),
  (doc_id, 'paragraph', '{"text": "This thesis has presented numerical methods for computational fluid dynamics with validation against standard benchmarks. The $k$-$\\omega$ SST model provides the best accuracy for separated flows, while computational efficiency favors simpler two-equation models for attached boundary layer flows."}', 35),
  (doc_id, 'pagebreak', '{}', 36),
  (doc_id, 'heading', '{"text": "References", "level": 2}', 37),
  (doc_id, 'bibliography', '{}', 38);

  INSERT INTO bibliography_entries (document_id, cite_key, entry_type, data) VALUES
  (doc_id, 'anderson1995', 'book', '{"author": "Anderson, John D.", "title": "Computational Fluid Dynamics: The Basics with Applications", "year": "1995", "publisher": "McGraw-Hill"}'),
  (doc_id, 'versteeg2007', 'book', '{"author": "Versteeg, H. K. and Malalasekera, W.", "title": "An Introduction to Computational Fluid Dynamics: The Finite Volume Method", "year": "2007", "publisher": "Pearson"}'),
  (doc_id, 'wilcox2006', 'book', '{"author": "Wilcox, David C.", "title": "Turbulence Modeling for CFD", "year": "2006", "publisher": "DCW Industries"}'),
  (doc_id, 'menter1994', 'article', '{"author": "Menter, Florian R.", "title": "Two-Equation Eddy-Viscosity Turbulence Models for Engineering Applications", "journal": "AIAA Journal", "year": "1994", "volume": "32", "pages": "1598-1605"}'),
  (doc_id, 'driver1985', 'techreport', '{"author": "Driver, David M. and Seegmiller, H. Lee", "title": "Features of a Reattaching Turbulent Shear Layer in Divergent Channel Flow", "institution": "NASA", "year": "1985", "number": "TM-85888"}');
END $$;

-- ============================================================================
-- Verification query
-- ============================================================================
-- Run this to verify the documents were created:
-- SELECT d.title, COUNT(b.id) as block_count, COUNT(be.id) as citation_count
-- FROM documents d
-- LEFT JOIN blocks b ON d.id = b.document_id
-- LEFT JOIN bibliography_entries be ON d.id = be.document_id
-- WHERE d.owner_id = 'sample-content'
-- GROUP BY d.id, d.title
-- ORDER BY d.created_at;

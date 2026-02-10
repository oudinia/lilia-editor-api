using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Infrastructure.Data.Seeds;

public static class SystemFormulaSeeder
{
    public static async Task SeedAsync(LiliaDbContext context)
    {
        // Remove existing system formulas and re-seed to ensure data is correct
        var existingSystemFormulas = await context.Formulas
            .Where(f => f.IsSystem)
            .ToListAsync();

        if (existingSystemFormulas.Any())
        {
            context.Formulas.RemoveRange(existingSystemFormulas);
            await context.SaveChangesAsync();
        }

        var formulas = GetSystemFormulas();
        context.Formulas.AddRange(formulas);
        await context.SaveChangesAsync();
    }

    private static List<Formula> GetSystemFormulas()
    {
        return new List<Formula>
        {
            // ================================================================
            // MATH — Algebra
            // ================================================================
            CreateFormula("Quadratic Formula", "Solutions to ax² + bx + c = 0",
                FormulaCategories.Math, FormulaSubcategories.Algebra,
                @"x = \frac{-b \pm \sqrt{b^2 - 4ac}}{2a}",
                new List<string> { "roots", "polynomial" }),

            CreateFormula("Binomial Theorem", "Expansion of (a + b)^n",
                FormulaCategories.Math, FormulaSubcategories.Algebra,
                @"(a + b)^n = \sum_{k=0}^{n} \binom{n}{k} a^{n-k} b^k",
                new List<string> { "expansion", "combinatorics" }),

            CreateFormula("Logarithm Change of Base", "Change base of logarithm",
                FormulaCategories.Math, FormulaSubcategories.Algebra,
                @"\log_b a = \frac{\ln a}{\ln b}",
                new List<string> { "logarithm", "base" }),

            CreateFormula("Geometric Series Sum", "Sum of infinite geometric series",
                FormulaCategories.Math, FormulaSubcategories.Algebra,
                @"\sum_{k=0}^{\infty} ar^k = \frac{a}{1-r}, \quad |r| < 1",
                new List<string> { "series", "infinite" }),

            // ================================================================
            // MATH — Calculus
            // ================================================================
            CreateFormula("Fundamental Theorem of Calculus", "Connects differentiation and integration",
                FormulaCategories.Math, FormulaSubcategories.Calculus,
                @"\int_a^b f'(x)\,dx = f(b) - f(a)",
                new List<string> { "integral", "derivative" }),

            CreateFormula("Integration by Parts", "Product rule for integration",
                FormulaCategories.Math, FormulaSubcategories.Calculus,
                @"\int u\,dv = uv - \int v\,du",
                new List<string> { "integral", "technique" }),

            CreateFormula("Chain Rule", "Derivative of composite functions",
                FormulaCategories.Math, FormulaSubcategories.Calculus,
                @"\frac{d}{dx}[f(g(x))] = f'(g(x)) \cdot g'(x)",
                new List<string> { "derivative", "composite" }),

            CreateFormula("Taylor Series", "Power series expansion of a function",
                FormulaCategories.Math, FormulaSubcategories.Calculus,
                @"f(x) = \sum_{n=0}^{\infty} \frac{f^{(n)}(a)}{n!}(x-a)^n",
                new List<string> { "series", "expansion", "approximation" }),

            CreateFormula("L'Hôpital's Rule", "Evaluate limits of indeterminate forms",
                FormulaCategories.Math, FormulaSubcategories.Calculus,
                @"\lim_{x \to c} \frac{f(x)}{g(x)} = \lim_{x \to c} \frac{f'(x)}{g'(x)}",
                new List<string> { "limit", "indeterminate" }),

            CreateFormula("Gaussian Integral", "Integral of e^(-x²)",
                FormulaCategories.Math, FormulaSubcategories.Calculus,
                @"\int_{-\infty}^{\infty} e^{-x^2}\,dx = \sqrt{\pi}",
                new List<string> { "integral", "gaussian" }),

            // ================================================================
            // MATH — Trigonometry
            // ================================================================
            CreateFormula("Pythagorean Identity", "Fundamental trig identity",
                FormulaCategories.Math, FormulaSubcategories.Trigonometry,
                @"\sin^2\theta + \cos^2\theta = 1",
                new List<string> { "identity", "pythagorean" }),

            CreateFormula("Euler's Formula", "Complex exponential and trig",
                FormulaCategories.Math, FormulaSubcategories.Trigonometry,
                @"e^{i\theta} = \cos\theta + i\sin\theta",
                new List<string> { "complex", "euler" }),

            CreateFormula("Law of Cosines", "Generalized Pythagorean theorem",
                FormulaCategories.Math, FormulaSubcategories.Trigonometry,
                @"c^2 = a^2 + b^2 - 2ab\cos C",
                new List<string> { "triangle", "cosine" }),

            CreateFormula("Double Angle (Sine)", "Sin of double angle",
                FormulaCategories.Math, FormulaSubcategories.Trigonometry,
                @"\sin 2\theta = 2\sin\theta\cos\theta",
                new List<string> { "identity", "double-angle" }),

            // ================================================================
            // MATH — Linear Algebra
            // ================================================================
            CreateFormula("Matrix Determinant (2×2)", "Determinant of a 2×2 matrix",
                FormulaCategories.Math, FormulaSubcategories.LinearAlgebra,
                @"\det\begin{pmatrix} a & b \\ c & d \end{pmatrix} = ad - bc",
                new List<string> { "matrix", "determinant" }),

            CreateFormula("Eigenvalue Equation", "Definition of eigenvalues",
                FormulaCategories.Math, FormulaSubcategories.LinearAlgebra,
                @"A\mathbf{v} = \lambda\mathbf{v}",
                new List<string> { "eigenvalue", "eigenvector" }),

            CreateFormula("Dot Product", "Inner product of vectors",
                FormulaCategories.Math, FormulaSubcategories.LinearAlgebra,
                @"\mathbf{a} \cdot \mathbf{b} = \sum_{i=1}^{n} a_i b_i = \|\mathbf{a}\|\|\mathbf{b}\|\cos\theta",
                new List<string> { "vector", "inner-product" }),

            CreateFormula("Cross Product", "Vector cross product",
                FormulaCategories.Math, FormulaSubcategories.LinearAlgebra,
                @"\mathbf{a} \times \mathbf{b} = \|\mathbf{a}\|\|\mathbf{b}\|\sin\theta\,\hat{\mathbf{n}}",
                new List<string> { "vector", "cross-product" }),

            // ================================================================
            // MATH — Set Theory
            // ================================================================
            CreateFormula("De Morgan's Laws", "Complement of union/intersection",
                FormulaCategories.Math, FormulaSubcategories.SetTheory,
                @"\overline{A \cup B} = \overline{A} \cap \overline{B}",
                new List<string> { "sets", "complement" }),

            // ================================================================
            // PHYSICS — Mechanics
            // ================================================================
            CreateFormula("Newton's Second Law", "Force equals mass times acceleration",
                FormulaCategories.Physics, FormulaSubcategories.Mechanics,
                @"\mathbf{F} = m\mathbf{a}",
                new List<string> { "force", "newton" }),

            CreateFormula("Kinetic Energy", "Energy of motion",
                FormulaCategories.Physics, FormulaSubcategories.Mechanics,
                @"E_k = \frac{1}{2}mv^2",
                new List<string> { "energy", "motion" }),

            CreateFormula("Newton's Law of Gravitation", "Gravitational force between two masses",
                FormulaCategories.Physics, FormulaSubcategories.Mechanics,
                @"F = G\frac{m_1 m_2}{r^2}",
                new List<string> { "gravity", "force" }),

            CreateFormula("Work-Energy Theorem", "Work done equals change in kinetic energy",
                FormulaCategories.Physics, FormulaSubcategories.Mechanics,
                @"W = \Delta E_k = \frac{1}{2}mv_f^2 - \frac{1}{2}mv_i^2",
                new List<string> { "work", "energy" }),

            CreateFormula("Simple Harmonic Motion", "Position in SHM",
                FormulaCategories.Physics, FormulaSubcategories.Mechanics,
                @"x(t) = A\cos(\omega t + \phi)",
                new List<string> { "oscillation", "harmonic" }),

            // ================================================================
            // PHYSICS — Electromagnetism
            // ================================================================
            CreateFormula("Coulomb's Law", "Electric force between charges",
                FormulaCategories.Physics, FormulaSubcategories.Electromagnetism,
                @"F = k_e \frac{q_1 q_2}{r^2}",
                new List<string> { "electric", "charge" }),

            CreateFormula("Maxwell's Equations (Gauss)", "Gauss's law for electric fields",
                FormulaCategories.Physics, FormulaSubcategories.Electromagnetism,
                @"\nabla \cdot \mathbf{E} = \frac{\rho}{\varepsilon_0}",
                new List<string> { "maxwell", "gauss", "electric-field" }),

            CreateFormula("Faraday's Law", "Electromagnetic induction",
                FormulaCategories.Physics, FormulaSubcategories.Electromagnetism,
                @"\mathcal{E} = -\frac{d\Phi_B}{dt}",
                new List<string> { "induction", "emf" }),

            CreateFormula("Ohm's Law", "Voltage, current, resistance relationship",
                FormulaCategories.Physics, FormulaSubcategories.Electromagnetism,
                @"V = IR",
                new List<string> { "circuit", "resistance" }),

            // ================================================================
            // PHYSICS — Thermodynamics
            // ================================================================
            CreateFormula("Ideal Gas Law", "PV = nRT",
                FormulaCategories.Physics, FormulaSubcategories.Thermodynamics,
                @"PV = nRT",
                new List<string> { "gas", "pressure" }),

            CreateFormula("Boltzmann Entropy", "Statistical definition of entropy",
                FormulaCategories.Physics, FormulaSubcategories.Thermodynamics,
                @"S = k_B \ln \Omega",
                new List<string> { "entropy", "statistical" }),

            CreateFormula("First Law of Thermodynamics", "Energy conservation",
                FormulaCategories.Physics, FormulaSubcategories.Thermodynamics,
                @"\Delta U = Q - W",
                new List<string> { "energy", "heat", "work" }),

            // ================================================================
            // PHYSICS — Quantum Mechanics
            // ================================================================
            CreateFormula("Schrödinger Equation", "Time-dependent Schrödinger equation",
                FormulaCategories.Physics, FormulaSubcategories.QuantumMechanics,
                @"i\hbar\frac{\partial}{\partial t}\Psi = \hat{H}\Psi",
                new List<string> { "wavefunction", "hamiltonian" }),

            CreateFormula("Heisenberg Uncertainty", "Position-momentum uncertainty",
                FormulaCategories.Physics, FormulaSubcategories.QuantumMechanics,
                @"\Delta x \, \Delta p \geq \frac{\hbar}{2}",
                new List<string> { "uncertainty", "quantum" }),

            CreateFormula("de Broglie Wavelength", "Matter wave wavelength",
                FormulaCategories.Physics, FormulaSubcategories.QuantumMechanics,
                @"\lambda = \frac{h}{p} = \frac{h}{mv}",
                new List<string> { "wave-particle", "duality" }),

            // ================================================================
            // PHYSICS — Relativity
            // ================================================================
            CreateFormula("Mass-Energy Equivalence", "Einstein's famous equation",
                FormulaCategories.Physics, FormulaSubcategories.Relativity,
                @"E = mc^2",
                new List<string> { "einstein", "energy" }),

            CreateFormula("Lorentz Factor", "Time dilation / length contraction factor",
                FormulaCategories.Physics, FormulaSubcategories.Relativity,
                @"\gamma = \frac{1}{\sqrt{1 - \frac{v^2}{c^2}}}",
                new List<string> { "lorentz", "time-dilation" }),

            // ================================================================
            // CHEMISTRY
            // ================================================================
            CreateFormula("Nernst Equation", "Electrode potential under non-standard conditions",
                FormulaCategories.Chemistry, FormulaSubcategories.PhysicalChemistry,
                @"E = E^\circ - \frac{RT}{nF}\ln Q",
                new List<string> { "electrochemistry", "potential" }),

            CreateFormula("Arrhenius Equation", "Temperature dependence of reaction rates",
                FormulaCategories.Chemistry, FormulaSubcategories.PhysicalChemistry,
                @"k = A e^{-E_a / RT}",
                new List<string> { "kinetics", "activation-energy" }),

            CreateFormula("Henderson-Hasselbalch", "pH of buffer solutions",
                FormulaCategories.Chemistry, FormulaSubcategories.GeneralChemistry,
                @"\text{pH} = \text{p}K_a + \log\frac{[\text{A}^-]}{[\text{HA}]}",
                new List<string> { "pH", "buffer", "acid-base" }),

            CreateFormula("Gibbs Free Energy", "Spontaneity of reactions",
                FormulaCategories.Chemistry, FormulaSubcategories.PhysicalChemistry,
                @"\Delta G = \Delta H - T\Delta S",
                new List<string> { "thermodynamics", "spontaneity" }),

            // ================================================================
            // STATISTICS
            // ================================================================
            CreateFormula("Bayes' Theorem", "Conditional probability",
                FormulaCategories.Statistics, FormulaSubcategories.Probability,
                @"P(A|B) = \frac{P(B|A)\,P(A)}{P(B)}",
                new List<string> { "probability", "conditional" }),

            CreateFormula("Normal Distribution", "Gaussian probability density function",
                FormulaCategories.Statistics, FormulaSubcategories.Distributions,
                @"f(x) = \frac{1}{\sigma\sqrt{2\pi}} e^{-\frac{(x-\mu)^2}{2\sigma^2}}",
                new List<string> { "gaussian", "bell-curve" }),

            CreateFormula("Standard Deviation", "Measure of dispersion",
                FormulaCategories.Statistics, FormulaSubcategories.Distributions,
                @"\sigma = \sqrt{\frac{1}{N}\sum_{i=1}^{N}(x_i - \mu)^2}",
                new List<string> { "variance", "dispersion" }),

            // ================================================================
            // COMPUTER SCIENCE
            // ================================================================
            CreateFormula("Shannon Entropy", "Information entropy",
                FormulaCategories.ComputerScience, FormulaSubcategories.InformationTheory,
                @"H(X) = -\sum_{i} p(x_i) \log_2 p(x_i)",
                new List<string> { "information", "entropy" }),

            CreateFormula("Stirling's Approximation", "Approximation of factorial",
                FormulaCategories.ComputerScience, FormulaSubcategories.Algorithms,
                @"\ln n! \approx n\ln n - n",
                new List<string> { "factorial", "approximation" }),

            CreateFormula("Big-O Master Theorem", "Recurrence relation solution",
                FormulaCategories.ComputerScience, FormulaSubcategories.Algorithms,
                @"T(n) = aT\!\left(\frac{n}{b}\right) + O(n^d)",
                new List<string> { "complexity", "recurrence" }),
        };
    }

    private static Formula CreateFormula(string name, string description, string category,
        string subcategory, string latexContent, List<string> tags)
    {
        var slug = name.ToLower()
            .Replace(" ", "-")
            .Replace("'", "")
            .Replace("é", "e")
            .Replace("ö", "o");

        return new Formula
        {
            Id = Guid.NewGuid(),
            UserId = null,
            Name = name,
            Description = description,
            LatexContent = latexContent,
            LmlContent = $"\n@equation(label: eq:{slug}, mode: display)\n{latexContent}\n",
            Category = category,
            Subcategory = subcategory,
            Tags = tags,
            IsFavorite = false,
            IsSystem = true,
            UsageCount = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }
}

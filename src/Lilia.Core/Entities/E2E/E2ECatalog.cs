using System.Text.Json;

namespace Lilia.Core.Entities.E2E;

// =====================================================================
//  E2E scenario database — Catalogue layer.
//
//  Models the static product taxonomy: modules → surfaces → UI
//  elements + entry points, plus the block-type and block-action
//  reference tables. Owned by the e2e schema (see configurations).
// =====================================================================

public class E2EModule
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Owner { get; set; }
    public string Criticality { get; set; } = "p1";  // p0 | p1 | p2
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<E2ESurface> Surfaces { get; set; } = new List<E2ESurface>();
    public virtual ICollection<E2EScenario> Scenarios { get; set; } = new List<E2EScenario>();
}

public class E2ESurface
{
    public Guid Id { get; set; }
    public Guid ModuleId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>page | modal | drawer | popover | popup | sheet | dialog | inline | overlay</summary>
    public string SurfaceKind { get; set; } = "page";
    public string? RoutePattern { get; set; }
    public string? SourceFile { get; set; }
    public string? TestidRoot { get; set; }
    public string Criticality { get; set; } = "p1";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual E2EModule Module { get; set; } = null!;
    public virtual ICollection<E2EUIElement> UIElements { get; set; } = new List<E2EUIElement>();
    public virtual ICollection<E2EEntryPoint> EntryPoints { get; set; } = new List<E2EEntryPoint>();
    public virtual ICollection<E2EScenario> Scenarios { get; set; } = new List<E2EScenario>();
}

public class E2EUIElement
{
    public Guid Id { get; set; }
    public Guid SurfaceId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>button | icon_button | link | input | textarea | menu_item | toggle | switch | radio | checkbox | select | tab | disclosure | sheet_handle | fab</summary>
    public string ElementKind { get; set; } = "button";
    public string? AccessibleName { get; set; }
    public string? Role { get; set; }
    public string? VisibleText { get; set; }
    public string? DefaultSelector { get; set; }
    public Guid? ProducesBlockTypeId { get; set; }
    public Guid? TriggersSurfaceId { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual E2ESurface Surface { get; set; } = null!;
    public virtual E2EBlockType? ProducesBlockType { get; set; }
    public virtual E2ESurface? TriggersSurface { get; set; }
    public virtual ICollection<E2ESelectorCandidate> SelectorCandidates { get; set; } = new List<E2ESelectorCandidate>();
}

public class E2ESelectorCandidate
{
    public Guid Id { get; set; }
    public Guid UIElementId { get; set; }
    public int Ordinal { get; set; }                 // 0 = primary
    public string Selector { get; set; } = string.Empty;
    public string? AccessibleName { get; set; }
    public string? Role { get; set; }
    public string? VisibleText { get; set; }
    public string? TagName { get; set; }
    public decimal Confidence { get; set; } = 1.0m;  // 0..1
    public DateTime? LastMatchedAt { get; set; }
    public DateTime? LastMissedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual E2EUIElement UIElement { get; set; } = null!;
}

public class E2EBlockType
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>text | structure | media | code | reference | math</summary>
    public string Category { get; set; } = "text";
    public string? Description { get; set; }
    public string? LatexRole { get; set; }
    public int ScenarioCount { get; set; } = 0;
    public DateTime? LastExercisedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class E2EBlockAction
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ExpectedSurfaceKind { get; set; }
}

public class E2EEntryPoint
{
    public Guid Id { get; set; }
    public Guid TargetSurfaceId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    /// <summary>toolbar_button | command_palette | keyboard_shortcut | url_state | context_menu | right_click | auto_open | deep_link | direct_mount | drag_drop | long_press</summary>
    public string OpenerKind { get; set; } = "toolbar_button";
    public Guid? OpenerElementId { get; set; }
    public string? ShortcutKeys { get; set; }
    public string Criticality { get; set; } = "p1";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual E2ESurface TargetSurface { get; set; } = null!;
    public virtual E2EUIElement? OpenerElement { get; set; }
}

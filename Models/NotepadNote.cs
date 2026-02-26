using System;
using System.Collections.Generic;

namespace BMS.Overlay.Models;

public class NotepadNote
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "Untitled Note";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The RTF content of the note, stored as a string so it can be serialized to JSON.
    /// Only used for freeform (non-form) notes.
    /// </summary>
    public string RtfContent { get; set; } = string.Empty;

    /// <summary>
    /// When true this note was created from a [FORM] template.
    /// The original template text is in <see cref="FormTemplate"/> and user input
    /// is stored in <see cref="FormData"/>.
    /// </summary>
    public bool IsForm { get; set; }

    /// <summary>
    /// The raw template text (lines after [FORM] header). Stored so the form can
    /// always be re-rendered even if the template file changes or is deleted.
    /// </summary>
    public string FormTemplate { get; set; } = string.Empty;

    /// <summary>
    /// User-entered values keyed by field index (0-based order of __ / ☐ in the template).
    /// Text fields store the typed string; checkboxes store "True" / "False".
    /// </summary>
    public Dictionary<string, string> FormData { get; set; } = new();
}

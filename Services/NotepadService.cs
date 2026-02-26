using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BMS.Overlay.Models;

namespace BMS.Overlay.Services;

/// <summary>
/// Handles persistence of notepad notes to %AppData%/BMS/notes.json.
/// Each note's rich-text content is stored as RTF inside the JSON payload.
/// </summary>
public class NotepadService
{
    private readonly string _notesPath;
    private List<NotepadNote> _notes = new();

    public NotepadService()
    {
        _notesPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BMS", "notes.json");
    }

    public IReadOnlyList<NotepadNote> Notes => _notes.AsReadOnly();

    public async Task LoadAsync()
    {
        try
        {
            if (File.Exists(_notesPath))
            {
                var json = await File.ReadAllTextAsync(_notesPath);
                _notes = JsonSerializer.Deserialize<List<NotepadNote>>(json) ?? new();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NotepadService] Load error: {ex.Message}");
            _notes = new();
        }
    }

    public async Task SaveAsync()
    {
        try
        {
            var dir = Path.GetDirectoryName(_notesPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_notes, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_notesPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NotepadService] Save error: {ex.Message}");
        }
    }

    public NotepadNote CreateNote(string title = "Untitled Note", string rtfContent = "")
    {
        var note = new NotepadNote
        {
            Title = title,
            RtfContent = rtfContent
        };
        _notes.Insert(0, note);
        return note;
    }

    public void UpdateNote(NotepadNote note)
    {
        var existing = _notes.FirstOrDefault(n => n.Id == note.Id);
        if (existing != null)
        {
            existing.Title = note.Title;
            existing.RtfContent = note.RtfContent;
            existing.UpdatedAt = DateTime.UtcNow;
        }
    }

    public void DeleteNote(string id)
    {
        _notes.RemoveAll(n => n.Id == id);
    }

    /// <summary>
    /// Returns all .md and .txt files found in the Templates directory next to the executable.
    /// </summary>
    public List<string> GetTemplateFiles()
    {
        var templates = new List<string>();
        try
        {
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;
            var templateDir = Path.Combine(exeDir, "Templates");
            if (Directory.Exists(templateDir))
            {
                templates.AddRange(Directory.GetFiles(templateDir, "*.md"));
                templates.AddRange(Directory.GetFiles(templateDir, "*.txt"));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NotepadService] Template scan error: {ex.Message}");
        }
        return templates;
    }

    public string ReadTemplateContent(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Returns true when the template content starts with [FORM],
    /// indicating it should be rendered as an interactive form.
    /// </summary>
    public bool IsFormTemplate(string content)
    {
        return content.TrimStart().StartsWith("[FORM]", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Strips the [FORM] header line and returns the remaining template body.
    /// </summary>
    public string GetFormBody(string content)
    {
        var idx = content.IndexOf('\n');
        return idx >= 0 ? content[(idx + 1)..] : string.Empty;
    }
}

using MyGarage.Database.Common;
using MyGarage.Database.JournalNotes;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Vehicles.JournalNotes;

public static class JournalNoteMapper
{
    public static JournalNoteDto ToDto(JournalNoteRecord note) =>
        new() { Id = note.Id, Category = note.Category, Body = note.Body, CreatedAt = note.CreatedAt, UpdatedAt = note.UpdatedAt };

    public static PagedResultDto<JournalNoteDto> ToPageDto(PagedResult<JournalNoteRecord> page) =>
        new()
        {
            Items = page.Items.Select(ToDto).ToList(),
            Page = page.Page,
            PageSize = page.PageSize,
            TotalCount = page.TotalCount,
            TotalPages = page.TotalPages,
        };

    // The category is free text: trimmed, and blank means none.
    public static string? CleanCategory(string? category) => string.IsNullOrWhiteSpace(category) ? null : category.Trim();
}

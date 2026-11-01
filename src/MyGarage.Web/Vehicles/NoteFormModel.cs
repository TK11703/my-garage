namespace MyGarage.Web.Vehicles;

// Text of a journal note and its optional free-text category, as typed in the add and edit forms.
public sealed class NoteFormModel
{
    public string? Body { get; set; }

    public string? Category { get; set; }
}

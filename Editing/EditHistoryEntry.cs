using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

internal readonly record struct EditHistoryEntry(int Index, string Name, string Title, bool CanReplay = false);

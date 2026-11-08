namespace MgaSonicAnvil.Domain;

/// <summary>連続選択帯の中での位置。中間は角丸なし。</summary>
internal enum SelectionBandEdge : byte
{
    None = 0,
    Solo = 1,
    First = 2,
    Middle = 3,
    Last = 4,
}

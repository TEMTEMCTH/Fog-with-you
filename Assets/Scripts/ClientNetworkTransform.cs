using Unity.Netcode.Components;

/// <summary>
/// NetworkTransform, где позицию задаёт клиент-владелец (а не сервер).
/// Нужен, чтобы игрок двигался без задержки. Повесьте на префаб игрока
/// ВМЕСТО обычного NetworkTransform.
/// </summary>
public class ClientNetworkTransform : NetworkTransform
{
    protected override bool OnIsServerAuthoritative() => false;
}
